"""Custom widgets for SonyPodsWin (design spec v1 styling)."""

from PySide6.QtCore import Property, QEasingCurve, QPropertyAnimation, QRectF, Qt, Signal
from PySide6.QtGui import QColor, QPainter, QPen
from PySide6.QtWidgets import (
    QAbstractButton,
    QFrame,
    QHBoxLayout,
    QLabel,
    QSlider,
    QVBoxLayout,
    QWidget,
)


class _PillSwitch(QAbstractButton):
    toggled_by_user = Signal(bool)

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setCheckable(True)
        self.setFixedSize(48, 26)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self._thumb_x = 3.0
        self._anim = QPropertyAnimation(self, b"thumbX", self)
        self._anim.setDuration(130)
        self._anim.setEasingCurve(QEasingCurve.Type.OutCubic)
        self.toggled.connect(self._start_anim)

    def get_thumb_x(self) -> float:
        return self._thumb_x

    def set_thumb_x(self, val: float) -> None:
        self._thumb_x = val
        self.update()

    thumbX = Property(float, get_thumb_x, set_thumb_x)

    def _start_anim(self, checked: bool) -> None:
        self._anim.stop()
        self._anim.setStartValue(self._thumb_x)
        self._anim.setEndValue(25.0 if checked else 3.0)
        self._anim.start()

    def set_checked_silent(self, checked: bool) -> None:
        self.blockSignals(True)
        self.setChecked(checked)
        self._thumb_x = 25.0 if checked else 3.0
        self.blockSignals(False)
        self.update()

    def mouseReleaseEvent(self, e) -> None:  # type: ignore[override]
        super().mouseReleaseEvent(e)
        if e.button() == Qt.MouseButton.LeftButton:
            self.toggled_by_user.emit(self.isChecked())

    def paintEvent(self, _e) -> None:  # type: ignore[override]
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        checked = self.isChecked()
        if checked:
            p.setBrush(QColor("#D4B16A"))
            p.setPen(QPen(QColor("#D4B16A"), 1.2))
        else:
            p.setBrush(QColor("#34373D"))
            p.setPen(QPen(QColor("#3A3D42"), 1.2))
        p.drawRoundedRect(QRectF(1.0, 1.0, self.width() - 2.0, self.height() - 2.0), 12.0, 12.0)

        thumb_color = QColor("#FFFFFF") if checked else QColor("#A9ADB3")
        p.setBrush(thumb_color)
        p.setPen(Qt.PenStyle.NoPen)
        p.drawEllipse(QRectF(self._thumb_x, 3.0, 20.0, 20.0))


class ToggleSwitch(QWidget):
    """WinUI 3 ToggleSwitch with '开 / 关' label on the left of the pill."""

    toggled_by_user = Signal(bool)

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(8)
        self._lbl = QLabel("关")
        self._lbl.setStyleSheet("color: #FFFFFF; font-size: 14px;")
        self._pill = _PillSwitch()
        self._pill.toggled.connect(lambda c: self._lbl.setText("开" if c else "关"))
        self._pill.toggled_by_user.connect(self.toggled_by_user.emit)
        layout.addWidget(self._lbl)
        layout.addWidget(self._pill)

    def isChecked(self) -> bool:
        return self._pill.isChecked()

    def setChecked(self, checked: bool) -> None:
        self._pill.setChecked(checked)
        self._lbl.setText("开" if checked else "关")

    def set_checked_silent(self, checked: bool) -> None:
        self._pill.set_checked_silent(checked)
        self._lbl.setText("开" if checked else "关")


class MagicSlider(QWidget):
    """MagicPods-style slider with percentage/value label on its left: '54% [===o---]'."""

    value_released = Signal(int)
    value_changed = Signal(int)

    def __init__(
        self,
        min_val: int = 0,
        max_val: int = 100,
        initial: int = 50,
        suffix: str = "%",
        width: int = 230,
    ) -> None:
        super().__init__()
        self._suffix = suffix
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(10)

        self.lbl_val = QLabel(f"{initial}{suffix}")
        self.lbl_val.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        self.lbl_val.setFixedWidth(64)
        self.lbl_val.setStyleSheet("color: #FFFFFF; font-size: 18px; font-weight: 500;")

        self.slider = QSlider(Qt.Orientation.Horizontal)
        self.slider.setRange(min_val, max_val)
        self.slider.setValue(initial)
        self.slider.setFixedWidth(width)
        self.slider.valueChanged.connect(self._on_changed)
        self.slider.sliderReleased.connect(lambda: self.value_released.emit(self.slider.value()))

        layout.addWidget(self.lbl_val)
        layout.addWidget(self.slider)

    def _on_changed(self, val: int) -> None:
        self.lbl_val.setText(f"{val}{self._suffix}")
        self.value_changed.emit(val)

    def value(self) -> int:
        return self.slider.value()

    def set_value_silent(self, val: int) -> None:
        self.slider.blockSignals(True)
        self.slider.setValue(val)
        self.lbl_val.setText(f"{self.slider.value()}{self._suffix}")
        self.slider.blockSignals(False)


class SettingRow(QWidget):
    """MagicPods-exact setting row with generous spacing, title, description, and right control."""

    def __init__(self, title: str, description: str = "", control: QWidget | None = None) -> None:
        super().__init__()
        layout = QHBoxLayout(self)
        layout.setContentsMargins(4, 10, 4, 10)
        layout.setSpacing(20)

        text_col = QVBoxLayout()
        text_col.setContentsMargins(0, 0, 0, 0)
        text_col.setSpacing(4)
        lbl_title = QLabel(title)
        lbl_title.setObjectName("RowTitle")
        text_col.addWidget(lbl_title)
        if description:
            lbl_desc = QLabel(description)
            lbl_desc.setObjectName("RowDesc")
            lbl_desc.setWordWrap(True)
            text_col.addWidget(lbl_desc)

        layout.addLayout(text_col, stretch=1)
        if control is not None:
            layout.addWidget(control, alignment=Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)


class CardFrame(QFrame):
    """Clean rounded section card."""

    def __init__(self, title: str = "") -> None:
        super().__init__()
        self.setObjectName("SectionCard")
        self.body = QVBoxLayout(self)
        self.body.setContentsMargins(20, 16, 20, 16)
        self.body.setSpacing(8)
        if title:
            lbl = QLabel(title)
            lbl.setObjectName("SectionHeader")
            self.body.addWidget(lbl)

    def add_divider(self) -> None:
        div = QFrame()
        div.setObjectName("RowDivider")
        self.body.addWidget(div)


class _RingCanvas(QWidget):
    """Thin circular progress ring: 42px diameter, 3px stroke (design spec v1)."""

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setFixedSize(42, 42)
        self._percent: int | None = None

    def set_percent(self, percent: int | None) -> None:
        self._percent = percent
        self.update()

    def paintEvent(self, _event) -> None:  # type: ignore[override]
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        rect = QRectF(1.5, 1.5, 39.0, 39.0)
        track = QPen(QColor("#3A3D42"), 3.0)
        track.setCapStyle(Qt.PenCapStyle.RoundCap)
        p.setPen(track)
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawEllipse(rect)
        if self._percent is not None:
            pct = max(0, min(100, self._percent))
            arc = QPen(QColor("#D4B16A"), 3.0)
            arc.setCapStyle(Qt.PenCapStyle.RoundCap)
            p.setPen(arc)
            # Start at 12 o'clock (90 deg in Qt arc units), sweep clockwise.
            p.drawArc(rect, 90 * 16, -int(360 * 16 * pct / 100))


class BatteryRing(QWidget):
    """Header battery display per design spec v1: '电量' + thin ring + big number + meta.

    Drop-in replacement for the header BatteryBadge: same set_status(percent, meta)
    calling convention; the meta string (codec / firmware) is rendered verbatim
    as secondary text.
    """

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(10)

        self._lbl_caption = QLabel("电量")
        self._lbl_caption.setStyleSheet("color: #A9ADB3; font-size: 14px;")
        layout.addWidget(self._lbl_caption)

        self._ring = _RingCanvas()
        layout.addWidget(self._ring)

        self._lbl_value = QLabel("--%")
        self._lbl_value.setStyleSheet("color: #FFFFFF; font-size: 20px; font-weight: 600;")
        layout.addWidget(self._lbl_value)

        self._lbl_meta = QLabel("")
        self._lbl_meta.setStyleSheet("color: #A9ADB3; font-size: 14px;")
        layout.addWidget(self._lbl_meta)
        layout.addStretch(1)

    def set_status(self, percent: int | None, meta: str = "") -> None:
        self._ring.set_percent(percent)
        self._lbl_value.setText(f"{percent}%" if percent is not None else "--%")
        meta = meta.strip()
        self._lbl_meta.setText(f"·  {meta}" if meta else "")
