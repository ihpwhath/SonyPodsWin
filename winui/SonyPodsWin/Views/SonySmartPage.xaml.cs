using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SonyPodsWin.Protocol;
using SonyPodsWin.ViewModels;

namespace SonyPodsWin.Views;

public sealed partial class SonySmartPage : Page
{
    private AppStateViewModel? _state;
    private bool _syncingUi;

    public SonySmartPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AppStateViewModel vm = e.Parameter as AppStateViewModel ?? App.State;
        AttachState(vm);
    }

    private void AttachState(AppStateViewModel vm)
    {
        if (ReferenceEquals(_state, vm))
        {
            SyncFromState();
            return;
        }

        if (_state is not null)
        {
            _state.PropertyChanged -= OnStatePropertyChanged;
        }

        _state = vm;
        _state.PropertyChanged += OnStatePropertyChanged;
        SyncFromState();
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncFromState();
    }

    private void SyncFromState()
    {
        if (_state is null)
        {
            return;
        }

        _syncingUi = true;
        try
        {
            SpeakToChatSwitch.IsOn = _state.SpeakToChatEnabled;
            MultipointSwitch.IsOn = _state.MultipointEnabled;
            CinemaModeSwitch.IsOn = _state.CinemaModeEnabled;
            PairedDevicesText.Text = _state.PairedDevicesText;
            AutoPowerOffText.Text = _state.AutoPowerOffText;

            HeadphoneVolumeSlider.Value = _state.HeadphoneVolume;
            VolumeValueText.Text = $"{_state.HeadphoneVolume}%";
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private void OnSpeakToChatToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingUi)
        {
            _state?.SetSpeakToChat(SpeakToChatSwitch.IsOn);
        }
    }

    private void OnMultipointToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingUi)
        {
            _state?.SetMultipoint(MultipointSwitch.IsOn);
        }
    }

    private void OnCinemaModeToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingUi)
        {
            _state?.SetCinemaMode(CinemaModeSwitch.IsOn);
        }
    }

    private void OnVolumeSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int val = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
        VolumeValueText.Text = $"{val}%";
    }

    private void OnVolumeSliderReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_syncingUi || _state is null)
        {
            return;
        }

        int val = Math.Clamp((int)Math.Round(HeadphoneVolumeSlider.Value), 0, 100);
        _state.SetHeadphoneVolume(val);
    }

    private void OnPrevTrackClicked(object sender, RoutedEventArgs e) =>
        _state?.SendPlaybackControl(PlaybackControl.TrackDown);

    private void OnPlayClicked(object sender, RoutedEventArgs e) =>
        _state?.SendPlaybackControl(PlaybackControl.Play);

    private void OnPauseClicked(object sender, RoutedEventArgs e) =>
        _state?.SendPlaybackControl(PlaybackControl.Pause);

    private void OnNextTrackClicked(object sender, RoutedEventArgs e) =>
        _state?.SendPlaybackControl(PlaybackControl.TrackUp);

    private async void OnPowerOffClicked(object sender, RoutedEventArgs e)
    {
        if (_state is null || !_state.IsConnected)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "确认关机",
            Content = "确定要关闭当前连接的索尼耳机电源吗？",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _state.PowerOffHeadphones();
        }
    }
}
