using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SonyPodsWin.Protocol;
using SonyPodsWin.ViewModels;
using Windows.Graphics;
using Windows.UI;

namespace SonyPodsWin.Views;

/// <summary>
/// 350×182 桌面右下角电量悬浮弹窗（逐项对齐 parity-checklist.md §4.2）：
/// - 无边框、置顶、不显示在任务栏/Alt+Tab
/// - 主屏工作区右下角，距右/下各 14px
/// - 显示后默认 5 秒自动隐藏；鼠标悬停停表，离开后 3 秒隐藏
/// - 电量徽标 ≤20% 变红 #EF4444，否则香槟金 #D4B16A，最小填充比例 0.05
/// - 快捷按钮顺序固定：降噪 / 关闭 / 环境声
/// </summary>
public sealed partial class BatteryPopupWindow : Window
{
    private const int PopupWidth = 350;
    private const int PopupHeight = 182;
    private const int ScreenMargin = 14;
    private const double BatteryInnerMaxWidth = 20.0;

    private static readonly SolidColorBrush LowBatteryRedBrush = new(Color.FromArgb(0xFF, 0xEF, 0x44, 0x44));

    private readonly AppStateViewModel _state;
    private readonly DispatcherQueueTimer _autoHideTimer;
    private bool _allowClose;

    public event EventHandler? OpenMainRequested;

    public BatteryPopupWindow(AppStateViewModel state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        InitializeComponent();

        ConfigureWindowChrome();

        _autoHideTimer = DispatcherQueue.CreateTimer();
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += (_, _) => HidePopup();

        _state.DeviceSummaryUpdated += (_, _) => SyncFromState();
        SyncFromState();
    }

    private void ConfigureWindowChrome()
    {
        AppWindow.IsShownInSwitchers = false;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        AppWindow.Resize(new SizeInt32(PopupWidth, PopupHeight));
        AppWindow.Closing += (_, args) =>
        {
            if (!_allowClose)
            {
                args.Cancel = true;
                HidePopup();
            }
        };
    }

    public void SyncFromState()
    {
        PopupModelNameText.Text = _state.ModelName;

        int? pct = _state.BatteryPercent;
        string pctText = pct.HasValue ? $"{pct.Value}%" : "--%";
        bool hasCodec = !string.IsNullOrEmpty(_state.AudioCodecLabel) && _state.AudioCodecLabel != "--";
        BatteryMetaText.Text = hasCodec ? $"{pctText} · {_state.AudioCodecLabel}" : pctText;

        double ratio = pct.HasValue ? Math.Max(0.05, Math.Clamp(pct.Value, 0, 100) / 100.0) : 0.05;
        BatteryFillBar.Width = BatteryInnerMaxWidth * ratio;
        BatteryFillBar.Background = pct.HasValue && pct.Value <= 20
            ? LowBatteryRedBrush
            : (Brush)Application.Current.Resources["AccentBrush"];

        UpdateNcButtons(_state.NoiseControlMode);
    }

    public void ShowNearTray(int autoHideMs = 5000)
    {
        SyncFromState();

        RectInt32 workArea = DisplayArea.Primary.WorkArea;
        int x = workArea.X + workArea.Width - PopupWidth - ScreenMargin;
        int y = workArea.Y + workArea.Height - PopupHeight - ScreenMargin;
        AppWindow.Move(new PointInt32(x, y));

        AppWindow.Show(true);
        Activate();

        _autoHideTimer.Stop();
        if (autoHideMs > 0)
        {
            _autoHideTimer.Interval = TimeSpan.FromMilliseconds(autoHideMs);
            _autoHideTimer.Start();
        }
    }

    public void HidePopup()
    {
        _autoHideTimer.Stop();
        AppWindow.Hide();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        _autoHideTimer.Stop();
        Close();
    }

    private void UpdateNcButtons(NoiseControlMode activeMode)
    {
        var accentBg = (Brush)Application.Current.Resources["AccentBrush"];
        var accentFg = (Brush)Application.Current.Resources["AccentTextOnBrush"];
        var idleBg = (Brush)Application.Current.Resources["SecondaryBackgroundBrush"];
        var idleFg = (Brush)Application.Current.Resources["TextPrimaryBrush"];

        ApplyBtn(BtnPopupAnc, activeMode == NoiseControlMode.NoiseCancelling, accentBg, accentFg, idleBg, idleFg);
        ApplyBtn(BtnPopupOff, activeMode == NoiseControlMode.Off, accentBg, accentFg, idleBg, idleFg);
        ApplyBtn(BtnPopupAsm, activeMode == NoiseControlMode.AmbientSound, accentBg, accentFg, idleBg, idleFg);
    }

    private static void ApplyBtn(Button btn, bool selected, Brush accentBg, Brush accentFg, Brush idleBg, Brush idleFg)
    {
        btn.Background = selected ? accentBg : idleBg;
        btn.Foreground = selected ? accentFg : idleFg;
    }

    private void OnPopupPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _autoHideTimer.Stop();
    }

    private void OnPopupPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _autoHideTimer.Stop();
        _autoHideTimer.Interval = TimeSpan.FromMilliseconds(3000);
        _autoHideTimer.Start();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => HidePopup();

    private void OnAncClicked(object sender, RoutedEventArgs e) =>
        _state.QuickSetNoiseControl(NoiseControlMode.NoiseCancelling);

    private void OnOffClicked(object sender, RoutedEventArgs e) =>
        _state.QuickSetNoiseControl(NoiseControlMode.Off);

    private void OnAsmClicked(object sender, RoutedEventArgs e) =>
        _state.QuickSetNoiseControl(NoiseControlMode.AmbientSound);

    private void OnOpenMainClicked(object sender, RoutedEventArgs e)
    {
        HidePopup();
        OpenMainRequested?.Invoke(this, EventArgs.Empty);
    }
}
