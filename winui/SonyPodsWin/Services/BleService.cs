using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SonyPodsWin.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace SonyPodsWin.Services;

/// <summary>
/// 扫描并发现的 Sony 蓝牙设备信息（覆盖经典蓝牙 RFCOMM SPP 与 BLE 设备，对应 DiscoveredDevice）。
/// </summary>
public sealed record DiscoveredDevice(string Name, string Address, short? Rssi = null);

public enum BleConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>
/// 基于原生 Windows.Devices.Bluetooth WinRT API 的 Sony Tandem 双通道传输服务。
/// 优先支持 Classic Bluetooth RFCOMM SPP V2/V1（如 WH-1000XM6 等采用 SPP 承载 Tandem 协议栈的机型），同时保留 BLE GATT 通道与断线 8 秒自动重连。
/// </summary>
public sealed class BleService : IAsyncDisposable
{
    private const ushort SonyCompanyId = 0x012D;
    private static readonly Guid GapServiceUuid = Guid.Parse("00001800-0000-1000-8000-00805f9b34fb");
    private static readonly Guid GapDeviceNameUuid = Guid.Parse("00002a00-0000-1000-8000-00805f9b34fb");
    private static readonly string[] SonyNameKeywords = { "wh-1000", "wf-1000", "linkbuds", "sony", "ult" };

    private static readonly (Guid ServiceUuid, Guid ToAccUuid, Guid FromAccUuid)[] GattCandidates =
    {
        (SonyGatt.TandemV2HpcServiceGuid, SonyGatt.TandemHpcToAccGuid, SonyGatt.TandemHpcFromAccGuid),
        (SonyGatt.TandemV2McServiceGuid, SonyGatt.TandemMcToAccGuid, SonyGatt.TandemMcFromAccGuid),
        (SonyGatt.TandemV1McServiceGuid, SonyGatt.TandemMcToAccGuid, SonyGatt.TandemMcFromAccGuid),
    };

    /// <summary>断线后重连等待间隔（按需求固定 8 秒）。</summary>
    public static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(8);

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _rxLock = new();
    private readonly object _ackLock = new();

    private readonly List<byte> _rxBuf = new(512);
    private bool _inFrame;

    private BluetoothLEDevice? _device;
    private GattSession? _gattSession;
    private GattDeviceService? _service;
    private GattCharacteristic? _toAccChar;
    private GattCharacteristic? _fromAccChar;
    private int _writableLen = 200;

    private BluetoothDevice? _rfcommDevice;
    private RfcommDeviceService? _rfcommService;
    private StreamSocket? _rfcommSocket;
    private Task? _rxTask;

    private byte _nextTxSeq;
    private byte? _awaitingAck;
    private TaskCompletionSource<bool>? _ackTcs;
    private Channel<(SonySppFrameType FrameType, byte[] Payload)>? _txChannel;
    private CancellationTokenSource? _sessionCts;
    private Task? _txTask;

    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private string? _lastAddress;
    private string? _preferredName;
    private bool _manualDisconnect;
    private volatile bool _closed = true;
    private int _disconnectSignaled;
    private BleConnectionState _state = BleConnectionState.Disconnected;

    /// <summary>是否启用意外断线后 8 秒自动重连（默认开启）。</summary>
    public bool AutoReconnect { get; set; } = true;

    public BleConnectionState State => _state;

    public string? ConnectedAddress => _lastAddress;

    public string? ConnectedDeviceName { get; private set; }

    public bool IsConnected =>
        !_closed &&
        ((_device is not null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected) ||
         (_rfcommSocket is not null && _rfcommDevice is not null && _rfcommDevice.ConnectionStatus == BluetoothConnectionStatus.Connected));

    public int WritableValueLength => _writableLen;

    /// <summary>连接状态变更事件。</summary>
    public event EventHandler<BleConnectionState>? ConnectionStateChanged;

    /// <summary>收到并完成帧型归一化后的 Tandem 业务消息字节（首字节已补回 0x0E 或 0x0F）。</summary>
    public event EventHandler<byte[]>? PayloadReceived;

    /// <summary>收到并通过校验的原始帧事件（含帧型、seq 与未补 dataType 的帧负载）。</summary>
    public event EventHandler<TandemFrame>? FrameReceived;

    /// <summary>日志事件。</summary>
    public event EventHandler<string>? LogMessage;

    /// <summary>意外断线事件（与 Python on_disconnected 对应）。</summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// 扫描已配对（经典蓝牙 SPP + BLE）与附近广播中的 Sony 设备。
    /// </summary>
    public static async Task<IReadOnlyList<DiscoveredDevice>> ScanDevicesAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan scanDuration = timeout ?? TimeSpan.FromSeconds(4.0);
        var byAddr = new ConcurrentDictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        var classicAddrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classicPreferredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sonyMfgAddrs = new ConcurrentDictionary<ulong, short>();

        // 1a. 枚举 Windows 已配对的经典蓝牙 Sony 设备（如 WH-1000XM6 的 RFCOMM SPP V2 通道）
        try
        {
            string classicSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            DeviceInformationCollection classicInfos = await DeviceInformation.FindAllAsync(classicSelector).AsTask(cancellationToken);
            foreach (DeviceInformation info in classicInfos)
            {
                string name = info.Name ?? string.Empty;
                if (!MatchesSonyName(name))
                {
                    continue;
                }

                using BluetoothDevice? btDev = await BluetoothDevice.FromIdAsync(info.Id).AsTask(cancellationToken);
                if (btDev is null)
                {
                    continue;
                }

                bool connected = btDev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                bool hasSpp = await HasTandemRfcommAsync(btDev, allowUncached: connected, cancellationToken);
                if (connected && !hasSpp)
                {
                    continue;
                }

                string addr = FormatBluetoothAddress(btDev.BluetoothAddress);
                string displayName = NormalizeDisplayName(string.IsNullOrEmpty(name) ? addr : name);
                byAddr[addr] = new DiscoveredDevice(displayName, addr, connected ? (short)0 : null);
                classicAddrs.Add(addr);
                if (hasSpp || connected)
                {
                    classicPreferredNames.Add(displayName);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 经典蓝牙枚举失败时不阻断 BLE 扫描
        }

        // 1b. 枚举 Windows 已配对的 Sony BLE 设备
        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            DeviceInformationCollection infos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
            foreach (DeviceInformation info in infos)
            {
                string name = info.Name ?? string.Empty;
                if (!MatchesSonyName(name))
                {
                    continue;
                }

                using BluetoothLEDevice? leDev = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(cancellationToken);
                if (leDev is null)
                {
                    continue;
                }

                string addr = FormatBluetoothAddress(leDev.BluetoothAddress);
                byAddr.TryAdd(addr, new DiscoveredDevice(
                    Name: string.IsNullOrEmpty(name) ? addr : NormalizeDisplayName(name),
                    Address: addr,
                    Rssi: null));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 与 Python 原版一致：已配对枚举失败不阻断主动扫描
        }

        // 2. 主动 BLE 广播扫描（含 Sony 0x012D 厂商广播识别，避免部分固件主广播无 LocalName 漏检）
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };

        void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            string localName = args.Advertisement?.LocalName ?? string.Empty;
            IList<Guid>? serviceUuids = args.Advertisement?.ServiceUuids;

            bool hasSonyUuid = serviceUuids is not null && (
                serviceUuids.Contains(SonyGatt.TandemV2HpcServiceGuid) ||
                serviceUuids.Contains(SonyGatt.TandemV2McServiceGuid) ||
                serviceUuids.Contains(SonyGatt.TandemV1McServiceGuid));
            bool hasSonyMfg = args.Advertisement?.ManufacturerData?.Any(m => m.CompanyId == SonyCompanyId) ?? false;

            if (!hasSonyUuid && !MatchesSonyName(localName))
            {
                if (hasSonyMfg)
                {
                    sonyMfgAddrs.AddOrUpdate(
                        args.BluetoothAddress,
                        args.RawSignalStrengthInDBm,
                        (_, prev) => Math.Max(prev, args.RawSignalStrengthInDBm));
                    string mfgAddr = FormatBluetoothAddress(args.BluetoothAddress);
                    if (byAddr.TryGetValue(mfgAddr, out var existingMfg))
                    {
                        byAddr[mfgAddr] = existingMfg with { Rssi = args.RawSignalStrengthInDBm };
                    }
                }

                return;
            }

            string addr = FormatBluetoothAddress(args.BluetoothAddress);
            string displayName = !string.IsNullOrEmpty(localName)
                ? localName
                : (byAddr.TryGetValue(addr, out var existing) ? existing.Name : addr);

            byAddr[addr] = new DiscoveredDevice(NormalizeDisplayName(displayName), addr, args.RawSignalStrengthInDBm);
        }

        watcher.Received += OnReceived;
        try
        {
            watcher.Start();
            await Task.Delay(scanDuration, cancellationToken);
        }
        finally
        {
            watcher.Stop();
            watcher.Received -= OnReceived;
        }

        // 3. 解析无 LocalName 的 Sony 0x012D / 已发现 BLE 设备真实名称
        foreach (var kv in sonyMfgAddrs)
        {
            string candidateAddr = FormatBluetoothAddress(kv.Key);
            if (byAddr.TryGetValue(candidateAddr, out var existing) &&
                !string.Equals(existing.Name, existing.Address, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? resolved = await TryResolveBleDeviceNameAsync(kv.Key, allowGapRead: kv.Value >= -75, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resolved) && MatchesSonyName(resolved))
            {
                string normalized = NormalizeDisplayName(resolved);
                if (!classicPreferredNames.Contains(normalized))
                {
                    byAddr[candidateAddr] = new DiscoveredDevice(normalized, candidateAddr, kv.Value);
                }
            }
        }

        foreach (var kv in byAddr.ToArray())
        {
            if (!string.Equals(kv.Value.Name, kv.Value.Address, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? resolved = await TryResolveBleDeviceNameAsync(ParseBluetoothAddress(kv.Value.Address), allowGapRead: false, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                byAddr[kv.Key] = kv.Value with { Name = NormalizeDisplayName(resolved) };
            }
        }

        // 4. 若已配对的经典蓝牙设备支持 Tandem SPP，去重同型号的冗余 BLE 条目，避免列表重复且用户误选无法建链的 LE 项
        return byAddr.Values
            .Where(d => classicAddrs.Contains(d.Address) || !classicPreferredNames.Contains(d.Name))
            .OrderByDescending(d => d.Rssi ?? -1)
            .ToList();
    }

    public Task ConnectAsync(string address, CancellationToken cancellationToken = default) =>
        ConnectAsync(address, preferredName: null, cancellationToken);

    public async Task ConnectAsync(string address, string? preferredName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Bluetooth address cannot be empty.", nameof(address));
        }

        CancelReconnectLoop();

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            _manualDisconnect = false;
            _lastAddress = address.Trim().ToUpperInvariant();
            _preferredName = string.IsNullOrWhiteSpace(preferredName) ? null : NormalizeDisplayName(preferredName);
            await DisconnectCoreAsync(updateState: false);
            SetState(BleConnectionState.Connecting);
            await ConnectCoreAsync(_lastAddress, cancellationToken);
        }
        catch
        {
            SetState(BleConnectionState.Disconnected);
            throw;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        _manualDisconnect = true;
        CancelReconnectLoop();

        await _lifecycleLock.WaitAsync();
        try
        {
            await DisconnectCoreAsync(updateState: true);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// 发送 Tandem 消息字节（首字节应为 0x0E 或 0x0F，由 SonySppPayloadMapper 剥离并选择帧型）。
    /// </summary>
    public void Send(ReadOnlySpan<byte> tandemBytes)
    {
        if (_closed)
        {
            return;
        }

        var (ftype, payload) = SonySppPayloadMapper.OutboundFromTandemBytes(tandemBytes);
        _txChannel?.Writer.TryWrite((ftype, payload));
    }

    public void Send(byte[] tandemBytes) => Send(tandemBytes.AsSpan());

    private async Task ConnectCoreAsync(string address, CancellationToken cancellationToken)
    {
        ulong btAddr = ParseBluetoothAddress(address);

        // 1. 若目标是已配对的经典蓝牙设备（或包含 Tandem RFCOMM SPP 服务），直接走 RFCOMM SPP 通道
        DeviceInformation? classicInfo = await FindClassicPairedInfoByAddressAsync(btAddr, cancellationToken);
        if (classicInfo is not null)
        {
            await ConnectRfcommCoreAsync(classicInfo, address, cancellationToken);
            return;
        }

        // 2. 尝试 BLE GATT 控制通道（保留对基于 GATT 承载 Tandem 协议的机型/固件兼容）
        try
        {
            await ConnectBleGattCoreAsync(btAddr, address, cancellationToken);
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception bleExc)
        {
            EmitLog($"BLE GATT 控制通道不可用 ({bleExc.Message})，正在回退至经典蓝牙 RFCOMM SPP 通道或执行重扫描...");

            // 3. 回退到匹配的已配对经典蓝牙 Sony SPP 设备（解决仅显示 LE 条目或旧配置残留 LE 地址的问题）
            DeviceInformation? fallbackClassic = await FindMatchingClassicSppCandidateAsync(_preferredName, cancellationToken);
            if (fallbackClassic is not null)
            {
                try
                {
                    await ConnectRfcommCoreAsync(fallbackClassic, null, cancellationToken);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception rfcommExc)
                {
                    EmitLog($"RFCOMM SPP 回退连接失败: {rfcommExc.Message}");
                }
            }

            // 4. 若 BLE 原地址已失效（例如未配对 RPA 轮换），刷新扫描一次并按名称重连
            if (bleExc.Message.StartsWith("无法打开 BLE 设备", StringComparison.Ordinal))
            {
                IReadOnlyList<DiscoveredDevice> rescanned = await ScanDevicesAsync(TimeSpan.FromSeconds(3.5), cancellationToken);
                DiscoveredDevice? target = rescanned.FirstOrDefault(d =>
                    !string.IsNullOrEmpty(_preferredName) &&
                    string.Equals(d.Name, _preferredName, StringComparison.OrdinalIgnoreCase))
                    ?? rescanned.FirstOrDefault();

                if (target is not null && !string.Equals(target.Address, address, StringComparison.OrdinalIgnoreCase))
                {
                    EmitLog($"已通过设备重扫描解析最新蓝牙地址: {target.Name} ({target.Address})");
                    _lastAddress = target.Address;
                    ulong newAddr = ParseBluetoothAddress(target.Address);
                    DeviceInformation? newClassic = await FindClassicPairedInfoByAddressAsync(newAddr, cancellationToken);
                    if (newClassic is not null)
                    {
                        await ConnectRfcommCoreAsync(newClassic, target.Address, cancellationToken);
                    }
                    else
                    {
                        await ConnectBleGattCoreAsync(newAddr, target.Address, cancellationToken);
                    }

                    return;
                }
            }

            throw;
        }
    }

    private async Task ConnectRfcommCoreAsync(
        DeviceInformation classicInfo,
        string? explicitAddress,
        CancellationToken cancellationToken)
    {
        BluetoothDevice? btDev = await BluetoothDevice.FromIdAsync(classicInfo.Id).AsTask(cancellationToken);
        if (btDev is null)
        {
            throw new InvalidOperationException($"无法打开经典蓝牙设备: {classicInfo.Name}");
        }

        RfcommDeviceService? sppSvc = null;
        StreamSocket? socket = null;

        try
        {
            sppSvc = await GetPreferredRfcommServiceAsync(btDev, BluetoothCacheMode.Uncached, cancellationToken)
                  ?? await GetPreferredRfcommServiceAsync(btDev, BluetoothCacheMode.Cached, cancellationToken);

            if (sppSvc is null)
            {
                throw new InvalidOperationException("未检测到 Sony Tandem RFCOMM SPP 服务，请确认耳机已开机并与 Windows 完成经典蓝牙连接");
            }

            socket = new StreamSocket();
            await socket
                .ConnectAsync(
                    sppSvc.ConnectionHostName,
                    sppSvc.ConnectionServiceName,
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication)
                .AsTask(cancellationToken);

            string resolvedAddr = explicitAddress ?? FormatBluetoothAddress(btDev.BluetoothAddress);
            _rfcommDevice = btDev;
            _rfcommService = sppSvc;
            _rfcommSocket = socket;
            _lastAddress = resolvedAddr;
            ConnectedDeviceName = NormalizeDisplayName(string.IsNullOrWhiteSpace(btDev.Name) ? classicInfo.Name : btDev.Name);

            ResetSessionBuffers();
            _rfcommDevice.ConnectionStatusChanged += OnRfcommConnectionStatusChanged;
            StartSessionLoops(startRfcommRx: true);

            EmitLog($"已建立 RFCOMM SPP 控制通道: {ConnectedDeviceName} ({resolvedAddr})");
            SetState(BleConnectionState.Connected);
        }
        catch
        {
            if (_rfcommDevice is not null)
            {
                _rfcommDevice.ConnectionStatusChanged -= OnRfcommConnectionStatusChanged;
            }

            socket?.Dispose();
            sppSvc?.Dispose();
            btDev.Dispose();
            _rfcommSocket = null;
            _rfcommService = null;
            _rfcommDevice = null;
            _closed = true;
            throw;
        }
    }

    private async Task ConnectBleGattCoreAsync(ulong btAddr, string address, CancellationToken cancellationToken)
    {
        BluetoothLEDevice? device = await BluetoothLEDevice.FromBluetoothAddressAsync(btAddr).AsTask(cancellationToken);
        if (device is null)
        {
            throw new InvalidOperationException($"无法打开 BLE 设备: {address}");
        }

        GattSession? gattSession = null;
        GattDeviceService? matchedService = null;
        GattCharacteristic? toAcc = null;
        GattCharacteristic? fromAcc = null;
        GattCharacteristic? wlenChar = null;

        try
        {
            if (device.DeviceInformation?.Pairing is { IsPaired: false, CanPair: true })
            {
                EmitLog($"BLE 设备未配对，尝试发起 WinRT 配对: {address}");
                BluetoothLEDevice? pairedDev = await TryPairBleDeviceAsync(device, cancellationToken);
                if (pairedDev is not null)
                {
                    device.Dispose();
                    device = pairedDev;
                }
            }

            gattSession = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(cancellationToken);
            gattSession.MaintainConnection = true;

            // 等待 BLE 链路与 MTU 稳定后再做 Uncached 服务发现，避免刚配对/建链时立即返回 Unreachable
            for (int i = 0; i < 15 && device.ConnectionStatus != BluetoothConnectionStatus.Connected; i++)
            {
                await Task.Delay(200, cancellationToken);
            }

            if (device.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                await Task.Delay(600, cancellationToken);
            }

            // 按 Python 实机验证顺序依次查找：V2 HPC -> V2 MC -> V1 MC
            GattDeviceServicesResult servicesResult = await device
                .GetGattServicesAsync(BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken);

            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException($"GATT 服务发现失败: {servicesResult.Status}");
            }

            foreach (var (svcUuid, toUuid, fromUuid) in GattCandidates)
            {
                GattDeviceService? svc = servicesResult.Services.FirstOrDefault(s => s.Uuid == svcUuid);
                if (svc is null)
                {
                    continue;
                }

                GattCharacteristicsResult charsResult = await svc
                    .GetCharacteristicsAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken);

                if (charsResult.Status != GattCommunicationStatus.Success)
                {
                    svc.Dispose();
                    continue;
                }

                GattCharacteristic? cTo = charsResult.Characteristics.FirstOrDefault(c => c.Uuid == toUuid);
                GattCharacteristic? cFrom = charsResult.Characteristics.FirstOrDefault(c => c.Uuid == fromUuid);
                if (cTo is not null && cFrom is not null)
                {
                    matchedService = svc;
                    toAcc = cTo;
                    fromAcc = cFrom;
                    wlenChar = charsResult.Characteristics.FirstOrDefault(c => c.Uuid == SonyGatt.WritableValueLengthGuid);
                    break;
                }

                svc.Dispose();
            }

            // 释放未命中的其他服务句柄
            foreach (GattDeviceService svc in servicesResult.Services)
            {
                if (!ReferenceEquals(svc, matchedService))
                {
                    svc.Dispose();
                }
            }

            if (matchedService is null || toAcc is null || fromAcc is null)
            {
                throw new InvalidOperationException("未找到 Sony Tandem GATT 服务特征");
            }

            // 按 Python 原版逻辑：不等待 DETERMINE_MTU，直接尝试读取 WRITABLE_VALUE_LENGTH（2 字节大端）；
            // 仅当读到 >0 的合法值时覆写 _writableLen，否则保留字段现值（首次默认 200，重连失败不退回 200）。
            if (wlenChar is not null)
            {
                try
                {
                    GattReadResult readResult = await wlenChar
                        .ReadValueAsync(BluetoothCacheMode.Uncached)
                        .AsTask(cancellationToken);

                    if (readResult.Status == GattCommunicationStatus.Success && readResult.Value is not null)
                    {
                        byte[] rawWlen = ReadBuffer(readResult.Value);
                        if (rawWlen.Length >= 2)
                        {
                            int val = (rawWlen[0] << 8) | rawWlen[1];
                            if (val > 0)
                            {
                                _writableLen = val;
                            }
                        }
                    }
                }
                catch (Exception exc)
                {
                    EmitLog($"读取 WRITABLE_VALUE_LENGTH 失败: {exc.Message}");
                }
            }

            _device = device;
            _gattSession = gattSession;
            _service = matchedService;
            _toAccChar = toAcc;
            _fromAccChar = fromAcc;
            _lastAddress = address;
            ConnectedDeviceName = NormalizeDisplayName(device.Name);

            ResetSessionBuffers();
            _device.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;
            _fromAccChar.ValueChanged += OnCharacteristicValueChanged;

            GattCommunicationStatus cccdStatus = await _fromAccChar
                .WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify)
                .AsTask(cancellationToken);

            if (cccdStatus != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException($"订阅 FROM_ACC Notify 失败: {cccdStatus}");
            }

            StartSessionLoops(startRfcommRx: false);

            EmitLog($"已连接 {address}, writable_len={_writableLen}");
            SetState(BleConnectionState.Connected);
        }
        catch
        {
            if (_fromAccChar is not null)
            {
                _fromAccChar.ValueChanged -= OnCharacteristicValueChanged;
            }

            if (_device is not null)
            {
                _device.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
            }

            matchedService?.Dispose();
            gattSession?.Dispose();
            device.Dispose();
            _device = null;
            _gattSession = null;
            _service = null;
            _toAccChar = null;
            _fromAccChar = null;
            _closed = true;
            throw;
        }
    }

    private void ResetSessionBuffers()
    {
        lock (_rxLock)
        {
            _rxBuf.Clear();
            _inFrame = false;
        }

        lock (_ackLock)
        {
            _nextTxSeq = 0;
            _awaitingAck = null;
            _ackTcs = null;
        }
    }

    private void StartSessionLoops(bool startRfcommRx)
    {
        _disconnectSignaled = 0;
        _closed = false;
        _sessionCts = new CancellationTokenSource();
        _txChannel = Channel.CreateUnbounded<(SonySppFrameType, byte[])>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        CancellationToken ct = _sessionCts.Token;
        _txTask = Task.Run(() => TxLoopAsync(ct));
        if (startRfcommRx && _rfcommSocket is not null)
        {
            StreamSocket socket = _rfcommSocket;
            _rxTask = Task.Run(() => RfcommRxLoopAsync(socket, ct));
        }
    }

    private async Task DisconnectCoreAsync(bool updateState)
    {
        _closed = true;

        _txChannel?.Writer.TryComplete();
        if (_sessionCts is not null)
        {
            try
            {
                _sessionCts.Cancel();
            }
            catch
            {
            }
        }

        if (_rfcommDevice is not null)
        {
            _rfcommDevice.ConnectionStatusChanged -= OnRfcommConnectionStatusChanged;
        }

        // 先关闭 Socket 以立即唤醒阻塞在 LoadAsync 的读取循环
        if (_rfcommSocket is not null)
        {
            try
            {
                _rfcommSocket.Dispose();
            }
            catch
            {
            }

            _rfcommSocket = null;
        }

        if (_txTask is not null)
        {
            try
            {
                await _txTask;
            }
            catch
            {
            }

            _txTask = null;
        }

        if (_rxTask is not null)
        {
            try
            {
                await _rxTask;
            }
            catch
            {
            }

            _rxTask = null;
        }

        _sessionCts?.Dispose();
        _sessionCts = null;
        _txChannel = null;

        _rfcommService?.Dispose();
        _rfcommService = null;

        if (_rfcommDevice is not null)
        {
            _rfcommDevice.Dispose();
            _rfcommDevice = null;
        }

        if (_fromAccChar is not null)
        {
            _fromAccChar.ValueChanged -= OnCharacteristicValueChanged;
            try
            {
                await _fromAccChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch
            {
            }

            _fromAccChar = null;
        }

        _toAccChar = null;

        _service?.Dispose();
        _service = null;

        _gattSession?.Dispose();
        _gattSession = null;

        if (_device is not null)
        {
            _device.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
            _device.Dispose();
            _device = null;
        }

        lock (_ackLock)
        {
            _awaitingAck = null;
            _ackTcs?.TrySetCanceled();
            _ackTcs = null;
        }

        if (updateState)
        {
            SetState(BleConnectionState.Disconnected);
        }
    }

    private void OnDeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            HandleUnexpectedDisconnect("BLE disconnected");
        }
    }

    private void OnRfcommConnectionStatusChanged(BluetoothDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            HandleUnexpectedDisconnect("Classic Bluetooth disconnected");
        }
    }

    private void HandleUnexpectedDisconnect(string reason)
    {
        if (_closed || Interlocked.CompareExchange(ref _disconnectSignaled, 1, 0) != 0)
        {
            return;
        }

        _closed = true;
        EmitLog(reason);
        Disconnected?.Invoke(this, EventArgs.Empty);

        if (!_manualDisconnect && AutoReconnect && !string.IsNullOrEmpty(_lastAddress))
        {
            StartReconnectLoop(_lastAddress);
        }
        else
        {
            SetState(BleConnectionState.Disconnected);
        }
    }

    private void StartReconnectLoop(string address)
    {
        CancelReconnectLoop();
        var cts = new CancellationTokenSource();
        _reconnectCts = cts;
        SetState(BleConnectionState.Reconnecting);

        _reconnectTask = Task.Run(async () =>
        {
            CancellationToken ct = cts.Token;
            while (!ct.IsCancellationRequested && !_manualDisconnect && AutoReconnect)
            {
                try
                {
                    EmitLog($"BLE 断线，{(int)ReconnectDelay.TotalSeconds} 秒后尝试重连 {address}...");
                    await Task.Delay(ReconnectDelay, ct);

                    await _lifecycleLock.WaitAsync(ct);
                    try
                    {
                        if (ct.IsCancellationRequested || _manualDisconnect)
                        {
                            return;
                        }

                        await DisconnectCoreAsync(updateState: false);
                        SetState(BleConnectionState.Reconnecting);
                        await ConnectCoreAsync(address, ct);
                        return;
                    }
                    finally
                    {
                        _lifecycleLock.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exc)
                {
                    EmitLog($"自动重连失败: {exc.Message}");
                }
            }
        });
    }

    private void CancelReconnectLoop()
    {
        if (_reconnectCts is not null)
        {
            try
            {
                _reconnectCts.Cancel();
            }
            catch
            {
            }

            _reconnectCts.Dispose();
            _reconnectCts = null;
        }

        _reconnectTask = null;
    }

    private async Task TxLoopAsync(CancellationToken ct)
    {
        if (_txChannel is null)
        {
            return;
        }

        try
        {
            await foreach (var (ftype, payload) in _txChannel.Reader.ReadAllAsync(ct))
            {
                if (_closed || ct.IsCancellationRequested)
                {
                    return;
                }

                byte seq;
                lock (_ackLock)
                {
                    seq = _nextTxSeq;
                }

                byte[] frame = SonyTandemFraming.Encode(ftype, seq, payload);
                FrameRetryPolicy? policy = ftype.GetRetryPolicy();

                if (policy is null)
                {
                    lock (_ackLock)
                    {
                        _nextTxSeq = SonyTandemFraming.InverseSequence(seq);
                    }

                    await WriteFragmentedAsync(frame, ct);
                    continue;
                }

                byte expectedAck = SonyTandemFraming.InverseSequence(seq);
                var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_ackLock)
                {
                    _awaitingAck = expectedAck;
                    _ackTcs = ackTcs;
                }

                TimeSpan timeout = policy.Value.Timeout;
                int maxRetries = policy.Value.MaxRetries;

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    if (_closed || ct.IsCancellationRequested)
                    {
                        return;
                    }

                    await WriteFragmentedAsync(frame, ct);

                    Task completed = await Task.WhenAny(ackTcs.Task, Task.Delay(timeout, ct));
                    if (completed == ackTcs.Task && ackTcs.Task.IsCompletedSuccessfully)
                    {
                        break;
                    }

                    if (attempt >= maxRetries)
                    {
                        EmitLog($"ACK 超时 seq={seq}");
                        lock (_ackLock)
                        {
                            // 与 Python 原版保持一致：ACK 超时耗尽后清空 _awaitingAck，不翻转 _nextTxSeq。
                            _awaitingAck = null;
                            _ackTcs = null;
                        }

                        break;
                    }

                    EmitLog($"ACK 超时重试 {attempt + 1}/{maxRetries} seq={seq}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exc)
        {
            EmitLog($"TX 循环异常: {exc.Message}");
        }
    }

    private async Task WriteFragmentedAsync(byte[] data, CancellationToken ct)
    {
        if (_closed)
        {
            return;
        }

        StreamSocket? rfcommSocket = _rfcommSocket;
        if (rfcommSocket is not null)
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                if (_closed || ct.IsCancellationRequested || !ReferenceEquals(_rfcommSocket, rfcommSocket))
                {
                    return;
                }

                using var writer = new DataWriter();
                writer.WriteBytes(data);
                IBuffer buffer = writer.DetachBuffer();
                await rfcommSocket.OutputStream.WriteAsync(buffer).AsTask(ct);
                await rfcommSocket.OutputStream.FlushAsync().AsTask(ct);
            }
            finally
            {
                _writeLock.Release();
            }

            return;
        }

        GattCharacteristic? toAcc = _toAccChar;
        if (toAcc is null)
        {
            return;
        }

        int limit = Math.Max(20, _writableLen);
        await _writeLock.WaitAsync(ct);
        try
        {
            for (int offset = 0; offset < data.Length; offset += limit)
            {
                if (_closed || ct.IsCancellationRequested)
                {
                    return;
                }

                int chunkLen = Math.Min(limit, data.Length - offset);
                using var writer = new DataWriter();
                byte[] chunk = new byte[chunkLen];
                System.Buffer.BlockCopy(data, offset, chunk, 0, chunkLen);
                writer.WriteBytes(chunk);
                IBuffer buffer = writer.DetachBuffer();

                // 与 Python bleak write_gatt_char(..., response=False) 逐项一致
                await toAcc
                    .WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse)
                    .AsTask(ct);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task RfcommRxLoopAsync(StreamSocket socket, CancellationToken ct)
    {
        var reader = new DataReader(socket.InputStream)
        {
            InputStreamOptions = InputStreamOptions.Partial,
        };

        try
        {
            while (!_closed && !ct.IsCancellationRequested)
            {
                uint count = await reader.LoadAsync(1024).AsTask(ct);
                if (count == 0)
                {
                    HandleUnexpectedDisconnect("RFCOMM 远程已断开");
                    return;
                }

                byte[] data = new byte[count];
                reader.ReadBytes(data);
                FeedRxBytes(data);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exc)
        {
            if (!_closed && !ct.IsCancellationRequested)
            {
                EmitLog($"RFCOMM 接收异常: {exc.Message}");
                HandleUnexpectedDisconnect("RFCOMM disconnected");
            }
        }
        finally
        {
            try
            {
                reader.DetachStream();
            }
            catch
            {
            }

            reader.Dispose();
        }
    }

    private void OnCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (args.CharacteristicValue is null)
        {
            return;
        }

        byte[] data = ReadBuffer(args.CharacteristicValue);
        FeedRxBytes(data);
    }

    private void FeedRxBytes(ReadOnlySpan<byte> data)
    {
        List<byte[]>? completed = null;

        lock (_rxLock)
        {
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b == SonyTandemFraming.FrameStart)
                {
                    _rxBuf.Clear();
                    _inFrame = true;
                }
                else if (b == SonyTandemFraming.FrameEnd)
                {
                    if (_inFrame)
                    {
                        completed ??= new List<byte[]>(1);
                        completed.Add(_rxBuf.ToArray());
                    }

                    _rxBuf.Clear();
                    _inFrame = false;
                }
                else if (_inFrame)
                {
                    _rxBuf.Add(b);
                }
            }
        }

        if (completed is null)
        {
            return;
        }

        foreach (byte[] escapedBody in completed)
        {
            ProcessFrame(escapedBody);
        }
    }

    private void ProcessFrame(byte[] escapedBody)
    {
        TandemFrame? frame = SonyTandemFraming.Decode(escapedBody);
        if (frame is null)
        {
            EmitLog("RX 帧校验失败");
            return;
        }

        FrameReceived?.Invoke(this, frame);

        if (frame.FrameType == SonySppFrameType.Ack)
        {
            lock (_ackLock)
            {
                if (_awaitingAck.HasValue && _awaitingAck.Value == frame.Sequence)
                {
                    _nextTxSeq = frame.Sequence;
                    _awaitingAck = null;
                    _ackTcs?.TrySetResult(true);
                    _ackTcs = null;
                }
            }

            return;
        }

        if (frame.FrameType.AckRequired())
        {
            byte ackSeq = SonyTandemFraming.InverseSequence(frame.Sequence);
            byte[] ackFrame = SonyTandemFraming.Encode(SonySppFrameType.Ack, ackSeq, ReadOnlySpan<byte>.Empty);
            CancellationToken ct = _sessionCts?.Token ?? CancellationToken.None;
            _ = Task.Run(async () =>
            {
                try
                {
                    await WriteFragmentedAsync(ackFrame, ct);
                }
                catch (Exception exc)
                {
                    EmitLog($"发送 ACK 失败: {exc.Message}");
                }
            });
        }

        byte[]? tandemBytes = SonySppPayloadMapper.InboundToTandemBytes(frame.FrameType, frame.Payload);
        if (tandemBytes is not null)
        {
            PayloadReceived?.Invoke(this, tandemBytes);
        }
    }

    private void SetState(BleConnectionState newState)
    {
        if (_state == newState)
        {
            return;
        }

        _state = newState;
        ConnectionStateChanged?.Invoke(this, newState);
    }

    private void EmitLog(string message) => LogMessage?.Invoke(this, message);

    public static string NormalizeDisplayName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        string trimmed = name.Trim();
        if (trimmed.StartsWith("LE_", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Substring(3);
        }

        return trimmed;
    }

    private static async Task<DeviceInformation?> FindClassicPairedInfoByAddressAsync(
        ulong btAddr,
        CancellationToken cancellationToken)
    {
        try
        {
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            DeviceInformationCollection infos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
            foreach (DeviceInformation info in infos)
            {
                using BluetoothDevice? btDev = await BluetoothDevice.FromIdAsync(info.Id).AsTask(cancellationToken);
                if (btDev is not null && btDev.BluetoothAddress == btAddr)
                {
                    return info;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return null;
    }

    private static async Task<DeviceInformation?> FindMatchingClassicSppCandidateAsync(
        string? preferredName,
        CancellationToken cancellationToken)
    {
        try
        {
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            DeviceInformationCollection infos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);

            DeviceInformation? connectedFallback = null;
            DeviceInformation? nameMatchFallback = null;

            foreach (DeviceInformation info in infos)
            {
                if (!MatchesSonyName(info.Name ?? string.Empty))
                {
                    continue;
                }

                using BluetoothDevice? btDev = await BluetoothDevice.FromIdAsync(info.Id).AsTask(cancellationToken);
                if (btDev is null)
                {
                    continue;
                }

                bool connected = btDev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                if (!await HasTandemRfcommAsync(btDev, allowUncached: connected, cancellationToken))
                {
                    continue;
                }

                string normalized = NormalizeDisplayName(info.Name ?? string.Empty);
                bool exactName = !string.IsNullOrWhiteSpace(preferredName) &&
                    string.Equals(normalized, NormalizeDisplayName(preferredName), StringComparison.OrdinalIgnoreCase);

                if (exactName && connected)
                {
                    return info;
                }

                if (exactName)
                {
                    nameMatchFallback ??= info;
                }

                if (connected)
                {
                    connectedFallback ??= info;
                }
            }

            return connectedFallback ?? nameMatchFallback;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> HasTandemRfcommAsync(
        BluetoothDevice btDev,
        bool allowUncached,
        CancellationToken cancellationToken)
    {
        if (await GetPreferredRfcommServiceAsync(btDev, BluetoothCacheMode.Cached, cancellationToken) is RfcommDeviceService cachedSvc)
        {
            cachedSvc.Dispose();
            return true;
        }

        if (!allowUncached)
        {
            return false;
        }

        if (await GetPreferredRfcommServiceAsync(btDev, BluetoothCacheMode.Uncached, cancellationToken) is RfcommDeviceService uncachedSvc)
        {
            uncachedSvc.Dispose();
            return true;
        }

        return false;
    }

    private static async Task<RfcommDeviceService?> GetPreferredRfcommServiceAsync(
        BluetoothDevice btDev,
        BluetoothCacheMode cacheMode,
        CancellationToken cancellationToken)
    {
        foreach (Guid uuid in new[] { SonyGatt.SppV2Guid, SonyGatt.SppV1Guid })
        {
            RfcommDeviceServicesResult res = await btDev
                .GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(uuid), cacheMode)
                .AsTask(cancellationToken);

            if (res.Error == BluetoothError.Success && res.Services.Count > 0)
            {
                for (int i = 1; i < res.Services.Count; i++)
                {
                    res.Services[i].Dispose();
                }

                return res.Services[0];
            }
        }

        return null;
    }

    private static async Task<string?> TryResolveBleDeviceNameAsync(
        ulong rawAddr,
        bool allowGapRead,
        CancellationToken cancellationToken)
    {
        try
        {
            using BluetoothLEDevice? cachedDev = await BluetoothLEDevice
                .FromBluetoothAddressAsync(rawAddr)
                .AsTask(cancellationToken);
            if (cachedDev is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(cachedDev.Name) &&
                !cachedDev.Name.StartsWith("Bluetooth ", StringComparison.OrdinalIgnoreCase))
            {
                return cachedDev.Name;
            }

            if (!allowGapRead)
            {
                return cachedDev.Name;
            }

            GattDeviceServicesResult gapRes = await cachedDev
                .GetGattServicesForUuidAsync(GapServiceUuid, BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken);
            if (gapRes.Status == GattCommunicationStatus.Success && gapRes.Services.Count > 0)
            {
                using GattDeviceService gapSvc = gapRes.Services[0];
                GattCharacteristicsResult chRes = await gapSvc
                    .GetCharacteristicsForUuidAsync(GapDeviceNameUuid, BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken);
                if (chRes.Status == GattCommunicationStatus.Success && chRes.Characteristics.Count > 0)
                {
                    GattReadResult valRes = await chRes.Characteristics[0]
                        .ReadValueAsync(BluetoothCacheMode.Uncached)
                        .AsTask(cancellationToken);
                    if (valRes.Status == GattCommunicationStatus.Success && valRes.Value is not null)
                    {
                        string gapName = Encoding.UTF8.GetString(ReadBuffer(valRes.Value)).TrimEnd('\0');
                        if (!string.IsNullOrWhiteSpace(gapName))
                        {
                            return gapName;
                        }
                    }
                }
            }

            return cachedDev.Name;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<BluetoothLEDevice?> TryPairBleDeviceAsync(BluetoothLEDevice device, CancellationToken cancellationToken)
    {
        try
        {
            DeviceInformation freshInfo = await DeviceInformation.CreateFromIdAsync(device.DeviceId).AsTask(cancellationToken);
            if (freshInfo.Pairing.IsPaired)
            {
                return await BluetoothLEDevice.FromIdAsync(device.DeviceId).AsTask(cancellationToken);
            }

            DeviceInformationCustomPairing custom = freshInfo.Pairing.Custom;
            void OnPairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
            {
                if (args.PairingKind is DevicePairingKinds.ConfirmOnly or DevicePairingKinds.ConfirmPinMatch or DevicePairingKinds.DisplayPin)
                {
                    args.Accept();
                }
            }

            custom.PairingRequested += OnPairingRequested;
            try
            {
                DevicePairingResult pairRes = await custom
                    .PairAsync(
                        DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.DisplayPin,
                        DevicePairingProtectionLevel.Encryption)
                    .AsTask(cancellationToken);

                EmitLog($"BLE 配对结果: {pairRes.Status} ({pairRes.ProtectionLevelUsed})");
                if (pairRes.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired)
                {
                    await Task.Delay(500, cancellationToken);
                    return await BluetoothLEDevice.FromIdAsync(device.DeviceId).AsTask(cancellationToken);
                }
            }
            finally
            {
                custom.PairingRequested -= OnPairingRequested;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exc)
        {
            EmitLog($"BLE 配对异常: {exc.Message}");
        }

        return null;
    }

    private static bool MatchesSonyName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        string lower = name.ToLowerInvariant();
        for (int i = 0; i < SonyNameKeywords.Length; i++)
        {
            if (lower.Contains(SonyNameKeywords[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string FormatBluetoothAddress(ulong rawAddress)
    {
        return string.Create(17, rawAddress, static (span, addr) =>
        {
            const string hex = "0123456789ABCDEF";
            int pos = 0;
            for (int i = 5; i >= 0; i--)
            {
                byte b = (byte)((addr >> (8 * i)) & 0xFF);
                span[pos++] = hex[b >> 4];
                span[pos++] = hex[b & 0x0F];
                if (i > 0)
                {
                    span[pos++] = ':';
                }
            }
        });
    }

    public static ulong ParseBluetoothAddress(string address)
    {
        string cleaned = address.Replace(":", string.Empty, StringComparison.Ordinal)
                                .Replace("-", string.Empty, StringComparison.Ordinal)
                                .Trim();
        if (cleaned.Length != 12 || !ulong.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value))
        {
            throw new FormatException($"无效的蓝牙 MAC 地址: {address}");
        }

        return value;
    }

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        using DataReader reader = DataReader.FromBuffer(buffer);
        byte[] bytes = new byte[buffer.Length];
        reader.ReadBytes(bytes);
        return bytes;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _lifecycleLock.Dispose();
        _writeLock.Dispose();
    }
}