using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using SonyPodsWin.Services;
using SonyPodsWin.ViewModels;
using SonyPodsWin.Views;

namespace SonyPodsWin;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private BatteryPopupWindow? _batteryPopup;
    private TrayIconService? _trayIcon;
    private bool _isExiting;

    public static BleService Ble { get; } = new();

    public static SettingsService Settings { get; } = new();

    public static AppStateViewModel State { get; } = new(Ble);

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 1. 启动读取 ~/.sonypodswin.json 并静默回填（不触发保存）
        SettingsSnapshot? saved = Settings.Load();
        if (saved is not null)
        {
            State.ApplySettings(saved);
        }

        // 2. 订阅配置变更事件实时落盘（仅 8 个应用键）
        State.SettingsChanged += (_, snapshot) => Settings.Save(snapshot);

        // 3. 创建主窗口与电量悬浮弹窗
        _mainWindow = new MainWindow(State);
        _batteryPopup = new BatteryPopupWindow(State);
        _batteryPopup.OpenMainRequested += (_, _) => _mainWindow.RestoreAndActivate();

        // 4. 创建系统托盘并复用 64×64 金色 "S" 图标为主窗口图标
        _trayIcon = new TrayIconService(_mainWindow.DispatcherQueue);
        _mainWindow.HideToTrayOnClose = _trayIcon.IsVisible;
        _mainWindow.SetWindowIcon(_trayIcon.IconHandle);

        // 5. 接线：托盘交互 -> 弹窗 / 主窗 / 快捷降噪 / 退出
        _trayIcon.TrayLeftClicked += (_, _) => _batteryPopup.ShowNearTray(5000);
        _trayIcon.TrayLeftDoubleClicked += (_, _) => _mainWindow.RestoreAndActivate();
        _trayIcon.OpenMainRequested += (_, _) => _mainWindow.RestoreAndActivate();
        _trayIcon.QuickNcModeSelected += (_, mode) => State.QuickSetNoiseControl(mode);
        _trayIcon.ExitRequested += async (_, _) => await QuitApplicationAsync();

        // 6. 接线：ViewModel 事件 -> 托盘 tooltip / 低电量提醒 / 连接 900ms 弹窗
        State.DeviceSummaryUpdated += (_, _) =>
        {
            if (State.BatteryPercent.HasValue)
            {
                _trayIcon?.UpdateTooltip($"{State.ModelName} · 电量 {State.BatteryPercent.Value}%");
            }
        };

        State.ConnectPopupRequested += (_, _) => _batteryPopup?.ShowNearTray(5000);

        State.LowBatteryAlertRequested += (_, alert) =>
        {
            _trayIcon?.ShowWarningNotification(
                title: "SonyPods 低电量提醒",
                message: $"{alert.ModelName} 电量仅剩 {alert.BatteryPercent}%，请及时充电。",
                timeoutMs: 5000);
        };

        _mainWindow.Activate();

        // 7. 启动流程（parity-checklist.md §3）：扫描与上次设备自动重连并行发起
        _ = State.ScanDevicesAsync();
        if (!string.IsNullOrWhiteSpace(State.CurrentAddress))
        {
            _ = State.ConnectToCurrentAsync();
        }
    }

    private async Task QuitApplicationAsync()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;

        try
        {
            _batteryPopup?.CloseForExit();
        }
        catch
        {
        }

        try
        {
            // 与 parity-checklist.md §4.1 一致：断开 BLE 最多等待 2 秒
            Task disconnectTask = State.DisconnectManualAsync();
            await Task.WhenAny(disconnectTask, Task.Delay(2000));
        }
        catch
        {
        }

        try
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
        catch
        {
        }

        try
        {
            _mainWindow?.CloseForExit();
        }
        catch
        {
        }

        Exit();
    }
}
