"""Main Fluent-style window and BLE controller bridge for SonyPodsWin."""

import asyncio
import json
import threading
from pathlib import Path

from PySide6.QtCore import QObject, Qt, QTimer, Signal
from PySide6.QtGui import QAction, QCloseEvent, QColor, QIcon, QPainter, QPixmap
from PySide6.QtWidgets import (
    QApplication,
    QButtonGroup,
    QComboBox,
    QFrame,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QMessageBox,
    QPushButton,
    QScrollArea,
    QSlider,
    QSystemTrayIcon,
    QTextEdit,
    QVBoxLayout,
    QWidget,
    QMenu,
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
    AUDIO_INQ_CONNECTION_MODE,
    AUDIO_INQ_CONNECTION_MODE_WITH_LDAC,
    AUDIO_INQ_UPSCALING,
    AUDIO_INQ_UPSCALING_WITH_REASON,
    AlertNtfyParamResponse,
    AudioCodecResponse,
    AutoPowerOffResponse,
    BatteryResponse,
    CapabilityInfoResponse,
    ConnectionQualityResponse,
    DeviceInfoResponse,
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
from .popup_window import BatteryPopup
from .widgets import CardFrame, SettingRow, ToggleSwitch

CONFIG_PATH = Path.home() / ".sonypodswin.json"

DEFAULT_PRESETS = [
    EqPresetId.OFF,
    EqPresetId.EXCITED,
    EqPresetId.MELLOW,
    EqPresetId.RELAXED,
    EqPresetId.VOCAL,
    EqPresetId.TREBLE,
    EqPresetId.BASS,
    EqPresetId.SPEECH,
    EqPresetId.ROCK,
    EqPresetId.POP,
    EqPresetId.JAZZ,
    EqPresetId.HEAVY,
    EqPresetId.CLEAR,
    EqPresetId.CUSTOM,
]


def _make_tray_icon() -> QIcon:
    pm = QPixmap(64, 64)
    pm.fill(Qt.GlobalColor.transparent)
    p = QPainter(pm)
    p.setRenderHint(QPainter.RenderHint.Antialiasing)
    p.setBrush(QColor("#1a8cff"))
    p.setPen(Qt.PenStyle.NoPen)
    p.drawRoundedRect(4, 4, 56, 56, 14, 14)
    p.setPen(QColor("#ffffff"))
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
                devs = await SonyBleTandemClient.scan_devices(timeout=4.0)
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
        self.setWindowTitle("SonyPods for Windows")
        self.setMinimumSize(620, 760)
        self.resize(660, 840)

        self._bridge = BleBridge()
        self._bridge.devices_found.connect(self._on_devices_found)
        self._bridge.connected.connect(self._on_connected)
        self._bridge.disconnected.connect(self._on_disconnected)
        self._bridge.response_received.connect(self._on_response)
        self._bridge.log_message.connect(self._append_log)
        self._bridge.error_message.connect(self._on_error)

        self._is_connected = False
        self._manual_disconnect = False
        self._current_address: str | None = None
        self._current_battery: int | None = None
        self._current_codec: str = "--"
        self._current_nc_mode: NoiseControlMode | None = None
        self._low_battery_notified = False
        self._nc_type = NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS_NA
        self._eq_type = EqEbbInquiredType.PRESET_EQ_AND_ERRORCODE
        self._upscaling_inq = AUDIO_INQ_UPSCALING_WITH_REASON
        self._conn_quality_inq = AUDIO_INQ_CONNECTION_MODE_WITH_LDAC
        self._multipoint_slot: int | None = None
        self._eq_sliders: list[QSlider] = []
        self._eq_value_labels: list[QLabel] = []
        self._band_count = 5
        self._eq_raw_max = 20
        self._eq_center = 10

        self._popup = BatteryPopup()
        self._popup.nc_mode_selected.connect(lambda m: self._quick_nc(NoiseControlMode(m)))
        self._popup.open_main_requested.connect(self._show_main_window)

        self._reconnect_timer = QTimer(self)
        self._reconnect_timer.setInterval(8000)
        self._reconnect_timer.timeout.connect(self._on_reconnect_tick)

        self._build_ui()
        self._setup_tray()
        self._load_config_and_scan()

    def _build_ui(self) -> None:
        root = QWidget()
        root.setObjectName("RootWindow")
        self.setCentralWidget(root)
        main_layout = QVBoxLayout(root)
        main_layout.setContentsMargins(16, 16, 16, 16)
        main_layout.setSpacing(12)

        # ── Header Card ────────────────────────────────────────────────────
        header = QFrame()
        header.setObjectName("HeaderCard")
        h_layout = QVBoxLayout(header)
        h_layout.setContentsMargins(16, 14, 16, 14)
        h_layout.setSpacing(10)

        top_row = QHBoxLayout()
        info_col = QVBoxLayout()
        info_col.setSpacing(4)
        self.lbl_model = QLabel("未连接 Sony 耳机")
        self.lbl_model.setObjectName("TitleLabel")
        self.lbl_fw = QLabel("点击右侧“扫描”以发现附近的索尼蓝牙设备")
        self.lbl_fw.setObjectName("SubtitleLabel")
        info_col.addWidget(self.lbl_model)
        info_col.addWidget(self.lbl_fw)
        top_row.addLayout(info_col, stretch=1)

        self.lbl_codec = QLabel("--")
        self.lbl_codec.setObjectName("BadgeLabel")
        self.lbl_battery = QLabel("电量 --%")
        self.lbl_battery.setObjectName("BadgeLabel")
        top_row.addWidget(self.lbl_codec)
        top_row.addWidget(self.lbl_battery)
        h_layout.addLayout(top_row)

        conn_row = QHBoxLayout()
        self.combo_devices = QComboBox()
        self.combo_devices.setMinimumWidth(260)
        self.btn_scan = QPushButton("扫描设备")
        self.btn_scan.clicked.connect(self._on_scan_clicked)
        self.btn_connect = QPushButton("连接")
        self.btn_connect.setObjectName("PrimaryButton")
        self.btn_connect.clicked.connect(self._on_connect_clicked)
        conn_row.addWidget(self.combo_devices, stretch=1)
        conn_row.addWidget(self.btn_scan)
        conn_row.addWidget(self.btn_connect)
        h_layout.addLayout(conn_row)
        main_layout.addWidget(header)

        # ── Scroll Area ────────────────────────────────────────────────────
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll_content = QWidget()
        scroll_content.setObjectName("ScrollContent")
        self.cards_layout = QVBoxLayout(scroll_content)
        self.cards_layout.setContentsMargins(0, 0, 4, 0)
        self.cards_layout.setSpacing(12)

        self._build_nc_card()
        self._build_sound_card()
        self._build_smart_card()
        self._build_prefs_card()
        self._build_log_card()

        self.cards_layout.addStretch(1)
        scroll.setWidget(scroll_content)
        main_layout.addWidget(scroll, stretch=1)

    def _build_nc_card(self) -> None:
        card = CardFrame("环境声音控制")
        seg_row = QHBoxLayout()
        seg_row.setSpacing(8)
        self.nc_group = QButtonGroup(self)
        self.nc_group.setExclusive(True)

        self.btn_nc = QPushButton("降噪")
        self.btn_off = QPushButton("关闭")
        self.btn_asm = QPushButton("环境声")
        for idx, (btn, mode) in enumerate(
            [
                (self.btn_nc, NoiseControlMode.NOISE_CANCELLING),
                (self.btn_off, NoiseControlMode.OFF),
                (self.btn_asm, NoiseControlMode.AMBIENT_SOUND),
            ]
        ):
            btn.setObjectName("SegmentedBtn")
            btn.setCheckable(True)
            self.nc_group.addButton(btn, int(mode))
            seg_row.addWidget(btn)
        self.btn_nc.setChecked(True)
        self.nc_group.idClicked.connect(lambda _id: self._apply_nc_asm())
        card.body.addLayout(seg_row)

        # Ambient level slider
        level_box = QWidget()
        level_layout = QHBoxLayout(level_box)
        level_layout.setContentsMargins(0, 0, 0, 0)
        self.lbl_asm_level = QLabel("10")
        self.lbl_asm_level.setFixedWidth(28)
        self.slider_asm = QSlider(Qt.Orientation.Horizontal)
        self.slider_asm.setRange(1, 20)
        self.slider_asm.setValue(10)
        self.slider_asm.valueChanged.connect(lambda v: self.lbl_asm_level.setText(str(v)))
        self.slider_asm.sliderReleased.connect(self._apply_nc_asm)
        level_layout.addWidget(self.lbl_asm_level)
        level_layout.addWidget(self.slider_asm)
        card.body.addWidget(SettingRow("环境声等级", "调节通透模式下的外部声音传入量 (1 - 20)", level_box))

        self.sw_voice_focus = ToggleSwitch()
        self.sw_voice_focus.toggled_by_user.connect(lambda _v: self._apply_nc_asm())
        card.body.addWidget(SettingRow("关注语音", "抑制背景噪音的同时保留人声", self.sw_voice_focus))

        self.sw_auto_ambient = ToggleSwitch()
        self.sw_auto_ambient.toggled_by_user.connect(lambda _v: self._apply_nc_asm())
        card.body.addWidget(SettingRow("自适应环境声", "根据环境噪音自动调整环境声等级", self.sw_auto_ambient))

        self.cards_layout.addWidget(card)

    def _build_sound_card(self) -> None:
        card = CardFrame("声音与均衡器 (EQ)")

        preset_row = QHBoxLayout()
        self.combo_eq = QComboBox()
        for p in DEFAULT_PRESETS:
            self.combo_eq.addItem(p.label, p)
        self.combo_eq.currentIndexChanged.connect(self._on_eq_preset_changed)
        btn_apply_eq = QPushButton("应用 EQ")
        btn_apply_eq.clicked.connect(self._apply_eq_custom)
        preset_row.addWidget(self.combo_eq, stretch=1)
        preset_row.addWidget(btn_apply_eq)
        card.body.addWidget(SettingRow("均衡器预设", "选择预设或调节下方频段后点击应用", self._wrap_layout(preset_row)))

        # EQ Sliders container
        self.eq_bands_widget = QWidget()
        self.eq_bands_layout = QHBoxLayout(self.eq_bands_widget)
        self.eq_bands_layout.setContentsMargins(0, 6, 0, 6)
        self.eq_bands_layout.setSpacing(10)
        self._rebuild_eq_sliders(5)
        card.body.addWidget(self.eq_bands_widget)

        # DSEE
        self.sw_dsee = ToggleSwitch()
        self.sw_dsee.toggled_by_user.connect(self._on_dsee_toggled)
        self.lbl_dsee_desc = "AI 实时提升压缩音源音质"
        card.body.addWidget(SettingRow("DSEE Extreme", self.lbl_dsee_desc, self.sw_dsee))

        # Connection quality
        self.combo_quality = QComboBox()
        self.combo_quality.addItem("声音质量优先", ConnectionQualityMode.SOUND_QUALITY_PRIOR)
        self.combo_quality.addItem("稳定连接优先", ConnectionQualityMode.CONNECTION_QUALITY_PRIOR)
        self.combo_quality.currentIndexChanged.connect(self._on_quality_changed)
        card.body.addWidget(SettingRow("蓝牙连接质量", "切换音质优先或连接稳定性优先", self.combo_quality))

        self.cards_layout.addWidget(card)

    def _build_smart_card(self) -> None:
        card = CardFrame("智能与系统控制")

        self.sw_speak_to_chat = ToggleSwitch()
        self.sw_speak_to_chat.toggled_by_user.connect(self._on_speak_to_chat_toggled)
        card.body.addWidget(SettingRow("智能免摘对话 (Speak-to-Chat)", "检测到你说话时自动暂停音乐并开启环境声", self.sw_speak_to_chat))

        self.sw_multipoint = ToggleSwitch()
        self.sw_multipoint.toggled_by_user.connect(self._on_multipoint_toggled)
        card.body.addWidget(SettingRow("同时连接 2 台设备 (多点连接)", "允许耳机同时保持两台蓝牙设备的连接", self.sw_multipoint))

        self.sw_cinema = ToggleSwitch()
        self.sw_cinema.toggled_by_user.connect(self._on_cinema_toggled)
        card.body.addWidget(SettingRow("影院模式 (Cinema)", "开启空间影院声场效果", self.sw_cinema))

        self.lbl_auto_power = QLabel("未探测")
        self.lbl_auto_power.setObjectName("SubtitleLabel")
        card.body.addWidget(SettingRow("自动关机 (Auto Power Off)", "耳机当前自动关机策略", self.lbl_auto_power))

        self.lbl_paired = QLabel("--")
        self.lbl_paired.setObjectName("SubtitleLabel")
        self.lbl_paired.setWordWrap(True)
        card.body.addWidget(SettingRow("多点已配对设备", "耳机记忆的蓝牙设备列表", self.lbl_paired))

        # Volume
        vol_box = QWidget()
        vol_layout = QHBoxLayout(vol_box)
        vol_layout.setContentsMargins(0, 0, 0, 0)
        self.lbl_vol = QLabel("50")
        self.lbl_vol.setFixedWidth(28)
        self.slider_vol = QSlider(Qt.Orientation.Horizontal)
        self.slider_vol.setRange(0, 100)
        self.slider_vol.setValue(50)
        self.slider_vol.valueChanged.connect(lambda v: self.lbl_vol.setText(str(v)))
        self.slider_vol.sliderReleased.connect(self._on_volume_released)
        vol_layout.addWidget(self.lbl_vol)
        vol_layout.addWidget(self.slider_vol)
        card.body.addWidget(SettingRow("耳机音量", "同步调节耳机端媒体音量", vol_box))

        # Playback controls + Power Off
        btn_row = QHBoxLayout()
        btn_prev = QPushButton("上一曲")
        btn_play = QPushButton("播放")
        btn_pause = QPushButton("暂停")
        btn_next = QPushButton("下一曲")
        btn_power = QPushButton("关闭耳机电源")
        btn_power.setObjectName("DangerButton")

        btn_prev.clicked.connect(lambda: self._send(v2_table1.build_playback(PlaybackControl.TRACK_DOWN)))
        btn_play.clicked.connect(lambda: self._send(v2_table1.build_playback(PlaybackControl.PLAY)))
        btn_pause.clicked.connect(lambda: self._send(v2_table1.build_playback(PlaybackControl.PAUSE)))
        btn_next.clicked.connect(lambda: self._send(v2_table1.build_playback(PlaybackControl.TRACK_UP)))
        btn_power.clicked.connect(self._on_power_off_clicked)

        for b in (btn_prev, btn_play, btn_pause, btn_next):
            btn_row.addWidget(b)
        btn_row.addStretch(1)
        btn_row.addWidget(btn_power)
        card.body.addLayout(btn_row)

        self.cards_layout.addWidget(card)

    def _build_prefs_card(self) -> None:
        card = CardFrame("桌面体验与偏好设置")

        self.sw_popup_on_connect = ToggleSwitch()
        self.sw_popup_on_connect.set_checked_silent(True)
        self.sw_popup_on_connect.toggled_by_user.connect(lambda _v: self._save_config())
        card.body.addWidget(SettingRow("连接时显示电量弹窗", "连接耳机后在右下角弹出快捷控制浮窗", self.sw_popup_on_connect))

        self.sw_auto_reconnect = ToggleSwitch()
        self.sw_auto_reconnect.set_checked_silent(True)
        self.sw_auto_reconnect.toggled_by_user.connect(lambda _v: self._save_config())
        card.body.addWidget(SettingRow("异常断线自动重连", "非手动断开时自动尝试重新连接耳机", self.sw_auto_reconnect))

        low_box = QWidget()
        low_layout = QHBoxLayout(low_box)
        low_layout.setContentsMargins(0, 0, 0, 0)
        self.lbl_low_bat = QLabel("20%")
        self.lbl_low_bat.setFixedWidth(36)
        self.slider_low_bat = QSlider(Qt.Orientation.Horizontal)
        self.slider_low_bat.setRange(5, 50)
        self.slider_low_bat.setValue(20)
        self.slider_low_bat.valueChanged.connect(lambda v: self.lbl_low_bat.setText(f"{v}%"))
        self.slider_low_bat.sliderReleased.connect(self._save_config)
        low_layout.addWidget(self.lbl_low_bat)
        low_layout.addWidget(self.slider_low_bat)
        card.body.addWidget(SettingRow("低电量提醒阈值", "耳机电量低于指定值时发送系统通知", low_box))

        self.cards_layout.addWidget(card)

    def _build_log_card(self) -> None:
        card = CardFrame("协议调试日志")
        self.txt_log = QTextEdit()
        self.txt_log.setObjectName("LogBox")
        self.txt_log.setReadOnly(True)
        self.txt_log.setFixedHeight(130)
        card.body.addWidget(self.txt_log)
        self.cards_layout.addWidget(card)

    def _rebuild_eq_sliders(self, count: int, raw_max: int | None = None, center: int | None = None) -> None:
        while self.eq_bands_layout.count():
            item = self.eq_bands_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        self._eq_sliders.clear()
        self._eq_value_labels.clear()
        self._band_count = count
        if raw_max is not None:
            self._eq_raw_max = raw_max
        if center is not None:
            self._eq_center = center

        labels_5 = ["400", "1k", "2.5k", "6.3k", "16k"]
        labels_10 = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"]
        names = labels_10 if count >= 10 else (labels_5 if count == 5 else [f"B{i+1}" for i in range(count)])

        for i in range(count):
            col = QWidget()
            v = QVBoxLayout(col)
            v.setContentsMargins(0, 0, 0, 0)
            v.setSpacing(4)
            lbl_val = QLabel("0")
            lbl_val.setAlignment(Qt.AlignmentFlag.AlignCenter)
            lbl_val.setStyleSheet("font-size: 11px; color: #93c5fd;")
            sld = QSlider(Qt.Orientation.Vertical)
            sld.setRange(0, self._eq_raw_max)
            sld.setValue(self._eq_center)
            sld.setFixedHeight(100)
            c = self._eq_center
            sld.valueChanged.connect(lambda val, lbl=lbl_val, ctr=c: lbl.setText(f"{val - ctr:+d}" if val != ctr else "0"))
            lbl_freq = QLabel(names[i] if i < len(names) else f"B{i+1}")
            lbl_freq.setAlignment(Qt.AlignmentFlag.AlignCenter)
            lbl_freq.setObjectName("SubtitleLabel")
            v.addWidget(lbl_val)
            v.addWidget(sld, alignment=Qt.AlignmentFlag.AlignHCenter)
            v.addWidget(lbl_freq)
            self.eq_bands_layout.addWidget(col)
            self._eq_sliders.append(sld)
            self._eq_value_labels.append(lbl_val)

    @staticmethod
    def _wrap_layout(layout: QHBoxLayout) -> QWidget:
        w = QWidget()
        layout.setContentsMargins(0, 0, 0, 0)
        w.setLayout(layout)
        return w

    def _setup_tray(self) -> None:
        self.tray = QSystemTrayIcon(_make_tray_icon(), self)
        menu = QMenu()
        act_show = QAction("显示主窗口", self)
        act_show.triggered.connect(self.showNormal)
        act_nc = QAction("降噪模式", self)
        act_nc.triggered.connect(lambda: self._quick_nc(NoiseControlMode.NOISE_CANCELLING))
        act_asm = QAction("环境声模式", self)
        act_asm.triggered.connect(lambda: self._quick_nc(NoiseControlMode.AMBIENT_SOUND))
        act_off = QAction("关闭降噪", self)
        act_off.triggered.connect(lambda: self._quick_nc(NoiseControlMode.OFF))
        act_quit = QAction("退出", self)
        act_quit.triggered.connect(self._quit_app)
        menu.addActions([act_show])
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

    def _sync_popup_state(self) -> None:
        self._popup.update_state(
            model_name=self.lbl_model.text(),
            battery=self._current_battery,
            codec=self._current_codec,
            nc_mode=self._current_nc_mode,
        )

    def _quick_nc(self, mode: NoiseControlMode) -> None:
        btn = self.nc_group.button(int(mode))
        if btn:
            btn.setChecked(True)
        self._apply_nc_asm()

    def _load_config_and_scan(self) -> None:
        last_addr = None
        if CONFIG_PATH.exists():
            try:
                cfg = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
                last_addr = cfg.get("last_address")
                last_name = cfg.get("last_name", last_addr)
                if last_addr:
                    self.combo_devices.addItem(f"{last_name} ({last_addr})", last_addr)
                self.sw_popup_on_connect.set_checked_silent(bool(cfg.get("show_popup_on_connect", True)))
                self.sw_auto_reconnect.set_checked_silent(bool(cfg.get("auto_reconnect", True)))
                low_thr = int(cfg.get("low_battery_threshold", 20))
                self.slider_low_bat.setValue(max(5, min(50, low_thr)))
                self.lbl_low_bat.setText(f"{self.slider_low_bat.value()}%")
            except Exception:
                pass
        self._on_scan_clicked()
        if last_addr:
            self._append_log(f"已加载上次设备: {last_addr}，正在自动连接...")
            self._current_address = last_addr
            self._bridge.connect_device(last_addr)

    def _save_config(self) -> None:
        try:
            name = self.combo_devices.currentText().split(" (")[0] if self.combo_devices.count() else ""
            data = {
                "last_name": name,
                "last_address": self._current_address or self.combo_devices.currentData(),
                "show_popup_on_connect": self.sw_popup_on_connect.isChecked(),
                "auto_reconnect": self.sw_auto_reconnect.isChecked(),
                "low_battery_threshold": self.slider_low_bat.value(),
            }
            CONFIG_PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        except Exception:
            pass

    def _on_scan_clicked(self) -> None:
        self.btn_scan.setEnabled(False)
        self.btn_scan.setText("扫描中...")
        self._bridge.scan()

    def _on_devices_found(self, devices: list[DiscoveredDevice]) -> None:
        self.btn_scan.setEnabled(True)
        self.btn_scan.setText("扫描设备")
        cur_addr = self.combo_devices.currentData()
        self.combo_devices.clear()
        for d in devices:
            self.combo_devices.addItem(f"{d.name} ({d.address})", d.address)
        if cur_addr:
            idx = self.combo_devices.findData(cur_addr)
            if idx >= 0:
                self.combo_devices.setCurrentIndex(idx)
        self._append_log(f"扫描完成，发现 {len(devices)} 台 Sony 设备")

    def _on_connect_clicked(self) -> None:
        if self._is_connected:
            self._manual_disconnect = True
            self._reconnect_timer.stop()
            self._bridge.disconnect_device()
            return
        addr = self.combo_devices.currentData()
        if not addr:
            QMessageBox.information(self, "提示", "请先扫描并选择一台 Sony 耳机。")
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
        self.lbl_fw.setText(f"已连接 ({address})，正在同步耳机状态...")
        self._save_config()
        self._query_initial_state()
        if self.sw_popup_on_connect.isChecked():
            QTimer.singleShot(900, self._show_connect_popup)

    def _show_connect_popup(self) -> None:
        if self._is_connected:
            self._sync_popup_state()
            self._popup.show_near_tray()

    def _on_disconnected(self) -> None:
        self._is_connected = False
        self.btn_connect.setEnabled(True)
        self.btn_connect.setText("连接")
        self.lbl_fw.setText("已断开连接")
        if not self._manual_disconnect and self._current_address and self.sw_auto_reconnect.isChecked():
            self._append_log("检测到异常断开，8 秒后尝试自动重连...")
            self._reconnect_timer.start()

    def _on_reconnect_tick(self) -> None:
        if self._is_connected or self._manual_disconnect or not self._current_address:
            self._reconnect_timer.stop()
            return
        self._append_log(f"正在尝试自动重连 {self._current_address}...")
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
            v2_table1.build_get_eq_ebb_extended_info(self._eq_type),
            v2_table1.build_get_eq_ebb_param(self._eq_type),
            v2_table1.build_get_upscaling(self._upscaling_inq),
            v2_table1.build_get_connection_quality(self._conn_quality_inq),
            v2_table1.build_get_speak_to_chat_param(),
            v2_table1.build_get_cinema_mode(),
            v2_table1.build_get_auto_power_off(0x05),
            v2_table1.build_get_auto_power_off(0x04),
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
                self.lbl_model.setText(resp.text)
            elif resp.info_type == DeviceInfoType.FW_VERSION and resp.text:
                self.lbl_fw.setText(f"固件版本 {resp.text} · 已连接")
        elif isinstance(resp, BatteryResponse):
            if resp.levels and resp.levels[0] is not None:
                pct = resp.levels[0]
                self._current_battery = pct
                self.lbl_battery.setText(f"电量 {pct}%")
                self.tray.setToolTip(f"{self.lbl_model.text()} · 电量 {pct}%")
                self._sync_popup_state()
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
            self.lbl_codec.setText(self._current_codec)
            self._sync_popup_state()
        elif isinstance(resp, SupportFunctionResponse):
            codes = {code for code, _ in resp.functions}
            if 0x6D in codes:
                self._nc_type = NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS_NA
            elif 0x68 in codes:
                self._nc_type = NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS
            elif 0x67 in codes:
                self._nc_type = NcAsmInquiredType.MODE_NC_ASM_AUTO_NC_MODE_SWITCH_AND_ASM_SEAMLESS
            if 0x57 in codes:
                self._eq_type = EqEbbInquiredType.PRESET_EQ_AND_ERRORCODE
            elif 0x51 in codes:
                self._eq_type = EqEbbInquiredType.PRESET_EQ
            self._append_log(f"功能探测完成: {len(codes)} 项特性")
        elif isinstance(resp, NoiseControlResponse):
            if resp.control_mode is not None:
                self._current_nc_mode = resp.control_mode
                btn = self.nc_group.button(int(resp.control_mode))
                if btn:
                    btn.setChecked(True)
                self._sync_popup_state()
            if resp.ambient_level is not None:
                self.slider_asm.blockSignals(True)
                self.slider_asm.setValue(max(1, min(20, resp.ambient_level)))
                self.lbl_asm_level.setText(str(self.slider_asm.value()))
                self.slider_asm.blockSignals(False)
            if resp.ambient_mode is not None:
                self.sw_voice_focus.set_checked_silent(resp.ambient_mode == AmbientSoundMode.VOICE)
            if resp.noise_adaptive is not None:
                self.sw_auto_ambient.set_checked_silent(resp.noise_adaptive)
        elif isinstance(resp, EqExtendedInfoResponse):
            if resp.band_count > 0:
                if resp.band_count >= 10 and not resp.has_clear_bass:
                    self._rebuild_eq_sliders(resp.band_count, raw_max=12, center=6)
                else:
                    self._rebuild_eq_sliders(resp.band_count, raw_max=20, center=10)
        elif isinstance(resp, EqEbbResponse):
            if resp.preset is not None:
                idx = self.combo_eq.findData(resp.preset)
                if idx >= 0:
                    self.combo_eq.blockSignals(True)
                    self.combo_eq.setCurrentIndex(idx)
                    self.combo_eq.blockSignals(False)
            if resp.band_steps:
                if len(resp.band_steps) != self._band_count:
                    self._rebuild_eq_sliders(len(resp.band_steps))
                for i, step in enumerate(resp.band_steps):
                    if i < len(self._eq_sliders):
                        self._eq_sliders[i].blockSignals(True)
                        self._eq_sliders[i].setValue(max(0, min(self._eq_raw_max, step)))
                        diff = step - self._eq_center
                        self._eq_value_labels[i].setText(f"{diff:+d}" if diff != 0 else "0")
                        self._eq_sliders[i].blockSignals(False)
        elif isinstance(resp, UpscalingParamResponse):
            self._upscaling_inq = resp.inquired_type
            self.sw_dsee.set_checked_silent(resp.enabled)
        elif isinstance(resp, UpscalingEffectResponse):
            active = "生效中" if resp.state == DseeEffectState.VALID else "待机"
            self._append_log(f"DSEE: {resp.generation.label} ({active})")
        elif isinstance(resp, ConnectionQualityResponse):
            self._conn_quality_inq = resp.inquired_type
            idx = self.combo_quality.findData(resp.mode)
            if idx >= 0:
                self.combo_quality.blockSignals(True)
                self.combo_quality.setCurrentIndex(idx)
                self.combo_quality.blockSignals(False)
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
                self.slider_vol.blockSignals(True)
                self.slider_vol.setValue(max(0, min(100, resp.volume)))
                self.lbl_vol.setText(str(resp.volume))
                self.slider_vol.blockSignals(False)
        elif isinstance(resp, AutoPowerOffResponse):
            hex_vals = " ".join(f"{v:02X}" for v in resp.values)
            self.lbl_auto_power.setText(f"type=0x{resp.inquired_type:02X}, val=[{hex_vals}]")
            self._append_log(f"自动关机探测: type=0x{resp.inquired_type:02X} raw={resp.raw.hex(' ')}")
        elif isinstance(resp, PeripheralDevicesResponse):
            names = [f"{d.name}{'(当前)' if d.is_connected else ''}" for d in resp.devices]
            self.lbl_paired.setText(" / ".join(names) if names else "--")
        elif isinstance(resp, AlertNtfyParamResponse):
            self._append_log(f"收到设备确认请求: inq=0x{resp.inquired_type:02X}, msg={resp.message_type}，自动确认")
            self._send(v2_table1.build_reply_alert_fixed_message(resp.message_type, positive=True))
        elif isinstance(resp, CapabilityInfoResponse):
            self._append_log(f"CapabilityInfo: counter={resp.counter} id={resp.identifier}")

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

    def _on_eq_preset_changed(self) -> None:
        preset = self.combo_eq.currentData()
        if isinstance(preset, EqPresetId):
            steps = [s.value() for s in self._eq_sliders] if preset == EqPresetId.CUSTOM else []
            self._send(v2_table1.build_set_eq_preset(preset, self._eq_type, steps))
            self._send(v2_table1.build_get_eq_ebb_param(self._eq_type))

    def _apply_eq_custom(self) -> None:
        steps = [s.value() for s in self._eq_sliders]
        idx = self.combo_eq.findData(EqPresetId.CUSTOM)
        if idx >= 0:
            self.combo_eq.blockSignals(True)
            self.combo_eq.setCurrentIndex(idx)
            self.combo_eq.blockSignals(False)
        self._send(v2_table1.build_set_eq_preset(EqPresetId.CUSTOM, self._eq_type, steps))

    def _on_dsee_toggled(self, checked: bool) -> None:
        self._send(v2_table1.build_set_upscaling(checked, self._upscaling_inq))

    def _on_quality_changed(self) -> None:
        mode = self.combo_quality.currentData()
        if isinstance(mode, ConnectionQualityMode):
            self._send(v2_table1.build_set_connection_quality(mode, self._conn_quality_inq))

    def _on_speak_to_chat_toggled(self, checked: bool) -> None:
        self._send(v2_table1.build_set_speak_to_chat_enabled(checked))

    def _on_multipoint_toggled(self, checked: bool) -> None:
        if self._multipoint_slot is None:
            self._append_log("未探测到多点连接设置槽位")
            return
        self._send(v2_table1.build_set_general_setting(self._multipoint_slot, checked))

    def _on_cinema_toggled(self, checked: bool) -> None:
        self._send(v2_table1.build_set_cinema_mode(checked))

    def _on_volume_released(self) -> None:
        self._send(v2_table1.build_set_playback_volume(self.slider_vol.value()))

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
        self.btn_scan.setEnabled(True)
        self.btn_scan.setText("扫描设备")

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
