using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Win32;
using SonyPodsWin.ViewModels;

namespace SonyPodsWin.Views;

public sealed partial class WindowsIntegrationPage : Page
{
    private const string StartupRunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "SonyPodsWin";

    private AppStateViewModel? _state;
    private bool _syncingUi;

    public WindowsIntegrationPage()
    {
        InitializeComponent();
        ScrollViewerWheelHelper.Attach(PageScrollViewer, () => _syncingUi);
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
            RunOnStartupSwitch.IsOn = IsRunOnStartupEnabled();
            AutoPlaySwitch.IsOn = _state.AutoPlay;
            AutoPauseSwitch.IsOn = _state.AutoPause;
            DefaultVolumeSlider.Value = _state.DefaultVolume;
            DefaultVolumeText.Text = $"{_state.DefaultVolume}%";
            PopupOnConnectSwitch.IsOn = _state.ShowPopupOnConnect;
            LowBatterySlider.Value = _state.LowBatteryThreshold;
            LowBatteryText.Text = $"{_state.LowBatteryThreshold}%";
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private void OnRunOnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_syncingUi)
        {
            return;
        }

        SetRunOnStartup(RunOnStartupSwitch.IsOn);
    }

    private static bool IsRunOnStartupEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRunRegistryKey, writable: false);
            return key?.GetValue(StartupValueName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    private static void SetRunOnStartup(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRunRegistryKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(StartupValueName, $"\"{exePath}\" --tray");
                }
            }
            else
            {
                key.DeleteValue(StartupValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
        }
    }

    private void OnAutoPlayToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingUi && _state is not null)
        {
            _state.AutoPlay = AutoPlaySwitch.IsOn;
        }
    }

    private void OnAutoPauseToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingUi && _state is not null)
        {
            _state.AutoPause = AutoPauseSwitch.IsOn;
        }
    }

    private void OnDefaultVolumeValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int val = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
        DefaultVolumeText.Text = $"{val}%";
    }

    private void OnDefaultVolumeReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_syncingUi && _state is not null)
        {
            _state.DefaultVolume = Math.Clamp((int)Math.Round(DefaultVolumeSlider.Value), 0, 100);
        }
    }

    private void OnPopupOnConnectToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncingUi && _state is not null)
        {
            _state.ShowPopupOnConnect = PopupOnConnectSwitch.IsOn;
        }
    }

    private void OnLowBatteryValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int val = Math.Clamp((int)Math.Round(e.NewValue), 5, 50);
        LowBatteryText.Text = $"{val}%";
    }

    private void OnLowBatteryReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_syncingUi && _state is not null)
        {
            _state.LowBatteryThreshold = Math.Clamp((int)Math.Round(LowBatterySlider.Value), 5, 50);
        }
    }
}
