"""Custom vector graphics, Win11 DWM dark titlebar, and interactive EQ curve widget."""

import ctypes
from ctypes import byref, c_int

from PySide6.QtCore import QPointF, QRectF, Qt, Signal
from PySide6.QtGui import (
    QColor,
    QFont,
    QLinearGradient,
    QPainter,
    QPainterPath,
    QPen,
    QPolygonF,
)
from PySide6.QtWidgets import QWidget


def enable_win11_dark_titlebar(widget: QWidget) -> None:
    """Enable Windows 11 native dark titlebar and rounded corners via DWM API."""
    try:
        hwnd = int(widget.winId())
        dwmapi = ctypes.windll.dwmapi
        # DWMWA_USE_IMMERSIVE_DARK_MODE = 20
        dark = c_int(1)
        dwmapi.DwmSetWindowAttribute(hwnd, 20, byref(dark), 4)
        # DWMWA_WINDOW_CORNER_PREFERENCE = 33 (DWMWCP_ROUND = 2)
        corner = c_int(2)
        dwmapi.DwmSetWindowAttribute(hwnd, 33, byref(corner), 4)
    except Exception:
        pass


class HeadphoneAvatar(QWidget):
    """Vector illustration of Sony WH-1000XM6 over-ear headphones."""

    def __init__(self, size: int = 52, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setFixedSize(size, size)

    def paintEvent(self, _event) -> None:  # type: ignore[override]
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        w = float(self.width())
        h = float(self.height())

        # Subtle circular backdrop (flat, design spec v1 palette)
        p.setBrush(QColor("#34373D"))
        p.setPen(QPen(QColor("#3A3D42"), 1.0))
        p.drawEllipse(QRectF(1.5, 1.5, w - 3, h - 3))

        # Headband arc
        pen_band = QPen(QColor("#d4d8e0"), w * 0.085)
        pen_band.setCapStyle(Qt.PenCapStyle.RoundCap)
        p.setPen(pen_band)
        p.setBrush(Qt.BrushStyle.NoBrush)
        arc_rect = QRectF(w * 0.20, h * 0.16, w * 0.60, h * 0.56)
        p.drawArc(arc_rect, 15 * 16, 150 * 16)

        # Yokes (stems)
        pen_stem = QPen(QColor("#9ca3af"), w * 0.045)
        pen_stem.setCapStyle(Qt.PenCapStyle.RoundCap)
        p.setPen(pen_stem)
        p.drawLine(QPointF(w * 0.21, h * 0.44), QPointF(w * 0.23, h * 0.58))
        p.drawLine(QPointF(w * 0.79, h * 0.44), QPointF(w * 0.77, h * 0.58))

        # Left and right earcups (tilted pills)
        p.setPen(Qt.PenStyle.NoPen)
        cup_grad = QLinearGradient(0, h * 0.48, 0, h * 0.84)
        cup_grad.setColorAt(0.0, QColor("#e2e6ee"))
        cup_grad.setColorAt(1.0, QColor("#aeb6c4"))

        for cx, angle in ((w * 0.26, -12.0), (w * 0.74, 12.0)):
            p.save()
            p.translate(cx, h * 0.65)
            p.rotate(angle)
            # Cushion (inner dark)
            p.setBrush(QColor("#374151"))
            idx_sign = 1.0 if angle < 0 else -1.0
            p.drawRoundedRect(
                QRectF(-w * 0.09 + idx_sign * w * 0.03, -h * 0.15, w * 0.18, h * 0.30),
                w * 0.08,
                w * 0.08,
            )
            # Outer shell
            p.setBrush(cup_grad)
            p.drawRoundedRect(QRectF(-w * 0.10, -h * 0.16, w * 0.20, h * 0.32), w * 0.09, w * 0.09)
            p.restore()


class BatteryBadge(QWidget):
    """MagicPods-style inline battery indicator: '电量 [🔋] 100%'."""

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self._percent: int | None = None
        self._codec: str = ""
        self.setFixedHeight(22)
        self.setMinimumWidth(180)

    def set_status(self, percent: int | None, codec: str = "") -> None:
        self._percent = percent
        self._codec = codec if codec and codec != "--" else ""
        self.update()

    def paintEvent(self, _event) -> None:  # type: ignore[override]
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        font = QFont("Microsoft YaHei UI", 9)
        p.setFont(font)

        # '电量' label
        p.setPen(QColor("#A9ADB3"))
        p.drawText(QRectF(0, 0, 30, 20), Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft, "电量")

        # Battery body
        bx = 32.0
        by = 4.0
        bw = 24.0
        bh = 12.0
        p.setPen(QPen(QColor("#A9ADB3"), 1.3))
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawRoundedRect(QRectF(bx, by, bw, bh), 3.0, 3.0)

        # Battery positive nub
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(QColor("#A9ADB3"))
        p.drawRoundedRect(QRectF(bx + bw + 1.5, by + 3.5, 2.2, 5.0), 1.0, 1.0)

        # Fill
        pct = self._percent
        if pct is not None:
            fill_ratio = max(0.05, min(1.0, pct / 100.0))
            fill_color = QColor("#ef4444") if pct <= 20 else QColor("#D4B16A")
            p.setBrush(fill_color)
            p.drawRoundedRect(QRectF(bx + 2.2, by + 2.2, (bw - 4.4) * fill_ratio, bh - 4.4), 1.5, 1.5)

        # Percentage text
        txt = f"{pct}%" if pct is not None else "--%"
        if self._codec:
            txt += f"  ·  {self._codec}"
        p.setPen(QColor("#FFFFFF"))
        p.drawText(QRectF(bx + bw + 8.0, 0, 140, 20), Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft, txt)


class InteractiveEqGraph(QWidget):
    """Sony Sound Connect style interactive EQ curve and draggable nodes."""

    bands_changed = Signal(list)
    bands_released = Signal(list)

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setMinimumHeight(195)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self._raw_max = 12
        self._center = 6
        self._steps: list[int] = [6] * 10
        self._labels: list[str] = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"]
        self._dragging_idx: int | None = None

    def configure_bands(self, count: int, raw_max: int, center: int, steps: list[int] | None = None) -> None:
        self._raw_max = max(2, raw_max)
        self._center = center
        if count >= 10:
            self._labels = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"]
        elif count == 5:
            self._labels = ["400", "1k", "2.5k", "6.3k", "16k"]
        else:
            self._labels = [f"B{i+1}" for i in range(count)]
        if steps and len(steps) == count:
            self._steps = [max(0, min(self._raw_max, s)) for s in steps]
        else:
            self._steps = [self._center] * count
        self.update()

    def set_steps(self, steps: list[int]) -> None:
        if len(steps) != len(self._steps):
            self.configure_bands(len(steps), self._raw_max, self._center, steps)
            return
        self._steps = [max(0, min(self._raw_max, s)) for s in steps]
        self.update()

    def steps(self) -> list[int]:
        return list(self._steps)

    def _node_positions(self) -> list[QPointF]:
        n = len(self._steps)
        if n == 0:
            return []
        w = float(self.width())
        h = float(self.height())
        pad_x = 28.0
        top_y = 28.0
        bot_y = h - 30.0
        span_w = max(10.0, w - pad_x * 2)
        span_h = max(10.0, bot_y - top_y)
        pts: list[QPointF] = []
        for i, val in enumerate(self._steps):
            x = pad_x + (span_w * i / max(1, n - 1))
            norm = val / float(self._raw_max)
            y = bot_y - norm * span_h
            pts.append(QPointF(x, y))
        return pts

    def paintEvent(self, _event) -> None:  # type: ignore[override]
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        w = float(self.width())
        h = float(self.height())
        pad_x = 28.0
        top_y = 28.0
        bot_y = h - 30.0
        mid_y = (top_y + bot_y) / 2.0

        # Background panel
        p.setBrush(QColor("#1E1F22"))
        p.setPen(QPen(QColor("#3A3D42"), 1.0))
        p.drawRoundedRect(QRectF(0.5, 0.5, w - 1, h - 1), 8.0, 8.0)

        # Horizontal reference grid lines (+max, 0, -max)
        grid_pen = QPen(QColor("#3A3D42"), 1.0, Qt.PenStyle.DashLine)
        p.setPen(grid_pen)
        for gy in (top_y, mid_y, bot_y):
            p.drawLine(QPointF(pad_x, gy), QPointF(w - pad_x, gy))

        pts = self._node_positions()
        if not pts:
            return

        # Vertical guide lines
        v_pen = QPen(QColor("#34373D"), 1.0)
        p.setPen(v_pen)
        for pt in pts:
            p.drawLine(QPointF(pt.x(), top_y), QPointF(pt.x(), bot_y))

        # Smooth Catmull-Rom / cubic curve path
        path = QPainterPath(pts[0])
        for i in range(len(pts) - 1):
            p0 = pts[i]
            p1 = pts[i + 1]
            cx = (p0.x() + p1.x()) / 2.0
            path.cubicTo(QPointF(cx, p0.y()), QPointF(cx, p1.y()), p1)

        # Curve stroke (design spec v1: flat grey line, no area fill or glow)
        curve_pen = QPen(QColor("#A9ADB3"), 1.5)
        p.setPen(curve_pen)
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawPath(path)

        # Nodes, dB labels, and frequency labels
        font_val = QFont("Microsoft YaHei UI", 8, QFont.Weight.DemiBold)
        font_freq = QFont("Microsoft YaHei UI", 8)
        for i, pt in enumerate(pts):
            diff = self._steps[i] - self._center
            val_str = f"{diff:+d}" if diff != 0 else "0"

            # Value label above top
            p.setFont(font_val)
            p.setPen(QColor("#FFFFFF") if diff != 0 else QColor("#6B7077"))
            p.drawText(QRectF(pt.x() - 18, 5, 36, 18), Qt.AlignmentFlag.AlignCenter, val_str)

            # Node circle: white stroke, dark fill, 10px diameter (spec v1)
            active = self._dragging_idx == i
            r = 6.0 if active else 5.0
            p.setPen(QPen(QColor("#FFFFFF"), 1.5))
            p.setBrush(QColor("#D4B16A") if active else QColor("#1E1F22"))
            p.drawEllipse(pt, r, r)

            # Frequency label at bottom
            p.setFont(font_freq)
            p.setPen(QColor("#6B7077"))
            lbl = self._labels[i] if i < len(self._labels) else ""
            p.drawText(QRectF(pt.x() - 20, bot_y + 6, 40, 18), Qt.AlignmentFlag.AlignCenter, lbl)

    def _update_from_pos(self, pos_x: float, pos_y: float, find_nearest: bool) -> None:
        pts = self._node_positions()
        if not pts:
            return
        if find_nearest or self._dragging_idx is None:
            best_i = min(range(len(pts)), key=lambda i: abs(pts[i].x() - pos_x))
            self._dragging_idx = best_i
        idx = self._dragging_idx
        if idx is None:
            return
        top_y = 28.0
        bot_y = float(self.height()) - 30.0
        span_h = max(10.0, bot_y - top_y)
        ratio = 1.0 - (pos_y - top_y) / span_h
        step = int(round(max(0.0, min(1.0, ratio)) * self._raw_max))
        if self._steps[idx] != step:
            self._steps[idx] = step
            self.update()
            self.bands_changed.emit(list(self._steps))

    def mousePressEvent(self, event) -> None:  # type: ignore[override]
        if event.button() == Qt.MouseButton.LeftButton:
            pos = event.position()
            self._update_from_pos(pos.x(), pos.y(), find_nearest=True)

    def mouseMoveEvent(self, event) -> None:  # type: ignore[override]
        if self._dragging_idx is not None:
            pos = event.position()
            self._update_from_pos(pos.x(), pos.y(), find_nearest=False)

    def mouseReleaseEvent(self, event) -> None:  # type: ignore[override]
        if event.button() == Qt.MouseButton.LeftButton and self._dragging_idx is not None:
            self._dragging_idx = None
            self.update()
            self.bands_released.emit(list(self._steps))
