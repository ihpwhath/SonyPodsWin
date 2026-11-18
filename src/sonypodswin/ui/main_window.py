"""Main WinUI 3 / MagicPods-style window and BLE controller bridge for SonyPodsWin."""

import asyncio
import ctypes
import json
import threading
from pathlib import Path

from PySide6.QtCore import QObject, Qt, QTimer, Signal
from PySide6.QtGui import QAction, QCloseEvent, QColor, QIcon, QPainter, QPixmap, QShowEvent
from PySide6.QtWidgets import (
    QApplication,
    QButtonGroup,
    QFrame,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QMenu,
    QMessageBox,
    QPushButton,
    QScrollArea,
    QStackedWidget,
    QSystemTrayIcon,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)

from ..protocol import (
    AmbientSoundMode,
    ConnectionQualityMode,
    DeviceInfoType,
    DseeEffectState,
    EqEbbInquiredType,
    EqPresetId,
    NcAsmInquiredType,
    NoiseControlMode,
    PlaybackControl,
    PlayInquiredType,
    PowerInquiredType,
    v2_table1,
)
from ..protocol.v2_table1 import (
    AUDIO_INQ_CONNECTION_MODE_WITH_LDAC,
    AUDIO_INQ_UPSCALING_WITH_REASON,
    AlertNtfyParamResponse,
    AudioCodecResponse,
    AutoPowerOffResponse,
    BatteryResponse,
    CapabilityInfoResponse,
    ConnectionQualityResponse,
    DeviceInfoResponse,
    EqCapabilityResponse,
    EqEbbResponse,
    EqExtendedInfoResponse,
    GeneralSettingCapabilityResponse,
    GeneralSettingParamResponse,
    NoiseControlResponse,
    PlaybackVolumeResponse,
    SpeakToChatResponse,
    SupportFunctionResponse,
    UpscalingEffectResponse,
    UpscalingParamResponse,
)
from ..protocol.v2_table2 import PeripheralDevicesResponse
from ..transport.ble_transport import DiscoveredDevice, SonyBleTandemClient
from .graphics import HeadphoneAvatar, InteractiveEqGraph, enable_win11_dark_titlebar
from .popup_window import BatteryPopup
from .widgets import BatteryRing, CardFrame, MagicSlider, SettingRow, ToggleSwitch

CONFIG_PATH = Path.home() / ".sonypodswin.json"

XM6_DEFAULT_PRESETS = [
    (EqPresetId.OFF, "关闭"),
    (EqPresetId.HEAVY, "重低音"),
    (EqPresetId.CLEAR, "清澈"),
    (EqPresetId.HARD, "硬朗"),
    (EqPresetId.SOFT, "柔和"),
    (EqPresetId.GAMING_EQ, "游戏"),
    (EqPresetId.CUSTOM, "自定义"),
    (EqPresetId.USER_SETTING1, "自定 1"),
    (EqPresetId.USER_SETTING2, "自定 2"),
]


def _send_media_play_pause() -> None:
    """Send Windows VK_MEDIA_PLAY_PAUSE (0xB3) key event."""
    try:
        user32 = ctypes.windll.user32
        user32.keybd_event(0xB3, 0, 0, 0)
        user32.keybd_event(0xB3, 0, 2, 0)
    except Exception:
        pass


def _make_tray_icon() -> QIcon:
    pm = QPixmap(64, 64)
    pm.fill(Qt.GlobalColor.transparent)
    p = QPainter(pm)
    p.setRenderHint(QPainter.RenderHint.Antialiasing)
    p.setBrush(QColor("#D4B16A"))
    p.setPen(Qt.PenStyle.NoPen)
    p.drawRoundedRect(4, 4, 56, 56, 14, 14)
    p.setPen(QColor("#1E1F22"))
    font = p.font()
    font.setPointSize(24)
    font.setBold(True)
    p.setFont(font)
    p.drawText(pm.rect(), Qt.AlignmentFlag.AlignCenter, "S")
    p.end()
    return QIcon(pm)


class BleBridge(QObject):
    devices_found = Signal(list)
    connected = Signal(str)
    disconnected = Signal()
    response_received = Signal(object)
    log_message = Signal(str)
    error_message = Signal(str)

    def __init__(self) -> None:
        super().__init__()
        self._loop = asyncio.new_event_loop()
        self._thread = threading.Thread(target=self._run_loop, daemon=True)
        self._thread.start()
        self._client = SonyBleTandemClient(
            on_payload=self._on_payload,
            on_log=lambda msg: self.log_message.emit(msg),
            on_disconnected=lambda: self.disconnected.emit(),
        )

    def _run_loop(self) -> None:
        asyncio.set_event_loop(self._loop)
        self._loop.run_forever()

    def _on_payload(self, raw: bytes) -> None:
        parsed = v2_table1.parse(raw)
        self.response_received.emit(parsed)

    def scan(self) -> None:
        async def _do() -> None:
            try:
                devs = await SonyBleTandemClient.scan_devices(timeout=3.5)
                self.devices_found.emit(devs)
            except Exception as exc:
                self.error_message.emit(f"扫描失败: {exc}")

        asyncio.run_coroutine_threadsafe(_do(), self._loop)

    def connect_device(self, address: str) -> None:
        async def _do() -> None:
            try:
                await self._client.connect(address)
                self.connected.emit(address)
            except Exception as exc:
                self.error_message.emit(f"连接失败: {exc}")
                self.disconnected.emit()

        asyncio.run_coroutine_threadsafe(_do(), self._loop)

    def disconnect_device(self) -> None:
        async def _do() -> None:
            await self._client.disconnect()
            self.disconnected.emit()

        asyncio.run_coroutine_threadsafe(_do(), self._loop)

    def send(self, payload: bytes) -> None:
        self._loop.call_soon_threadsafe(self._client.send, payload)

    def shutdown(self) -> None:
        fut = asyncio.run_coroutine_threadsafe(self._client.disconnect(), self._loop)
        try:
            fut.result(timeout=2.0)
        except Exception:
            pass
        self._loop.call_soon_threadsafe(self._loop.stop)


class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("SonyPods")
        self.setWindowIcon(_make_tray_icon())
        self.setMinimumSize(680, 720)
        self.resize(720, 780)

        self._bridge = BleBridge()
        self._bridge.devices_found.connect(self._on_devices_found)
        self._bridge.connected.connect(self._on_connected)
        self._bridge.disconnected.connect(self._on_disconnected)
        self._bridge.response_received.connect(self._on_response)
        self._bridge.log_message.connect(self._append_log)
        self._bridge.error_message.connect(self._on_error)

        self._is_connected = False
        self._manual_disconnect = False
        self._discovered_devices: list[DiscoveredDevice] = []
        self._current_address: str | None = None
        self._current_name: str = "WH-1000XM6"
        self._current_fw: str = ""
        self._current_battery: int | None = None
        self._current_codec: str = ""
        self._current_nc_mode: NoiseControlMode | None = NoiseControlMode.NOISE_CANCELLING
        self._low_battery_notified = False

        self._nc_type = NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS_NA
        self._eq_type = EqEbbInquiredType.PRESET_EQ_AND_ERRORCODE
        self._upscaling_inq = AUDIO_INQ_UPSCALING_WITH_REASON
        self._conn_quality_inq = AUDIO_INQ_CONNECTION_MODE_WITH_LDAC
        self._multipoint_slot: int | None = None

        self._popup = BatteryPopup()
        self._popup.nc_mode_selected.connect(lambda m: self._quick_nc(NoiseControlMode(m)))
        self._popup.open_main_requested.connect(self._show_main_window)

        self._reconnect_timer = QTimer(self)
        self._reconnect_timer.setInterval(8000)
        self._reconnect_timer.timeout.connect(self._on_reconnect_tick)

        self._build_ui()
        self._setup_tray()
        enable_win11_dark_titlebar(self)
        self._load_config_and_scan()

    def showEvent(self, event: QShowEvent) -> None:
        super().showEvent(event)
        enable_win11_dark_titlebar(self)

    def _build_ui(self) -> None:
        root = QWidget()
        root.setObjectName("RootWindow")
        self.setCentralWidget(root)
        main_layout = QVBoxLayout(root)
        main_layout.setContentsMargins(24, 16, 24, 16)
        main_layout.setSpacing(16)

        # ── MagicPods-style Top Hero Banner ────────────────────────────────
        banner = QFrame()
        banner.setObjectName("HeaderBanner")
        b_layout = QHBoxLayout(banner)
        b_layout.setContentsMargins(20, 16, 20, 16)
        b_layout.setSpacing(16)

        self.avatar = HeadphoneAvatar(52)
        b_layout.addWidget(self.avatar)

        title_col = QVBoxLayout()
        title_col.setContentsMargins(0, 0, 0, 0)
        title_col.setSpacing(4)
        self.lbl_model = QLabel("WH-1000XM6")
        self.lbl_model.setObjectName("HeroTitle")
        self.bat_badge = BatteryRing()
        title_col.addWidget(self.lbl_model)
        title_col.addWidget(self.bat_badge)
        b_layout.addLayout(title_col, stretch=1)

        self.btn_device_menu = QPushButton("切换设备 ▾")
        self.btn_device_menu.setObjectName("GhostBtn")
        self.btn_device_menu.setCursor(Qt.CursorShape.PointingHandCursor)
        self.btn_device_menu.clicked.connect(self._show_device_menu)

        self.btn_connect = QPushButton("连接")
        self.btn_connect.setObjectName("MagicActionBtn")
        self.btn_connect.setCursor(Qt.CursorShape.PointingHandCursor)
        self.btn_connect.clicked.connect(self._on_connect_clicked)

        b_layout.addWidget(self.btn_device_menu)
        b_layout.addWidget(self.btn_connect)
        main_layout.addWidget(banner)

        # ── Segmented Pivot Navigation Bar ─────────────────────────────────
        nav_bar = QHBoxLayout()
        nav_bar.setContentsMargins(4, 0, 4, 0)
        nav_bar.setSpacing(6)
        self.nav_group = QButtonGroup(self)
        self.nav_group.setExclusive(True)

        tab_titles = [
            "降噪与音质",
            "索尼智能控制",
            "Windows 联动增强",
            "协议日志",
        ]
        for idx, title in enumerate(tab_titles):
            btn = QPushButton(title)
            btn.setObjectName("NavTabBtn")
            btn.setCheckable(True)
            btn.setCursor(Qt.CursorShape.PointingHandCursor)
            if idx == 0:
                btn.setChecked(True)
            self.nav_group.addButton(btn, idx)
            nav_bar.addWidget(btn)
        nav_bar.addStretch(1)
        main_layout.addLayout(nav_bar)

        # ── Stacked Pages ──────────────────────────────────────────────────
        self.stack = QStackedWidget()
        self.stack.addWidget(self._build_page_anc_eq())
        self.stack.addWidget(self._build_page_sony_smart())
        self.stack.addWidget(self._build_page_windows_magic())
        self.stack.addWidget(self._build_page_logs())
        self.nav_group.idClicked.connect(self.stack.setCurrentIndex)
        main_layout.addWidget(self.stack, stretch=1)

    @staticmethod
    def _wrap_scroll(content_widget: QWidget) -> QScrollArea:
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        content_widget.setObjectName("ScrollContent")
        scroll.setWidget(content_widget)
        return scroll

    def _build_page_anc_eq(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 4, 4, 4)
        layout.setSpacing(16)

        # 1. ANC / Ambient Sound Card
        anc_card = CardFrame("环境声音控制")
        mode_row = QHBoxLayout()
        mode_row.setSpacing(0)
        self.nc_group = QButtonGroup(self)
        self.nc_group.setExclusive(True)

        for seg_idx, (label, mode) in enumerate(
            (
                ("降噪", NoiseControlMode.NOISE_CANCELLING),
                ("环境声", NoiseControlMode.AMBIENT_SOUND),
                ("关闭", NoiseControlMode.OFF),
            )
        ):
            btn = QPushButton(label)
            btn.setObjectName("ModeCardBtn")
            btn.setProperty("segment", ("first", "mid", "last")[seg_idx])
            btn.setFixedHeight(58)
            btn.setCheckable(True)
            btn.setCursor(Qt.CursorShape.PointingHandCursor)
            self.nc_group.addButton(btn, int(mode))
            mode_row.addWidget(btn)
        self.nc_group.button(int(NoiseControlMode.NOISE_CANCELLING)).setChecked(True)
        self.nc_group.idClicked.connect(lambda _id: self._apply_nc_asm())
        anc_card.body.addLayout(mode_row)
        anc_card.add_divider()

        self.slider_asm = MagicSlider(1, 20, 15, suffix=" 级", width=210)
        self.slider_asm.value_released.connect(lambda _v: self._apply_nc_asm())
        anc_card.body.addWidget(
            SettingRow("环境声等级", "调节通透模式下的外部环境音传入强度 (1 – 20)", self.slider_asm)
        )
        anc_card.add_divider()

        self.sw_voice_focus = ToggleSwitch()
        self.sw_voice_focus.toggled_by_user.connect(lambda _v: self._apply_nc_asm())
        anc_card.body.addWidget(
            SettingRow("关注语音", "在环境声模式下抑制背景噪音并重点保留人声", self.sw_voice_focus)
        )
        anc_card.add_divider()

        self.sw_auto_ambient = ToggleSwitch()
        self.sw_auto_ambient.toggled_by_user.connect(lambda _v: self._apply_nc_asm())
        anc_card.body.addWidget(
            SettingRow("自适应环境声灵敏度", "根据周围环境噪音自动平滑调节环境声通透度", self.sw_auto_ambient)
        )
        layout.addWidget(anc_card)

        # 2. Equalizer & Sound Quality Card
        eq_card = CardFrame("10 段均衡器与声音增强")

        # Preset chips row
        self.preset_bar = QHBoxLayout()
        self.preset_bar.setSpacing(6)
        self.preset_group = QButtonGroup(self)
        self.preset_group.setExclusive(True)
        self._populate_preset_chips(XM6_DEFAULT_PRESETS)
        self.preset_group.idClicked.connect(self._on_preset_chip_clicked)
        eq_card.body.addLayout(self.preset_bar)

        # Interactive EQ Curve Graph
        self.eq_graph = InteractiveEqGraph()
        self.eq_graph.bands_released.connect(self._on_eq_graph_released)
        eq_card.body.addWidget(self.eq_graph)
        eq_card.add_divider()

        self.sw_dsee = ToggleSwitch()
        self.sw_dsee.toggled_by_user.connect(self._on_dsee_toggled)
        eq_card.body.addWidget(
            SettingRow("DSEE 音质提升", "使用 AI 实时恢复压缩音源损失的高频细节", self.sw_dsee)
        )
        eq_card.add_divider()

        self.sw_ldac_priority = ToggleSwitch()
        self.sw_ldac_priority.set_checked_silent(True)
        self.sw_ldac_priority.toggled_by_user.connect(self._on_quality_toggled)
        eq_card.body.addWidget(
            SettingRow(
                "声音质量优先",
                "开启后优先保证最高音频传输质量；关闭则切换为稳定连接优先",
                self.sw_ldac_priority,
            )
        )

        layout.addWidget(eq_card)
        layout.addStretch(1)
        return self._wrap_scroll(page)

    def _populate_preset_chips(self, presets: list[tuple[EqPresetId, str]]) -> None:
        for b in self.preset_group.buttons():
            self.preset_group.removeButton(b)
            b.deleteLater()
        while self.preset_bar.count():
            item = self.preset_bar.takeAt(0)
            if item.widget():
                item.widget().deleteLater()

        for idx, (preset, label) in enumerate(presets):
            chip = QPushButton(label)
            chip.setObjectName("PresetChip")
            chip.setCheckable(True)
            chip.setCursor(Qt.CursorShape.PointingHandCursor)
            if idx == 0:
                chip.setChecked(True)
            self.preset_group.addButton(chip, int(preset))
            self.preset_bar.addWidget(chip)
        self.preset_bar.addStretch(1)

    def _build_page_sony_smart(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 4, 4, 4)
        layout.setSpacing(16)

        smart_card = CardFrame("智能聆听与多点连接")

        self.sw_speak_to_chat = ToggleSwitch()
        self.sw_speak_to_chat.toggled_by_user.connect(self._on_speak_to_chat_toggled)
        smart_card.body.addWidget(
            SettingRow(
                "智能免摘对话",
                "检测到你开口说话时自动暂停媒体播放并切换至环境声模式",
                self.sw_speak_to_chat,
            )
        )
        smart_card.add_divider()

        self.sw_multipoint = ToggleSwitch()
        self.sw_multipoint.toggled_by_user.connect(self._on_multipoint_toggled)
        smart_card.body.addWidget(
            SettingRow(
                "同时连接 2 台设备",
                "允许 WH-1000XM6 同时保持与电脑和手机的双设备蓝牙连接",
                self.sw_multipoint,
            )
        )
        smart_card.add_divider()

        self.sw_cinema = ToggleSwitch()
        self.sw_cinema.toggled_by_user.connect(self._on_cinema_toggled)
        smart_card.body.addWidget(
            SettingRow("影院声场模式", "开启索尼 360 空间环绕影院立体声场效果", self.sw_cinema)
        )
        smart_card.add_divider()

        self.lbl_paired = QLabel("正在读取已配对设备...")
        self.lbl_paired.setStyleSheet("color: #D4B16A; font-size: 12px; font-weight: 500;")
        smart_card.body.addWidget(
            SettingRow("已配对多点设备列表", "耳机当前记忆的蓝牙主机及活动连接状态", self.lbl_paired)
        )
        smart_card.add_divider()

        self.lbl_auto_power = QLabel("已启用佩戴检测自动关机")
        self.lbl_auto_power.setStyleSheet("color: #A9ADB3; font-size: 12px;")
        smart_card.body.addWidget(
            SettingRow("自动关机策略", "取下耳机一段时间后自动关闭电源以节省电量", self.lbl_auto_power)
        )
        layout.addWidget(smart_card)

        media_card = CardFrame("耳机音量与电源管理")
        self.slider_vol = MagicSlider(0, 100, 50, suffix="%", width=210)
        self.slider_vol.value_released.connect(self._on_volume_released)
        media_card.body.addWidget(
            SettingRow("耳机媒体音量", "实时同步并调节耳机内部放大器音量", self.slider_vol)
        )
        media_card.add_divider()

        ctrl_row = QHBoxLayout()
        ctrl_row.setContentsMargins(4, 8, 4, 4)
        ctrl_row.setSpacing(10)
        btn_prev = QPushButton("⏮ 上一曲")
        btn_play = QPushButton("▶ 播放")
        btn_pause = QPushButton("⏸ 暂停")
        btn_next = QPushButton("⏭ 下一曲")
        btn_power = QPushButton("⏻ 关闭耳机电源")
        btn_power.setObjectName("DangerBtn")

        btn_prev.clicked.connect(lambda: self._send(v2_table1.build_playback(PlaybackControl.TRACK_DOWN)))
        btn_play.clicked.connect(lambda: self._send(v2_table1.build_playback(PlaybackControl.PLAY)))
        btn_pause.clicked.connect(lambda: self._send(v2_table1.build_playback(PlaybackControl.PAUSE)))
        btn_next.clicked.connect(lambda: self._send(v2_table1.build_playback(PlaybackControl.TRACK_UP)))
        btn_power.clicked.connect(self._on_power_off_clicked)

        for b in (btn_prev, btn_play, btn_pause, btn_next):
            b.setCursor(Qt.CursorShape.PointingHandCursor)
            ctrl_row.addWidget(b)
        ctrl_row.addStretch(1)
        btn_power.setCursor(Qt.CursorShape.PointingHandCursor)
        ctrl_row.addWidget(btn_power)
        media_card.body.addLayout(ctrl_row)

        layout.addWidget(media_card)
        layout.addStretch(1)
        return self._wrap_scroll(page)

    def _build_page_windows_magic(self) -> QWidget:
        """MagicPods-exact Windows integration settings page."""
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 4, 4, 4)
        layout.setSpacing(16)

        card = CardFrame("Windows 系统联动与自动化")

        self.sw_auto_play = ToggleSwitch()
        self.sw_auto_play.set_checked_silent(False)
        self.sw_auto_play.toggled_by_user.connect(lambda _v: self._save_config())
        card.body.addWidget(
            SettingRow(
                "自动播放",
                "与耳机建立连接之后发送“播放”指令。如果没有启动的播放器，Windows 会忽略这条指令。",
                self.sw_auto_play,
            )
        )
        card.add_divider()

        self.sw_auto_pause = ToggleSwitch()
        self.sw_auto_pause.set_checked_silent(True)
        self.sw_auto_pause.toggled_by_user.connect(lambda _v: self._save_config())
        card.body.addWidget(
            SettingRow("自动暂停", "耳机断开连接后自动暂停当前媒体播放。", self.sw_auto_pause)
        )
        card.add_divider()

        self.slider_default_vol = MagicSlider(0, 100, 54, suffix="%", width=220)
        self.slider_default_vol.value_released.connect(lambda _v: self._save_config())
        card.body.addWidget(
            SettingRow("默认音量", "选定的音量将在每次耳机连接之后自动应用（设为 0% 则不改动）。", self.slider_default_vol)
        )
        card.add_divider()

        self.sw_popup_on_connect = ToggleSwitch()
        self.sw_popup_on_connect.set_checked_silent(True)
        self.sw_popup_on_connect.toggled_by_user.connect(lambda _v: self._save_config())
        card.body.addWidget(
            SettingRow(
                "连接时弹出右下角电量卡片",
                "连接耳机或单击系统托盘图标时，在桌面右下角弹出快捷降噪与电量浮窗。",
                self.sw_popup_on_connect,
            )
        )
        card.add_divider()

        self.sw_auto_reconnect = ToggleSwitch()
        self.sw_auto_reconnect.set_checked_silent(True)
        self.sw_auto_reconnect.toggled_by_user.connect(lambda _v: self._save_config())
        card.body.addWidget(
            SettingRow(
                "异常断线自动重连",
                "当耳机非手动断开连接时，后台每 8 秒自动探测并恢复连接。",
                self.sw_auto_reconnect,
            )
        )
        card.add_divider()

        self.slider_low_bat = MagicSlider(5, 50, 30, suffix="%", width=220)
        self.slider_low_bat.value_released.connect(lambda _v: self._save_config())
        card.body.addWidget(
            SettingRow("低电量提醒", "当耳机电量低于指定值时显示 Windows 系统通知。", self.slider_low_bat)
        )

        layout.addWidget(card)
        layout.addStretch(1)
        return self._wrap_scroll(page)

    def _build_page_logs(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 4, 4, 4)
        card = CardFrame("实时通信日志")
        self.txt_log = QTextEdit()
        self.txt_log.setObjectName("LogBox")
        self.txt_log.setReadOnly(True)
        card.body.addWidget(self.txt_log)
        layout.addWidget(card)
        return page

    def _setup_tray(self) -> None:
        self.tray = QSystemTrayIcon(_make_tray_icon(), self)
        menu = QMenu()
        act_show = QAction("打开主面板", self)
        act_show.triggered.connect(self._show_main_window)
        act_nc = QAction("切换：降噪模式", self)
        act_nc.triggered.connect(lambda: self._quick_nc(NoiseControlMode.NOISE_CANCELLING))
        act_asm = QAction("切换：环境声模式", self)
        act_asm.triggered.connect(lambda: self._quick_nc(NoiseControlMode.AMBIENT_SOUND))
        act_off = QAction("切换：关闭降噪", self)
        act_off.triggered.connect(lambda: self._quick_nc(NoiseControlMode.OFF))
        act_quit = QAction("退出程序", self)
        act_quit.triggered.connect(self._quit_app)
        menu.addAction(act_show)
        menu.addSeparator()
        menu.addActions([act_nc, act_asm, act_off])
        menu.addSeparator()
        menu.addAction(act_quit)
        self.tray.setContextMenu(menu)
        self.tray.activated.connect(self._on_tray_activated)
        self.tray.show()

    def _show_main_window(self) -> None:
        self.showNormal()
        self.activateWindow()

    def _on_tray_activated(self, reason: QSystemTrayIcon.ActivationReason) -> None:
        if reason == QSystemTrayIcon.ActivationReason.Trigger:
            self._sync_popup_state()
            self._popup.show_near_tray()
        elif reason == QSystemTrayIcon.ActivationReason.DoubleClick:
            self._show_main_window()

    def _sync_header_and_popup(self) -> None:
        extra = self._current_codec
        if self._current_fw:
            extra = f"{extra} · FW {self._current_fw}" if extra else f"FW {self._current_fw}"
        self.bat_badge.set_status(self._current_battery, extra)
        self._sync_popup_state()

    def _sync_popup_state(self) -> None:
        self._popup.update_state(
            model_name=self.lbl_model.text(),
            battery=self._current_battery,
            codec=self._current_codec or "--",
            nc_mode=self._current_nc_mode,
        )

    def _show_device_menu(self) -> None:
        menu = QMenu(self)
        menu.setStyleSheet(
            "QMenu { background-color: #2A2D32; border: 1px solid #3A3D42; color: #FFFFFF; padding: 4px; }"
            "QMenu::item { padding: 6px 16px; border-radius: 4px; }"
            "QMenu::item:selected { background-color: #D4B16A; color: #1E1F22; }"
        )
        for d in self._discovered_devices:
            act = menu.addAction(f"{d.name}  ({d.address})")
            act.triggered.connect(lambda _c=False, dev=d: self._select_and_connect(dev.name, dev.address))
        if self._discovered_devices:
            menu.addSeparator()
        act_scan = menu.addAction("↻ 重新扫描附近的索尼耳机")
        act_scan.triggered.connect(self._on_scan_clicked)
        menu.exec(self.btn_device_menu.mapToGlobal(self.btn_device_menu.rect().bottomLeft()))

    def _select_and_connect(self, name: str, address: str) -> None:
        self._current_name = name
        self._current_address = address
        self.lbl_model.setText(name)
        self._manual_disconnect = False
        self.btn_connect.setEnabled(False)
        self.btn_connect.setText("连接中...")
        self._bridge.connect_device(address)

    def _quick_nc(self, mode: NoiseControlMode) -> None:
        btn = self.nc_group.button(int(mode))
        if btn:
            btn.setChecked(True)
        self._apply_nc_asm()

    def _load_config_and_scan(self) -> None:
        if CONFIG_PATH.exists():
            try:
                cfg = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
                self._current_address = cfg.get("last_address")
                self._current_name = cfg.get("last_name") or "WH-1000XM6"
                self.lbl_model.setText(self._current_name)
                self.sw_popup_on_connect.set_checked_silent(bool(cfg.get("show_popup_on_connect", True)))
                self.sw_auto_reconnect.set_checked_silent(bool(cfg.get("auto_reconnect", True)))
                self.sw_auto_play.set_checked_silent(bool(cfg.get("auto_play", False)))
                self.sw_auto_pause.set_checked_silent(bool(cfg.get("auto_pause", True)))
                self.slider_default_vol.set_value_silent(int(cfg.get("default_volume", 54)))
                self.slider_low_bat.set_value_silent(max(5, min(50, int(cfg.get("low_battery_threshold", 30)))))
            except Exception:
                pass
        self._on_scan_clicked()
        if self._current_address:
            self._append_log(f"正在自动连接上次设备: {self._current_address}...")
            self.btn_connect.setText("连接中...")
            self._bridge.connect_device(self._current_address)

    def _save_config(self) -> None:
        try:
            data = {
                "last_name": self._current_name,
                "last_address": self._current_address,
                "show_popup_on_connect": self.sw_popup_on_connect.isChecked(),
                "auto_reconnect": self.sw_auto_reconnect.isChecked(),
                "auto_play": self.sw_auto_play.isChecked(),
                "auto_pause": self.sw_auto_pause.isChecked(),
                "default_volume": self.slider_default_vol.value(),
                "low_battery_threshold": self.slider_low_bat.value(),
            }
            CONFIG_PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        except Exception:
            pass

    def _on_scan_clicked(self) -> None:
        self.btn_device_menu.setText("扫描中...")
        self._bridge.scan()

    def _on_devices_found(self, devices: list[DiscoveredDevice]) -> None:
        self.btn_device_menu.setText("切换设备 ▾")
        self._discovered_devices = devices
        if not self._current_address and devices:
            self._current_address = devices[0].address
            self._current_name = devices[0].name
            self.lbl_model.setText(self._current_name)
        self._append_log(f"发现 {len(devices)} 台 Sony 蓝牙设备")

    def _on_connect_clicked(self) -> None:
        if self._is_connected:
            self._manual_disconnect = True
            self._reconnect_timer.stop()
            self._bridge.disconnect_device()
            return
        addr = self._current_address or (self._discovered_devices[0].address if self._discovered_devices else None)
        if not addr:
            self._show_device_menu()
            return
        self._manual_disconnect = False
        self.btn_connect.setEnabled(False)
        self.btn_connect.setText("连接中...")
        self._current_address = addr
        self._bridge.connect_device(addr)

    def _on_connected(self, address: str) -> None:
        self._is_connected = True
        self._manual_disconnect = False
        self._low_battery_notified = False
        self._reconnect_timer.stop()
        self._current_address = address
        self.btn_connect.setEnabled(True)
        self.btn_connect.setText("断开连接")
        self._save_config()
        self._query_initial_state()

        if self.sw_auto_play.isChecked():
            _send_media_play_pause()
        def_vol = self.slider_default_vol.value()
        if def_vol > 0:
            self._send(v2_table1.build_set_playback_volume(def_vol))

        if self.sw_popup_on_connect.isChecked():
            QTimer.singleShot(900, self._show_connect_popup)

    def _show_connect_popup(self) -> None:
        if self._is_connected:
            self._sync_popup_state()
            self._popup.show_near_tray()

    def _on_disconnected(self) -> None:
        was_connected = self._is_connected
        self._is_connected = False
        self.btn_connect.setEnabled(True)
        self.btn_connect.setText("连接")
        if was_connected and self.sw_auto_pause.isChecked():
            _send_media_play_pause()
        if not self._manual_disconnect and self._current_address and self.sw_auto_reconnect.isChecked():
            self._append_log("连接已断开，8 秒后尝试自动重连...")
            self._reconnect_timer.start()

    def _on_reconnect_tick(self) -> None:
        if self._is_connected or self._manual_disconnect or not self._current_address:
            self._reconnect_timer.stop()
            return
        self._bridge.connect_device(self._current_address)

    def _query_initial_state(self) -> None:
        cmds = [
            v2_table1.build_get_protocol_info(),
            v2_table1.build_get_capability_info(),
            v2_table1.build_get_device_info(DeviceInfoType.MODEL_NAME),
            v2_table1.build_get_device_info(DeviceInfoType.FW_VERSION),
            v2_table1.build_get_support_function(),
            v2_table1.build_get_battery_status(PowerInquiredType.BATTERY),
            v2_table1.build_get_audio_codec_status(),
            v2_table1.build_get_upscaling_effect_status(),
            v2_table1.build_get_nc_asm_param(self._nc_type),
            v2_table1.build_get_eq_ebb_capability(self._eq_type),
            v2_table1.build_get_eq_ebb_extended_info(self._eq_type),
            v2_table1.build_get_eq_ebb_param(self._eq_type),
            v2_table1.build_get_upscaling(self._upscaling_inq),
            v2_table1.build_get_connection_quality(self._conn_quality_inq),
            v2_table1.build_get_speak_to_chat_param(),
            v2_table1.build_get_cinema_mode(),
            v2_table1.build_get_auto_power_off(0x05),
            v2_table1.build_get_playback_param(PlayInquiredType.MUSIC_VOLUME),
        ]
        for slot in (0xD1, 0xD2, 0xD3, 0xD4):
            cmds.append(v2_table1.build_get_general_setting_capability(slot))
        for c in cmds:
            self._send(c)

    def _send(self, data: bytes) -> None:
        if not self._is_connected:
            return
        self._bridge.send(data)

    def _on_response(self, resp: object) -> None:
        if isinstance(resp, DeviceInfoResponse):
            if resp.info_type == DeviceInfoType.MODEL_NAME and resp.text:
                self._current_name = resp.text
                self.lbl_model.setText(resp.text)
                self._save_config()
            elif resp.info_type == DeviceInfoType.FW_VERSION and resp.text:
                self._current_fw = resp.text
                self._sync_header_and_popup()
        elif isinstance(resp, BatteryResponse):
            if (
                resp.kind in (PowerInquiredType.BATTERY, PowerInquiredType.LEFT_RIGHT_BATTERY, PowerInquiredType.CRADLE_BATTERY)
                and resp.levels
                and resp.levels[0] is not None
            ):
                pct = resp.levels[0]
                self._current_battery = pct
                self.tray.setToolTip(f"{self.lbl_model.text()} · 电量 {pct}%")
                self._sync_header_and_popup()
                if pct <= self.slider_low_bat.value() and not self._low_battery_notified:
                    self._low_battery_notified = True
                    self.tray.showMessage(
                        "SonyPods 低电量提醒",
                        f"{self.lbl_model.text()} 电量仅剩 {pct}%，请及时充电。",
                        QSystemTrayIcon.MessageIcon.Warning,
                        5000,
                    )
        elif isinstance(resp, AudioCodecResponse):
            self._current_codec = resp.codec.label
            self._sync_header_and_popup()
        elif isinstance(resp, SupportFunctionResponse):
            codes = {code for code, _ in resp.functions}
            if 0x6D in codes:
                self._nc_type = NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS_NA
            elif 0x68 in codes:
                self._nc_type = NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS
            if 0x57 in codes:
                self._eq_type = EqEbbInquiredType.PRESET_EQ_AND_ERRORCODE
            elif 0x51 in codes:
                self._eq_type = EqEbbInquiredType.PRESET_EQ
        elif isinstance(resp, NoiseControlResponse):
            if resp.control_mode is not None:
                self._current_nc_mode = resp.control_mode
                btn = self.nc_group.button(int(resp.control_mode))
                if btn:
                    btn.setChecked(True)
                self._sync_popup_state()
            if resp.ambient_level is not None:
                self.slider_asm.set_value_silent(max(1, min(20, resp.ambient_level)))
            if resp.ambient_mode is not None:
                self.sw_voice_focus.set_checked_silent(resp.ambient_mode == AmbientSoundMode.VOICE)
            if resp.noise_adaptive is not None:
                self.sw_auto_ambient.set_checked_silent(resp.noise_adaptive)
        elif isinstance(resp, EqCapabilityResponse):
            if resp.presets:
                short_names = {
                    EqPresetId.OFF: "关闭",
                    EqPresetId.HEAVY: "重低音",
                    EqPresetId.CLEAR: "清澈",
                    EqPresetId.HARD: "硬朗",
                    EqPresetId.SOFT: "柔和",
                    EqPresetId.GAMING_EQ: "游戏",
                    EqPresetId.CUSTOM: "自定义",
                    EqPresetId.USER_SETTING1: "自定 1",
                    EqPresetId.USER_SETTING2: "自定 2",
                    EqPresetId.USER_SETTING3: "自定 3",
                }
                pairs = [(p, short_names.get(p, p.label.split(" ")[0])) for p in resp.presets]
                self._populate_preset_chips(pairs)
        elif isinstance(resp, EqExtendedInfoResponse):
            if resp.band_count > 0:
                if resp.band_count >= 10 and not resp.has_clear_bass:
                    self.eq_graph.configure_bands(resp.band_count, raw_max=12, center=6)
                else:
                    self.eq_graph.configure_bands(resp.band_count, raw_max=20, center=10)
        elif isinstance(resp, EqEbbResponse):
            if resp.preset is not None:
                btn = self.preset_group.button(int(resp.preset))
                if btn:
                    btn.setChecked(True)
            if resp.band_steps:
                self.eq_graph.set_steps(resp.band_steps)
        elif isinstance(resp, UpscalingParamResponse):
            self._upscaling_inq = resp.inquired_type
            self.sw_dsee.set_checked_silent(resp.enabled)
        elif isinstance(resp, UpscalingEffectResponse):
            active = "生效中" if resp.state == DseeEffectState.VALID else "待机"
            self._append_log(f"DSEE: {resp.generation.label} ({active})")
        elif isinstance(resp, ConnectionQualityResponse):
            self._conn_quality_inq = resp.inquired_type
            self.sw_ldac_priority.set_checked_silent(
                resp.mode != ConnectionQualityMode.CONNECTION_QUALITY_PRIOR
            )
        elif isinstance(resp, SpeakToChatResponse):
            self.sw_speak_to_chat.set_checked_silent(resp.enabled)
        elif isinstance(resp, GeneralSettingCapabilityResponse):
            if "MULTIPOINT" in resp.title.upper():
                self._multipoint_slot = resp.slot
                self._send(v2_table1.build_get_general_setting_param(resp.slot))
        elif isinstance(resp, GeneralSettingParamResponse):
            if resp.slot == self._multipoint_slot:
                self.sw_multipoint.set_checked_silent(resp.enabled)
        elif isinstance(resp, PlaybackVolumeResponse):
            if resp.play_type == PlayInquiredType.MUSIC_VOLUME:
                self.slider_vol.set_value_silent(max(0, min(100, resp.volume)))
        elif isinstance(resp, AutoPowerOffResponse):
            val = resp.values[0] if resp.values else 0
            label = "取下耳机自动关机 (已启用)" if val == 0x10 else f"状态码 0x{val:02X}"
            self.lbl_auto_power.setText(label)
        elif isinstance(resp, PeripheralDevicesResponse):
            names = [f"{d.name}{' [已连接]' if d.is_connected else ''}" for d in resp.devices]
            self.lbl_paired.setText("  ·  ".join(names) if names else "--")
        elif isinstance(resp, AlertNtfyParamResponse):
            self._send(v2_table1.build_reply_alert_fixed_message(resp.message_type, positive=True))
        elif isinstance(resp, CapabilityInfoResponse):
            self._append_log(f"CapabilityInfo counter={resp.counter} id={resp.identifier}")

    def _apply_nc_asm(self) -> None:
        checked_id = self.nc_group.checkedId()
        mode = NoiseControlMode(checked_id if checked_id >= 0 else int(NoiseControlMode.NOISE_CANCELLING))
        amb_mode = AmbientSoundMode.VOICE if self.sw_voice_focus.isChecked() else AmbientSoundMode.NORMAL
        self._send(
            v2_table1.build_set_noise_control_mode(
                control_mode=mode,
                ambient_level=self.slider_asm.value(),
                ambient_mode=amb_mode,
                nc_type=self._nc_type,
                noise_adaptive=self.sw_auto_ambient.isChecked(),
            )
        )

    def _on_preset_chip_clicked(self, preset_id: int) -> None:
        try:
            preset = EqPresetId(preset_id)
        except ValueError:
            return
        steps = self.eq_graph.steps() if preset in (EqPresetId.CUSTOM, EqPresetId.USER_SETTING1, EqPresetId.USER_SETTING2) else []
        self._send(v2_table1.build_set_eq_preset(preset, self._eq_type, steps))
        self._send(v2_table1.build_get_eq_ebb_param(self._eq_type))

    def _on_eq_graph_released(self, steps: list[int]) -> None:
        btn = self.preset_group.button(int(EqPresetId.CUSTOM))
        if btn:
            btn.setChecked(True)
        self._send(v2_table1.build_set_eq_preset(EqPresetId.CUSTOM, self._eq_type, steps))

    def _on_dsee_toggled(self, checked: bool) -> None:
        self._send(v2_table1.build_set_upscaling(checked, self._upscaling_inq))

    def _on_quality_toggled(self, checked: bool) -> None:
        mode = (
            ConnectionQualityMode.SOUND_QUALITY_PRIOR
            if checked
            else ConnectionQualityMode.CONNECTION_QUALITY_PRIOR
        )
        self._send(v2_table1.build_set_connection_quality(mode, self._conn_quality_inq))

    def _on_speak_to_chat_toggled(self, checked: bool) -> None:
        self._send(v2_table1.build_set_speak_to_chat_enabled(checked))

    def _on_multipoint_toggled(self, checked: bool) -> None:
        if self._multipoint_slot is not None:
            self._send(v2_table1.build_set_general_setting(self._multipoint_slot, checked))

    def _on_cinema_toggled(self, checked: bool) -> None:
        self._send(v2_table1.build_set_cinema_mode(checked))

    def _on_volume_released(self, val: int) -> None:
        self._send(v2_table1.build_set_playback_volume(val))

    def _on_power_off_clicked(self) -> None:
        if not self._is_connected:
            return
        ans = QMessageBox.question(self, "确认关机", "确定要关闭当前连接的索尼耳机电源吗？")
        if ans == QMessageBox.StandardButton.Yes:
            self._send(v2_table1.build_power_off())

    def _append_log(self, msg: str) -> None:
        self.txt_log.append(msg)

    def _on_error(self, msg: str) -> None:
        self._append_log(f"[错误] {msg}")
        self.btn_device_menu.setText("切换设备 ▾")

    def _quit_app(self) -> None:
        self._manual_disconnect = True
        self._reconnect_timer.stop()
        self._popup.close()
        self._bridge.shutdown()
        self.tray.hide()
        QApplication.quit()

    def closeEvent(self, event: QCloseEvent) -> None:
        if self.tray.isVisible():
            self.hide()
            event.ignore()
            return
        self._bridge.shutdown()
        super().closeEvent(event)
