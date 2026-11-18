"""Windows 11 dark Fluent theme stylesheet."""

DARK_FLUENT_QSS = """
QMainWindow, QWidget#RootWindow {
    background-color: #181a1f;
    color: #f3f4f6;
    font-family: "Microsoft YaHei UI", "Segoe UI", sans-serif;
    font-size: 13px;
}

QScrollArea {
    border: none;
    background: transparent;
}

QWidget#ScrollContent {
    background: transparent;
}

QFrame#Card {
    background-color: #21242b;
    border: 1px solid #2e323c;
    border-radius: 10px;
}

QFrame#HeaderCard {
    background-color: #242830;
    border: 1px solid #323744;
    border-radius: 12px;
}

QLabel#TitleLabel {
    font-size: 18px;
    font-weight: 600;
    color: #ffffff;
}

QLabel#SubtitleLabel {
    font-size: 12px;
    color: #9ca3af;
}

QLabel#SectionTitle {
    font-size: 14px;
    font-weight: 600;
    color: #e5e7eb;
}

QLabel#BadgeLabel {
    background-color: #2b313d;
    border: 1px solid #3b4252;
    border-radius: 6px;
    padding: 3px 8px;
    font-size: 12px;
    color: #93c5fd;
    font-weight: 600;
}

QPushButton {
    background-color: #2c303a;
    border: 1px solid #3c4250;
    border-radius: 6px;
    padding: 6px 14px;
    color: #f3f4f6;
    font-weight: 500;
}

QPushButton:hover {
    background-color: #353a46;
    border-color: #4b5263;
}

QPushButton:pressed {
    background-color: #252932;
}

QPushButton#PrimaryButton {
    background-color: #1a8cff;
    border: 1px solid #3399ff;
    color: #ffffff;
    font-weight: 600;
}

QPushButton#PrimaryButton:hover {
    background-color: #2b96ff;
}

QPushButton#DangerButton {
    background-color: #3a2326;
    border: 1px solid #6b3036;
    color: #fca5a5;
}

QPushButton#DangerButton:hover {
    background-color: #4c2a2e;
}

QPushButton#SegmentedBtn {
    background-color: #262a33;
    border: 1px solid #343946;
    border-radius: 6px;
    padding: 8px 12px;
    color: #cbd5e1;
    font-weight: 500;
}

QPushButton#SegmentedBtn:checked {
    background-color: #1a8cff;
    border-color: #3399ff;
    color: #ffffff;
    font-weight: 600;
}

QComboBox {
    background-color: #262a33;
    border: 1px solid #3a404d;
    border-radius: 6px;
    padding: 5px 10px;
    color: #f3f4f6;
    min-width: 150px;
}

QComboBox::drop-down {
    border: none;
    width: 24px;
}

QComboBox QAbstractItemView {
    background-color: #21242b;
    border: 1px solid #3a404d;
    selection-background-color: #1a8cff;
    color: #f3f4f6;
}

QSlider::groove:horizontal {
    height: 4px;
    background: #374151;
    border-radius: 2px;
}

QSlider::sub-page:horizontal {
    background: #1a8cff;
    border-radius: 2px;
}

QSlider::handle:horizontal {
    background: #60b0ff;
    border: 2px solid #ffffff;
    width: 14px;
    height: 14px;
    margin: -6px 0;
    border-radius: 8px;
}

QSlider::groove:vertical {
    width: 4px;
    background: #374151;
    border-radius: 2px;
}

QSlider::add-page:vertical {
    background: #1a8cff;
    border-radius: 2px;
}

QSlider::handle:vertical {
    background: #60b0ff;
    border: 2px solid #ffffff;
    width: 14px;
    height: 14px;
    margin: 0 -6px;
    border-radius: 8px;
}

QTextEdit#LogBox {
    background-color: #131519;
    border: 1px solid #2a2e38;
    border-radius: 6px;
    color: #9ca3af;
    font-family: "Consolas", "Cascadia Mono", monospace;
    font-size: 11px;
}

QScrollBar:vertical {
    background: transparent;
    width: 8px;
    margin: 2px;
}

QScrollBar::handle:vertical {
    background: #3b404d;
    border-radius: 4px;
    min-height: 24px;
}

QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {
    height: 0px;
}
"""
