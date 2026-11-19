using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using SonyPodsWin.Protocol;

namespace SonyPodsWin.Services;

/// <summary>
/// 基于原生 Win32 Shell_NotifyIconW 的系统托盘服务（零额外 NuGet 依赖，完全兼容非打包单文件 WinUI 3）。
/// 逐项对齐 parity-checklist.md §4.1 与低电量托盘提醒：
/// - 程序绘制 64×64 图标：透明底、香槟金 #D4B16A 圆角方块（半径 14）+ 深色粗体 "S"
/// - 托盘右键菜单 5 项 + 2 条分隔线，文案与顺序一字不差
/// - 单击弹电量弹窗、双击打开主窗口、支持动态更新 tooltip 与系统低电量 Warning 通知
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint WmTrayCallback = WmApp + 1;
    private const uint WmCommand = 0x0111;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NiifWarning = 0x00000002;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const uint CmdOpenMain = 1001;
    private const uint CmdModeAnc = 1002;
    private const uint CmdModeAsm = 1003;
    private const uint CmdModeOff = 1004;
    private const uint CmdExit = 1005;

    private static readonly IntPtr HwndMessage = new(-3);

    private readonly DispatcherQueue _dispatcher;
    private readonly WndProcDelegate _wndProcDelegate;
    private readonly string _windowClassName;
    private readonly IntPtr _hWnd;
    private readonly IntPtr _hIcon;
    private readonly uint _taskbarCreatedMsg;

    private string _currentTip = string.Empty;
    private bool _iconAdded;
    private bool _disposed;

    public bool IsVisible => _iconAdded && !_disposed;

    /// <summary>生成的 64×64 图标句柄，可供主窗口通过 WM_SETICON 复用。</summary>
    public IntPtr IconHandle => _hIcon;

    public event EventHandler? TrayLeftClicked;
    public event EventHandler? TrayLeftDoubleClicked;
    public event EventHandler? OpenMainRequested;
    public event EventHandler<NoiseControlMode>? QuickNcModeSelected;
    public event EventHandler? ExitRequested;

    public TrayIconService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _wndProcDelegate = WndProc;
        _windowClassName = $"SonyPodsWinTrayHost_{Guid.NewGuid():N}";
        _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandleW(null),
            lpszClassName = _windowClassName,
        };

        RegisterClassExW(ref wc);
        _hWnd = CreateWindowExW(
            0,
            _windowClassName,
            "SonyPodsWinTrayHost",
            0,
            0,
            0,
            0,
            0,
            HwndMessage,
            IntPtr.Zero,
            wc.hInstance,
            IntPtr.Zero);

        _hIcon = CreateGoldSIcon();
        AddTrayIcon();
    }

    public void UpdateTooltip(string tooltip)
    {
        if (_disposed || !_iconAdded)
        {
            return;
        }

        _currentTip = tooltip ?? string.Empty;
        NOTIFYICONDATAW data = CreateBaseData();
        data.uFlags = NifTip;
        data.szTip = Truncate(_currentTip, 127);
        Shell_NotifyIconW(NimModify, ref data);
    }

    /// <summary>
    /// 通过系统托盘气泡/通知中心弹出低电量 Warning 提醒（等价于 Python tray.showMessage）。
    /// </summary>
    public void ShowWarningNotification(string title, string message, uint timeoutMs = 5000)
    {
        if (_disposed || !_iconAdded)
        {
            return;
        }

        NOTIFYICONDATAW data = CreateBaseData();
        data.uFlags = NifInfo;
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(message, 255);
        data.dwInfoFlags = NiifWarning;
        data.uTimeoutOrVersion = timeoutMs;
        Shell_NotifyIconW(NimModify, ref data);
    }

    private void AddTrayIcon()
    {
        if (_hWnd == IntPtr.Zero)
        {
            return;
        }

        NOTIFYICONDATAW data = CreateBaseData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = WmTrayCallback;
        data.hIcon = _hIcon;
        data.szTip = Truncate(_currentTip, 127);
        _iconAdded = Shell_NotifyIconW(NimAdd, ref data);
    }

    private NOTIFYICONDATAW CreateBaseData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hWnd,
        uID = 1,
    };

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmTrayCallback)
        {
            uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == WmLButtonUp)
            {
                _dispatcher.TryEnqueue(() => TrayLeftClicked?.Invoke(this, EventArgs.Empty));
            }
            else if (mouseMsg == WmLButtonDblClk)
            {
                _dispatcher.TryEnqueue(() => TrayLeftDoubleClicked?.Invoke(this, EventArgs.Empty));
            }
            else if (mouseMsg == WmRButtonUp || mouseMsg == WmContextMenu)
            {
                ShowContextMenu();
            }

            return IntPtr.Zero;
        }

        if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
        {
            AddTrayIcon();
            return IntPtr.Zero;
        }

        if (msg == WmCommand)
        {
            uint cmdId = (uint)(wParam.ToInt64() & 0xFFFF);
            DispatchMenuCommand(cmdId);
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // 严格按 parity-checklist.md §4.1 顺序与文案构建 5 项 + 2 分隔线
            AppendMenuW(hMenu, MfString, CmdOpenMain, "打开主面板");
            AppendMenuW(hMenu, MfSeparator, 0, null);
            AppendMenuW(hMenu, MfString, CmdModeAnc, "切换：降噪模式");
            AppendMenuW(hMenu, MfString, CmdModeAsm, "切换：环境声模式");
            AppendMenuW(hMenu, MfString, CmdModeOff, "切换：关闭降噪");
            AppendMenuW(hMenu, MfSeparator, 0, null);
            AppendMenuW(hMenu, MfString, CmdExit, "退出程序");

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hWnd);
            uint selected = TrackPopupMenu(hMenu, TpmRightButton | TpmReturnCmd, pt.X, pt.Y, 0, _hWnd, IntPtr.Zero);
            if (selected != 0)
            {
                DispatchMenuCommand(selected);
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private void DispatchMenuCommand(uint cmdId)
    {
        switch (cmdId)
        {
            case CmdOpenMain:
                _dispatcher.TryEnqueue(() => OpenMainRequested?.Invoke(this, EventArgs.Empty));
                break;
            case CmdModeAnc:
                _dispatcher.TryEnqueue(() => QuickNcModeSelected?.Invoke(this, NoiseControlMode.NoiseCancelling));
                break;
            case CmdModeAsm:
                _dispatcher.TryEnqueue(() => QuickNcModeSelected?.Invoke(this, NoiseControlMode.AmbientSound));
                break;
            case CmdModeOff:
                _dispatcher.TryEnqueue(() => QuickNcModeSelected?.Invoke(this, NoiseControlMode.Off));
                break;
            case CmdExit:
                _dispatcher.TryEnqueue(() => ExitRequested?.Invoke(this, EventArgs.Empty));
                break;
        }
    }

    /// <summary>
    /// 程序绘制 64×64 32 位 BGRA 托盘图标：透明底、金色圆角方块（#D4B16A，半径 14）+ 深色粗体 "S"。
    /// </summary>
    private static IntPtr CreateGoldSIcon()
    {
        const int size = 64;
        const int radius = 14;

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size, // top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            },
        };

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr colorBmp = CreateDIBSection(memDc, ref bmi, 0, out IntPtr bitsPtr, IntPtr.Zero, 0);
        IntPtr oldBmp = SelectObject(memDc, colorBmp);

        try
        {
            // 1. 先在 DIB 缓冲里用 GDI 渲染白色背景上的深色 "S"，再合成精确的圆角 Alpha 通道
            byte[] pixels = new byte[size * size * 4];
            FillRoundedRect(pixels, size, radius, 0xD4, 0xB1, 0x6A, 0xFF);
            Marshal.Copy(pixels, 0, bitsPtr, pixels.Length);

            IntPtr font = CreateFontW(
                -42, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI");
            IntPtr oldFont = SelectObject(memDc, font);
            SetBkMode(memDc, 1); // TRANSPARENT
            SetTextColor(memDc, 0x00221F1E); // COLORREF = 0x00BBGGRR (#1E1F22)

            var rect = new RECT { Left = 0, Top = 0, Right = size, Bottom = size };
            DrawTextW(memDc, "S", 1, ref rect, 0x00000001 | 0x00000004 | 0x00000020); // DT_CENTER | DT_VCENTER | DT_SINGLELINE

            SelectObject(memDc, oldFont);
            DeleteObject(font);

            // 修复 GDI DrawText 覆盖字形像素 Alpha 为 0 的问题：对圆角内部像素恢复不透明 Alpha
            Marshal.Copy(bitsPtr, pixels, 0, pixels.Length);
            RestoreRoundedRectAlpha(pixels, size, radius);
            Marshal.Copy(pixels, 0, bitsPtr, pixels.Length);

            IntPtr maskBmp = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
            var iconInfo = new ICONINFO
            {
                fIcon = true,
                hbmColor = colorBmp,
                hbmMask = maskBmp,
            };

            IntPtr hIcon = CreateIconIndirect(ref iconInfo);
            DeleteObject(maskBmp);
            return hIcon;
        }
        finally
        {
            SelectObject(memDc, oldBmp);
            DeleteObject(colorBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static void FillRoundedRect(byte[] bgra, int size, int r, byte red, byte green, byte blue, byte alpha)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (!IsInsideRoundedRect(x, y, size, r))
                {
                    continue;
                }

                int idx = (y * size + x) * 4;
                bgra[idx + 0] = blue;
                bgra[idx + 1] = green;
                bgra[idx + 2] = red;
                bgra[idx + 3] = alpha;
            }
        }
    }

    private static void RestoreRoundedRectAlpha(byte[] bgra, int size, int r)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                bgra[idx + 3] = IsInsideRoundedRect(x, y, size, r) ? (byte)0xFF : (byte)0x00;
            }
        }
    }

    private static bool IsInsideRoundedRect(int x, int y, int size, int r)
    {
        int cx = x < r ? r : (x >= size - r ? size - r - 1 : x);
        int cy = y < r ? r : (y >= size - r ? size - r - 1 : y);
        int dx = x - cx;
        int dy = y - cy;
        return dx * dx + dy * dy <= r * r;
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_iconAdded)
        {
            NOTIFYICONDATAW data = CreateBaseData();
            Shell_NotifyIconW(NimDelete, ref data);
            _iconAdded = false;
        }

        if (_hWnd != IntPtr.Zero)
        {
            DestroyWindow(_hWnd);
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
        }

        UnregisterClassW(_windowClassName, GetModuleHandleW(null));
    }

    // ── Win32 Interop ─────────────────────────────────────────────────────────

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, IntPtr lpBits);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(
        int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
        uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet,
        uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint color);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
