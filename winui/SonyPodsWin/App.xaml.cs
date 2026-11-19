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

        // 3. 创建主窗口
        _mainWindow = new MainWindow(State);

        // 4. 创建系统托盘并复用 64×64 金色 "S" 图标为主窗口图标
        _trayIcon = new TrayIconService(_mainWindow.DispatcherQueue);
        _mainWindow.HideToTrayOnClose = _trayIcon.IsVisible;
        _mainWindow.SetWindowIcon(_trayIcon.IconHandle);

        // 5. 接线：托盘交互 -> 主窗 / 快捷降噪 / 退出
        _trayIcon.TrayLeftClicked += (_, _) => _mainWindow.RestoreAndActivate();
        _trayIcon.TrayLeftDoubleClicked += (_, _) => _mainWindow.RestoreAndActivate();
        _trayIcon.OpenMainRequested += (_, _) => _mainWindow.RestoreAndActivate();
        _trayIcon.QuickNcModeSelected += (_, mode) => State.QuickSetNoiseControl(mode);
        _trayIcon.ExitRequested += async (_, _) => await QuitApplicationAsync();

        // 6. 接线：ViewModel 事件 -> 托盘 tooltip / 低电量提醒 / 连接成功 Windows 系统通知
        State.DeviceSummaryUpdated += (_, _) =>
        {
            if (State.BatteryPercent.HasValue)
            {
                _trayIcon?.UpdateTooltip($"{State.ModelName} · 电量 {State.BatteryPercent.Value}%");
            }
        };

        State.ConnectPopupRequested += (_, _) =>
        {
            string pctText = State.BatteryPercent.HasValue ? $"{State.BatteryPercent.Value}%" : "--%";
            string codecText = !string.IsNullOrEmpty(State.AudioCodecLabel) && State.AudioCodecLabel != "--"
                ? $" · 编码 {State.AudioCodecLabel}"
                : string.Empty;
            string modeText = State.NoiseControlMode switch
            {
                Protocol.NoiseControlMode.NoiseCancelling => "降噪",
                Protocol.NoiseControlMode.AmbientSound => "环境声",
                _ => "关闭",
            };
            _trayIcon?.ShowInfoNotification(
                title: $"{State.ModelName} 已连接",
                message: $"电量 {pctText}{codecText} · 当前模式：{modeText}",
                timeoutMs: 5000);
        };

        State.LowBatteryAlertRequested += (_, alert) =>
        {
            _trayIcon?.ShowWarningNotification(
                title: "SonyPods 低电量提醒",
                message: $"{alert.ModelName} 电量仅剩 {alert.BatteryPercent}%，请及时充电。",
                timeoutMs: 5000);
        };

        bool startInTray = Array.Exists(Environment.GetCommandLineArgs(), a =>
            string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        if (!startInTray)
        {
            _mainWindow.Activate();
        }

        // 7. 启动流程：仅扫描设备，绝不自动发起蓝牙连接，避免抢占用户手机连接
        _ = State.ScanDevicesAsync();
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
