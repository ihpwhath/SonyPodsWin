using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SonyPodsWin.ViewModels;
using SonyPodsWin.Views;
using Windows.Graphics;
using WinRT.Interop;

namespace SonyPodsWin;

public sealed partial class MainWindow : Window
{
    private const uint WmSetIcon = 0x0080;
    private static readonly IntPtr IconSmall = IntPtr.Zero;
    private static readonly IntPtr IconBig = new(1);

    private readonly AppStateViewModel _state;
    private bool _allowClose;

    /// <summary>托盘可见时关窗默认隐藏到托盘驻留（对应 parity-checklist.md §0 / §4.1）。</summary>
    public bool HideToTrayOnClose { get; set; } = true;

    public MainWindow(AppStateViewModel state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        InitializeComponent();

        Title = "SonyPods";
        _state.AttachDispatcher(DispatcherQueue);

        ConfigureWindowSizing();
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

    private void ConfigureWindowSizing()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 900;
            presenter.PreferredMinimumHeight = 600;
        }

        AppWindow.Resize(new SizeInt32(980, 640));
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose && HideToTrayOnClose)
        {
            args.Cancel = true;
            AppWindow.Hide();
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
