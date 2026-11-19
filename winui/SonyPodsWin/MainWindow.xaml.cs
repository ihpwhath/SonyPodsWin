using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SonyPodsWin.ViewModels;
using SonyPodsWin.Views;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace SonyPodsWin;

public sealed partial class MainWindow : Window
{
    private const uint WmSetIcon = 0x0080;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmWindowPosChanging = 0x0046;
    private const uint WmDpiChanged = 0x02E0;
    private const uint SwpNosize = 0x0001;
    private const int MinWindowWidthDips = 980;
    private const int MinWindowHeightDips = 600;
    private const int DefaultWindowWidthDips = 1020;
    private const int DefaultWindowHeightDips = 640;

    private static readonly IntPtr IconSmall = IntPtr.Zero;
    private static readonly IntPtr IconBig = new(1);

    private delegate IntPtr WndProcDelegate(
        IntPtr hWnd,
        uint uMsg,
        IntPtr wParam,
        IntPtr lParam);

    private readonly AppStateViewModel _state;
    private readonly WndProcDelegate _customWndProc;
    private IntPtr _origWndProc;
    private bool _allowClose;

    /// <summary>托盘可见时关窗默认隐藏到托盘驻留（对应 parity-checklist.md §0 / §4.1）。</summary>
    public bool HideToTrayOnClose { get; set; } = true;

    public MainWindow(AppStateViewModel state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _customWndProc = OnWindowMessage;
        InitializeComponent();

        Title = "SonyPods";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureTitleBarColors();
        _state.AttachDispatcher(DispatcherQueue);
        _state.PropertyChanged += OnStatePropertyChanged;

        ConfigureWindowSizing();
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnAppWindowClosing;

        // 默认选中首屏「降噪与音质」
        if (NavView.MenuItems.FirstOrDefault() is NavigationViewItem firstItem)
        {
            NavView.SelectedItem = firstItem;
            NavigateByTag(firstItem.Tag?.ToString());
        }
    }

    public void SetWindowIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero)
        {
            return;
        }

        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        SendMessageW(hWnd, WmSetIcon, IconSmall, hIcon);
        SendMessageW(hWnd, WmSetIcon, IconBig, hIcon);
    }

    public void RestoreAndActivate()
    {
        AppWindow.Show(true);
        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        Activate();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void ConfigureTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Color.FromArgb(0xFF, 0xEA, 0xEC, 0xEF);
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x8A, 0x90, 0x9B);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        titleBar.ButtonHoverForegroundColor = Colors.White;
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
        titleBar.ButtonPressedForegroundColor = Colors.White;
    }

    private void ConfigureWindowSizing()
    {
        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        uint dpi = hWnd != IntPtr.Zero ? GetDpiForWindow(hWnd) : 96u;
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;

        if (hWnd != IntPtr.Zero && _origWndProc == IntPtr.Zero)
        {
            _origWndProc = SetWindowLongPtrW(hWnd, -4, Marshal.GetFunctionPointerForDelegate(_customWndProc));
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)Math.Round(MinWindowWidthDips * scale);
            presenter.PreferredMinimumHeight = (int)Math.Round(MinWindowHeightDips * scale);
        }

        AppWindow.Resize(new SizeInt32(
            (int)Math.Round(DefaultWindowWidthDips * scale),
            (int)Math.Round(DefaultWindowHeightDips * scale)));
    }

    private IntPtr OnWindowMessage(
        IntPtr hWnd,
        uint uMsg,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (uMsg == WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            IntPtr result = CallWindowProcW(_origWndProc, hWnd, uMsg, wParam, lParam);
            uint dpi = GetDpiForWindow(hWnd);
            double scale = dpi > 0 ? dpi / 96.0 : 1.0;
            MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.ptMinTrackSize.X = Math.Max(info.ptMinTrackSize.X, (int)Math.Round(MinWindowWidthDips * scale));
            info.ptMinTrackSize.Y = Math.Max(info.ptMinTrackSize.Y, (int)Math.Round(MinWindowHeightDips * scale));
            Marshal.StructureToPtr(info, lParam, false);
            return result;
        }

        if (uMsg == WmWindowPosChanging && lParam != IntPtr.Zero)
        {
            WindowPos pos = Marshal.PtrToStructure<WindowPos>(lParam);
            if ((pos.flags & SwpNosize) == 0 && !IsIconic(hWnd))
            {
                uint dpi = GetDpiForWindow(hWnd);
                double scale = dpi > 0 ? dpi / 96.0 : 1.0;
                int minW = (int)Math.Round(MinWindowWidthDips * scale);
                int minH = (int)Math.Round(MinWindowHeightDips * scale);
                if (pos.cx > 0 && pos.cx < minW)
                {
                    pos.cx = minW;
                }

                if (pos.cy > 0 && pos.cy < minH)
                {
                    pos.cy = minH;
                }

                Marshal.StructureToPtr(pos, lParam, false);
            }
        }

        if (uMsg == WmDpiChanged && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            uint dpi = GetDpiForWindow(hWnd);
            double scale = dpi > 0 ? dpi / 96.0 : 1.0;
            presenter.PreferredMinimumWidth = (int)Math.Round(MinWindowWidthDips * scale);
            presenter.PreferredMinimumHeight = (int)Math.Round(MinWindowHeightDips * scale);
        }

        return CallWindowProcW(_origWndProc, hWnd, uMsg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Win32Point ptReserved;
        public Win32Point ptMaxSize;
        public Win32Point ptMaxPosition;
        public Win32Point ptMinTrackSize;
        public Win32Point ptMaxTrackSize;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange)
        {
            return;
        }

        if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            return;
        }

        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        uint dpi = hWnd != IntPtr.Zero ? GetDpiForWindow(hWnd) : 96u;
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;
        int minW = (int)Math.Round(MinWindowWidthDips * scale);
        int minH = (int)Math.Round(MinWindowHeightDips * scale);

        SizeInt32 cur = sender.Size;
        if (cur.Width < minW || cur.Height < minH)
        {
            sender.Resize(new SizeInt32(Math.Max(cur.Width, minW), Math.Max(cur.Height, minH)));
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose && HideToTrayOnClose)
        {
            args.Cancel = true;
            AppWindow.Hide();
        }
    }

    private void OnStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppStateViewModel.IsDeveloperLogEnabled))
        {
            ProtocolLogNavItem.Visibility = _state.IsDeveloperLogEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (!_state.IsDeveloperLogEnabled && ContentFrame.CurrentSourcePageType == typeof(ProtocolLogPage))
            {
                NavigationViewItem? aboutItem = NavView.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(i => i.Tag?.ToString() == "About");
                if (aboutItem is not null)
                {
                    NavView.SelectedItem = aboutItem;
                    NavigateByTag("About");
                }
            }
        }
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            NavigateByTag(item.Tag?.ToString());
        }
    }

    private void NavigateByTag(string? tag)
    {
        Type? targetPage = tag switch
        {
            "NoiseAudio" => typeof(NoiseAudioPage),
            "SonySmart" => typeof(SonySmartPage),
            "WindowsIntegration" => typeof(WindowsIntegrationPage),
            "ProtocolLog" => typeof(ProtocolLogPage),
            "About" => typeof(AboutPage),
            _ => null,
        };

        if (targetPage is not null && ContentFrame.CurrentSourcePageType != targetPage)
        {
            ContentFrame.Navigate(targetPage, _state);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
