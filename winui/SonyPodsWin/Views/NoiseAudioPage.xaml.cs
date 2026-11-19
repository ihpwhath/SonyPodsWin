using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SonyPodsWin.Protocol;
using SonyPodsWin.Services;
using SonyPodsWin.ViewModels;

namespace SonyPodsWin.Views;

public sealed partial class NoiseAudioPage : Page
{
    private AppStateViewModel? _state;
    private bool _syncingUi;

    public NoiseAudioPage()
    {
        InitializeComponent();
        EqGraph.BandsReleased += OnEqGraphBandsReleased;
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
            SyncAllFromState();
            return;
        }

        if (_state is not null)
        {
            _state.PropertyChanged -= OnStatePropertyChanged;
            _state.EqGraphSyncRequested -= OnEqGraphSyncRequested;
        }

        _state = vm;
        _state.PropertyChanged += OnStatePropertyChanged;
        _state.EqGraphSyncRequested += OnEqGraphSyncRequested;

        EqPresetComboBox.ItemsSource = _state.EqPresets;
        SyncAllFromState();
        SyncEqGraphFromState();
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncAllFromState();
    }

    private void OnEqGraphSyncRequested(object? sender, EventArgs e)
    {
        SyncEqGraphFromState();
    }

    private void SyncEqGraphFromState()
    {
        if (_state is null)
        {
            return;
        }

        EqGraph.Configure(_state.EqBandCount, _state.EqRawMax, _state.EqCenter, _state.EqBandSteps);
    }

    private void SyncAllFromState()
    {
        if (_state is null)
        {
            return;
        }

        _syncingUi = true;
        try
        {
            ModelNameText.Text = _state.ModelName;
            StatusText.Text = _state.ConnectionStatusText;
            StatusDot.Fill = _state.ConnectionState switch
            {
                BleConnectionState.Connected => (Brush)Application.Current.Resources["SuccessBrush"],
                BleConnectionState.Connecting or BleConnectionState.Reconnecting => (Brush)Application.Current.Resources["AccentBrush"],
                _ => (Brush)Application.Current.Resources["TextMutedBrush"],
            };

            BatteryRing.Value = _state.BatteryRingValue;
            BatteryText.Text = _state.BatteryDisplay;
            CodecText.Text = _state.AudioCodecLabel;
            FirmwareText.Text = _state.FirmwareVersion;

            SwitchDeviceButton.Content = _state.ScanButtonText;
            ConnectButton.Content = _state.ConnectButtonText;
            ConnectButton.IsEnabled = _state.IsConnectButtonEnabled;

            UpdateNcSegmentStyles(_state.NoiseControlMode);

            AmbientLevelSlider.Value = _state.AmbientLevel;
            AmbientLevelText.Text = $"{_state.AmbientLevel} 级";
            VoiceFocusSwitch.IsOn = _state.VoiceFocusEnabled;
            AutoAmbientSwitch.IsOn = _state.AutoAmbientEnabled;

            EqPresetOption? match = _state.EqPresets.FirstOrDefault(p => p.Id == _state.SelectedEqPreset);
            if (match is not null && !ReferenceEquals(EqPresetComboBox.SelectedItem, match))
            {
                EqPresetComboBox.SelectedItem = match;
            }

            DseeSwitch.IsOn = _state.DseeEnabled;
            QualityPrioritySwitch.IsOn = _state.SoundQualityPriority;
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private void UpdateNcSegmentStyles(NoiseControlMode activeMode)
    {
        var accentBg = (Brush)Application.Current.Resources["AccentBrush"];
        var accentFg = (Brush)Application.Current.Resources["AccentTextOnBrush"];
        var idleBg = (Brush)Application.Current.Resources["SecondaryBackgroundBrush"];
        var idleFg = (Brush)Application.Current.Resources["TextPrimaryBrush"];

        ApplySegmentButtonStyle(NcModeAncButton, activeMode == NoiseControlMode.NoiseCancelling, accentBg, accentFg, idleBg, idleFg);
        ApplySegmentButtonStyle(NcModeAsmButton, activeMode == NoiseControlMode.AmbientSound, accentBg, accentFg, idleBg, idleFg);
        ApplySegmentButtonStyle(NcModeOffButton, activeMode == NoiseControlMode.Off, accentBg, accentFg, idleBg, idleFg);
    }

    private static void ApplySegmentButtonStyle(
        Button btn,
        bool isSelected,
        Brush accentBg,
        Brush accentFg,
        Brush idleBg,
        Brush idleFg)
    {
        btn.Background = isSelected ? accentBg : idleBg;
        btn.Foreground = isSelected ? accentFg : idleFg;
    }

    private void OnSwitchDeviceClicked(object sender, RoutedEventArgs e)
    {
        if (_state is null)
        {
            return;
        }

        var flyout = new MenuFlyout();
        if (_state.DiscoveredDevices.Count > 0)
        {
            foreach (DiscoveredDevice device in _state.DiscoveredDevices)
            {
                var item = new MenuFlyoutItem
                {
                    Text = $"{device.Name}  ({device.Address})",
                };
                DiscoveredDevice captured = device;
                item.Click += async (_, _) => await _state.SelectAndConnectAsync(captured);
                flyout.Items.Add(item);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        var rescanItem = new MenuFlyoutItem { Text = "↻ 重新扫描附近的索尼耳机" };
        rescanItem.Click += async (_, _) => await _state.ScanDevicesAsync();
        flyout.Items.Add(rescanItem);

        flyout.ShowAt(SwitchDeviceButton);
    }

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        if (_state is not null)
        {
            await _state.ToggleConnectAsync();
        }
    }

    private void OnNcModeAncClicked(object sender, RoutedEventArgs e) =>
        _state?.SetNoiseControlMode(NoiseControlMode.NoiseCancelling);

    private void OnNcModeAsmClicked(object sender, RoutedEventArgs e) =>
        _state?.SetNoiseControlMode(NoiseControlMode.AmbientSound);

    private void OnNcModeOffClicked(object sender, RoutedEventArgs e) =>
        _state?.SetNoiseControlMode(NoiseControlMode.Off);

    private void OnAmbientSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int val = Math.Clamp((int)Math.Round(e.NewValue), 1, 20);
        AmbientLevelText.Text = $"{val} 级";
    }

    private void OnAmbientSliderReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_syncingUi || _state is null)
        {
            return;
        }

        int val = Math.Clamp((int)Math.Round(AmbientLevelSlider.Value), 1, 20);
        _state.SetAmbientLevel(val);
    }

    private void OnVoiceFocusToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingUi)
        {
            _state?.SetVoiceFocus(VoiceFocusSwitch.IsOn);
        }
    }

    private void OnAutoAmbientToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingUi)
        {
            _state?.SetAutoAmbient(AutoAmbientSwitch.IsOn);
        }
    }

    private void OnEqPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || _state is null)
        {
            return;
        }

        if (EqPresetComboBox.SelectedItem is EqPresetOption option)
        {
            _state.SelectEqPreset(option.Id);
        }
    }

    private void OnEqGraphBandsReleased(object? sender, IReadOnlyList<int> steps)
    {
        if (!_syncingUi)
        {
            _state?.ApplyCustomEqBands(steps);
        }
    }

    private void OnDseeToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingUi)
        {
            _state?.SetDsee(DseeSwitch.IsOn);
        }
    }

    private void OnQualityPriorityToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingUi)
        {
            _state?.SetSoundQualityPriority(QualityPrioritySwitch.IsOn);
        }
    }
}
