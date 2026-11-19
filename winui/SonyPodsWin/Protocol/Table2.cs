using System;
using System.Collections.Generic;
using System.Text;

namespace SonyPodsWin.Protocol;

/// <summary>
/// 多点配对设备条目（对应 v2_table2.py 中的 PairedDeviceInfo）。
/// </summary>
public sealed record PairedDeviceInfo(
    string Address,
    byte ConnectedOrder,
    string Name,
    bool IsConnected);

/// <summary>
/// Table2 多点设备表响应（对应 v2_table2.py 中的 PeripheralDevicesResponse）。
/// </summary>
public sealed record PeripheralDevicesResponse(
    IReadOnlyList<PairedDeviceInfo> Devices,
    byte PlaybackHolderOrder,
    byte[] Raw) : TandemResponse(Raw);

/// <summary>
/// Sony Tandem V2 Table 2 (dataType 0x0F) 协议解析器，逐项等价于 v2_table2.py。
/// </summary>
public static class Table2
{
    public const byte PeriRetCapability = 0x31;
    public const byte PeriRetStatus = 0x33;
    public const byte PeriNtfyStatus = 0x35;
    public const byte PeriRetParam = 0x37;
    public const byte PeriNtfyParam = 0x39;

    // 使用不抛出异常的回退解码器，与 Python bytes.decode(..., errors="ignore") 行为一致。
    private static readonly Encoding AsciiIgnore = Encoding.GetEncoding(
        Encoding.ASCII.CodePage,
        EncoderFallback.ReplacementFallback,
        new DecoderReplacementFallback(string.Empty));

    private static readonly Encoding Utf8Ignore = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    public static PeripheralDevicesResponse? ParseTable2(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 3 || raw[0] != (byte)SonyTandemDataType.DataMdrNo2)
        {
            return null;
        }

        byte cmd = raw[1];
        ReadOnlySpan<byte> payload = raw[2..];
        if ((cmd != PeriRetParam && cmd != PeriNtfyParam) || payload.IsEmpty)
        {
            return null;
        }

        byte inqType = payload[0];
        if (inqType != 0x00 && inqType != 0x02)
        {
            return null;
        }

        if (payload.Length < 2)
        {
            return null;
        }

        byte count = payload[1];
        int idx = 2;
        bool hasCod = inqType == 0x02;
        var devices = new List<PairedDeviceInfo>(count);

        for (int i = 0; i < count; i++)
        {
            if (idx + 18 > payload.Length)
            {
                break;
            }

            string addr = AsciiIgnore.GetString(payload.Slice(idx, 17));
            idx += 17;

            byte order = payload[idx];
            idx += 1;

            if (hasCod)
            {
                if (idx + 3 > payload.Length)
                {
                    break;
                }

                idx += 3;
            }

            if (idx >= payload.Length)
            {
                break;
            }

            byte nameLen = payload[idx];
            idx += 1;

            if (idx + nameLen > payload.Length)
            {
                break;
            }

            string name = DecodeUtf8Ignore(payload.Slice(idx, nameLen)).Trim();
            idx += nameLen;

            devices.Add(new PairedDeviceInfo(
                Address: addr,
                ConnectedOrder: order,
                Name: string.IsNullOrEmpty(name) ? addr : name,
                IsConnected: order > 0));
        }

        byte holder = idx < payload.Length ? payload[idx] : (byte)0;
        return new PeripheralDevicesResponse(devices, holder, raw.ToArray());
    }

    private static string DecodeUtf8Ignore(ReadOnlySpan<byte> bytes)
    {
        // Utf8Ignore 对非法字节会输出 U+FFFD，此处移除 U+FFFD 以贴近 Python errors="ignore"。
        string decoded = Utf8Ignore.GetString(bytes);
        return decoded.IndexOf('\uFFFD') >= 0 ? decoded.Replace("\uFFFD", string.Empty) : decoded;
    }
}
