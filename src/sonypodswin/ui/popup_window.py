"""MagicPods-style floating battery and quick-control popup for Windows."""

from PySide6.QtCore import Qt, QTimer, Signal
from PySide6.QtGui import QGuiApplication
from PySide6.QtWidgets import (
    QButtonGroup,
    QFrame,
    QHBoxLayout,
    QLabel,
    QProgressBar,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from ..protocol import NoiseControlMode


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
        self.setFixedSize(330, 178)

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
                background-color: rgba(28, 31, 38, 246);
                border: 1px solid #3a404e;
                border-radius: 14px;
            }
            QLabel {
                border: none;
                background: transparent;
                color: #f3f4f6;
            }
            """
        )
        layout = QVBoxLayout(card)
        layout.setContentsMargins(16, 14, 16, 14)
        layout.setSpacing(10)

        # Header row
        top = QHBoxLayout()
        self.lbl_name = QLabel("WH-1000XM6")
        self.lbl_name.setStyleSheet("font-size: 15px; font-weight: 600; color: #ffffff;")
        self.lbl_codec = QLabel("--")
        self.lbl_codec.setStyleSheet(
            "background-color: #2b313d; border-radius: 5px; padding: 2px 7px; font-size: 11px; color: #93c5fd;"
        )
        btn_close = QPushButton("×")
        btn_close.setFixedSize(22, 22)
        btn_close.setStyleSheet(
            "QPushButton { background: transparent; border: none; color: #9ca3af; font-size: 15px; }"
            "QPushButton:hover { color: #ffffff; }"
        )
        btn_close.clicked.connect(self.hide)
        top.addWidget(self.lbl_name, stretch=1)
        top.addWidget(self.lbl_codec)
        top.addWidget(btn_close)
        layout.addLayout(top)

        # Battery row
        bat_row = QHBoxLayout()
        self.bar_battery = QProgressBar()
        self.bar_battery.setRange(0, 100)
        self.bar_battery.setValue(0)
        self.bar_battery.setTextVisible(False)
        self.bar_battery.setFixedHeight(8)
        self.bar_battery.setStyleSheet(
            """
            QProgressBar {
                background-color: #2d323e;
                border: none;
                border-radius: 4px;
            }
            QProgressBar::chunk {
                background-color: #22c55e;
                border-radius: 4px;
            }
            """
        )
        self.lbl_pct = QLabel("--%")
        self.lbl_pct.setStyleSheet("font-size: 13px; font-weight: 600; color: #e5e7eb;")
        bat_row.addWidget(self.bar_battery, stretch=1)
        bat_row.addWidget(self.lbl_pct)
        layout.addLayout(bat_row)

        # Quick NC toggle row
        nc_row = QHBoxLayout()
        nc_row.setSpacing(6)
        self._nc_group = QButtonGroup(self)
        self._nc_group.setExclusive(True)
        for label, mode in (
            ("降噪", NoiseControlMode.NOISE_CANCELLING),
            ("关闭", NoiseControlMode.OFF),
            ("环境声", NoiseControlMode.AMBIENT_SOUND),
        ):
            btn = QPushButton(label)
            btn.setCheckable(True)
            btn.setStyleSheet(
                """
                QPushButton {
                    background-color: #262a33;
                    border: 1px solid #353b48;
                    border-radius: 7px;
                    padding: 6px 8px;
                    color: #cbd5e1;
                    font-size: 12px;
                }
                QPushButton:checked {
                    background-color: #1a8cff;
                    border-color: #3399ff;
                    color: #ffffff;
                    font-weight: 600;
                }
                """
            )
            self._nc_group.addButton(btn, int(mode))
            nc_row.addWidget(btn)
        self._nc_group.idClicked.connect(self.nc_mode_selected.emit)
        layout.addLayout(nc_row)

        # Footer link
        btn_open = QPushButton("打开完整控制面板")
        btn_open.setStyleSheet(
            "QPushButton { background: transparent; border: none; color: #60a5fa; font-size: 12px; text-align: right; }"
            "QPushButton:hover { color: #93c5fd; text-decoration: underline; }"
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
        self.lbl_codec.setText(codec)
        if battery is not None:
            self.bar_battery.setValue(battery)
            self.lbl_pct.setText(f"{battery}%")
            color = "#ef4444" if battery <= 20 else "#22c55e"
            self.bar_battery.setStyleSheet(
                f"QProgressBar {{ background-color: #2d323e; border: none; border-radius: 4px; }}"
                f"QProgressBar::chunk {{ background-color: {color}; border-radius: 4px; }}"
            )
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
