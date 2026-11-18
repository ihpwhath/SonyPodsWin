"""Custom Fluent-style widgets for SonyPodsWin."""

from PySide6.QtCore import Property, QEasingCurve, QPropertyAnimation, QRectF, Qt, Signal
from PySide6.QtGui import QColor, QPainter
from PySide6.QtWidgets import QAbstractButton, QFrame, QHBoxLayout, QLabel, QVBoxLayout, QWidget


class ToggleSwitch(QAbstractButton):
    """Windows 11 style animated pill toggle switch."""

    toggled_by_user = Signal(bool)

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setCheckable(True)
        self.setFixedSize(44, 22)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self._thumb_x = 3.0
        self._anim = QPropertyAnimation(self, b"thumbX", self)
        self._anim.setDuration(140)
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
        self._anim.setEndValue(24.0 if checked else 3.0)
        self._anim.start()

    def set_checked_silent(self, checked: bool) -> None:
        self.blockSignals(True)
        self.setChecked(checked)
        self._thumb_x = 24.0 if checked else 3.0
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
        bg = QColor("#1a8cff") if checked else QColor("#2a2e37")
        border = QColor("#3399ff") if checked else QColor("#6b7280")
        p.setBrush(bg)
        p.setPen(border)
        p.drawRoundedRect(QRectF(0.5, 0.5, self.width() - 1, self.height() - 1), 10.5, 10.5)

        thumb_color = QColor("#111317") if checked else QColor("#d1d5db")
        p.setBrush(thumb_color)
        p.setPen(Qt.PenStyle.NoPen)
        p.drawEllipse(QRectF(self._thumb_x, 3.0, 16.0, 16.0))


class SettingRow(QWidget):
    """A single setting row with title, optional description, and right-side control."""

    def __init__(self, title: str, description: str = "", control: QWidget | None = None) -> None:
        super().__init__()
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 4, 0, 4)
        layout.setSpacing(12)

        text_col = QVBoxLayout()
        text_col.setContentsMargins(0, 0, 0, 0)
        text_col.setSpacing(2)
        lbl_title = QLabel(title)
        lbl_title.setStyleSheet("font-size: 13px; font-weight: 500; color: #f3f4f6;")
        text_col.addWidget(lbl_title)
        if description:
            lbl_desc = QLabel(description)
            lbl_desc.setObjectName("SubtitleLabel")
            lbl_desc.setWordWrap(True)
            text_col.addWidget(lbl_desc)

        layout.addLayout(text_col, stretch=1)
        if control is not None:
            layout.addWidget(control, alignment=Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)


class CardFrame(QFrame):
    """Grouped card container with title."""

    def __init__(self, title: str = "") -> None:
        super().__init__()
        self.setObjectName("Card")
        self.body = QVBoxLayout(self)
        self.body.setContentsMargins(16, 14, 16, 14)
        self.body.setSpacing(10)
        if title:
            lbl = QLabel(title)
            lbl.setObjectName("SectionTitle")
            self.body.addWidget(lbl)
