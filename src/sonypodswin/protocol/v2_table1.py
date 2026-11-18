"""Sony Tandem V2 Table 1 protocol builders and parsers."""

from dataclasses import dataclass, field

from .constants import (
    AmbientSoundMode,
    CommonInquiredType,
    ConnectionQualityMode,
    DeviceInfoType,
    DseeEffectState,
    DseeGeneration,
    EqEbbInquiredType,
    EqPresetId,
    NcAsmInquiredType,
    NoiseAdaptiveSensitivity,
    NoiseControlMode,
    PlaybackControl,
    PlayInquiredType,
    PowerInquiredType,
    SonyTandemDataType,
    SoundQualityCodec,
)

from .v2_table2 import PeripheralDevicesResponse, parse_table2

# Command codes
CONNECT_GET_PROTOCOL_INFO = 0x00
CONNECT_RET_PROTOCOL_INFO = 0x01
CONNECT_GET_CAPABILITY_INFO = 0x02
CONNECT_RET_CAPABILITY_INFO = 0x03
CONNECT_GET_DEVICE_INFO = 0x04
CONNECT_RET_DEVICE_INFO = 0x05
CONNECT_GET_SUPPORT_FUNCTION = 0x06
CONNECT_RET_SUPPORT_FUNCTION = 0x07

COMMON_GET_STATUS = 0x12
COMMON_RET_STATUS = 0x13
COMMON_NTFY_STATUS = 0x15

POWER_GET_STATUS = 0x22
POWER_RET_STATUS = 0x23
POWER_SET_STATUS = 0x24
POWER_NTFY_STATUS = 0x25
POWER_GET_PARAM = 0x26
POWER_RET_PARAM = 0x27
POWER_SET_PARAM = 0x28
POWER_NTFY_PARAM = 0x29

ALERT_SET_PARAM = 0x98
ALERT_NTFY_PARAM = 0x99

EQEBB_GET_CAPABILITY = 0x50
EQEBB_RET_CAPABILITY = 0x51
EQEBB_GET_STATUS = 0x52
EQEBB_RET_STATUS = 0x53
EQEBB_NTFY_STATUS = 0x55
EQEBB_GET_PARAM = 0x56
EQEBB_RET_PARAM = 0x57
EQEBB_SET_PARAM = 0x58
EQEBB_NTFY_PARAM = 0x59
EQEBB_GET_EXTENDED_INFO = 0x5A
EQEBB_RET_EXTENDED_INFO = 0x5B

NCASM_GET_CAPABILITY = 0x60
NCASM_RET_CAPABILITY = 0x61
NCASM_GET_STATUS = 0x62
NCASM_RET_STATUS = 0x63
NCASM_NTFY_STATUS = 0x65
NCASM_GET_PARAM = 0x66
NCASM_RET_PARAM = 0x67
NCASM_SET_PARAM = 0x68
NCASM_NTFY_PARAM = 0x69

PLAY_GET_CAPABILITY = 0xA0
PLAY_RET_CAPABILITY = 0xA1
PLAY_GET_STATUS = 0xA2
PLAY_RET_STATUS = 0xA3
PLAY_SET_STATUS = 0xA4
PLAY_NTFY_STATUS = 0xA5
PLAY_GET_PARAM = 0xA6
PLAY_RET_PARAM = 0xA7
PLAY_SET_PARAM = 0xA8
PLAY_NTFY_PARAM = 0xA9

GS_GET_CAPABILITY = 0xD0
GS_RET_CAPABILITY = 0xD1
GS_GET_STATUS = 0xD2
GS_RET_STATUS = 0xD3
GS_NTFY_STATUS = 0xD5
GS_GET_PARAM = 0xD6
GS_RET_PARAM = 0xD7
GS_SET_PARAM = 0xD8
GS_NTFY_PARAM = 0xD9

AUDIO_GET_CAPABILITY = 0xE0
AUDIO_RET_CAPABILITY = 0xE1
AUDIO_GET_STATUS = 0xE2
AUDIO_RET_STATUS = 0xE3
AUDIO_NTFY_STATUS = 0xE5
AUDIO_GET_PARAM = 0xE6
AUDIO_RET_PARAM = 0xE7
AUDIO_SET_PARAM = 0xE8
AUDIO_NTFY_PARAM = 0xE9

SYSTEM_GET_CAPABILITY = 0xF0
SYSTEM_RET_CAPABILITY = 0xF1
SYSTEM_GET_STATUS = 0xF2
SYSTEM_RET_STATUS = 0xF3
SYSTEM_NTFY_STATUS = 0xF5
SYSTEM_GET_PARAM = 0xF6
SYSTEM_RET_PARAM = 0xF7
SYSTEM_SET_PARAM = 0xF8
SYSTEM_NTFY_PARAM = 0xF9
SYSTEM_GET_EXT_PARAM = 0xFA
SYSTEM_RET_EXT_PARAM = 0xFB
SYSTEM_SET_EXT_PARAM = 0xFC
SYSTEM_NTFY_EXT_PARAM = 0xFD

# NCASM constants
NCASM_OFF = 0x00
NCASM_ON = 0x01
VALUE_CHANGED = 0x01
NCASM_MODE_NC = 0x00
NCASM_MODE_ASM = 0x01
NC_VALUE_OFF = 0x00
NC_VALUE_ON_SINGLE = 0x01
NC_VALUE_ON_DUAL = 0x02
NC_VALUE_AUTO = 0x03

# Audio constants
AUDIO_INQ_CONNECTION_MODE = 0x00
AUDIO_INQ_UPSCALING = 0x01
AUDIO_INQ_CONNECTION_MODE_WITH_LDAC = 0x02
AUDIO_INQ_CONNECTION_MODE_LEA = 0x05
AUDIO_INQ_UPSCALING_WITH_REASON = 0x0B
AUDIO_INQ_UPMIX_CINEMA = 0x20
AUDIO_INQ_BGM_MODE = 0x30
UPSCALING_OFF = 0x00
UPSCALING_AUTO = 0x01

# System constants
SYS_SMART_TALKING_TYPE1 = 0x01
SYS_SMART_TALKING_TYPE2 = 0x02
SYS_WEARING_STATUS_DETECTOR = 0x04


def _msg(cmd: int, payload: bytes = b"") -> bytes:
    return bytes([SonyTandemDataType.DATA_MDR, cmd & 0xFF]) + payload


# ── Builders ───────────────────────────────────────────────────────────────

def build_get_protocol_info() -> bytes:
    return _msg(CONNECT_GET_PROTOCOL_INFO, b"\x00")


def build_get_capability_info() -> bytes:
    return _msg(CONNECT_GET_CAPABILITY_INFO, b"\x00")


def build_get_device_info(info_type: DeviceInfoType) -> bytes:
    return _msg(CONNECT_GET_DEVICE_INFO, bytes([int(info_type)]))


def build_get_support_function() -> bytes:
    return _msg(CONNECT_GET_SUPPORT_FUNCTION, b"\x00")


def build_get_battery_status(power_type: PowerInquiredType = PowerInquiredType.BATTERY) -> bytes:
    return _msg(POWER_GET_STATUS, bytes([int(power_type)]))


def build_power_off() -> bytes:
    return _msg(POWER_SET_STATUS, bytes([0x03, 0x01]))


def build_get_auto_power_off(inquired_type: int = 0x05) -> bytes:
    return _msg(POWER_GET_PARAM, bytes([inquired_type & 0xFF]))


def build_set_auto_power_off(inquired_type: int, value_bytes: bytes) -> bytes:
    return _msg(POWER_SET_PARAM, bytes([inquired_type & 0xFF]) + value_bytes)


def build_reply_alert_fixed_message(message_type: int, positive: bool = True) -> bytes:
    return _msg(ALERT_SET_PARAM, bytes([0x00, message_type & 0xFF, 0x01 if positive else 0x00]))


def build_get_audio_codec_status() -> bytes:
    return _msg(COMMON_GET_STATUS, bytes([int(CommonInquiredType.AUDIO_CODEC)]))


def build_get_upscaling_effect_status() -> bytes:
    return _msg(COMMON_GET_STATUS, bytes([int(CommonInquiredType.UPSCALING_EFFECT)]))


def build_get_nc_asm_capability(nc_type: NcAsmInquiredType) -> bytes:
    return _msg(NCASM_GET_CAPABILITY, bytes([int(nc_type)]))


def build_get_nc_asm_status(nc_type: NcAsmInquiredType) -> bytes:
    return _msg(NCASM_GET_STATUS, bytes([int(nc_type)]))


def build_get_nc_asm_param(nc_type: NcAsmInquiredType) -> bytes:
    return _msg(NCASM_GET_PARAM, bytes([int(nc_type)]))


def build_set_noise_control_mode(
    control_mode: NoiseControlMode,
    ambient_level: int = 10,
    ambient_mode: AmbientSoundMode = AmbientSoundMode.NORMAL,
    nc_type: NcAsmInquiredType = NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS,
    noise_adaptive: bool = False,
    noise_adaptive_sensitivity: NoiseAdaptiveSensitivity = NoiseAdaptiveSensitivity.STANDARD,
    wind_noise_reduction: bool = False,
) -> bytes:
    effect = NCASM_OFF if control_mode == NoiseControlMode.OFF else NCASM_ON
    mode = NCASM_MODE_ASM if control_mode == NoiseControlMode.AMBIENT_SOUND else NCASM_MODE_NC
    level = max(1, min(20, ambient_level))
    nc_on = NCASM_ON if control_mode == NoiseControlMode.NOISE_CANCELLING else NCASM_OFF
    if control_mode == NoiseControlMode.NOISE_CANCELLING:
        nc_value = NC_VALUE_ON_SINGLE if wind_noise_reduction else NC_VALUE_ON_DUAL
        auto_nc_value = NC_VALUE_AUTO if wind_noise_reduction else NC_VALUE_ON_DUAL
    else:
        nc_value = NC_VALUE_OFF
        auto_nc_value = NC_VALUE_ON_DUAL
    asm_on = NCASM_ON if control_mode == NoiseControlMode.AMBIENT_SOUND else NCASM_OFF

    if nc_type == NcAsmInquiredType.NC_AMB_TOGGLE:
        fn_map = {
            NoiseControlMode.OFF: 0x00,
            NoiseControlMode.NOISE_CANCELLING: 0x02,
            NoiseControlMode.AMBIENT_SOUND: 0x01,
        }
        return _msg(NCASM_SET_PARAM, bytes([int(nc_type), fn_map[control_mode]]))

    if nc_type == NcAsmInquiredType.NC_ON_OFF:
        body = bytes([nc_on])
    elif nc_type == NcAsmInquiredType.NC_ON_OFF_AND_ASM_ON_OFF:
        body = bytes([nc_on, int(ambient_mode), asm_on])
    elif nc_type == NcAsmInquiredType.NC_MODE_SWITCH_AND_ASM_ON_OFF:
        body = bytes([nc_value, int(ambient_mode), asm_on])
    elif nc_type == NcAsmInquiredType.NC_ON_OFF_AND_ASM_SEAMLESS:
        body = bytes([nc_on, int(ambient_mode), level])
    elif nc_type == NcAsmInquiredType.NC_MODE_SWITCH_AND_ASM_SEAMLESS:
        body = bytes([nc_value, int(ambient_mode), level])
    elif nc_type == NcAsmInquiredType.MODE_NC_ASM_AUTO_NC_MODE_SWITCH_AND_ASM_SEAMLESS:
        body = bytes([mode, auto_nc_value, int(ambient_mode), level])
    elif nc_type == NcAsmInquiredType.MODE_NC_ASM_DUAL_SINGLE_NC_MODE_SWITCH_AND_ASM_SEAMLESS:
        body = bytes([mode, nc_value, int(ambient_mode), level])
    elif nc_type in (
        NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS,
        NcAsmInquiredType.MODE_NC_NCSS_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS,
    ):
        body = bytes([mode, int(ambient_mode), level])
    elif nc_type == NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS_NA:
        body = bytes([
            mode,
            int(ambient_mode),
            level,
            NCASM_ON if noise_adaptive else NCASM_OFF,
            int(noise_adaptive_sensitivity),
        ])
    elif nc_type == NcAsmInquiredType.ASM_ON_OFF:
        body = bytes([int(ambient_mode), asm_on])
    elif nc_type == NcAsmInquiredType.ASM_SEAMLESS:
        body = bytes([int(ambient_mode), level])
    else:
        body = bytes([mode, int(ambient_mode), level])

    return _msg(NCASM_SET_PARAM, bytes([int(nc_type), VALUE_CHANGED, effect]) + body)


def build_get_eq_ebb_capability(eq_type: EqEbbInquiredType = EqEbbInquiredType.PRESET_EQ) -> bytes:
    return _msg(EQEBB_GET_CAPABILITY, bytes([int(eq_type), 0x00]))


def build_get_eq_ebb_status(eq_type: EqEbbInquiredType = EqEbbInquiredType.PRESET_EQ) -> bytes:
    return _msg(EQEBB_GET_STATUS, bytes([int(eq_type)]))


def build_get_eq_ebb_param(eq_type: EqEbbInquiredType = EqEbbInquiredType.PRESET_EQ) -> bytes:
    return _msg(EQEBB_GET_PARAM, bytes([int(eq_type)]))


def build_get_eq_ebb_extended_info(eq_type: EqEbbInquiredType = EqEbbInquiredType.PRESET_EQ) -> bytes:
    return _msg(EQEBB_GET_EXTENDED_INFO, bytes([int(eq_type)]))


def build_set_eq_preset(
    preset: EqPresetId,
    eq_type: EqEbbInquiredType = EqEbbInquiredType.PRESET_EQ,
    band_steps: list[int] | None = None,
) -> bytes:
    steps = [max(0, min(255, s)) for s in (band_steps or [])]
    if eq_type == EqEbbInquiredType.CUSTOM_EQ:
        body = bytes([int(eq_type), len(steps), *steps])
    elif eq_type == EqEbbInquiredType.PRESET_EQ_AND_ULT_MODE:
        body = bytes([int(eq_type), int(preset), 0x00, len(steps), *steps])
    else:
        body = bytes([int(eq_type), int(preset), len(steps), *steps])
    return _msg(EQEBB_SET_PARAM, body)


def build_set_clear_bass(level: int, eq_type: EqEbbInquiredType = EqEbbInquiredType.EBB) -> bytes:
    clamped = max(-10, min(10, level)) & 0xFF
    return _msg(EQEBB_SET_PARAM, bytes([int(eq_type), clamped]))


def build_get_upscaling(inquired_type: int = AUDIO_INQ_UPSCALING) -> bytes:
    return _msg(AUDIO_GET_PARAM, bytes([inquired_type & 0xFF]))


def build_set_upscaling(on: bool, inquired_type: int = AUDIO_INQ_UPSCALING) -> bytes:
    return _msg(AUDIO_SET_PARAM, bytes([inquired_type & 0xFF, UPSCALING_AUTO if on else UPSCALING_OFF]))


def build_get_connection_quality(inquired_type: int = AUDIO_INQ_CONNECTION_MODE) -> bytes:
    return _msg(AUDIO_GET_PARAM, bytes([inquired_type & 0xFF]))


def build_set_connection_quality(
    mode: ConnectionQualityMode,
    inquired_type: int = AUDIO_INQ_CONNECTION_MODE,
) -> bytes:
    return _msg(AUDIO_SET_PARAM, bytes([inquired_type & 0xFF, int(mode)]))


def build_get_cinema_mode() -> bytes:
    return _msg(AUDIO_GET_PARAM, bytes([AUDIO_INQ_UPMIX_CINEMA]))


def build_set_cinema_mode(enabled: bool) -> bytes:
    return _msg(AUDIO_SET_PARAM, bytes([AUDIO_INQ_UPMIX_CINEMA, 0x00 if enabled else 0x01]))


def build_get_speak_to_chat_status(sys_type: int = SYS_SMART_TALKING_TYPE1) -> bytes:
    return _msg(SYSTEM_GET_STATUS, bytes([sys_type]))


def build_get_speak_to_chat_param(sys_type: int = SYS_SMART_TALKING_TYPE1) -> bytes:
    return _msg(SYSTEM_GET_PARAM, bytes([sys_type]))


def build_set_speak_to_chat_enabled(enabled: bool, sys_type: int = SYS_SMART_TALKING_TYPE1) -> bytes:
    return _msg(SYSTEM_SET_PARAM, bytes([sys_type, 0x00 if enabled else 0x01, 0x00]))


def build_get_wearing_status() -> bytes:
    return _msg(SYSTEM_GET_PARAM, bytes([SYS_WEARING_STATUS_DETECTOR]))


def build_get_general_setting_capability(slot: int) -> bytes:
    return _msg(GS_GET_CAPABILITY, bytes([slot & 0xFF, 0x01]))


def build_get_general_setting_param(slot: int) -> bytes:
    return _msg(GS_GET_PARAM, bytes([slot & 0xFF]))


def build_set_general_setting(slot: int, on: bool) -> bytes:
    # GsSettingType.BOOLEAN_TYPE=0x00, ON=0x00, OFF=0x01
    return _msg(GS_SET_PARAM, bytes([slot & 0xFF, 0x00, 0x00 if on else 0x01]))


def build_get_playback_param(play_type: PlayInquiredType = PlayInquiredType.MUSIC_VOLUME) -> bytes:
    return _msg(PLAY_GET_PARAM, bytes([int(play_type)]))


def build_set_playback_volume(volume: int, play_type: PlayInquiredType = PlayInquiredType.MUSIC_VOLUME) -> bytes:
    return _msg(PLAY_SET_PARAM, bytes([int(play_type), max(0, min(100, volume))]))


def build_playback(control: PlaybackControl, play_type: PlayInquiredType = PlayInquiredType.PLAYBACK_CONTROL_WITH_CALL_VOLUME_ADJUSTMENT) -> bytes:
    return _msg(PLAY_SET_STATUS, bytes([int(play_type), 0x00, int(control)]))


# ── Parsed responses ───────────────────────────────────────────────────────

@dataclass
class ProtocolInfoResponse:
    version: int
    raw: bytes


@dataclass
class DeviceInfoResponse:
    info_type: DeviceInfoType | None
    text: str | None
    raw: bytes


@dataclass
class SupportFunctionResponse:
    functions: list[tuple[int, int]]  # (function_code, order)
    raw: bytes


@dataclass
class BatteryResponse:
    kind: PowerInquiredType | None
    levels: list[int | None]
    raw: bytes


@dataclass
class NoiseControlResponse:
    nc_type: NcAsmInquiredType | None
    control_mode: NoiseControlMode | None
    ambient_level: int | None
    ambient_mode: AmbientSoundMode | None
    noise_adaptive: bool | None
    raw: bytes


@dataclass
class EqEbbResponse:
    eq_type: EqEbbInquiredType | None
    enabled: bool | None
    preset: EqPresetId | None
    band_steps: list[int]
    clear_bass: int | None
    raw: bytes


@dataclass
class AudioCodecResponse:
    codec: SoundQualityCodec
    raw: bytes


@dataclass
class UpscalingEffectResponse:
    generation: DseeGeneration
    state: DseeEffectState
    raw: bytes


@dataclass
class UpscalingParamResponse:
    enabled: bool
    inquired_type: int
    raw: bytes


@dataclass
class ConnectionQualityResponse:
    mode: ConnectionQualityMode
    inquired_type: int
    raw: bytes


@dataclass
class SpeakToChatResponse:
    enabled: bool
    sys_type: int
    raw: bytes


@dataclass
class GeneralSettingCapabilityResponse:
    slot: int
    title: str
    raw: bytes


@dataclass
class GeneralSettingParamResponse:
    slot: int
    enabled: bool
    raw: bytes


@dataclass
class PlaybackVolumeResponse:
    play_type: PlayInquiredType
    volume: int
    raw: bytes


@dataclass
class CapabilityInfoResponse:
    counter: int
    identifier: str
    raw: bytes


@dataclass
class AutoPowerOffResponse:
    inquired_type: int
    values: list[int]
    raw: bytes


@dataclass
class EqCapabilityResponse:
    eq_type: EqEbbInquiredType | None
    presets: list[EqPresetId]
    raw: bytes


@dataclass
class EqExtendedInfoResponse:
    eq_type: EqEbbInquiredType | None
    band_count: int
    has_clear_bass: bool
    raw: bytes


@dataclass
class AlertNtfyParamResponse:
    inquired_type: int
    message_type: int
    raw: bytes


@dataclass
class UnknownResponse:
    data_type: int
    command: int
    payload: bytes
    raw: bytes


ParsedResponse = (
    ProtocolInfoResponse
    | DeviceInfoResponse
    | SupportFunctionResponse
    | BatteryResponse
    | NoiseControlResponse
    | EqEbbResponse
    | AudioCodecResponse
    | UpscalingEffectResponse
    | UpscalingParamResponse
    | ConnectionQualityResponse
    | SpeakToChatResponse
    | GeneralSettingCapabilityResponse
    | GeneralSettingParamResponse
    | PlaybackVolumeResponse
    | UnknownResponse
)


def _pct_or_null(b: int) -> int | None:
    if b == 0xFF or b == 0x00 and False:
        return None
    if 0 <= b <= 100:
        return b
    return None


def _len_prefixed_str(payload: bytes, offset: int) -> str | None:
    if offset >= len(payload):
        return None
    length = payload[offset]
    start = offset + 1
    if length <= 0 or start + length > len(payload):
        raw_s = payload[1:].decode("utf-8", errors="ignore").rstrip("\x00").strip()
        return raw_s or None
    s = payload[start : start + length].decode("utf-8", errors="ignore").rstrip("\x00").strip()
    return s or None


def parse(raw: bytes) -> ParsedResponse | PeripheralDevicesResponse:
    if raw and raw[0] == SonyTandemDataType.DATA_MDR_NO2:
        t2 = parse_table2(raw)
        if t2 is not None:
            return t2
        return UnknownResponse(raw[0], raw[1] if len(raw) > 1 else 0, raw[2:], raw)

    normalized = raw if raw and raw[0] == SonyTandemDataType.DATA_MDR else bytes([SonyTandemDataType.DATA_MDR]) + raw
    if len(normalized) < 2:
        return UnknownResponse(0, 0, b"", raw)
    data_type = normalized[0]
    cmd = normalized[1]
    payload = normalized[2:]
    if data_type != SonyTandemDataType.DATA_MDR:
        return UnknownResponse(data_type, cmd, payload, raw)

    if cmd == CONNECT_RET_PROTOCOL_INFO and len(payload) >= 5:
        ver = (payload[1] << 24) | (payload[2] << 16) | (payload[3] << 8) | payload[4]
        return ProtocolInfoResponse(version=ver, raw=raw)

    if cmd == CONNECT_RET_CAPABILITY_INFO and len(payload) >= 3:
        counter = payload[1]
        ident = _len_prefixed_str(payload, 2) or ""
        return CapabilityInfoResponse(counter=counter, identifier=ident, raw=raw)

    if cmd == CONNECT_RET_DEVICE_INFO and payload:
        try:
            itype = DeviceInfoType(payload[0])
        except ValueError:
            itype = None
        text = None
        if itype in (DeviceInfoType.MODEL_NAME, DeviceInfoType.FW_VERSION, DeviceInfoType.INSTRUCTION_GUIDE):
            text = _len_prefixed_str(payload, 1)
        return DeviceInfoResponse(info_type=itype, text=text, raw=raw)

    if cmd == CONNECT_RET_SUPPORT_FUNCTION and len(payload) >= 2:
        count = payload[1]
        funcs: list[tuple[int, int]] = []
        for i in range(count):
            idx = 2 + i * 2
            if idx + 1 >= len(payload):
                break
            funcs.append((payload[idx], payload[idx + 1]))
        funcs.sort(key=lambda x: x[1])
        return SupportFunctionResponse(functions=funcs, raw=raw)

    if cmd in (POWER_RET_STATUS, POWER_NTFY_STATUS) and payload:
        code = payload[0]
        if code == 0x09:
            kind = PowerInquiredType.LEFT_RIGHT_BATTERY
        else:
            try:
                kind = PowerInquiredType(code)
            except ValueError:
                kind = None
        levels: list[int | None] = []
        if kind in (PowerInquiredType.BATTERY, PowerInquiredType.CRADLE_BATTERY) and len(payload) >= 2:
            levels = [_pct_or_null(payload[1])]
        elif kind == PowerInquiredType.LEFT_RIGHT_BATTERY:
            l_val = _pct_or_null(payload[1]) if len(payload) >= 2 else None
            r_val = _pct_or_null(payload[3]) if len(payload) >= 4 else None
            levels = [l_val, r_val]
        else:
            levels = list(payload[1:])
        return BatteryResponse(kind=kind, levels=levels, raw=raw)

    if cmd in (POWER_RET_PARAM, POWER_NTFY_PARAM) and payload:
        return AutoPowerOffResponse(inquired_type=payload[0], values=list(payload[1:]), raw=raw)

    if cmd in (COMMON_RET_STATUS, COMMON_NTFY_STATUS) and payload:
        if payload[0] == CommonInquiredType.AUDIO_CODEC and len(payload) >= 2:
            codec_val = payload[1] if len(payload) == 2 else ((payload[1] << 8) | payload[2])
            try:
                return AudioCodecResponse(codec=SoundQualityCodec(codec_val), raw=raw)
            except ValueError:
                pass
        if payload[0] == CommonInquiredType.UPSCALING_EFFECT and len(payload) == 3:
            try:
                return UpscalingEffectResponse(
                    generation=DseeGeneration(payload[1]),
                    state=DseeEffectState(payload[2]),
                    raw=raw,
                )
            except ValueError:
                pass

    if cmd in (NCASM_RET_PARAM, NCASM_NTFY_PARAM) and payload:
        return _parse_ncasm_param(payload, raw)

    if cmd == EQEBB_RET_CAPABILITY and len(payload) >= 5:
        try:
            eq_type = EqEbbInquiredType(payload[0])
        except ValueError:
            eq_type = None
        # Layout: [type][bandCount][levelSteps][presetCount][(presetId, 0x00)...]
        preset_count = payload[3]
        presets: list[EqPresetId] = []
        for i in range(preset_count):
            idx = 4 + i * 2
            if idx >= len(payload):
                break
            try:
                presets.append(EqPresetId(payload[idx]))
            except ValueError:
                pass
        return EqCapabilityResponse(eq_type=eq_type, presets=presets, raw=raw)

    if cmd in (EQEBB_RET_STATUS, EQEBB_NTFY_STATUS, EQEBB_RET_PARAM, EQEBB_NTFY_PARAM) and payload:
        return _parse_eq_ebb(cmd, payload, raw)

    if cmd == EQEBB_RET_EXTENDED_INFO and len(payload) >= 2:
        try:
            eq_type = EqEbbInquiredType(payload[0])
        except ValueError:
            eq_type = None
        count = payload[1]
        has_clear_bass = len(payload) >= 5 and payload[2] == 0x10
        return EqExtendedInfoResponse(eq_type=eq_type, band_count=count, has_clear_bass=has_clear_bass, raw=raw)

    if cmd == ALERT_NTFY_PARAM and len(payload) >= 2:
        return AlertNtfyParamResponse(inquired_type=payload[0], message_type=payload[1], raw=raw)

    if cmd in (AUDIO_RET_PARAM, AUDIO_NTFY_PARAM) and len(payload) >= 2:
        inq = payload[0]
        if inq in (AUDIO_INQ_UPSCALING, AUDIO_INQ_UPSCALING_WITH_REASON):
            return UpscalingParamResponse(enabled=(payload[1] == UPSCALING_AUTO), inquired_type=inq, raw=raw)
        if inq in (AUDIO_INQ_CONNECTION_MODE, AUDIO_INQ_CONNECTION_MODE_WITH_LDAC, AUDIO_INQ_CONNECTION_MODE_LEA):
            try:
                return ConnectionQualityResponse(mode=ConnectionQualityMode(payload[1]), inquired_type=inq, raw=raw)
            except ValueError:
                pass

    if cmd in (SYSTEM_RET_PARAM, SYSTEM_NTFY_PARAM) and len(payload) >= 2:
        if payload[0] in (SYS_SMART_TALKING_TYPE1, SYS_SMART_TALKING_TYPE2):
            return SpeakToChatResponse(enabled=(payload[1] == 0x00), sys_type=payload[0], raw=raw)

    if cmd == GS_RET_CAPABILITY and len(payload) >= 4:
        slot = payload[0]
        title = _len_prefixed_str(payload, 3) or f"Slot 0x{slot:02X}"
        return GeneralSettingCapabilityResponse(slot=slot, title=title, raw=raw)

    if cmd in (GS_RET_PARAM, GS_NTFY_PARAM) and len(payload) >= 3:
        return GeneralSettingParamResponse(slot=payload[0], enabled=(payload[2] == 0x00), raw=raw)

    if cmd in (PLAY_RET_PARAM, PLAY_NTFY_PARAM) and len(payload) >= 2:
        try:
            ptype = PlayInquiredType(payload[0])
            if ptype in (PlayInquiredType.MUSIC_VOLUME, PlayInquiredType.CALL_VOLUME):
                return PlaybackVolumeResponse(play_type=ptype, volume=payload[1], raw=raw)
        except ValueError:
            pass

    return UnknownResponse(data_type, cmd, payload, raw)


def _parse_ncasm_param(payload: bytes, raw: bytes) -> NoiseControlResponse:
    try:
        nc_type = NcAsmInquiredType(payload[0])
    except ValueError:
        nc_type = None

    total_effect = payload[2] if len(payload) > 2 else None
    mode_byte = payload[3] if len(payload) > 3 else None

    control_mode: NoiseControlMode | None = None
    ambient_level: int | None = None
    ambient_mode: AmbientSoundMode | None = None
    noise_adaptive: bool | None = None

    if nc_type in (
        NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS,
        NcAsmInquiredType.MODE_NC_NCSS_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS,
        NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS_NA,
    ):
        if total_effect == NCASM_OFF:
            control_mode = NoiseControlMode.OFF
        elif mode_byte == NCASM_MODE_ASM:
            control_mode = NoiseControlMode.AMBIENT_SOUND
        elif mode_byte == NCASM_MODE_NC:
            control_mode = NoiseControlMode.NOISE_CANCELLING
        if len(payload) > 4:
            try:
                ambient_mode = AmbientSoundMode(payload[4])
            except ValueError:
                pass
        if len(payload) > 5:
            ambient_level = payload[5]
        if nc_type == NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS_NA and len(payload) > 6:
            noise_adaptive = (payload[6] == NCASM_ON)
    elif nc_type in (
        NcAsmInquiredType.MODE_NC_ASM_AUTO_NC_MODE_SWITCH_AND_ASM_SEAMLESS,
        NcAsmInquiredType.MODE_NC_ASM_DUAL_SINGLE_NC_MODE_SWITCH_AND_ASM_SEAMLESS,
    ):
        if total_effect == NCASM_OFF:
            control_mode = NoiseControlMode.OFF
        elif mode_byte == NCASM_MODE_ASM:
            control_mode = NoiseControlMode.AMBIENT_SOUND
        elif mode_byte == NCASM_MODE_NC:
            control_mode = NoiseControlMode.NOISE_CANCELLING
        if len(payload) > 5:
            try:
                ambient_mode = AmbientSoundMode(payload[5])
            except ValueError:
                pass
        if len(payload) > 6:
            ambient_level = payload[6]
    elif nc_type in (
        NcAsmInquiredType.NC_ON_OFF_AND_ASM_SEAMLESS,
        NcAsmInquiredType.NC_MODE_SWITCH_AND_ASM_SEAMLESS,
    ):
        if total_effect == NCASM_OFF:
            control_mode = NoiseControlMode.OFF
        elif mode_byte in (NCASM_ON, NC_VALUE_ON_SINGLE, NC_VALUE_ON_DUAL):
            control_mode = NoiseControlMode.NOISE_CANCELLING
        else:
            control_mode = NoiseControlMode.AMBIENT_SOUND
        if len(payload) > 4:
            try:
                ambient_mode = AmbientSoundMode(payload[4])
            except ValueError:
                pass
        if len(payload) > 5:
            ambient_level = payload[5]

    return NoiseControlResponse(
        nc_type=nc_type,
        control_mode=control_mode,
        ambient_level=ambient_level,
        ambient_mode=ambient_mode,
        noise_adaptive=noise_adaptive,
        raw=raw,
    )


def _parse_eq_ebb(cmd: int, payload: bytes, raw: bytes) -> EqEbbResponse:
    try:
        eq_type = EqEbbInquiredType(payload[0])
    except ValueError:
        eq_type = None

    if cmd in (EQEBB_RET_STATUS, EQEBB_NTFY_STATUS):
        enabled = (payload[1] == 0x00) if len(payload) > 1 else None
        return EqEbbResponse(eq_type=eq_type, enabled=enabled, preset=None, band_steps=[], clear_bass=None, raw=raw)

    preset: EqPresetId | None = None
    band_steps: list[int] = []
    clear_bass: int | None = None

    if eq_type == EqEbbInquiredType.EBB and len(payload) >= 2:
        val = payload[1]
        clear_bass = val - 256 if val >= 128 else val
    elif eq_type in (
        EqEbbInquiredType.PRESET_EQ,
        EqEbbInquiredType.PRESET_EQ_NONCUSTOMIZABLE,
        EqEbbInquiredType.PRESET_EQ_AND_ERRORCODE,
    ) and len(payload) >= 2:
        try:
            preset = EqPresetId(payload[1])
        except ValueError:
            preset = None
        if len(payload) >= 3:
            count = payload[2]
            band_steps = list(payload[3 : 3 + count])
    elif eq_type == EqEbbInquiredType.PRESET_EQ_AND_ULT_MODE and len(payload) >= 2:
        try:
            preset = EqPresetId(payload[1])
        except ValueError:
            preset = None
        if len(payload) >= 4:
            count = payload[3]
            band_steps = list(payload[4 : 4 + count])
    elif eq_type == EqEbbInquiredType.CUSTOM_EQ and len(payload) >= 2:
        count = payload[1]
        band_steps = list(payload[2 : 2 + count])

    return EqEbbResponse(
        eq_type=eq_type,
        enabled=None,
        preset=preset,
        band_steps=band_steps,
        clear_bass=clear_bass,
        raw=raw,
    )
