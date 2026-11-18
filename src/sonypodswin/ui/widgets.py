"""WinUI 3 / MagicPods-style widgets for SonyPodsWin."""

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
        self.setFixedSize(40, 20)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self._thumb_x = 4.0
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
        self._anim.setEndValue(22.0 if checked else 4.0)
        self._anim.start()

    def set_checked_silent(self, checked: bool) -> None:
        self.blockSignals(True)
        self.setChecked(checked)
        self._thumb_x = 22.0 if checked else 4.0
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
            p.setBrush(QColor("#38bdf8"))
            p.setPen(QPen(QColor("#38bdf8"), 1.2))
        else:
            p.setBrush(QColor("#1b1f26"))
            p.setPen(QPen(QColor("#8b949e"), 1.3))
        p.drawRoundedRect(QRectF(1.0, 1.0, self.width() - 2.0, self.height() - 2.0), 9.0, 9.0)

        thumb_color = QColor("#0f172a") if checked else QColor("#c9d1d9")
        p.setBrush(thumb_color)
        p.setPen(Qt.PenStyle.NoPen)
        p.drawEllipse(QRectF(self._thumb_x, 4.0, 12.0, 12.0))


class ToggleSwitch(QWidget):
    """WinUI 3 ToggleSwitch with '开 / 关' label on the left of the pill."""

    toggled_by_user = Signal(bool)

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(8)
        self._lbl = QLabel("关")
        self._lbl.setStyleSheet("color: #d1d5db; font-size: 13px;")
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
        self.lbl_val.setFixedWidth(42)
        self.lbl_val.setStyleSheet("color: #e5e7eb; font-size: 13px;")

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
        self.body.setContentsMargins(18, 14, 18, 14)
        self.body.setSpacing(6)
        if title:
            lbl = QLabel(title)
            lbl.setObjectName("SectionHeader")
            self.body.addWidget(lbl)

    def add_divider(self) -> None:
        div = QFrame()
        div.setObjectName("RowDivider")
        self.body.addWidget(div)
