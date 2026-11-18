"""Windows 11 WinUI 3 / MagicPods dark theme stylesheet."""

DARK_FLUENT_QSS = """
QMainWindow, QWidget#RootWindow {
    background-color: #181b20;
    color: #f0f3f8;
    font-family: "Microsoft YaHei UI", "Segoe UI Variable Display", "Segoe UI", sans-serif;
    font-size: 13px;
}

QScrollArea {
    border: none;
    background: transparent;
}

QWidget#ScrollContent {
    background: transparent;
}

QFrame#HeaderBanner {
    background-color: #1f232b;
    border: 1px solid #2b303b;
    border-radius: 10px;
}

QFrame#SectionCard {
    background-color: #1d2128;
    border: 1px solid #282d37;
    border-radius: 10px;
}

QFrame#RowDivider {
    background-color: #252a34;
    max-height: 1px;
    min-height: 1px;
    border: none;
}

QLabel#HeroTitle {
    font-size: 18px;
    font-weight: 600;
    color: #ffffff;
}

QLabel#SectionHeader {
    font-size: 13px;
    font-weight: 600;
    color: #93c5fd;
    padding-bottom: 2px;
}

QLabel#RowTitle {
    font-size: 14px;
    font-weight: 500;
    color: #f0f3f8;
}

QLabel#RowDesc {
    font-size: 12px;
    color: #8b949e;
}

QPushButton {
    background-color: #262b35;
    border: 1px solid #343b48;
    border-radius: 6px;
    padding: 6px 14px;
    color: #e5e7eb;
    font-size: 13px;
}

QPushButton:hover {
    background-color: #2f3542;
    border-color: #454e5f;
}

QPushButton:pressed {
    background-color: #1f242d;
}

QPushButton#MagicActionBtn {
    background-color: #38bdf8;
    border: 1px solid #38bdf8;
    border-radius: 5px;
    padding: 6px 16px;
    color: #0f172a;
    font-size: 13px;
    font-weight: 600;
}

QPushButton#MagicActionBtn:hover {
    background-color: #56ccf2;
    border-color: #56ccf2;
}

QPushButton#NavTabBtn {
    background-color: transparent;
    border: none;
    border-bottom: 2px solid transparent;
    border-radius: 0px;
    padding: 8px 14px;
    color: #8b949e;
    font-size: 13px;
    font-weight: 600;
}

QPushButton#NavTabBtn:hover {
    color: #d1d5db;
}

QPushButton#NavTabBtn:checked {
    color: #38bdf8;
    border-bottom: 2px solid #38bdf8;
}

QPushButton#ModeCardBtn {
    background-color: #232832;
    border: 1px solid #303643;
    border-radius: 8px;
    padding: 11px 14px;
    color: #cbd5e1;
    font-size: 13px;
    font-weight: 500;
}

QPushButton#ModeCardBtn:hover {
    background-color: #2a303c;
    border-color: #3d4556;
}

QPushButton#ModeCardBtn:checked {
    background-color: #132f4c;
    border: 1px solid #38bdf8;
    color: #38bdf8;
    font-weight: 600;
}

QPushButton#PresetChip {
    background-color: #232832;
    border: 1px solid #303643;
    border-radius: 13px;
    padding: 4px 12px;
    color: #9ca3af;
    font-size: 12px;
}

QPushButton#PresetChip:hover {
    background-color: #2b313d;
    color: #e5e7eb;
}

QPushButton#PresetChip:checked {
    background-color: #38bdf8;
    border-color: #38bdf8;
    color: #0f172a;
    font-weight: 600;
}

QPushButton#DangerBtn {
    background-color: #2d1d22;
    border: 1px solid #5c2b33;
    border-radius: 6px;
    padding: 6px 14px;
    color: #fca5a5;
}

QPushButton#DangerBtn:hover {
    background-color: #3d242b;
}

QSlider::groove:horizontal {
    height: 4px;
    background: #6b7280;
    border-radius: 2px;
}

QSlider::sub-page:horizontal {
    background: #38bdf8;
    border-radius: 2px;
}

QSlider::handle:horizontal {
    background: #38bdf8;
    border: 4px solid #2b303c;
    width: 12px;
    height: 12px;
    margin: -8px 0;
    border-radius: 10px;
}

QSlider::handle:horizontal:hover {
    border-color: #374151;
}

QTextEdit#LogBox {
    background-color: #12151a;
    border: 1px solid #252a34;
    border-radius: 8px;
    color: #9ca3af;
    font-family: "Cascadia Mono", "Consolas", monospace;
    font-size: 11px;
    padding: 6px;
}

QScrollBar:vertical {
    background: transparent;
    width: 6px;
    margin: 2px;
}

QScrollBar::handle:vertical {
    background: #323846;
    border-radius: 3px;
    min-height: 24px;
}

QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {
    height: 0px;
}
"""
