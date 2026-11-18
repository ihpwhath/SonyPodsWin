"""MagicPods-style floating battery and quick-control popup for Windows."""

from PySide6.QtCore import Qt, QTimer, Signal
from PySide6.QtGui import QGuiApplication
from PySide6.QtWidgets import (
    QButtonGroup,
    QFrame,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from ..protocol import NoiseControlMode
from .graphics import BatteryBadge, HeadphoneAvatar


class BatteryPopup(QWidget):
    """Bottom-right floating Windows 11 style battery & noise-control popup."""

    nc_mode_selected = Signal(int)
    open_main_requested = Signal()

    def __init__(self) -> None:
        super().__init__(
            None,
            Qt.WindowType.FramelessWindowHint
            | Qt.WindowType.WindowStaysOnTopHint
            | Qt.WindowType.Tool,
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setFixedSize(350, 182)

        self._auto_hide_timer = QTimer(self)
        self._auto_hide_timer.setSingleShot(True)
        self._auto_hide_timer.timeout.connect(self.hide)

        self._build_ui()

    def _build_ui(self) -> None:
        outer = QVBoxLayout(self)
        outer.setContentsMargins(6, 6, 6, 6)

        card = QFrame()
        card.setStyleSheet(
            """
            QFrame {
                background-color: rgba(27, 31, 38, 248);
                border: 1px solid #323846;
                border-radius: 12px;
            }
            QLabel {
                border: none;
                background: transparent;
                color: #f0f3f8;
            }
            """
        )
        layout = QVBoxLayout(card)
        layout.setContentsMargins(16, 14, 16, 14)
        layout.setSpacing(12)

        # Header row: Avatar + Name + BatteryBadge + Close
        top = QHBoxLayout()
        top.setSpacing(12)
        self.avatar = HeadphoneAvatar(44)
        top.addWidget(self.avatar)

        info_col = QVBoxLayout()
        info_col.setSpacing(3)
        self.lbl_name = QLabel("WH-1000XM6")
        self.lbl_name.setStyleSheet("font-size: 16px; font-weight: 600; color: #ffffff;")
        self.bat_badge = BatteryBadge()
        info_col.addWidget(self.lbl_name)
        info_col.addWidget(self.bat_badge)
        top.addLayout(info_col, stretch=1)

        btn_close = QPushButton("×")
        btn_close.setFixedSize(24, 24)
        btn_close.setStyleSheet(
            "QPushButton { background: transparent; border: none; color: #8b949e; font-size: 16px; }"
            "QPushButton:hover { color: #ffffff; }"
        )
        btn_close.clicked.connect(self.hide)
        top.addWidget(btn_close, alignment=Qt.AlignmentFlag.AlignTop)
        layout.addLayout(top)

        # Quick NC toggle row
        nc_row = QHBoxLayout()
        nc_row.setSpacing(8)
        self._nc_group = QButtonGroup(self)
        self._nc_group.setExclusive(True)
        for label, mode in (
            ("降噪", NoiseControlMode.NOISE_CANCELLING),
            ("关闭", NoiseControlMode.OFF),
            ("环境声", NoiseControlMode.AMBIENT_SOUND),
        ):
            btn = QPushButton(label)
            btn.setCheckable(True)
            btn.setCursor(Qt.CursorShape.PointingHandCursor)
            btn.setStyleSheet(
                """
                QPushButton {
                    background-color: #242933;
                    border: 1px solid #323846;
                    border-radius: 7px;
                    padding: 7px 8px;
                    color: #cbd5e1;
                    font-size: 12px;
                    font-weight: 500;
                }
                QPushButton:hover {
                    background-color: #2c323e;
                }
                QPushButton:checked {
                    background-color: #38bdf8;
                    border-color: #38bdf8;
                    color: #0f172a;
                    font-weight: 600;
                }
                """
            )
            self._nc_group.addButton(btn, int(mode))
            nc_row.addWidget(btn)
        self._nc_group.idClicked.connect(self.nc_mode_selected.emit)
        layout.addLayout(nc_row)

        # Footer link
        btn_open = QPushButton("打开完整控制面板 ›")
        btn_open.setCursor(Qt.CursorShape.PointingHandCursor)
        btn_open.setStyleSheet(
            "QPushButton { background: transparent; border: none; color: #38bdf8; font-size: 12px; }"
            "QPushButton:hover { color: #7dd3fc; }"
        )
        btn_open.clicked.connect(self._on_open_main)
        layout.addWidget(btn_open, alignment=Qt.AlignmentFlag.AlignRight)

        outer.addWidget(card)

    def _on_open_main(self) -> None:
        self.hide()
        self.open_main_requested.emit()

    def update_state(
        self,
        model_name: str,
        battery: int | None,
        codec: str,
        nc_mode: NoiseControlMode | None,
    ) -> None:
        self.lbl_name.setText(model_name)
        self.bat_badge.set_status(battery, codec)
        if nc_mode is not None:
            btn = self._nc_group.button(int(nc_mode))
            if btn:
                btn.setChecked(True)

    def show_near_tray(self, auto_hide_ms: int = 5000) -> None:
        screen = QGuiApplication.primaryScreen()
        if screen is not None:
            geo = screen.availableGeometry()
            x = geo.right() - self.width() - 14
            y = geo.bottom() - self.height() - 14
            self.move(x, y)
        self.show()
        self.raise_()
        if auto_hide_ms > 0:
            self._auto_hide_timer.start(auto_hide_ms)

    def enterEvent(self, event) -> None:  # type: ignore[override]
        self._auto_hide_timer.stop()
        super().enterEvent(event)

    def leaveEvent(self, event) -> None:  # type: ignore[override]
        self._auto_hide_timer.start(3000)
        super().leaveEvent(event)
