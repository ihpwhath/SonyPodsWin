using System;
using System.Collections.Generic;

namespace SonyPodsWin.Protocol;

/// <summary>
/// Sony Tandem GATT 服务与特征 UUID 常量（逐项对应 constants.py 中的 SonyGatt）。
/// </summary>
public static class SonyGatt
{
    public const string UuidSuffix = "-6bc7-4802-8e9a-723ceca4bd8f";

    public static string Service(byte suffix) => $"5b833e{suffix:x2}{UuidSuffix}";
    public static Guid ServiceGuid(byte suffix) => Guid.Parse(Service(suffix));

    public static string Characteristic(byte suffix) => $"5b833c{suffix:x2}{UuidSuffix}";
    public static Guid CharacteristicGuid(byte suffix) => Guid.Parse(Characteristic(suffix));

    public const string TandemV2HpcService = "5b833e20-6bc7-4802-8e9a-723ceca4bd8f";
    public const string TandemV2McService = "5b833e21-6bc7-4802-8e9a-723ceca4bd8f";
    public const string TandemV1McService = "5b833e23-6bc7-4802-8e9a-723ceca4bd8f";

    public const string TandemHpcToAcc = "5b833c60-6bc7-4802-8e9a-723ceca4bd8f";
    public const string TandemHpcFromAcc = "5b833c61-6bc7-4802-8e9a-723ceca4bd8f";
    public const string TandemMcToAcc = "5b833c62-6bc7-4802-8e9a-723ceca4bd8f";
    public const string TandemMcFromAcc = "5b833c63-6bc7-4802-8e9a-723ceca4bd8f";

    public const string ReadyToStartInitialCommunication = "5b833c90-6bc7-4802-8e9a-723ceca4bd8f";
    public const string WritableValueLength = "5b833c91-6bc7-4802-8e9a-723ceca4bd8f";
    public const string DetermineMtu = "5b833c93-6bc7-4802-8e9a-723ceca4bd8f";
    public const string OptimalMtu = "5b833c94-6bc7-4802-8e9a-723ceca4bd8f";

    public const string SppV1Uuid = "96cc203e-5068-46ad-b32d-e316f5e069ba";
    public const string SppV2Uuid = "956c7b26-d49a-4ba8-b03f-b17d393cb6e2";

    public static readonly Guid TandemV2HpcServiceGuid = Guid.Parse(TandemV2HpcService);
    public static readonly Guid TandemV2McServiceGuid = Guid.Parse(TandemV2McService);
    public static readonly Guid TandemV1McServiceGuid = Guid.Parse(TandemV1McService);

    public static readonly Guid TandemHpcToAccGuid = Guid.Parse(TandemHpcToAcc);
    public static readonly Guid TandemHpcFromAccGuid = Guid.Parse(TandemHpcFromAcc);
    public static readonly Guid TandemMcToAccGuid = Guid.Parse(TandemMcToAcc);
    public static readonly Guid TandemMcFromAccGuid = Guid.Parse(TandemMcFromAcc);

    public static readonly Guid ReadyToStartInitialCommunicationGuid = Guid.Parse(ReadyToStartInitialCommunication);
    public static readonly Guid WritableValueLengthGuid = Guid.Parse(WritableValueLength);
    public static readonly Guid DetermineMtuGuid = Guid.Parse(DetermineMtu);
    public static readonly Guid OptimalMtuGuid = Guid.Parse(OptimalMtu);

    public static readonly Guid SppV1Guid = Guid.Parse(SppV1Uuid);
    public static readonly Guid SppV2Guid = Guid.Parse(SppV2Uuid);
}

/// <summary>
/// 消息层数据类型标记（注意：与帧层 SonySppFrameType 是两个独立命名空间；
/// 消息层 0x0E = Table1 DATA_MDR，而帧层 0x0E = Table2 载体 DATA_MDR_NO2）。
/// </summary>
public enum SonyTandemDataType : byte
{
    DataMdr = 0x0E,
    DataMdrNo2 = 0x0F,
}

/// <summary>
/// 帧层（线缆上）帧类型标记（对应 constants.py 中的 SonySppFrameType）。
/// </summary>
public enum SonySppFrameType : byte
{
    Ack = 0x01,
    DataMdr = 0x0C,
    DataMdrNo2 = 0x0E,
    ShotMdr = 0x1C,
    ShotMdrNo2 = 0x1E,
    LargeDataMdr = 0x2C,
    Unknown = 0xFF,
}

public enum DeviceInfoType : byte
{
    ModelName = 0x01,
    FwVersion = 0x02,
    SeriesAndColorInfo = 0x03,
    InstructionGuide = 0x04,
}

public enum CommonInquiredType : byte
{
    Concierge = 0x00,
    ConnectionStatus = 0x01,
    AudioCodec = 0x02,
    UpscalingEffect = 0x03,
    BleSetup = 0x04,
    ConnectionEstablishedTime = 0x05,
    DeviceSpecialMode = 0x06,
    DisplayFwVersion = 0x09,
}

public enum PowerInquiredType : byte
{
    Battery = 0x00,
    LeftRightBattery = 0x01,
    CradleBattery = 0x02,
    AutoPowerOff = 0x04,
    PowerSaveMode = 0x06,
    Stamina = 0x0E,
}

/// <summary>
/// 音频编码格式。底层类型使用 int 以兼容 Table1 中按 2 字节大端读取编码值的分支。
/// </summary>
public enum SoundQualityCodec
{
    Unsettled = 0x00,
    Sbc = 0x01,
    Aac = 0x02,
    Ldac = 0x10,
    AptX = 0x20,
    AptXHd = 0x21,
    Lc3 = 0x30,
    Other = 0xFF,
}

public enum DseeGeneration : byte
{
    DseeHx = 0x00,
    Dsee = 0x01,
    DseeHxAi = 0x02,
    DseeUltimate = 0x03,
}

public enum DseeEffectState : byte
{
    Off = 0x00,
    Valid = 0x01,
    Invalid = 0x02,
}

public enum ConnectionQualityMode : byte
{
    SoundQualityPrior = 0x00,
    ConnectionQualityPrior = 0x01,
    LowLatencyPriorBeta = 0x02,
}

public enum NoiseControlMode : byte
{
    Off = 0,
    NoiseCancelling = 1,
    AmbientSound = 2,
}

public enum AmbientSoundMode : byte
{
    Normal = 0x00,
    Voice = 0x01,
}

public enum NoiseAdaptiveSensitivity : byte
{
    Standard = 0x00,
    High = 0x01,
    Low = 0x02,
}

public enum NcAsmInquiredType : byte
{
    NcOnOff = 0x01,
    NcOnOffAndAsmOnOff = 0x02,
    NcModeSwitchAndAsmOnOff = 0x03,
    NcOnOffAndAsmSeamless = 0x13,
    NcModeSwitchAndAsmSeamless = 0x14,
    ModeNcAsmAutoNcModeSwitchAndAsmSeamless = 0x15,
    ModeNcAsmDualSingleNcModeSwitchAndAsmSeamless = 0x16,
    ModeNcAsmDualNcModeSwitchAndAsmSeamless = 0x17,
    ModeNcNcssAsmDualNcModeSwitchAndAsmSeamless = 0x18,
    ModeNcAsmDualNcModeSwitchAndAsmSeamlessNa = 0x19,
    AsmOnOff = 0x21,
    AsmSeamless = 0x22,
    NcAmbToggle = 0x30,
    NcTestMode = 0x40,
}

public enum EqEbbInquiredType : byte
{
    PresetEq = 0x00,
    Ebb = 0x01,
    PresetEqNoncustomizable = 0x02,
    PresetEqAndUltMode = 0x03,
    PresetEqAndErrorcode = 0x04,
    SoundEffect = 0x30,
    CustomEq = 0x31,
    TurnKeyEq = 0x32,
    CustomizableSoundEffectSelect = 0x33,
    UltBtnSoundEffectAssign = 0x40,
}

public enum EqPresetId : byte
{
    Off = 0x00,
    Rock = 0x01,
    Pop = 0x02,
    Jazz = 0x03,
    Dance = 0x04,
    Edm = 0x05,
    RAndBHipHop = 0x06,
    Acoustic = 0x07,
    Bright = 0x10,
    Excited = 0x11,
    Mellow = 0x12,
    Relaxed = 0x13,
    Vocal = 0x14,
    Treble = 0x15,
    Bass = 0x16,
    Speech = 0x17,
    GamingEq = 0x20,
    Fps1 = 0x21,
    Fps2 = 0x22,
    Fps3 = 0x23,
    Heavy = 0x30,
    Clear = 0x31,
    Hard = 0x32,
    Soft = 0x33,
    Flat = 0x34,
    LiveSound = 0x35,
    Ult = 0x40,
    Ult1 = 0x41,
    Ult2 = 0x42,
    Custom = 0xA0,
    UserSetting1 = 0xA1,
    UserSetting2 = 0xA2,
    UserSetting3 = 0xA3,
    UserSetting4 = 0xA4,
    UserSetting5 = 0xA5,
    Unspecified = 0xFF,
}

public enum PlayInquiredType : byte
{
    PlaybackControlWithCallVolumeAdjustment = 0x01,
    PlaybackControlWithCallVolumeAdjustmentAndFunctionChange = 0x02,
    PlaybackControlWithFunctionChange = 0x03,
    MusicVolume = 0x20,
    CallVolume = 0x21,
    MusicVolumeWithMute = 0x30,
    CallVolumeWithMute = 0x31,
    PlayMode = 0x40,
}

public enum PlaybackControl : byte
{
    Pause = 0x01,
    TrackUp = 0x02,
    TrackDown = 0x03,
    Stop = 0x06,
    Play = 0x07,
}

/// <summary>
/// 帧重发策略（对应 Python SonySppFrameType.retry_policy 返回的 (timeout_s, max_retries)）。
/// </summary>
public readonly record struct FrameRetryPolicy(TimeSpan Timeout, int MaxRetries);

/// <summary>
/// 对应 constants.py 中各 IntEnum 的 @property 辅助方法。
/// </summary>
public static class SonyProtocolEnumExtensions
{
    private static readonly IReadOnlyDictionary<int, string> CodecLabels = new Dictionary<int, string>
    {
        [0x00] = "未确定",
        [0x01] = "SBC",
        [0x02] = "AAC",
        [0x10] = "LDAC",
        [0x20] = "aptX",
        [0x21] = "aptX HD",
        [0x30] = "LC3",
        [0xFF] = "其他",
    };

    private static readonly IReadOnlyDictionary<byte, string> DseeLabels = new Dictionary<byte, string>
    {
        [0x00] = "DSEE HX",
        [0x01] = "DSEE",
        [0x02] = "DSEE Extreme",
        [0x03] = "DSEE Ultimate",
    };

    private static readonly IReadOnlyDictionary<byte, string> EqPresetLabels = new Dictionary<byte, string>
    {
        [0x00] = "关闭 (Off)",
        [0x01] = "摇滚 (Rock)",
        [0x02] = "流行 (Pop)",
        [0x03] = "爵士 (Jazz)",
        [0x04] = "舞曲 (Dance)",
        [0x05] = "电子 (EDM)",
        [0x06] = "R&B / Hip-Hop",
        [0x07] = "原声 (Acoustic)",
        [0x10] = "明亮 (Bright)",
        [0x11] = "激昂 (Excited)",
        [0x12] = "柔和 (Mellow)",
        [0x13] = "轻松 (Relaxed)",
        [0x14] = "人声 (Vocal)",
        [0x15] = "增强高音 (Treble)",
        [0x16] = "增强低音 (Bass)",
        [0x17] = "演讲 (Speech)",
        [0x30] = "重低音 (Heavy)",
        [0x31] = "清澈 (Clear)",
        [0x32] = "硬朗 (Hard)",
        [0x33] = "柔和 (Soft)",
        [0x34] = "平直 (Flat)",
        [0x35] = "现场 (Live)",
        [0xA0] = "自定义 (Custom)",
        [0xA1] = "自定义 1",
        [0xA2] = "自定义 2",
        [0xA3] = "自定义 3",
        [0xA4] = "自定义 4",
        [0xA5] = "自定义 5",
    };

    public static bool AckRequired(this SonySppFrameType frameType) =>
        frameType is SonySppFrameType.DataMdr
            or SonySppFrameType.DataMdrNo2
            or SonySppFrameType.LargeDataMdr;

    public static FrameRetryPolicy? GetRetryPolicy(this SonySppFrameType frameType) =>
        frameType switch
        {
            SonySppFrameType.DataMdr or SonySppFrameType.DataMdrNo2 => new FrameRetryPolicy(TimeSpan.FromMilliseconds(750), 10),
            SonySppFrameType.LargeDataMdr => new FrameRetryPolicy(TimeSpan.FromSeconds(5.0), 2),
            _ => null,
        };

    public static string GetLabel(this SoundQualityCodec codec) =>
        CodecLabels.TryGetValue((int)codec, out var label) ? label : codec.ToString();

    public static string GetLabel(this DseeGeneration generation) =>
        DseeLabels.TryGetValue((byte)generation, out var label) ? label : generation.ToString();

    public static string GetLabel(this EqPresetId preset) =>
        EqPresetLabels.TryGetValue((byte)preset, out var label) ? label : preset.ToString();
}
