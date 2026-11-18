"""Sony Tandem protocol constants, GATT UUIDs, and enumerations."""

from enum import IntEnum


class SonyGatt:
    UUID_SUFFIX = "-6bc7-4802-8e9a-723ceca4bd8f"

    @classmethod
    def service(cls, suffix: int) -> str:
        return f"5b833e{suffix:02x}{cls.UUID_SUFFIX}"

    @classmethod
    def characteristic(cls, suffix: int) -> str:
        return f"5b833c{suffix:02x}{cls.UUID_SUFFIX}"

    TANDEM_V2_HPC_SERVICE = "5b833e20-6bc7-4802-8e9a-723ceca4bd8f"
    TANDEM_V2_MC_SERVICE = "5b833e21-6bc7-4802-8e9a-723ceca4bd8f"
    TANDEM_V1_MC_SERVICE = "5b833e23-6bc7-4802-8e9a-723ceca4bd8f"

    TANDEM_HPC_TO_ACC = "5b833c60-6bc7-4802-8e9a-723ceca4bd8f"
    TANDEM_HPC_FROM_ACC = "5b833c61-6bc7-4802-8e9a-723ceca4bd8f"
    TANDEM_MC_TO_ACC = "5b833c62-6bc7-4802-8e9a-723ceca4bd8f"
    TANDEM_MC_FROM_ACC = "5b833c63-6bc7-4802-8e9a-723ceca4bd8f"

    READY_TO_START_INITIAL_COMMUNICATION = "5b833c90-6bc7-4802-8e9a-723ceca4bd8f"
    WRITABLE_VALUE_LENGTH = "5b833c91-6bc7-4802-8e9a-723ceca4bd8f"
    DETERMINE_MTU = "5b833c93-6bc7-4802-8e9a-723ceca4bd8f"
    OPTIMAL_MTU = "5b833c94-6bc7-4802-8e9a-723ceca4bd8f"

    SPP_V1_UUID = "96cc203e-5068-46ad-b32d-e316f5e069ba"
    SPP_V2_UUID = "956c7b26-d49a-4ba8-b03f-b17d393cb6e2"


class SonyTandemDataType(IntEnum):
    DATA_MDR = 0x0E
    DATA_MDR_NO2 = 0x0F


class SonySppFrameType(IntEnum):
    ACK = 0x01
    DATA_MDR = 0x0C
    DATA_MDR_NO2 = 0x0E
    SHOT_MDR = 0x1C
    SHOT_MDR_NO2 = 0x1E
    LARGE_DATA_MDR = 0x2C
    UNKNOWN = 0xFF

    @property
    def ack_required(self) -> bool:
        return self in (
            SonySppFrameType.DATA_MDR,
            SonySppFrameType.DATA_MDR_NO2,
            SonySppFrameType.LARGE_DATA_MDR,
        )

    @property
    def retry_policy(self) -> tuple[float, int] | None:
        if self in (SonySppFrameType.DATA_MDR, SonySppFrameType.DATA_MDR_NO2):
            return (0.75, 10)
        if self == SonySppFrameType.LARGE_DATA_MDR:
            return (5.0, 2)
        return None


class DeviceInfoType(IntEnum):
    MODEL_NAME = 0x01
    FW_VERSION = 0x02
    SERIES_AND_COLOR_INFO = 0x03
    INSTRUCTION_GUIDE = 0x04


class CommonInquiredType(IntEnum):
    CONCIERGE = 0x00
    CONNECTION_STATUS = 0x01
    AUDIO_CODEC = 0x02
    UPSCALING_EFFECT = 0x03
    BLE_SETUP = 0x04
    CONNECTION_ESTABLISHED_TIME = 0x05
    DEVICE_SPECIAL_MODE = 0x06
    DISPLAY_FW_VERSION = 0x09


class PowerInquiredType(IntEnum):
    BATTERY = 0x00
    LEFT_RIGHT_BATTERY = 0x01
    CRADLE_BATTERY = 0x02
    AUTO_POWER_OFF = 0x04
    POWER_SAVE_MODE = 0x06
    STAMINA = 0x0E


class SoundQualityCodec(IntEnum):
    UNSETTLED = 0x00
    SBC = 0x01
    AAC = 0x02
    LDAC = 0x10
    APT_X = 0x20
    APT_X_HD = 0x21
    LC3 = 0x30
    OTHER = 0xFF

    @property
    def label(self) -> str:
        return {
            0x00: "未确定",
            0x01: "SBC",
            0x02: "AAC",
            0x10: "LDAC",
            0x20: "aptX",
            0x21: "aptX HD",
            0x30: "LC3",
            0xFF: "其他",
        }.get(int(self), self.name)


class DseeGeneration(IntEnum):
    DSEE_HX = 0x00
    DSEE = 0x01
    DSEE_HX_AI = 0x02
    DSEE_ULTIMATE = 0x03

    @property
    def label(self) -> str:
        return {
            0x00: "DSEE HX",
            0x01: "DSEE",
            0x02: "DSEE Extreme",
            0x03: "DSEE Ultimate",
        }.get(int(self), self.name)


class DseeEffectState(IntEnum):
    OFF = 0x00
    VALID = 0x01
    INVALID = 0x02


class ConnectionQualityMode(IntEnum):
    SOUND_QUALITY_PRIOR = 0x00
    CONNECTION_QUALITY_PRIOR = 0x01
    LOW_LATENCY_PRIOR_BETA = 0x02


class NoiseControlMode(IntEnum):
    OFF = 0
    NOISE_CANCELLING = 1
    AMBIENT_SOUND = 2


class AmbientSoundMode(IntEnum):
    NORMAL = 0x00
    VOICE = 0x01


class NoiseAdaptiveSensitivity(IntEnum):
    STANDARD = 0x00
    HIGH = 0x01
    LOW = 0x02


class NcAsmInquiredType(IntEnum):
    NC_ON_OFF = 0x01
    NC_ON_OFF_AND_ASM_ON_OFF = 0x02
    NC_MODE_SWITCH_AND_ASM_ON_OFF = 0x03
    NC_ON_OFF_AND_ASM_SEAMLESS = 0x13
    NC_MODE_SWITCH_AND_ASM_SEAMLESS = 0x14
    MODE_NC_ASM_AUTO_NC_MODE_SWITCH_AND_ASM_SEAMLESS = 0x15
    MODE_NC_ASM_DUAL_SINGLE_NC_MODE_SWITCH_AND_ASM_SEAMLESS = 0x16
    MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS = 0x17
    MODE_NC_NCSS_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS = 0x18
    MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS_NA = 0x19
    ASM_ON_OFF = 0x21
    ASM_SEAMLESS = 0x22
    NC_AMB_TOGGLE = 0x30
    NC_TEST_MODE = 0x40


class EqEbbInquiredType(IntEnum):
    PRESET_EQ = 0x00
    EBB = 0x01
    PRESET_EQ_NONCUSTOMIZABLE = 0x02
    PRESET_EQ_AND_ULT_MODE = 0x03
    PRESET_EQ_AND_ERRORCODE = 0x04
    SOUND_EFFECT = 0x30
    CUSTOM_EQ = 0x31
    TURN_KEY_EQ = 0x32
    CUSTOMIZABLE_SOUND_EFFECT_SELECT = 0x33
    ULT_BTN_SOUND_EFFECT_ASSIGN = 0x40


class EqPresetId(IntEnum):
    OFF = 0x00
    ROCK = 0x01
    POP = 0x02
    JAZZ = 0x03
    DANCE = 0x04
    EDM = 0x05
    R_AND_B_HIP_HOP = 0x06
    ACOUSTIC = 0x07
    BRIGHT = 0x10
    EXCITED = 0x11
    MELLOW = 0x12
    RELAXED = 0x13
    VOCAL = 0x14
    TREBLE = 0x15
    BASS = 0x16
    SPEECH = 0x17
    GAMING_EQ = 0x20
    FPS_1 = 0x21
    FPS_2 = 0x22
    FPS_3 = 0x23
    HEAVY = 0x30
    CLEAR = 0x31
    HARD = 0x32
    SOFT = 0x33
    FLAT = 0x34
    LIVE_SOUND = 0x35
    ULT = 0x40
    ULT_1 = 0x41
    ULT_2 = 0x42
    CUSTOM = 0xA0
    USER_SETTING1 = 0xA1
    USER_SETTING2 = 0xA2
    USER_SETTING3 = 0xA3
    USER_SETTING4 = 0xA4
    USER_SETTING5 = 0xA5
    UNSPECIFIED = 0xFF

    @property
    def label(self) -> str:
        return {
            0x00: "关闭 (Off)",
            0x01: "摇滚 (Rock)",
            0x02: "流行 (Pop)",
            0x03: "爵士 (Jazz)",
            0x04: "舞曲 (Dance)",
            0x05: "电子 (EDM)",
            0x06: "R&B / Hip-Hop",
            0x07: "原声 (Acoustic)",
            0x10: "明亮 (Bright)",
            0x11: "激昂 (Excited)",
            0x12: "柔和 (Mellow)",
            0x13: "轻松 (Relaxed)",
            0x14: "人声 (Vocal)",
            0x15: "增强高音 (Treble)",
            0x16: "增强低音 (Bass)",
            0x17: "演讲 (Speech)",
            0x30: "重低音 (Heavy)",
            0x31: "清澈 (Clear)",
            0x32: "硬朗 (Hard)",
            0x33: "柔和 (Soft)",
            0x34: "平直 (Flat)",
            0x35: "现场 (Live)",
            0xA0: "自定义 (Custom)",
            0xA1: "自定义 1",
            0xA2: "自定义 2",
            0xA3: "自定义 3",
            0xA4: "自定义 4",
            0xA5: "自定义 5",
        }.get(int(self), self.name)


class PlayInquiredType(IntEnum):
    PLAYBACK_CONTROL_WITH_CALL_VOLUME_ADJUSTMENT = 0x01
    PLAYBACK_CONTROL_WITH_CALL_VOLUME_ADJUSTMENT_AND_FUNCTION_CHANGE = 0x02
    PLAYBACK_CONTROL_WITH_FUNCTION_CHANGE = 0x03
    MUSIC_VOLUME = 0x20
    CALL_VOLUME = 0x21
    MUSIC_VOLUME_WITH_MUTE = 0x30
    CALL_VOLUME_WITH_MUTE = 0x31
    PLAY_MODE = 0x40


class PlaybackControl(IntEnum):
    PAUSE = 0x01
    TRACK_UP = 0x02
    TRACK_DOWN = 0x03
    STOP = 0x06
    PLAY = 0x07
