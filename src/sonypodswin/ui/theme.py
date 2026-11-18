"""Sony Sound Connect style restrained dark theme (design spec v1).

Palette:
    background  #1E1F22   card        #2A2D32   secondary #34373D
    border      #3A3D42   accent      #D4B16A (champagne gold, only accent)
    text        #FFFFFF   secondary   #A9ADB3   muted     #6B7077
    slider rest #555960
Font: Microsoft YaHei UI. Radii: window/cards 12px, controls 8px,
segmented control 20px.
"""

DARK_FLUENT_QSS = """
QMainWindow, QWidget#RootWindow {
    background-color: #1E1F22;
    color: #FFFFFF;
    font-family: "Microsoft YaHei UI", "Microsoft YaHei", sans-serif;
    font-size: 14px;
}

QScrollArea {
    border: none;
    background: transparent;
}

QWidget#ScrollContent {
    background: transparent;
}

QFrame#HeaderBanner {
    background-color: #2A2D32;
    border: 1px solid #3A3D42;
    border-radius: 12px;
}

QFrame#SectionCard {
    background-color: #2A2D32;
    border: 1px solid #3A3D42;
    border-radius: 12px;
}

QFrame#RowDivider {
    background-color: #3A3D42;
    max-height: 1px;
    min-height: 1px;
    border: none;
}

QLabel#HeroTitle {
    font-size: 26px;
    font-weight: 700;
    color: #FFFFFF;
}

QLabel#SectionHeader {
    font-size: 18px;
    font-weight: 600;
    color: #FFFFFF;
    padding-bottom: 2px;
}

QLabel#RowTitle {
    font-size: 16px;
    font-weight: 500;
    color: #FFFFFF;
}

QLabel#RowDesc {
    font-size: 12px;
    color: #A9ADB3;
}

QPushButton {
    background-color: #34373D;
    border: 1px solid #3A3D42;
    border-radius: 8px;
    padding: 8px 16px;
    color: #FFFFFF;
    font-size: 14px;
}

QPushButton:hover {
    background-color: #3A3D42;
}

QPushButton:pressed {
    background-color: #2A2D32;
}

QPushButton:disabled {
    color: #6B7077;
}

QPushButton#MagicActionBtn {
    background-color: #D4B16A;
    border: 1px solid #D4B16A;
    border-radius: 8px;
    padding: 8px 18px;
    color: #1E1F22;
    font-size: 14px;
    font-weight: 600;
}

QPushButton#MagicActionBtn:hover {
    background-color: #DFC07C;
    border-color: #DFC07C;
}

QPushButton#MagicActionBtn:disabled {
    background-color: #34373D;
    border-color: #3A3D42;
    color: #6B7077;
}

QPushButton#GhostBtn {
    background-color: transparent;
    border: 1px solid #3A3D42;
    border-radius: 8px;
    padding: 8px 16px;
    color: #FFFFFF;
    font-size: 14px;
}

QPushButton#GhostBtn:hover {
    background-color: #2A2D32;
}

QPushButton#NavTabBtn {
    background-color: transparent;
    border: none;
    border-bottom: 2px solid transparent;
    border-radius: 0px;
    padding: 8px 14px;
    color: #A9ADB3;
    font-size: 14px;
    font-weight: 600;
}

QPushButton#NavTabBtn:hover {
    color: #FFFFFF;
}

QPushButton#NavTabBtn:checked {
    color: #D4B16A;
    border-bottom: 2px solid #D4B16A;
}

QPushButton#ModeCardBtn {
    background-color: #34373D;
    border: 1px solid #3A3D42;
    border-radius: 20px;
    padding: 0px;
    color: #FFFFFF;
    font-size: 14px;
    font-weight: 500;
}

QPushButton#ModeCardBtn:hover {
    background-color: #3A3D42;
}

QPushButton#ModeCardBtn[segment="first"] {
    border-top-left-radius: 20px;
    border-bottom-left-radius: 20px;
    border-top-right-radius: 0px;
    border-bottom-right-radius: 0px;
}

QPushButton#ModeCardBtn[segment="mid"] {
    border-radius: 0px;
}

QPushButton#ModeCardBtn[segment="last"] {
    border-top-right-radius: 20px;
    border-bottom-right-radius: 20px;
    border-top-left-radius: 0px;
    border-bottom-left-radius: 0px;
}

QPushButton#ModeCardBtn:checked {
    background-color: #D4B16A;
    border: 1px solid #D4B16A;
    color: #1E1F22;
    font-weight: 600;
}

QPushButton#PresetChip {
    background-color: #34373D;
    border: 1px solid #3A3D42;
    border-radius: 18px;
    min-height: 36px;
    padding: 0px 14px;
    color: #FFFFFF;
    font-size: 14px;
}

QPushButton#PresetChip:hover {
    background-color: #3A3D42;
}

QPushButton#PresetChip:checked {
    background-color: #D4B16A;
    border-color: #D4B16A;
    color: #1E1F22;
    font-weight: 600;
}

QPushButton#DangerBtn {
    background-color: #2A2D32;
    border: 1px solid #5C2B33;
    border-radius: 8px;
    padding: 8px 16px;
    color: #F0A3A3;
    font-size: 14px;
}

QPushButton#DangerBtn:hover {
    background-color: #3A2A2E;
}

QSlider::groove:horizontal {
    height: 4px;
    background: #555960;
    border-radius: 2px;
}

QSlider::sub-page:horizontal {
    background: #D4B16A;
    border-radius: 2px;
}

QSlider::handle:horizontal {
    background: #D4B16A;
    border: none;
    width: 14px;
    height: 14px;
    margin: -5px 0;
    border-radius: 7px;
}

QSlider::handle:horizontal:hover {
    background: #DFC07C;
}

QTextEdit#LogBox {
    background-color: #1E1F22;
    border: 1px solid #3A3D42;
    border-radius: 8px;
    color: #A9ADB3;
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
    background: #3A3D42;
    border-radius: 3px;
    min-height: 24px;
}

QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {
    height: 0px;
}
"""
