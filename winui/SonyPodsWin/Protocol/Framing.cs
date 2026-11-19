using System;
using System.Collections.Generic;

namespace SonyPodsWin.Protocol;

/// <summary>
/// 解码后的 Sony Tandem 帧（对应 framing.py 中的 TandemFrame）。
/// </summary>
public sealed record TandemFrame(
    SonySppFrameType FrameType,
    byte Sequence,
    byte[] Payload);

/// <summary>
/// Sony Tandem 帧编解码器（0x3E ... 0x3C），逐项等价于 framing.py 中的 SonyTandemFraming。
/// </summary>
public static class SonyTandemFraming
{
    public const byte FrameStart = 0x3E;
    public const byte FrameEnd = 0x3C;
    public const byte EscapeByte = 0x3D;
    public const int HeaderSize = 6;
    public const int ChecksumSize = 1;
    public const int MinFrameSize = HeaderSize + ChecksumSize;

    public static byte[] Encode(SonySppFrameType frameType, byte sequence, ReadOnlySpan<byte> payload) =>
        Encode((byte)frameType, sequence, payload);

    public static byte[] Encode(byte frameType, byte sequence, ReadOnlySpan<byte> payload)
    {
        int length = payload.Length;
        byte[] body = new byte[HeaderSize + length + ChecksumSize];
        body[0] = frameType;
        body[1] = sequence;
        body[2] = (byte)((length >> 24) & 0xFF);
        body[3] = (byte)((length >> 16) & 0xFF);
        body[4] = (byte)((length >> 8) & 0xFF);
        body[5] = (byte)(length & 0xFF);
        payload.CopyTo(body.AsSpan(HeaderSize, length));
        body[^1] = Checksum(body.AsSpan(0, body.Length - 1));

        byte[] escaped = Escape(body);
        byte[] frame = new byte[escaped.Length + 2];
        frame[0] = FrameStart;
        escaped.CopyTo(frame, 1);
        frame[^1] = FrameEnd;
        return frame;
    }

    public static TandemFrame? Decode(ReadOnlySpan<byte> escapedBody)
    {
        byte[] body = Unescape(escapedBody);
        if (body.Length < MinFrameSize)
        {
            return null;
        }

        byte expected = Checksum(body.AsSpan(0, body.Length - 1));
        byte actual = body[^1];
        if (expected != actual)
        {
            return null;
        }

        int length = (body[2] << 24) | (body[3] << 16) | (body[4] << 8) | body[5];
        if (length < 0 || body.Length != HeaderSize + length + ChecksumSize)
        {
            return null;
        }

        SonySppFrameType ftype = Enum.IsDefined(typeof(SonySppFrameType), body[0])
            ? (SonySppFrameType)body[0]
            : SonySppFrameType.Unknown;

        byte[] payload = body.AsSpan(HeaderSize, length).ToArray();
        return new TandemFrame(ftype, body[1], payload);
    }

    public static byte[] Escape(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>(data.Length + 8);
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            if (b == FrameEnd)
            {
                output.Add(EscapeByte);
                output.Add(0x2C);
            }
            else if (b == EscapeByte)
            {
                output.Add(EscapeByte);
                output.Add(0x2D);
            }
            else if (b == FrameStart)
            {
                output.Add(EscapeByte);
                output.Add(0x2E);
            }
            else
            {
                output.Add(b);
            }
        }

        return output.ToArray();
    }

    public static byte[] Unescape(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>(data.Length);
        int i = 0;
        int n = data.Length;
        while (i < n)
        {
            byte b = data[i];
            if (b == EscapeByte && i + 1 < n)
            {
                i++;
                output.Add((byte)(data[i] | 0x10));
            }
            else
            {
                // 与 Python 原版保持一致：若末尾出现孤立的 0x3D，直接原样保留。
                output.Add(b);
            }

            i++;
        }

        return output.ToArray();
    }

    public static byte Checksum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];
        }

        return (byte)(sum & 0xFF);
    }

    public static byte InverseSequence(byte sequence) =>
        (byte)((1 - sequence) & 0xFF);
}

/// <summary>
/// 帧层（SonySppFrameType）与消息层（SonyTandemDataType）双向归一化映射器，
/// 逐项等价于 framing.py 中的 SonySppPayloadMapper。
/// </summary>
public static class SonySppPayloadMapper
{
    /// <summary>
    /// 出站归一化：剥离消息首字节 dataType 并选择对应帧型。
    /// 消息 0x0E (Table1) -> 帧型 0x0C (DATA_MDR)；
    /// 消息 0x0F (Table2) -> 帧型 0x0E (DATA_MDR_NO2)；
    /// 其他或空字节 -> 帧型 0x0C 且原样保留 data。
    /// </summary>
    public static (SonySppFrameType FrameType, byte[] Payload) OutboundFromTandemBytes(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return (SonySppFrameType.DataMdr, Array.Empty<byte>());
        }

        if (data[0] == (byte)SonyTandemDataType.DataMdr)
        {
            return (SonySppFrameType.DataMdr, data[1..].ToArray());
        }

        if (data[0] == (byte)SonyTandemDataType.DataMdrNo2)
        {
            return (SonySppFrameType.DataMdrNo2, data[1..].ToArray());
        }

        return (SonySppFrameType.DataMdr, data.ToArray());
    }

    /// <summary>
    /// 入站归一化：按帧型补回消息层 dataType 首字节。
    /// 帧型 0x0C/0x1C/0x2C -> 补 0x0E (Table1)；
    /// 帧型 0x0E/0x1E -> 补 0x0F (Table2)；
    /// ACK/UNKNOWN -> 返回 null（不进入消息解析）。
    /// </summary>
    public static byte[]? InboundToTandemBytes(SonySppFrameType frameType, ReadOnlySpan<byte> payload)
    {
        byte dataType;
        if (frameType is SonySppFrameType.DataMdr or SonySppFrameType.ShotMdr or SonySppFrameType.LargeDataMdr)
        {
            dataType = (byte)SonyTandemDataType.DataMdr;
        }
        else if (frameType is SonySppFrameType.DataMdrNo2 or SonySppFrameType.ShotMdrNo2)
        {
            dataType = (byte)SonyTandemDataType.DataMdrNo2;
        }
        else
        {
            return null;
        }

        byte[] result = new byte[payload.Length + 1];
        result[0] = dataType;
        payload.CopyTo(result.AsSpan(1));
        return result;
    }
}
