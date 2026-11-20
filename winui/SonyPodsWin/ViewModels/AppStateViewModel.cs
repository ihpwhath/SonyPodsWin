using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using SonyPodsWin.Protocol;
using SonyPodsWin.Services;

namespace SonyPodsWin.ViewModels;

public enum ProtocolLogLevel
{
    Info,
    Frame,
    Warning,
    Error,
}

public sealed record ProtocolLogEntry(
    DateTime Timestamp,
    ProtocolLogLevel Level,
    string Source,
    string Message)
{
    public string FormattedLine => $"[{Timestamp:HH:mm:ss.fff}] [{Level.ToString().ToUpperInvariant()}] [{Source}] {Message}";
}

public sealed record EqPresetOption(EqPresetId Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 对应第三阶段 ~/.sonypodswin.json 持久化 8 个键的快照结构，供第三阶段直接读写。
/// 键名约定：last_name / last_address / show_popup_on_connect / auto_reconnect /
/// auto_play / auto_pause / default_volume / low_battery_threshold。
/// </summary>
public sealed record SettingsSnapshot(
    string LastName,
    string? LastAddress,
    bool ShowPopupOnConnect,
    bool AutoReconnect,
    bool AutoPlay,
    bool AutoPause,
    int DefaultVolume,
    int LowBatteryThreshold);

/// <summary>
/// 全局应用状态与协议编排器（不修改 Protocol 与 BleService 任何协议逻辑，向上承接五页 UI 与第三阶段托盘/弹窗/持久化）。
/// </summary>
public sealed class AppStateViewModel : INotifyPropertyChanged
{
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyeventfKeyup = 0x0002;
    private const int MaxLogEntries = 2000;

    private static readonly IReadOnlyDictionary<EqPresetId, string> PresetShortNames = new Dictionary<EqPresetId, string>
    {
        [EqPresetId.Off] = "关闭",
        [EqPresetId.Heavy] = "重低音",
        [EqPresetId.Clear] = "清澈",
        [EqPresetId.Hard] = "硬朗",
        [EqPresetId.Soft] = "柔和",
        [EqPresetId.GamingEq] = "游戏",
        [EqPresetId.Custom] = "自定义",
        [EqPresetId.UserSetting1] = "自定 1",
        [EqPresetId.UserSetting2] = "自定 2",
        [EqPresetId.UserSetting3] = "自定 3",
    };

    private static readonly EqPresetId[] Xm6DefaultPresets =
    {
        EqPresetId.Off,
        EqPresetId.Heavy,
        EqPresetId.Clear,
        EqPresetId.Hard,
        EqPresetId.Soft,
        EqPresetId.GamingEq,
        EqPresetId.Custom,
        EqPresetId.UserSetting1,
        EqPresetId.UserSetting2,
    };

    private readonly BleService _ble;
    private DispatcherQueue? _dispatcher;
    private CancellationTokenSource? _popupDelayCts;

    // 协议协商上下文（与 Python MainWindow 一致）
    private NcAsmInquiredType _ncType = NcAsmInquiredType.ModeNcAsmDualNcModeSwitchAndAsmSeamless;
    private EqEbbInquiredType _eqType = EqEbbInquiredType.PresetEq;
    private byte _upscalingInq = Table1.AudioInqUpscaling;
    private byte? _multipointSlot;
    private bool _wasConnected;
    private bool _lowBatteryNotified;
    private bool _isConnectingUi;
    private bool _isScanning;

    // ── 设备顶部栏状态 ────────────────────────────────────────────────────────
    private string _modelName = "WH-1000XM6";
    private string? _currentAddress;
    private string _firmwareVersion = "--";
    private string _audioCodecLabel = "--";
    private int? _batteryPercent;
    private BleConnectionState _connectionState = BleConnectionState.Disconnected;

    // ── Page 01 降噪与音质状态 ────────────────────────────────────────────────
    private NoiseControlMode _noiseControlMode = NoiseControlMode.NoiseCancelling;
    private int _ambientLevel = 15;
    private bool _voiceFocusEnabled;
    private bool _autoAmbientEnabled;
    private EqPresetId _selectedEqPreset = EqPresetId.Off;
    private int _eqBandCount = 10;
    private int _eqRawMax = 12;
    private int _eqCenter = 6;
    private int[] _eqBandSteps = Enumerable.Repeat(6, 10).ToArray();
    private bool _dseeEnabled;
    private bool _soundQualityPriority = true;

    // ── Page 02 索尼智能控制状态 ──────────────────────────────────────────────
    private bool _speakToChatEnabled;
    private bool _multipointEnabled;
    private bool _cinemaModeEnabled;
    private string _pairedDevicesText = "正在读取已配对设备...";
    private string _autoPowerOffText = "已启用佩戴检测自动关机";
    private int _headphoneVolume = 50;

    // ── Page 03 Windows 联动增强状态（与第三阶段持久化 8 键完全一致） ──────────
    private bool _autoPlay;
    private bool _autoPause = true;
    private int _defaultVolume = 54;
    private bool _showPopupOnConnect = true;
    private bool _autoReconnect = false;
    private int _lowBatteryThreshold = 30;
    private bool _isDeveloperLogEnabled = false;

    public AppStateViewModel(BleService ble)
    {
        _ble = ble ?? throw new ArgumentNullException(nameof(ble));
        _ble.AutoReconnect = _autoReconnect;

        DiscoveredDevices = new ObservableCollection<DiscoveredDevice>();
        EqPresets = new ObservableCollection<EqPresetOption>(
            Xm6DefaultPresets.Select(CreatePresetOption));
        LogEntries = new ObservableCollection<ProtocolLogEntry>();

        _ble.ConnectionStateChanged += OnBleConnectionStateChanged;
        _ble.PayloadReceived += OnBlePayloadReceived;
        _ble.FrameReceived += OnBleFrameReceived;
        _ble.LogMessage += OnBleLogMessage;
        _ble.Disconnected += OnBleDisconnected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>EQ 几何或频段步进被设备回包更新时触发，供 EqualizerControl 静默同步。</summary>
    public event EventHandler? EqGraphSyncRequested;

    /// <summary>供第三阶段接入 ~/.sonypodswin.json 持久化落盘。</summary>
    public event EventHandler<SettingsSnapshot>? SettingsChanged;

    /// <summary>供第三阶段托盘低电量提醒通知订阅（每连接会话仅触发一次）。</summary>
    public event EventHandler<(string ModelName, int BatteryPercent)>? LowBatteryAlertRequested;

    /// <summary>供第三阶段电量弹窗订阅（连接成功 900ms 后且仍处于连接态触发）。</summary>
    public event EventHandler? ConnectPopupRequested;

    /// <summary>供第三阶段托盘 tooltip 与电量弹窗实时同步订阅。</summary>
    public event EventHandler? DeviceSummaryUpdated;

    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; }
    public ObservableCollection<EqPresetOption> EqPresets { get; }
    public ObservableCollection<ProtocolLogEntry> LogEntries { get; }

    public void AttachDispatcher(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    // ── 只读/计算属性 ─────────────────────────────────────────────────────────

    public string ModelName
    {
        get => _modelName;
        private set
        {
            if (SetField(ref _modelName, value))
            {
                OnPropertyChanged(nameof(HeaderMetaText));
                DeviceSummaryUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string? CurrentAddress
    {
        get => _currentAddress;
        private set => SetField(ref _currentAddress, value);
    }

    public string FirmwareVersion
    {
        get => _firmwareVersion;
        private set
        {
            if (SetField(ref _firmwareVersion, value))
            {
                OnPropertyChanged(nameof(HeaderMetaText));
                DeviceSummaryUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string AudioCodecLabel
    {
        get => _audioCodecLabel;
        private set
        {
            if (SetField(ref _audioCodecLabel, value))
            {
                OnPropertyChanged(nameof(HeaderMetaText));
                DeviceSummaryUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int? BatteryPercent
    {
        get => _batteryPercent;
        private set
        {
            if (SetField(ref _batteryPercent, value))
            {
                OnPropertyChanged(nameof(BatteryDisplay));
                OnPropertyChanged(nameof(BatteryRingValue));
                DeviceSummaryUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string BatteryDisplay => _batteryPercent.HasValue ? $"{_batteryPercent.Value}%" : "--%";
    public double BatteryRingValue => _batteryPercent.HasValue ? Math.Clamp(_batteryPercent.Value, 0, 100) : 0;

    /// <summary>与 parity-checklist §0 一致的 meta 拼串格式。</summary>
    public string HeaderMetaText
    {
        get
        {
            bool hasCodec = !string.IsNullOrEmpty(_audioCodecLabel) && _audioCodecLabel != "--";
            bool hasFw = !string.IsNullOrEmpty(_firmwareVersion) && _firmwareVersion != "--";
            if (hasCodec && hasFw)
            {
                return $"{_audioCodecLabel} · FW {_firmwareVersion}";
            }

            if (hasFw)
            {
                return $"FW {_firmwareVersion}";
            }

            return hasCodec ? _audioCodecLabel : "--";
        }
    }

    public BleConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            if (SetField(ref _connectionState, value))
            {
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(ConnectionStatusText));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(IsConnectButtonEnabled));
            }
        }
    }

    public bool IsConnected => _connectionState == BleConnectionState.Connected && _ble.IsConnected;

    public string ConnectionStatusText => _connectionState switch
    {
        BleConnectionState.Connected => "已连接",
        BleConnectionState.Connecting => "连接中...",
        BleConnectionState.Reconnecting => "重连中...",
        _ => "未连接",
    };

    public string ConnectButtonText => _connectionState switch
    {
        BleConnectionState.Connected => "断开连接",
        BleConnectionState.Connecting => "连接中...",
        BleConnectionState.Reconnecting => "重连中...",
        _ => _isConnectingUi ? "连接中..." : "连接",
    };

    public bool IsConnectButtonEnabled => !_isConnectingUi && _connectionState != BleConnectionState.Connecting;

    public string ScanButtonText => _isScanning ? "扫描中..." : "切换设备 ▾";

    // ── Page 01 属性 ──────────────────────────────────────────────────────────

    public NoiseControlMode NoiseControlMode
    {
        get => _noiseControlMode;
        private set
        {
            if (SetField(ref _noiseControlMode, value))
            {
                DeviceSummaryUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int AmbientLevel
    {
        get => _ambientLevel;
        private set => SetField(ref _ambientLevel, value);
    }

    public bool VoiceFocusEnabled
    {
        get => _voiceFocusEnabled;
        private set => SetField(ref _voiceFocusEnabled, value);
    }

    public bool AutoAmbientEnabled
    {
        get => _autoAmbientEnabled;
        private set => SetField(ref _autoAmbientEnabled, value);
    }

    public EqPresetId SelectedEqPreset
    {
        get => _selectedEqPreset;
        private set => SetField(ref _selectedEqPreset, value);
    }

    public int EqBandCount => _eqBandCount;
    public int EqRawMax => _eqRawMax;
    public int EqCenter => _eqCenter;
    public IReadOnlyList<int> EqBandSteps => _eqBandSteps;

    public bool DseeEnabled
    {
        get => _dseeEnabled;
        private set => SetField(ref _dseeEnabled, value);
    }

    public bool SoundQualityPriority
    {
        get => _soundQualityPriority;
        private set => SetField(ref _soundQualityPriority, value);
    }

    // ── Page 02 属性 ──────────────────────────────────────────────────────────

    public bool SpeakToChatEnabled
    {
        get => _speakToChatEnabled;
        private set => SetField(ref _speakToChatEnabled, value);
    }

    public bool MultipointEnabled
    {
        get => _multipointEnabled;
        private set => SetField(ref _multipointEnabled, value);
    }

    public bool CinemaModeEnabled
    {
        get => _cinemaModeEnabled;
        private set => SetField(ref _cinemaModeEnabled, value);
    }

    public string PairedDevicesText
    {
        get => _pairedDevicesText;
        private set => SetField(ref _pairedDevicesText, value);
    }

    public string AutoPowerOffText
    {
        get => _autoPowerOffText;
        private set => SetField(ref _autoPowerOffText, value);
    }

    public int HeadphoneVolume
    {
        get => _headphoneVolume;
        private set => SetField(ref _headphoneVolume, value);
    }

    // ── Page 03 属性（供 UI 与第三阶段持久化共用） ──────────────────────────────

    public bool AutoPlay
    {
        get => _autoPlay;
        set
        {
            if (SetField(ref _autoPlay, value))
            {
                RaiseSettingsChanged();
            }
        }
    }

    public bool AutoPause
    {
        get => _autoPause;
        set
        {
            if (SetField(ref _autoPause, value))
            {
                RaiseSettingsChanged();
            }
        }
    }

    public int DefaultVolume
    {
        get => _defaultVolume;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (SetField(ref _defaultVolume, clamped))
            {
                RaiseSettingsChanged();
            }
        }
    }

    public bool ShowPopupOnConnect
    {
        get => _showPopupOnConnect;
        set
        {
            if (SetField(ref _showPopupOnConnect, value))
            {
                RaiseSettingsChanged();
            }
        }
    }

    public bool AutoReconnect
    {
        get => _autoReconnect;
        set
        {
            if (SetField(ref _autoReconnect, value))
            {
                _ble.AutoReconnect = value;
                RaiseSettingsChanged();
            }
        }
    }

    public int LowBatteryThreshold
    {
        get => _lowBatteryThreshold;
        set
        {
            int clamped = Math.Clamp(value, 5, 50);
            if (SetField(ref _lowBatteryThreshold, clamped))
            {
                RaiseSettingsChanged();
            }
        }
    }

    public bool IsDeveloperLogEnabled
    {
        get => _isDeveloperLogEnabled;
        set
        {
            if (SetField(ref _isDeveloperLogEnabled, value))
            {
                if (!value)
                {
                    LogEntries.Clear();
                }
            }
        }
    }

    // ── 第三阶段持久化接口 ────────────────────────────────────────────────────

    public SettingsSnapshot ExportSettings() => new(
        LastName: _modelName,
        LastAddress: _currentAddress,
        ShowPopupOnConnect: _showPopupOnConnect,
        AutoReconnect: _autoReconnect,
        AutoPlay: _autoPlay,
        AutoPause: _autoPause,
        DefaultVolume: _defaultVolume,
        LowBatteryThreshold: _lowBatteryThreshold);

    /// <summary>供第三阶段启动加载 ~/.sonypodswin.json 时静默回填（不触发保存）。</summary>
    public void ApplySettings(SettingsSnapshot snapshot)
    {
        ModelName = string.IsNullOrWhiteSpace(snapshot.LastName) ? "WH-1000XM6" : snapshot.LastName;
        CurrentAddress = snapshot.LastAddress;
        SetField(ref _showPopupOnConnect, snapshot.ShowPopupOnConnect, nameof(ShowPopupOnConnect));
        SetField(ref _autoReconnect, false, nameof(AutoReconnect));
        _ble.AutoReconnect = false;
        SetField(ref _autoPlay, snapshot.AutoPlay, nameof(AutoPlay));
        SetField(ref _autoPause, snapshot.AutoPause, nameof(AutoPause));
        SetField(ref _defaultVolume, snapshot.DefaultVolume, nameof(DefaultVolume));
        SetField(ref _lowBatteryThreshold, Math.Clamp(snapshot.LowBatteryThreshold, 5, 50), nameof(LowBatteryThreshold));
    }

    // ── 设备扫描与连接控制 ────────────────────────────────────────────────────

    public async Task ScanDevicesAsync()
    {
        if (_isScanning)
        {
            return;
        }

        _isScanning = true;
        OnPropertyChanged(nameof(ScanButtonText));
        try
        {
            // 与 parity-checklist §2 一致：UI 侧扫描超时 3.5 秒
            IReadOnlyList<DiscoveredDevice> found = await BleService.ScanDevicesAsync(TimeSpan.FromSeconds(3.5));
            RunOnUi(() =>
            {
                DiscoveredDevices.Clear();
                foreach (DiscoveredDevice d in found)
                {
                    DiscoveredDevices.Add(d);
                }

                if (string.IsNullOrEmpty(_currentAddress) && found.Count > 0)
                {
                    CurrentAddress = found[0].Address;
                    ModelName = found[0].Name;
                }

                AppendLog(ProtocolLogLevel.Info, "SCAN", $"发现 {found.Count} 台 Sony 蓝牙设备");
            });
        }
        catch (Exception exc)
        {
            RunOnUi(() => AppendError($"扫描失败: {exc.Message}"));
        }
        finally
        {
            RunOnUi(() =>
            {
                _isScanning = false;
                OnPropertyChanged(nameof(ScanButtonText));
            });
        }
    }

    public async Task SelectAndConnectAsync(DiscoveredDevice device)
    {
        CurrentAddress = device.Address;
        ModelName = device.Name;
        await ConnectToCurrentAsync();
    }

    public async Task ToggleConnectAsync()
    {
        if (IsConnected || _connectionState == BleConnectionState.Reconnecting)
        {
            await DisconnectManualAsync();
            return;
        }

        if (string.IsNullOrEmpty(_currentAddress))
        {
            await ScanDevicesAsync();
            if (string.IsNullOrEmpty(_currentAddress))
            {
                AppendError("未找到可连接的 Sony 耳机，请先扫描设备");
                return;
            }
        }

        await ConnectToCurrentAsync();
    }

    public async Task ConnectToCurrentAsync()
    {
        if (string.IsNullOrEmpty(_currentAddress))
        {
            return;
        }

        _isConnectingUi = true;
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(IsConnectButtonEnabled));

        try
        {
            await _ble.ConnectAsync(_currentAddress);
        }
        catch (Exception exc)
        {
            RunOnUi(() => AppendError($"连接失败：请确认耳机已开机、且未被手机上的索尼 App 占用；若仍连不上，请到 Windows 设置→蓝牙里删除耳机后重新配对（配对时注意完成 LE 设备的配对请求），再重试。原始错误：{exc.Message}"));
        }
        finally
        {
            RunOnUi(() =>
            {
                _isConnectingUi = false;
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(IsConnectButtonEnabled));
            });
        }
    }

    public async Task DisconnectManualAsync()
    {
        _popupDelayCts?.Cancel();
        bool wasConn = _wasConnected;
        _wasConnected = false;
        await _ble.DisconnectAsync();
        if (wasConn && _autoPause)
        {
            SendMediaPlayPauseKey();
        }
    }

    // ── Page 01 业务下发 ──────────────────────────────────────────────────────

    public void SetNoiseControlMode(NoiseControlMode mode)
    {
        NoiseControlMode = mode;
        ApplyNcAsm();
    }

    public void SetAmbientLevel(int level)
    {
        AmbientLevel = Math.Clamp(level, 1, 20);
        ApplyNcAsm();
    }

    public void SetVoiceFocus(bool enabled)
    {
        VoiceFocusEnabled = enabled;
        ApplyNcAsm();
    }

    public void SetAutoAmbient(bool enabled)
    {
        AutoAmbientEnabled = enabled;
        ApplyNcAsm();
    }

    /// <summary>供第三阶段托盘菜单与电量弹窗快捷切换降噪模式。</summary>
    public void QuickSetNoiseControl(NoiseControlMode mode) => SetNoiseControlMode(mode);

    private void ApplyNcAsm()
    {
        byte[] msg = Table1.BuildSetNoiseControlMode(
            controlMode: _noiseControlMode,
            ambientLevel: _ambientLevel,
            ambientMode: _voiceFocusEnabled ? AmbientSoundMode.Voice : AmbientSoundMode.Normal,
            ncType: _ncType,
            noiseAdaptive: _autoAmbientEnabled);
        Send(msg);
    }

    public void SelectEqPreset(EqPresetId preset)
    {
        SelectedEqPreset = preset;
        IReadOnlyList<int>? steps = preset is EqPresetId.Custom or EqPresetId.UserSetting1 or EqPresetId.UserSetting2
            ? _eqBandSteps
            : null;
        Send(Table1.BuildSetEqPreset(preset, _eqType, steps));
        // 与 parity-checklist §1.1 一致：下发预设后立即回读一次 EQ 参数同步
        Send(Table1.BuildGetEqEbbParam(_eqType));
    }

    public void ApplyCustomEqBands(IReadOnlyList<int> steps)
    {
        _eqBandSteps = steps.ToArray();
        SelectedEqPreset = EqPresetId.Custom;
        Send(Table1.BuildSetEqPreset(EqPresetId.Custom, _eqType, _eqBandSteps));
    }

    public void SetDsee(bool enabled)
    {
        DseeEnabled = enabled;
        Send(Table1.BuildSetUpscaling(enabled, _upscalingInq));
    }

    public void SetSoundQualityPriority(bool priority)
    {
        SoundQualityPriority = priority;
        ConnectionQualityMode mode = priority
            ? ConnectionQualityMode.SoundQualityPrior
            : ConnectionQualityMode.ConnectionQualityPrior;
        Send(Table1.BuildSetConnectionQuality(mode));
    }

    // ── Page 02 业务下发 ──────────────────────────────────────────────────────

    public void SetSpeakToChat(bool enabled)
    {
        SpeakToChatEnabled = enabled;
        Send(Table1.BuildSetSpeakToChatEnabled(enabled));
    }

    public void SetMultipoint(bool enabled)
    {
        MultipointEnabled = enabled;
        // 与 parity-checklist §1.2 一致：仅当已协商出 _multipointSlot 才下发
        if (_multipointSlot.HasValue)
        {
            Send(Table1.BuildSetGeneralSetting(_multipointSlot.Value, enabled));
        }
    }

    public void SetCinemaMode(bool enabled)
    {
        CinemaModeEnabled = enabled;
        Send(Table1.BuildSetCinemaMode(enabled));
    }

    public void SetHeadphoneVolume(int volume)
    {
        int clamped = Math.Clamp(volume, 0, 100);
        HeadphoneVolume = clamped;
        Send(Table1.BuildSetPlaybackVolume(clamped));
    }

    public void SendPlaybackControl(PlaybackControl control) =>
        Send(Table1.BuildPlayback(control));

    public void PowerOffHeadphones()
    {
        if (!IsConnected)
        {
            return;
        }

        Send(Table1.BuildPowerOff());
    }

    // ── 日志操作 ──────────────────────────────────────────────────────────────

    public void ClearLogs() => RunOnUi(() => LogEntries.Clear());

    public string ExportAllLogsText() =>
        string.Join(Environment.NewLine, LogEntries.Select(e => e.FormattedLine));

    // ── 协议发送与握手后初始化查询序列（parity-checklist §2 逐条对齐） ──────────

    private void Send(byte[] tandemBytes)
    {
        if (!IsConnected)
        {
            return;
        }

        string hex = Convert.ToHexString(tandemBytes);
        RunOnUi(() => AppendLog(ProtocolLogLevel.Frame, "TX", hex));
        _ble.Send(tandemBytes);
    }

    private void QueryInitialState()
    {
        // 按 parity-checklist §2 顺序下发 18 条 + 4 条通用设置槽位（0xD1..0xD4）
        Send(Table1.BuildGetProtocolInfo());
        Send(Table1.BuildGetCapabilityInfo());
        Send(Table1.BuildGetDeviceInfo(DeviceInfoType.ModelName));
        Send(Table1.BuildGetDeviceInfo(DeviceInfoType.FwVersion));
        Send(Table1.BuildGetSupportFunction());
        Send(Table1.BuildGetBatteryStatus(PowerInquiredType.Battery));
        Send(Table1.BuildGetAudioCodecStatus());
        Send(Table1.BuildGetUpscalingEffectStatus());
        Send(Table1.BuildGetUpscaling(_upscalingInq));
        Send(Table1.BuildGetConnectionQuality());
        Send(Table1.BuildGetSpeakToChatParam());
        Send(Table1.BuildGetCinemaMode());
        Send(Table1.BuildGetAutoPowerOff(0x05));
        Send(Table1.BuildGetPlaybackParam(PlayInquiredType.MusicVolume));

        for (byte slot = 0xD1; slot <= 0xD4; slot++)
        {
            Send(Table1.BuildGetGeneralSettingCapability(slot));
        }
    }

    // ── BleService 事件处理 ───────────────────────────────────────────────────

    private void OnBleConnectionStateChanged(object? sender, BleConnectionState newState)
    {
        RunOnUi(() =>
        {
            ConnectionState = newState;
            if (newState == BleConnectionState.Connected)
            {
                HandleConnected();
            }
        });
    }

    private void HandleConnected()
    {
        _wasConnected = true;
        _lowBatteryNotified = false;
        _multipointSlot = null;
        RaiseSettingsChanged();
        QueryInitialState();

        if (_autoPlay)
        {
            SendMediaPlayPauseKey();
        }

        if (_defaultVolume > 0)
        {
            Send(Table1.BuildSetPlaybackVolume(_defaultVolume));
        }

        if (_showPopupOnConnect)
        {
            _popupDelayCts?.Cancel();
            var cts = new CancellationTokenSource();
            _popupDelayCts = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(900, cts.Token);
                    RunOnUi(() =>
                    {
                        if (IsConnected)
                        {
                            ConnectPopupRequested?.Invoke(this, EventArgs.Empty);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                }
            });
        }
    }

    private void OnBleDisconnected(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            _popupDelayCts?.Cancel();
            if (_wasConnected && _autoPause)
            {
                SendMediaPlayPauseKey();
            }

            _wasConnected = false;
        });
    }

    private void OnBleFrameReceived(object? sender, TandemFrame frame)
    {
        string hex = frame.Payload.Length > 0 ? Convert.ToHexString(frame.Payload) : "(empty)";
        RunOnUi(() => AppendLog(
            ProtocolLogLevel.Frame,
            "RX",
            $"type={frame.FrameType}(0x{(byte)frame.FrameType:X2}) seq={frame.Sequence} payload={hex}"));
    }

    private void OnBleLogMessage(object? sender, string message)
    {
        RunOnUi(() => AppendLog(ProtocolLogLevel.Info, "BLE", message));
    }

    private void OnBlePayloadReceived(object? sender, byte[] tandemBytes)
    {
        TandemResponse response = Table1.Parse(tandemBytes);
        RunOnUi(() => HandleResponse(response));
    }

    // ── 协议响应分发（parity-checklist §1 & §2 逐条对齐） ─────────────────────

    private void HandleResponse(TandemResponse response)
    {
        switch (response)
        {
            case CapabilityInfoResponse cap:
                AppendLog(ProtocolLogLevel.Info, "PROTO", $"CapabilityInfo counter={cap.Counter} id={cap.Identifier}");
                break;

            case DeviceInfoResponse dev:
                if (dev.InfoType == DeviceInfoType.ModelName && !string.IsNullOrWhiteSpace(dev.Text))
                {
                    ModelName = dev.Text;
                    RaiseSettingsChanged();
                }
                else if (dev.InfoType == DeviceInfoType.FwVersion && !string.IsNullOrWhiteSpace(dev.Text))
                {
                    FirmwareVersion = dev.Text;
                }

                break;

            case SupportFunctionResponse sf:
                var codes = new HashSet<byte>(sf.Functions.Select(f => f.FunctionCode));
                if (codes.Contains(0x6D))
                {
                    _ncType = NcAsmInquiredType.ModeNcAsmDualNcModeSwitchAndAsmSeamlessNa;
                }
                else if (codes.Contains(0x68))
                {
                    _ncType = NcAsmInquiredType.ModeNcAsmDualNcModeSwitchAndAsmSeamless;
                }

                if (codes.Contains(0x57))
                {
                    _eqType = EqEbbInquiredType.PresetEqAndErrorcode;
                }
                else if (codes.Contains(0x51))
                {
                    _eqType = EqEbbInquiredType.PresetEq;
                }

                Send(Table1.BuildGetNcAsmParam(_ncType));
                Send(Table1.BuildGetEqEbbCapability(_eqType));
                Send(Table1.BuildGetEqEbbExtendedInfo(_eqType));
                Send(Table1.BuildGetEqEbbParam(_eqType));
                break;

            case BatteryResponse bat:
                if ((bat.Kind is PowerInquiredType.Battery or PowerInquiredType.LeftRightBattery or PowerInquiredType.CradleBattery)
                    && bat.Levels.Count > 0 && bat.Levels[0].HasValue)
                {
                    int pct = bat.Levels[0]!.Value;
                    if (pct > 0)
                    {
                        BatteryPercent = pct;
                        if (pct <= _lowBatteryThreshold && !_lowBatteryNotified)
                        {
                            _lowBatteryNotified = true;
                            LowBatteryAlertRequested?.Invoke(this, (_modelName, pct));
                        }
                    }
                }

                break;

            case AudioCodecResponse codec:
                AudioCodecLabel = codec.Codec.GetLabel();
                break;

            case UpscalingEffectResponse eff:
                string stateStr = eff.State == DseeEffectState.Valid ? "生效中" : "待机";
                AppendLog(ProtocolLogLevel.Info, "DSEE", $"DSEE: {eff.Generation.GetLabel()} ({stateStr})");
                break;

            case NoiseControlResponse nc:
                if (nc.ControlMode.HasValue)
                {
                    NoiseControlMode = nc.ControlMode.Value;
                }

                if (nc.AmbientLevel.HasValue)
                {
                    AmbientLevel = Math.Clamp(nc.AmbientLevel.Value, 1, 20);
                }

                if (nc.AmbientMode.HasValue)
                {
                    VoiceFocusEnabled = nc.AmbientMode.Value == AmbientSoundMode.Voice;
                }

                if (nc.NoiseAdaptive.HasValue)
                {
                    AutoAmbientEnabled = nc.NoiseAdaptive.Value;
                }

                break;

            case EqCapabilityResponse eqCap:
                if (eqCap.Presets.Count > 0)
                {
                    EqPresets.Clear();
                    foreach (EqPresetId p in eqCap.Presets)
                    {
                        EqPresets.Add(CreatePresetOption(p));
                    }
                }

                break;

            case EqExtendedInfoResponse eqExt:
                int bandCount = eqExt.BandCount > 0 ? eqExt.BandCount : 10;
                bool isTenBandNoClearBass = bandCount >= 10 && !eqExt.HasClearBass;
                _eqBandCount = bandCount;
                _eqRawMax = isTenBandNoClearBass ? 12 : 20;
                _eqCenter = isTenBandNoClearBass ? 6 : 10;
                if (_eqBandSteps.Length != _eqBandCount)
                {
                    _eqBandSteps = Enumerable.Repeat(_eqCenter, _eqBandCount).ToArray();
                }

                EqGraphSyncRequested?.Invoke(this, EventArgs.Empty);
                break;

            case EqEbbResponse eq:
                if (eq.Preset.HasValue)
                {
                    SelectedEqPreset = eq.Preset.Value;
                }

                if (eq.BandSteps.Count > 0)
                {
                    if (eq.BandSteps.Count != _eqBandCount)
                    {
                        _eqBandCount = eq.BandSteps.Count;
                    }

                    _eqBandSteps = eq.BandSteps.Select(b => (int)b).ToArray();
                    EqGraphSyncRequested?.Invoke(this, EventArgs.Empty);
                }

                break;

            case UpscalingParamResponse up:
                _upscalingInq = up.InquiredType;
                DseeEnabled = up.Enabled;
                break;

            case ConnectionQualityResponse cq:
                SoundQualityPriority = cq.Mode != ConnectionQualityMode.ConnectionQualityPrior;
                break;

            case SpeakToChatResponse stc:
                SpeakToChatEnabled = stc.Enabled;
                break;

            case GeneralSettingCapabilityResponse gsCap:
                if (gsCap.Title.Contains("MULTIPOINT", StringComparison.OrdinalIgnoreCase))
                {
                    _multipointSlot = gsCap.Slot;
                    Send(Table1.BuildGetGeneralSettingParam(gsCap.Slot));
                }

                break;

            case GeneralSettingParamResponse gsParam:
                if (_multipointSlot.HasValue && gsParam.Slot == _multipointSlot.Value)
                {
                    MultipointEnabled = gsParam.Enabled;
                }

                break;

            case PeripheralDevicesResponse peri:
                if (peri.Devices.Count == 0)
                {
                    PairedDevicesText = "--";
                }
                else
                {
                    PairedDevicesText = string.Join(
                        " · ",
                        peri.Devices.Select(d => d.IsConnected ? $"{d.Name} [已连接]" : d.Name));
                }

                break;

            case AutoPowerOffResponse apo:
                if (apo.Values.Count > 0)
                {
                    byte val = apo.Values[0];
                    AutoPowerOffText = val == 0x10
                        ? "取下耳机自动关机 (已启用)"
                        : $"状态码 0x{val:X2}";
                }

                break;

            case PlaybackVolumeResponse vol:
                if (vol.PlayType == PlayInquiredType.MusicVolume)
                {
                    HeadphoneVolume = Math.Clamp((int)vol.Volume, 0, 100);
                }

                break;

            case AlertNtfyParamResponse alert:
                // 与 parity-checklist §2 一致：收到设备告警通知参数后自动回肯定应答
                Send(Table1.BuildReplyAlertFixedMessage(alert.MessageType, positive: true));
                break;
        }
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────────

    private static EqPresetOption CreatePresetOption(EqPresetId preset)
    {
        if (PresetShortNames.TryGetValue(preset, out string? shortName))
        {
            return new EqPresetOption(preset, shortName);
        }

        string label = preset.GetLabel();
        int spaceIdx = label.IndexOf(' ');
        return new EqPresetOption(preset, spaceIdx > 0 ? label[..spaceIdx] : label);
    }

    private void AppendLog(ProtocolLogLevel level, string source, string message)
    {
        if (!_isDeveloperLogEnabled)
        {
            return;
        }

        LogEntries.Add(new ProtocolLogEntry(DateTime.Now, level, source, message));
        if (LogEntries.Count > MaxLogEntries)
        {
            LogEntries.RemoveAt(0);
        }
    }

    private void AppendError(string message)
    {
        AppendLog(ProtocolLogLevel.Error, "ERR", $"[错误] {message}");
    }

    private void RaiseSettingsChanged() => SettingsChanged?.Invoke(this, ExportSettings());

    private void RunOnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.TryEnqueue(() => action());
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static void SendMediaPlayPauseKey()
    {
        try
        {
            keybd_event(VkMediaPlayPause, 0, 0, UIntPtr.Zero);
            keybd_event(VkMediaPlayPause, 0, KeyeventfKeyup, UIntPtr.Zero);
        }
        catch
        {
        }
    }
}
