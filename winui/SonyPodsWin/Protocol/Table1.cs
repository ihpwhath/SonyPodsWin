using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SonyPodsWin.Protocol;

/// <summary>
/// 所有已解析 Tandem 响应（Table1 与 Table2）的统一基类。
/// </summary>
public abstract record TandemResponse(byte[] Raw);

public sealed record ProtocolInfoResponse(uint Version, byte[] Raw) : TandemResponse(Raw);

public sealed record DeviceInfoResponse(DeviceInfoType? InfoType, string? Text, byte[] Raw) : TandemResponse(Raw);

public sealed record SupportFunctionEntry(byte FunctionCode, byte Order);

public sealed record SupportFunctionResponse(IReadOnlyList<SupportFunctionEntry> Functions, byte[] Raw) : TandemResponse(Raw);

public sealed record BatteryResponse(PowerInquiredType? Kind, IReadOnlyList<int?> Levels, byte[] Raw) : TandemResponse(Raw);

public sealed record NoiseControlResponse(
    NcAsmInquiredType? NcType,
    NoiseControlMode? ControlMode,
    int? AmbientLevel,
    AmbientSoundMode? AmbientMode,
    bool? NoiseAdaptive,
    byte[] Raw) : TandemResponse(Raw);

public sealed record EqEbbResponse(
    EqEbbInquiredType? EqType,
    bool? Enabled,
    EqPresetId? Preset,
    IReadOnlyList<byte> BandSteps,
    int? ClearBass,
    byte[] Raw) : TandemResponse(Raw);

public sealed record AudioCodecResponse(SoundQualityCodec Codec, byte[] Raw) : TandemResponse(Raw);

public sealed record UpscalingEffectResponse(DseeGeneration Generation, DseeEffectState State, byte[] Raw) : TandemResponse(Raw);

public sealed record UpscalingParamResponse(bool Enabled, byte InquiredType, byte[] Raw) : TandemResponse(Raw);

public sealed record ConnectionQualityResponse(ConnectionQualityMode Mode, byte InquiredType, byte[] Raw) : TandemResponse(Raw);

public sealed record SpeakToChatResponse(bool Enabled, byte SysType, byte[] Raw) : TandemResponse(Raw);

public sealed record GeneralSettingCapabilityResponse(byte Slot, string Title, byte[] Raw) : TandemResponse(Raw);

public sealed record GeneralSettingParamResponse(byte Slot, bool Enabled, byte[] Raw) : TandemResponse(Raw);

public sealed record PlaybackVolumeResponse(PlayInquiredType PlayType, byte Volume, byte[] Raw) : TandemResponse(Raw);

public sealed record CapabilityInfoResponse(byte Counter, string Identifier, byte[] Raw) : TandemResponse(Raw);

public sealed record AutoPowerOffResponse(byte InquiredType, IReadOnlyList<byte> Values, byte[] Raw) : TandemResponse(Raw);

public sealed record EqCapabilityResponse(EqEbbInquiredType? EqType, IReadOnlyList<EqPresetId> Presets, byte[] Raw) : TandemResponse(Raw);

public sealed record EqExtendedInfoResponse(EqEbbInquiredType? EqType, byte BandCount, bool HasClearBass, byte[] Raw) : TandemResponse(Raw);

public sealed record AlertNtfyParamResponse(byte InquiredType, byte MessageType, byte[] Raw) : TandemResponse(Raw);

public sealed record UnknownResponse(byte DataType, byte Command, byte[] Payload, byte[] Raw) : TandemResponse(Raw);

/// <summary>
/// Sony Tandem V2 Table 1 协议命令构造器与响应解析器，逐项等价于 v2_table1.py。
/// </summary>
public static class Table1
{
    // Command codes
    public const byte ConnectGetProtocolInfo = 0x00;
    public const byte ConnectRetProtocolInfo = 0x01;
    public const byte ConnectGetCapabilityInfo = 0x02;
    public const byte ConnectRetCapabilityInfo = 0x03;
    public const byte ConnectGetDeviceInfo = 0x04;
    public const byte ConnectRetDeviceInfo = 0x05;
    public const byte ConnectGetSupportFunction = 0x06;
    public const byte ConnectRetSupportFunction = 0x07;

    public const byte CommonGetStatus = 0x12;
    public const byte CommonRetStatus = 0x13;
    public const byte CommonNtfyStatus = 0x15;

    public const byte PowerGetStatus = 0x22;
    public const byte PowerRetStatus = 0x23;
    public const byte PowerSetStatus = 0x24;
    public const byte PowerNtfyStatus = 0x25;
    public const byte PowerGetParam = 0x26;
    public const byte PowerRetParam = 0x27;
    public const byte PowerSetParam = 0x28;
    public const byte PowerNtfyParam = 0x29;

    public const byte AlertSetParam = 0x98;
    public const byte AlertNtfyParam = 0x99;

    public const byte EqEbbGetCapability = 0x50;
    public const byte EqEbbRetCapability = 0x51;
    public const byte EqEbbGetStatus = 0x52;
    public const byte EqEbbRetStatus = 0x53;
    public const byte EqEbbNtfyStatus = 0x55;
    public const byte EqEbbGetParam = 0x56;
    public const byte EqEbbRetParam = 0x57;
    public const byte EqEbbSetParam = 0x58;
    public const byte EqEbbNtfyParam = 0x59;
    public const byte EqEbbGetExtendedInfo = 0x5A;
    public const byte EqEbbRetExtendedInfo = 0x5B;

    public const byte NcAsmGetCapability = 0x60;
    public const byte NcAsmRetCapability = 0x61;
    public const byte NcAsmGetStatus = 0x62;
    public const byte NcAsmRetStatus = 0x63;
    public const byte NcAsmNtfyStatus = 0x65;
    public const byte NcAsmGetParam = 0x66;
    public const byte NcAsmRetParam = 0x67;
    public const byte NcAsmSetParam = 0x68;
    public const byte NcAsmNtfyParam = 0x69;

    public const byte PlayGetCapability = 0xA0;
    public const byte PlayRetCapability = 0xA1;
    public const byte PlayGetStatus = 0xA2;
    public const byte PlayRetStatus = 0xA3;
    public const byte PlaySetStatus = 0xA4;
    public const byte PlayNtfyStatus = 0xA5;
    public const byte PlayGetParam = 0xA6;
    public const byte PlayRetParam = 0xA7;
    public const byte PlaySetParam = 0xA8;
    public const byte PlayNtfyParam = 0xA9;

    public const byte GsGetCapability = 0xD0;
    public const byte GsRetCapability = 0xD1;
    public const byte GsGetStatus = 0xD2;
    public const byte GsRetStatus = 0xD3;
    public const byte GsNtfyStatus = 0xD5;
    public const byte GsGetParam = 0xD6;
    public const byte GsRetParam = 0xD7;
    public const byte GsSetParam = 0xD8;
    public const byte GsNtfyParam = 0xD9;

    public const byte AudioGetCapability = 0xE0;
    public const byte AudioRetCapability = 0xE1;
    public const byte AudioGetStatus = 0xE2;
    public const byte AudioRetStatus = 0xE3;
    public const byte AudioNtfyStatus = 0xE5;
    public const byte AudioGetParam = 0xE6;
    public const byte AudioRetParam = 0xE7;
    public const byte AudioSetParam = 0xE8;
    public const byte AudioNtfyParam = 0xE9;

    public const byte SystemGetCapability = 0xF0;
    public const byte SystemRetCapability = 0xF1;
    public const byte SystemGetStatus = 0xF2;
    public const byte SystemRetStatus = 0xF3;
    public const byte SystemNtfyStatus = 0xF5;
    public const byte SystemGetParam = 0xF6;
    public const byte SystemRetParam = 0xF7;
    public const byte SystemSetParam = 0xF8;
    public const byte SystemNtfyParam = 0xF9;
    public const byte SystemGetExtParam = 0xFA;
    public const byte SystemRetExtParam = 0xFB;
    public const byte SystemSetExtParam = 0xFC;
    public const byte SystemNtfyExtParam = 0xFD;

    // NCASM constants
    public const byte NcAsmOff = 0x00;
    public const byte NcAsmOn = 0x01;
    public const byte ValueChanged = 0x01;
    public const byte NcAsmModeNc = 0x00;
    public const byte NcAsmModeAsm = 0x01;
    public const byte NcValueOff = 0x00;
    public const byte NcValueOnSingle = 0x01;
    public const byte NcValueOnDual = 0x02;
    public const byte NcValueAuto = 0x03;

    // Audio constants
    public const byte AudioInqConnectionMode = 0x00;
    public const byte AudioInqUpscaling = 0x01;
    public const byte AudioInqConnectionModeWithLdac = 0x02;
    public const byte AudioInqConnectionModeLea = 0x05;
    public const byte AudioInqUpscalingWithReason = 0x0B;
    public const byte AudioInqUpmixCinema = 0x20;
    public const byte AudioInqBgmMode = 0x30;
    public const byte UpscalingOff = 0x00;
    public const byte UpscalingAuto = 0x01;

    // System constants
    // 注意：此处严格照搬 v2_table1.py 原值（0x01 / 0x02 / 0x04）；
    // protocol-notes.md §4.10 记载的上游 SonyTandemEnums.kt 为 0x02 / 0x0C / 0x06，
    // 移植按 Python 源码为唯一事实标准保持不变。
    public const byte SysSmartTalkingType1 = 0x01;
    public const byte SysSmartTalkingType2 = 0x02;
    public const byte SysWearingStatusDetector = 0x04;

    private static readonly Encoding Utf8Ignore = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    private static byte[] Msg(byte cmd, ReadOnlySpan<byte> payload = default)
    {
        byte[] result = new byte[2 + payload.Length];
        result[0] = (byte)SonyTandemDataType.DataMdr;
        result[1] = cmd;
        payload.CopyTo(result.AsSpan(2));
        return result;
    }

    // ── Builders ───────────────────────────────────────────────────────────────

    public static byte[] BuildGetProtocolInfo() =>
        Msg(ConnectGetProtocolInfo, new byte[] { 0x00 });

    public static byte[] BuildGetCapabilityInfo() =>
        Msg(ConnectGetCapabilityInfo, new byte[] { 0x00 });

    public static byte[] BuildGetDeviceInfo(DeviceInfoType infoType) =>
        Msg(ConnectGetDeviceInfo, new byte[] { (byte)infoType });

    public static byte[] BuildGetSupportFunction() =>
        Msg(ConnectGetSupportFunction, new byte[] { 0x00 });

    public static byte[] BuildGetBatteryStatus(PowerInquiredType powerType = PowerInquiredType.Battery) =>
        Msg(PowerGetStatus, new byte[] { (byte)powerType });

    public static byte[] BuildPowerOff() =>
        Msg(PowerSetStatus, new byte[] { 0x03, 0x01 });

    // 注意：v2_table1.py 默认 inquired_type=0x05（佩戴检测型自动关机），与 PowerInquiredType.AutoPowerOff=0x04 不同，按原版保留。
    public static byte[] BuildGetAutoPowerOff(byte inquiredType = 0x05) =>
        Msg(PowerGetParam, new byte[] { inquiredType });

    public static byte[] BuildSetAutoPowerOff(byte inquiredType, ReadOnlySpan<byte> valueBytes)
    {
        byte[] payload = new byte[1 + valueBytes.Length];
        payload[0] = inquiredType;
        valueBytes.CopyTo(payload.AsSpan(1));
        return Msg(PowerSetParam, payload);
    }

    public static byte[] BuildReplyAlertFixedMessage(byte messageType, bool positive = true) =>
        Msg(AlertSetParam, new byte[] { 0x00, messageType, positive ? (byte)0x01 : (byte)0x00 });

    public static byte[] BuildGetAudioCodecStatus() =>
        Msg(CommonGetStatus, new byte[] { (byte)CommonInquiredType.AudioCodec });

    public static byte[] BuildGetUpscalingEffectStatus() =>
        Msg(CommonGetStatus, new byte[] { (byte)CommonInquiredType.UpscalingEffect });

    public static byte[] BuildGetNcAsmCapability(NcAsmInquiredType ncType) =>
        Msg(NcAsmGetCapability, new byte[] { (byte)ncType });

    public static byte[] BuildGetNcAsmStatus(NcAsmInquiredType ncType) =>
        Msg(NcAsmGetStatus, new byte[] { (byte)ncType });

    public static byte[] BuildGetNcAsmParam(NcAsmInquiredType ncType) =>
        Msg(NcAsmGetParam, new byte[] { (byte)ncType });

    public static byte[] BuildSetNoiseControlMode(
        NoiseControlMode controlMode,
        int ambientLevel = 10,
        AmbientSoundMode ambientMode = AmbientSoundMode.Normal,
        NcAsmInquiredType ncType = NcAsmInquiredType.ModeNcAsmDualNcModeSwitchAndAsmSeamless,
        bool noiseAdaptive = false,
        NoiseAdaptiveSensitivity noiseAdaptiveSensitivity = NoiseAdaptiveSensitivity.Standard,
        bool windNoiseReduction = false)
    {
        byte effect = controlMode == NoiseControlMode.Off ? NcAsmOff : NcAsmOn;
        byte mode = controlMode == NoiseControlMode.AmbientSound ? NcAsmModeAsm : NcAsmModeNc;
        byte level = (byte)Math.Clamp(ambientLevel, 1, 20);
        byte ncOn = controlMode == NoiseControlMode.NoiseCancelling ? NcAsmOn : NcAsmOff;

        byte ncValue;
        byte autoNcValue;
        if (controlMode == NoiseControlMode.NoiseCancelling)
        {
            ncValue = windNoiseReduction ? NcValueOnSingle : NcValueOnDual;
            autoNcValue = windNoiseReduction ? NcValueAuto : NcValueOnDual;
        }
        else
        {
            ncValue = NcValueOff;
            autoNcValue = NcValueOnDual;
        }

        byte asmOn = controlMode == NoiseControlMode.AmbientSound ? NcAsmOn : NcAsmOff;

        if (ncType == NcAsmInquiredType.NcAmbToggle)
        {
            // 注意：此处严格照搬 v2_table1.py 原版映射（OFF=0x00, NC=0x02, ASM=0x01）；
            // protocol-notes.md §4.4 记载的映射为 OFF=0x01, ASM=0x03, NC=0x04，按 Python 源码保持原样。
            byte fn = controlMode switch
            {
                NoiseControlMode.Off => 0x00,
                NoiseControlMode.NoiseCancelling => 0x02,
                NoiseControlMode.AmbientSound => 0x01,
                _ => throw new ArgumentOutOfRangeException(nameof(controlMode), controlMode, null),
            };
            return Msg(NcAsmSetParam, new byte[] { (byte)ncType, fn });
        }

        byte[] body = ncType switch
        {
            NcAsmInquiredType.NcOnOff => new byte[] { ncOn },
            NcAsmInquiredType.NcOnOffAndAsmOnOff => new byte[] { ncOn, (byte)ambientMode, asmOn },
            NcAsmInquiredType.NcModeSwitchAndAsmOnOff => new byte[] { ncValue, (byte)ambientMode, asmOn },
            NcAsmInquiredType.NcOnOffAndAsmSeamless => new byte[] { ncOn, (byte)ambientMode, level },
            NcAsmInquiredType.NcModeSwitchAndAsmSeamless => new byte[] { ncValue, (byte)ambientMode, level },
            NcAsmInquiredType.ModeNcAsmAutoNcModeSwitchAndAsmSeamless => new byte[] { mode, autoNcValue, (byte)ambientMode, level },
            NcAsmInquiredType.ModeNcAsmDualSingleNcModeSwitchAndAsmSeamless => new byte[] { mode, ncValue, (byte)ambientMode, level },
            NcAsmInquiredType.ModeNcAsmDualNcModeSwitchAndAsmSeamless or
            NcAsmInquiredType.ModeNcNcssAsmDualNcModeSwitchAndAsmSeamless => new byte[] { mode, (byte)ambientMode, level },
            NcAsmInquiredType.ModeNcAsmDualNcModeSwitchAndAsmSeamlessNa => new byte[]
            {
                mode,
                (byte)ambientMode,
                level,
                noiseAdaptive ? NcAsmOn : NcAsmOff,
                (byte)noiseAdaptiveSensitivity,
            },
            NcAsmInquiredType.AsmOnOff => new byte[] { (byte)ambientMode, asmOn },
            NcAsmInquiredType.AsmSeamless => new byte[] { (byte)ambientMode, level },
            _ => new byte[] { mode, (byte)ambientMode, level },
        };

        byte[] payload = new byte[3 + body.Length];
        payload[0] = (byte)ncType;
        payload[1] = ValueChanged;
        payload[2] = effect;
        body.CopyTo(payload, 3);
        return Msg(NcAsmSetParam, payload);
    }

    public static byte[] BuildGetEqEbbCapability(EqEbbInquiredType eqType = EqEbbInquiredType.PresetEq) =>
        Msg(EqEbbGetCapability, new byte[] { (byte)eqType, 0x00 });

    public static byte[] BuildGetEqEbbStatus(EqEbbInquiredType eqType = EqEbbInquiredType.PresetEq) =>
        Msg(EqEbbGetStatus, new byte[] { (byte)eqType });

    public static byte[] BuildGetEqEbbParam(EqEbbInquiredType eqType = EqEbbInquiredType.PresetEq) =>
        Msg(EqEbbGetParam, new byte[] { (byte)eqType });

    public static byte[] BuildGetEqEbbExtendedInfo(EqEbbInquiredType eqType = EqEbbInquiredType.PresetEq) =>
        Msg(EqEbbGetExtendedInfo, new byte[] { (byte)eqType });

    public static byte[] BuildSetEqPreset(
        EqPresetId preset,
        EqEbbInquiredType eqType = EqEbbInquiredType.PresetEq,
        IReadOnlyList<int>? bandSteps = null)
    {
        byte[] steps = bandSteps is null
            ? Array.Empty<byte>()
            : bandSteps.Select(s => (byte)Math.Clamp(s, 0, 255)).ToArray();

        byte[] body;
        if (eqType == EqEbbInquiredType.CustomEq)
        {
            body = new byte[2 + steps.Length];
            body[0] = (byte)eqType;
            body[1] = (byte)steps.Length;
            steps.CopyTo(body, 2);
        }
        else if (eqType == EqEbbInquiredType.PresetEqAndUltMode)
        {
            // 注意：v2_table1.py 中此处 ultMode 固定写死为 0x00，按原版保留。
            body = new byte[4 + steps.Length];
            body[0] = (byte)eqType;
            body[1] = (byte)preset;
            body[2] = 0x00;
            body[3] = (byte)steps.Length;
            steps.CopyTo(body, 4);
        }
        else
        {
            body = new byte[3 + steps.Length];
            body[0] = (byte)eqType;
            body[1] = (byte)preset;
            body[2] = (byte)steps.Length;
            steps.CopyTo(body, 3);
        }

        return Msg(EqEbbSetParam, body);
    }

    public static byte[] BuildSetClearBass(int level, EqEbbInquiredType eqType = EqEbbInquiredType.Ebb)
    {
        byte clamped = unchecked((byte)(Math.Clamp(level, -10, 10) & 0xFF));
        return Msg(EqEbbSetParam, new byte[] { (byte)eqType, clamped });
    }

    public static byte[] BuildGetUpscaling(byte inquiredType = AudioInqUpscaling) =>
        Msg(AudioGetParam, new byte[] { inquiredType });

    public static byte[] BuildSetUpscaling(bool on, byte inquiredType = AudioInqUpscaling) =>
        Msg(AudioSetParam, new byte[] { inquiredType, on ? UpscalingAuto : UpscalingOff });

    public static byte[] BuildGetConnectionQuality(byte inquiredType = AudioInqConnectionMode) =>
        Msg(AudioGetParam, new byte[] { inquiredType });

    public static byte[] BuildSetConnectionQuality(
        ConnectionQualityMode mode,
        byte inquiredType = AudioInqConnectionMode) =>
        Msg(AudioSetParam, new byte[] { inquiredType, (byte)mode });

    public static byte[] BuildGetCinemaMode() =>
        Msg(AudioGetParam, new byte[] { AudioInqUpmixCinema });

    public static byte[] BuildSetCinemaMode(bool enabled) =>
        Msg(AudioSetParam, new byte[] { AudioInqUpmixCinema, enabled ? (byte)0x00 : (byte)0x01 });

    public static byte[] BuildGetSpeakToChatStatus(byte sysType = SysSmartTalkingType1) =>
        Msg(SystemGetStatus, new byte[] { sysType });

    public static byte[] BuildGetSpeakToChatParam(byte sysType = SysSmartTalkingType1) =>
        Msg(SystemGetParam, new byte[] { sysType });

    // 注意：v2_table1.py 末字节固定为 0x00（protocol-notes.md §4.10 记载为 0x01），按 Python 源码保留。
    public static byte[] BuildSetSpeakToChatEnabled(bool enabled, byte sysType = SysSmartTalkingType1) =>
        Msg(SystemSetParam, new byte[] { sysType, enabled ? (byte)0x00 : (byte)0x01, 0x00 });

    public static byte[] BuildGetWearingStatus() =>
        Msg(SystemGetParam, new byte[] { SysWearingStatusDetector });

    public static byte[] BuildGetGeneralSettingCapability(byte slot) =>
        Msg(GsGetCapability, new byte[] { slot, 0x01 });

    public static byte[] BuildGetGeneralSettingParam(byte slot) =>
        Msg(GsGetParam, new byte[] { slot });

    public static byte[] BuildSetGeneralSetting(byte slot, bool on) =>
        // GsSettingType.BOOLEAN_TYPE=0x00, ON=0x00, OFF=0x01
        Msg(GsSetParam, new byte[] { slot, 0x00, on ? (byte)0x00 : (byte)0x01 });

    public static byte[] BuildGetPlaybackParam(PlayInquiredType playType = PlayInquiredType.MusicVolume) =>
        Msg(PlayGetParam, new byte[] { (byte)playType });

    public static byte[] BuildSetPlaybackVolume(int volume, PlayInquiredType playType = PlayInquiredType.MusicVolume) =>
        Msg(PlaySetParam, new byte[] { (byte)playType, (byte)Math.Clamp(volume, 0, 100) });

    public static byte[] BuildPlayback(
        PlaybackControl control,
        PlayInquiredType playType = PlayInquiredType.PlaybackControlWithCallVolumeAdjustment) =>
        Msg(PlaySetStatus, new byte[] { (byte)playType, 0x00, (byte)control });

    // ── Parsers ────────────────────────────────────────────────────────────────

    private static int? PctOrNull(byte b)
    {
        // 原版 Python 为 `if b == 0xFF or b == 0x00 and False: return None`，
        // 其中的 `and False` 永不生效，因此 0x00 合法返回 0，仅 0xFF 或 >100 返回 null。
        if (b == 0xFF)
        {
            return null;
        }

        return b <= 100 ? b : null;
    }

    private static string? LenPrefixedStr(ReadOnlySpan<byte> payload, int offset)
    {
        if (offset >= payload.Length)
        {
            return null;
        }

        byte length = payload[offset];
        int start = offset + 1;
        if (length <= 0 || start + length > payload.Length)
        {
            // 注意：严格照搬 v2_table1.py 原版兜底逻辑（无论 offset 为何值都从 payload[1..] 解码）。
            string rawS = payload.Length > 1
                ? DecodeUtf8Ignore(payload[1..]).TrimEnd('\0').Trim()
                : string.Empty;
            return string.IsNullOrEmpty(rawS) ? null : rawS;
        }

        string s = DecodeUtf8Ignore(payload.Slice(start, length)).TrimEnd('\0').Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static string DecodeUtf8Ignore(ReadOnlySpan<byte> bytes)
    {
        string decoded = Utf8Ignore.GetString(bytes);
        return decoded.IndexOf('\uFFFD') >= 0 ? decoded.Replace("\uFFFD", string.Empty) : decoded;
    }

    public static TandemResponse Parse(ReadOnlySpan<byte> raw)
    {
        byte[] rawCopy = raw.ToArray();

        if (!raw.IsEmpty && raw[0] == (byte)SonyTandemDataType.DataMdrNo2)
        {
            PeripheralDevicesResponse? t2 = Table2.ParseTable2(raw);
            if (t2 is not null)
            {
                return t2;
            }

            return new UnknownResponse(
                DataType: raw[0],
                Command: raw.Length > 1 ? raw[1] : (byte)0,
                Payload: raw.Length > 2 ? raw[2..].ToArray() : Array.Empty<byte>(),
                Raw: rawCopy);
        }

        // 与 v2_table1.py 一致：若非 0x0E 开头则自动补 0x0E 前缀（空数组补后长度为 1 < 2，落入下方 UnknownResponse(0,0)）。
        byte[] normalized;
        if (!raw.IsEmpty && raw[0] == (byte)SonyTandemDataType.DataMdr)
        {
            normalized = rawCopy;
        }
        else
        {
            normalized = new byte[raw.Length + 1];
            normalized[0] = (byte)SonyTandemDataType.DataMdr;
            raw.CopyTo(normalized.AsSpan(1));
        }

        if (normalized.Length < 2)
        {
            return new UnknownResponse(0, 0, Array.Empty<byte>(), rawCopy);
        }

        byte dataType = normalized[0];
        byte cmd = normalized[1];
        ReadOnlySpan<byte> payload = normalized.AsSpan(2);

        if (dataType != (byte)SonyTandemDataType.DataMdr)
        {
            return new UnknownResponse(dataType, cmd, payload.ToArray(), rawCopy);
        }

        if (cmd == ConnectRetProtocolInfo && payload.Length >= 5)
        {
            // 注意：v2_table1.py 从 payload[1..4] 读取 4 字节大端版本号（protocol-notes.md §2.3 记载 V2 偏移为 2..5），按 Python 源码保留。
            uint ver = ((uint)payload[1] << 24)
                     | ((uint)payload[2] << 16)
                     | ((uint)payload[3] << 8)
                     | payload[4];
            return new ProtocolInfoResponse(ver, rawCopy);
        }

        if (cmd == ConnectRetCapabilityInfo && payload.Length >= 3)
        {
            byte counter = payload[1];
            string ident = LenPrefixedStr(payload, 2) ?? string.Empty;
            return new CapabilityInfoResponse(counter, ident, rawCopy);
        }

        if (cmd == ConnectRetDeviceInfo && !payload.IsEmpty)
        {
            DeviceInfoType? itype = Enum.IsDefined(typeof(DeviceInfoType), payload[0])
                ? (DeviceInfoType)payload[0]
                : null;
            string? text = null;
            if (itype is DeviceInfoType.ModelName or DeviceInfoType.FwVersion or DeviceInfoType.InstructionGuide)
            {
                text = LenPrefixedStr(payload, 1);
            }

            return new DeviceInfoResponse(itype, text, rawCopy);
        }

        if (cmd == ConnectRetSupportFunction && payload.Length >= 2)
        {
            byte count = payload[1];
            var funcs = new List<SupportFunctionEntry>(count);
            for (int i = 0; i < count; i++)
            {
                int idx = 2 + i * 2;
                if (idx + 1 >= payload.Length)
                {
                    break;
                }

                funcs.Add(new SupportFunctionEntry(payload[idx], payload[idx + 1]));
            }

            // 按 order 升序稳定排序，与 Python list.sort(key=lambda x: x[1]) 等价。
            var sorted = funcs.OrderBy(x => x.Order).ToList();
            return new SupportFunctionResponse(sorted, rawCopy);
        }

        if ((cmd == PowerRetStatus || cmd == PowerNtfyStatus) && !payload.IsEmpty)
        {
            byte code = payload[0];
            PowerInquiredType? kind;
            if (code == 0x09)
            {
                // 扩展电量 NTFY 特判：0x09 映射为 LEFT_RIGHT_BATTERY。
                kind = PowerInquiredType.LeftRightBattery;
            }
            else
            {
                kind = Enum.IsDefined(typeof(PowerInquiredType), code)
                    ? (PowerInquiredType)code
                    : null;
            }

            var levels = new List<int?>();
            if (kind is PowerInquiredType.Battery or PowerInquiredType.CradleBattery && payload.Length >= 2)
            {
                levels.Add(PctOrNull(payload[1]));
            }
            else if (kind == PowerInquiredType.LeftRightBattery)
            {
                int? lVal;
                int? rVal;
                if (code == 0x09 && payload.Length >= 7)
                {
                    lVal = PctOrNull(payload[5]);
                    rVal = PctOrNull(payload[6]);
                }
                else
                {
                    lVal = payload.Length >= 2 ? PctOrNull(payload[1]) : null;
                    rVal = payload.Length >= 4 ? PctOrNull(payload[3]) : null;
                }

                levels.Add(lVal);
                levels.Add(rVal);
            }
            else
            {
                for (int i = 1; i < payload.Length; i++)
                {
                    levels.Add(payload[i]);
                }
            }

            return new BatteryResponse(kind, levels, rawCopy);
        }

        if ((cmd == PowerRetParam || cmd == PowerNtfyParam) && !payload.IsEmpty)
        {
            return new AutoPowerOffResponse(payload[0], payload[1..].ToArray(), rawCopy);
        }

        if ((cmd == CommonRetStatus || cmd == CommonNtfyStatus) && !payload.IsEmpty)
        {
            if (payload[0] == (byte)CommonInquiredType.AudioCodec && payload.Length >= 2)
            {
                int codecVal = payload.Length == 2
                    ? payload[1]
                    : ((payload[1] << 8) | payload[2]);
                if (Enum.IsDefined(typeof(SoundQualityCodec), codecVal))
                {
                    return new AudioCodecResponse((SoundQualityCodec)codecVal, rawCopy);
                }
            }

            if (payload[0] == (byte)CommonInquiredType.UpscalingEffect && payload.Length == 3)
            {
                if (Enum.IsDefined(typeof(DseeGeneration), payload[1]) &&
                    Enum.IsDefined(typeof(DseeEffectState), payload[2]))
                {
                    return new UpscalingEffectResponse(
                        (DseeGeneration)payload[1],
                        (DseeEffectState)payload[2],
                        rawCopy);
                }
            }
        }

        if ((cmd == NcAsmRetParam || cmd == NcAsmNtfyParam) && !payload.IsEmpty)
        {
            return ParseNcAsmParam(payload, rawCopy);
        }

        if (cmd == EqEbbRetCapability && payload.Length >= 5)
        {
            EqEbbInquiredType? eqType = Enum.IsDefined(typeof(EqEbbInquiredType), payload[0])
                ? (EqEbbInquiredType)payload[0]
                : null;
            // Layout: [type][bandCount][levelSteps][presetCount][(presetId, 0x00)...]
            byte presetCount = payload[3];
            var presets = new List<EqPresetId>(presetCount);
            for (int i = 0; i < presetCount; i++)
            {
                int idx = 4 + i * 2;
                if (idx >= payload.Length)
                {
                    break;
                }

                if (Enum.IsDefined(typeof(EqPresetId), payload[idx]))
                {
                    presets.Add((EqPresetId)payload[idx]);
                }
            }

            return new EqCapabilityResponse(eqType, presets, rawCopy);
        }

        if ((cmd == EqEbbRetStatus || cmd == EqEbbNtfyStatus || cmd == EqEbbRetParam || cmd == EqEbbNtfyParam) && !payload.IsEmpty)
        {
            return ParseEqEbb(cmd, payload, rawCopy);
        }

        if (cmd == EqEbbRetExtendedInfo && payload.Length >= 2)
        {
            EqEbbInquiredType? eqType = Enum.IsDefined(typeof(EqEbbInquiredType), payload[0])
                ? (EqEbbInquiredType)payload[0]
                : null;
            byte count = payload[1];
            bool hasClearBass = payload.Length >= 5 && payload[2] == 0x10;
            return new EqExtendedInfoResponse(eqType, count, hasClearBass, rawCopy);
        }

        if (cmd == AlertNtfyParam && payload.Length >= 2)
        {
            return new AlertNtfyParamResponse(payload[0], payload[1], rawCopy);
        }

        if ((cmd == AudioRetParam || cmd == AudioNtfyParam) && payload.Length >= 2)
        {
            byte inq = payload[0];
            if (inq == AudioInqUpscaling || inq == AudioInqUpscalingWithReason)
            {
                return new UpscalingParamResponse(payload[1] == UpscalingAuto, inq, rawCopy);
            }

            if (inq == AudioInqConnectionMode || inq == AudioInqConnectionModeWithLdac || inq == AudioInqConnectionModeLea)
            {
                if (Enum.IsDefined(typeof(ConnectionQualityMode), payload[1]))
                {
                    return new ConnectionQualityResponse((ConnectionQualityMode)payload[1], inq, rawCopy);
                }
            }
        }

        if ((cmd == SystemRetParam || cmd == SystemNtfyParam) && payload.Length >= 2)
        {
            if (payload[0] == SysSmartTalkingType1 || payload[0] == SysSmartTalkingType2)
            {
                return new SpeakToChatResponse(payload[1] == 0x00, payload[0], rawCopy);
            }
        }

        if (cmd == GsRetCapability && payload.Length >= 4)
        {
            byte slot = payload[0];
            string title = LenPrefixedStr(payload, 3) ?? $"Slot 0x{slot:X2}";
            return new GeneralSettingCapabilityResponse(slot, title, rawCopy);
        }

        if ((cmd == GsRetParam || cmd == GsNtfyParam) && payload.Length >= 3)
        {
            return new GeneralSettingParamResponse(payload[0], payload[2] == 0x00, rawCopy);
        }

        if ((cmd == PlayRetParam || cmd == PlayNtfyParam) && payload.Length >= 2)
        {
            if (Enum.IsDefined(typeof(PlayInquiredType), payload[0]))
            {
                var ptype = (PlayInquiredType)payload[0];
                if (ptype is PlayInquiredType.MusicVolume or PlayInquiredType.CallVolume)
                {
                    return new PlaybackVolumeResponse(ptype, payload[1], rawCopy);
                }
            }
        }

        return new UnknownResponse(dataType, cmd, payload.ToArray(), rawCopy);
    }

    private static NoiseControlResponse ParseNcAsmParam(ReadOnlySpan<byte> payload, byte[] raw)
    {
        NcAsmInquiredType? ncType = Enum.IsDefined(typeof(NcAsmInquiredType), payload[0])
            ? (NcAsmInquiredType)payload[0]
            : null;

        byte? totalEffect = payload.Length > 2 ? payload[2] : null;
        byte? modeByte = payload.Length > 3 ? payload[3] : null;

        NoiseControlMode? controlMode = null;
        int? ambientLevel = null;
        AmbientSoundMode? ambientMode = null;
        bool? noiseAdaptive = null;

        if (ncType is NcAsmInquiredType.ModeNcAsmDualNcModeSwitchAndAsmSeamless
            or NcAsmInquiredType.ModeNcNcssAsmDualNcModeSwitchAndAsmSeamless
            or NcAsmInquiredType.ModeNcAsmDualNcModeSwitchAndAsmSeamlessNa)
        {
            if (totalEffect == NcAsmOff)
            {
                controlMode = NoiseControlMode.Off;
            }
            else if (modeByte == NcAsmModeAsm)
            {
                controlMode = NoiseControlMode.AmbientSound;
            }
            else if (modeByte == NcAsmModeNc)
            {
                controlMode = NoiseControlMode.NoiseCancelling;
            }

            if (payload.Length > 4 && Enum.IsDefined(typeof(AmbientSoundMode), payload[4]))
            {
                ambientMode = (AmbientSoundMode)payload[4];
            }

            if (payload.Length > 5)
            {
                ambientLevel = payload[5];
            }

            if (ncType == NcAsmInquiredType.ModeNcAsmDualNcModeSwitchAndAsmSeamlessNa && payload.Length > 6)
            {
                noiseAdaptive = payload[6] == NcAsmOn;
            }
        }
        else if (ncType is NcAsmInquiredType.ModeNcAsmAutoNcModeSwitchAndAsmSeamless
            or NcAsmInquiredType.ModeNcAsmDualSingleNcModeSwitchAndAsmSeamless)
        {
            if (totalEffect == NcAsmOff)
            {
                controlMode = NoiseControlMode.Off;
            }
            else if (modeByte == NcAsmModeAsm)
            {
                controlMode = NoiseControlMode.AmbientSound;
            }
            else if (modeByte == NcAsmModeNc)
            {
                controlMode = NoiseControlMode.NoiseCancelling;
            }

            if (payload.Length > 5 && Enum.IsDefined(typeof(AmbientSoundMode), payload[5]))
            {
                ambientMode = (AmbientSoundMode)payload[5];
            }

            if (payload.Length > 6)
            {
                ambientLevel = payload[6];
            }
        }
        else if (ncType is NcAsmInquiredType.NcOnOffAndAsmSeamless
            or NcAsmInquiredType.NcModeSwitchAndAsmSeamless)
        {
            if (totalEffect == NcAsmOff)
            {
                controlMode = NoiseControlMode.Off;
            }
            else if (modeByte is NcAsmOn or NcValueOnSingle or NcValueOnDual)
            {
                controlMode = NoiseControlMode.NoiseCancelling;
            }
            else
            {
                controlMode = NoiseControlMode.AmbientSound;
            }

            if (payload.Length > 4 && Enum.IsDefined(typeof(AmbientSoundMode), payload[4]))
            {
                ambientMode = (AmbientSoundMode)payload[4];
            }

            if (payload.Length > 5)
            {
                ambientLevel = payload[5];
            }
        }

        return new NoiseControlResponse(ncType, controlMode, ambientLevel, ambientMode, noiseAdaptive, raw);
    }

    private static EqEbbResponse ParseEqEbb(byte cmd, ReadOnlySpan<byte> payload, byte[] raw)
    {
        EqEbbInquiredType? eqType = Enum.IsDefined(typeof(EqEbbInquiredType), payload[0])
            ? (EqEbbInquiredType)payload[0]
            : null;

        if (cmd == EqEbbRetStatus || cmd == EqEbbNtfyStatus)
        {
            bool? enabled = payload.Length > 1 ? payload[1] == 0x00 : null;
            return new EqEbbResponse(eqType, enabled, null, Array.Empty<byte>(), null, raw);
        }

        EqPresetId? preset = null;
        byte[] bandSteps = Array.Empty<byte>();
        int? clearBass = null;

        if (eqType == EqEbbInquiredType.Ebb && payload.Length >= 2)
        {
            byte val = payload[1];
            clearBass = val >= 128 ? val - 256 : val;
        }
        else if (eqType is EqEbbInquiredType.PresetEq
            or EqEbbInquiredType.PresetEqNoncustomizable
            or EqEbbInquiredType.PresetEqAndErrorcode && payload.Length >= 2)
        {
            preset = Enum.IsDefined(typeof(EqPresetId), payload[1]) ? (EqPresetId)payload[1] : null;
            if (payload.Length >= 3)
            {
                int count = payload[2];
                int avail = Math.Max(0, Math.Min(count, payload.Length - 3));
                bandSteps = payload.Slice(3, avail).ToArray();
            }
        }
        else if (eqType == EqEbbInquiredType.PresetEqAndUltMode && payload.Length >= 2)
        {
            preset = Enum.IsDefined(typeof(EqPresetId), payload[1]) ? (EqPresetId)payload[1] : null;
            if (payload.Length >= 4)
            {
                int count = payload[3];
                int avail = Math.Max(0, Math.Min(count, payload.Length - 4));
                bandSteps = payload.Slice(4, avail).ToArray();
            }
        }
        else if (eqType == EqEbbInquiredType.CustomEq && payload.Length >= 2)
        {
            int count = payload[1];
            int avail = Math.Max(0, Math.Min(count, payload.Length - 2));
            bandSteps = payload.Slice(2, avail).ToArray();
        }

        return new EqEbbResponse(eqType, null, preset, bandSteps, clearBass, raw);
    }
}
