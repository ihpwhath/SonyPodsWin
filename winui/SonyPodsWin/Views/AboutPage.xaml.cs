using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SonyPodsWin.Assets;
using SonyPodsWin.ViewModels;

namespace SonyPodsWin.Views;

public sealed partial class AboutPage : Page
{
    private AppStateViewModel? _state;
    private bool _qrLoaded;
    private int _versionClickCount;
    private DateTime _lastVersionClickUtc = DateTime.MinValue;

    public AboutPage()
    {
        InitializeComponent();
        ScrollViewerWheelHelper.Attach(PageScrollViewer);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _state = e.Parameter as AppStateViewModel ?? App.State;
        _versionClickCount = 0;
        UpdateDevModeHint();
    }

    private void OnVersionClicked(object sender, RoutedEventArgs e)
    {
        AppStateViewModel state = _state ?? App.State;
        if (state.IsDeveloperLogEnabled)
        {
            DevModeHintText.Text = "协议日志已开启（可在协议日志页右上角停止并隐藏）";
            DevModeHintText.Visibility = Visibility.Visible;
            return;
        }

        DateTime now = DateTime.UtcNow;
        if ((now - _lastVersionClickUtc).TotalSeconds > 3.0)
        {
            _versionClickCount = 0;
        }

        _lastVersionClickUtc = now;
        _versionClickCount++;

        int remaining = 5 - _versionClickCount;
        if (remaining <= 0)
        {
            _versionClickCount = 0;
            state.IsDeveloperLogEnabled = true;
            DevModeHintText.Text = "已开启「协议日志」调试页（左侧菜单已显示）";
            DevModeHintText.Visibility = Visibility.Visible;
        }
        else if (_versionClickCount >= 2)
        {
            DevModeHintText.Text = $"再点击 {remaining} 次版本号即可开启协议日志";
            DevModeHintText.Visibility = Visibility.Visible;
        }
    }

    private void UpdateDevModeHint()
    {
        AppStateViewModel state = _state ?? App.State;
        if (state.IsDeveloperLogEnabled)
        {
            DevModeHintText.Text = "协议日志处于开启状态";
            DevModeHintText.Visibility = Visibility.Visible;
        }
        else
        {
            DevModeHintText.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnZanshangExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (_qrLoaded)
        {
            return;
        }

        _qrLoaded = true;
        BitmapImage? image = await ZanshangAsset.LoadBitmapImageAsync();
        if (image is not null)
        {
            ZanshangQrImage.Source = image;
        }
    }
}
