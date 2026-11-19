using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SonyPodsWin.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace SonyPodsWin.Services;

/// <summary>
/// 扫描到的 Sony BLE 设备信息（对应 ble_transport.py 中的 DiscoveredDevice）。
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
/// 基于原生 Windows.Devices.Bluetooth WinRT API 的 Sony Tandem BLE GATT 传输服务。
/// 逐项等价于 ble_transport.py 中的 SonyBleTandemClient，并补齐断线后 8 秒自动重连与状态/帧事件上抛。
/// </summary>
public sealed class BleService : IAsyncDisposable
{
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
    private GattDeviceService? _service;
    private GattCharacteristic? _toAccChar;
    private GattCharacteristic? _fromAccChar;
    private int _writableLen = 200;

    private byte _nextTxSeq;
    private byte? _awaitingAck;
    private TaskCompletionSource<bool>? _ackTcs;
    private Channel<(SonySppFrameType FrameType, byte[] Payload)>? _txChannel;
    private CancellationTokenSource? _sessionCts;
    private Task? _txTask;

    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private string? _lastAddress;
    private bool _manualDisconnect;
    private volatile bool _closed = true;
    private BleConnectionState _state = BleConnectionState.Disconnected;

    /// <summary>是否启用意外断线后 8 秒自动重连（默认开启）。</summary>
    public bool AutoReconnect { get; set; } = true;

    public BleConnectionState State => _state;

    public bool IsConnected =>
        !_closed &&
        _device is not null &&
        _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

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
    /// 扫描已配对与附近广播中的 Sony BLE 设备（逐项对应 ble_transport.py scan_devices）。
    /// </summary>
    public static async Task<IReadOnlyList<DiscoveredDevice>> ScanDevicesAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan scanDuration = timeout ?? TimeSpan.FromSeconds(4.0);
        var byAddr = new ConcurrentDictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

        // 1. 枚举 Windows 已配对的 Sony BLE 设备
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
                byAddr[addr] = new DiscoveredDevice(
                    Name: string.IsNullOrEmpty(name) ? addr : name,
                    Address: addr,
                    Rssi: null);
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

        // 2. 主动 BLE 广播扫描
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

            if (!hasSonyUuid && !MatchesSonyName(localName))
            {
                return;
            }

            string addr = FormatBluetoothAddress(args.BluetoothAddress);
            string displayName = !string.IsNullOrEmpty(localName)
                ? localName
                : (byAddr.TryGetValue(addr, out var existing) ? existing.Name : addr);

            byAddr[addr] = new DiscoveredDevice(displayName, addr, args.RawSignalStrengthInDBm);
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

        // 3. 广播无 LocalName 且未命中已配对名时，尝试通过 WinRT BluetoothLEDevice 读取系统缓存的已知设备名（贴近 Python bleak dev.name 回退）。
        // 残余差异说明：WinRT 仅能返回 Windows 蓝牙栈已缓存的设备名；若设备从未被系统缓存且广播包与扫描响应均未携带 LocalName，
        // 仍会回退为 MAC 地址，无法像部分跨平台蓝牙栈那样凭空补齐名称。
        foreach (var kv in byAddr.ToArray())
        {
            if (!string.Equals(kv.Value.Name, kv.Value.Address, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                ulong rawAddr = ParseBluetoothAddress(kv.Value.Address);
                using BluetoothLEDevice? cachedDev = await BluetoothLEDevice
                    .FromBluetoothAddressAsync(rawAddr)
                    .AsTask(cancellationToken);
                if (cachedDev is not null && !string.IsNullOrWhiteSpace(cachedDev.Name))
                {
                    byAddr[kv.Key] = kv.Value with { Name = cachedDev.Name };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 系统未缓存或查询失败时保留 MAC 地址回退
            }
        }

        // 与 Python 原版排序一致：rssi 降序，None 按 -1 处理
        return byAddr.Values
            .OrderByDescending(d => d.Rssi ?? -1)
            .ToList();
    }

    public async Task ConnectAsync(string address, CancellationToken cancellationToken = default)
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
        BluetoothLEDevice? device = await BluetoothLEDevice.FromBluetoothAddressAsync(btAddr).AsTask(cancellationToken);
        if (device is null)
        {
            throw new InvalidOperationException($"无法打开 BLE 设备: {address}");
        }

        GattDeviceService? matchedService = null;
        GattCharacteristic? toAcc = null;
        GattCharacteristic? fromAcc = null;
        GattCharacteristic? wlenChar = null;

        try
        {
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
            _service = matchedService;
            _toAccChar = toAcc;
            _fromAccChar = fromAcc;

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

            _closed = false;
            _sessionCts = new CancellationTokenSource();
            _txChannel = Channel.CreateUnbounded<(SonySppFrameType, byte[])>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
            _txTask = Task.Run(() => TxLoopAsync(_sessionCts.Token));

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
            device.Dispose();
            _device = null;
            _service = null;
            _toAccChar = null;
            _fromAccChar = null;
            _closed = true;
            throw;
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

        _sessionCts?.Dispose();
        _sessionCts = null;
        _txChannel = null;

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
        if (sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected)
        {
            return;
        }

        if (_closed)
        {
            return;
        }

        _closed = true;
        EmitLog("BLE disconnected");
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
        GattCharacteristic? toAcc = _toAccChar;
        if (toAcc is null || _closed)
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
                Buffer.BlockCopy(data, offset, chunk, 0, chunkLen);
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

    private void OnCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (args.CharacteristicValue is null)
        {
            return;
        }

        byte[] data = ReadBuffer(args.CharacteristicValue);
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