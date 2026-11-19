# SonyPodsWin 协议层单元测试要点清单（黄金字节样例）

> 本文档所有「消息层字节」与「线缆帧字节（`seq=0`）」均由随附的 Python 源码（`constants.py` / `framing.py` / `v2_table1.py` / `v2_table2.py`）直接运行推导得出，供 C# 单元测试逐字节断言核对。

## 1. 帧层与归一化黄金样例（`Framing.cs`）

| 测试场景 | 输入 | 期望输出（Hex） | 核对要点 |
|---|---|---|---|
| ACK 帧（确认 seq=0） | `Encode(Ack, seq=1, [])` | `3E 01 01 00 00 00 00 02 3C` | `InverseSequence(0) == 1`，空负载 |
| ACK 帧（确认 seq=1） | `Encode(Ack, seq=0, [])` | `3E 01 00 00 00 00 00 01 3C` | `InverseSequence(1) == 0` |
| 特殊字节转义 | `Escape([3C, 3D, 3E, 00])` | `3D 2C 3D 2D 3D 2E 00` | 仅转义 `0x3C/0x3D/0x3E` |
| 含转义字节整帧编码 | `Encode(0x0C, seq=0, [3C, 3D, 3E])` | `3E 0C 00 00 00 00 03 3D 2C 3D 2D 3D 2E C6 3C` | 校验和按未转义帧体计算 |
| 校验和本身命中 `0x3E` | `build_set_auto_power_off(0x05, [01, 00])` | `3E 0C 00 00 00 00 04 28 05 01 00 3D 2E 3C` | 校验和 `0x3E` 必须转义为 `3D 2E` |
| 出站 Table1 归一化 | `Outbound([0E, 02, 00])` | `FrameType=0x0C`, `Payload=02 00` | 剥离首字节 `0x0E` 选帧型 `0x0C` |
| 出站 Table2 归一化 | `Outbound([0F, 36, 02])` | `FrameType=0x0E`, `Payload=36 02` -> 线缆帧 `3E 0E 00 00 00 00 02 36 02 48 3C` | 剥离 `0x0F` 选帧型 `0x0E`（`DATA_MDR_NO2`） |
| 入站 Table1 归一化 | `Inbound(0x0C, [03, 00, 01, 02, 41, 42])` | `0E 03 00 01 02 41 42` | `0x0C/0x1C/0x2C` 均补回 `0x0E` |
| 入站 Table2 归一化 | `Inbound(0x0E, [37, 02, 00])` | `0F 37 02 00` | `0x0E/0x1E` 均补回 `0x0F` |
| 入站 ACK / UNKNOWN | `Inbound(0x01, [])` / `Inbound(0xFF, [])` | `null` | 不进入业务解析 |
| 畸形帧丢弃 | 长度 < 7、校验和错、length 字段与实长不符 | `Decode(...) == null` | 严格按先解转义→长度→校验和→length 校验 |

## 2. Table1 全量命令构造黄金字节（`Table1.cs`）

| 方法与输入参数 | 消息层期望字节（Hex） | 线缆帧期望字节（`seq=0`，Hex） |
|---|---|---|
| `BuildGetProtocolInfo()` | `0E 00 00` | `3E 0C 00 00 00 00 02 00 00 0E 3C` |
| `BuildGetCapabilityInfo()` | `0E 02 00` | `3E 0C 00 00 00 00 02 02 00 10 3C` |
| `BuildGetDeviceInfo(ModelName)` | `0E 04 01` | `3E 0C 00 00 00 00 02 04 01 13 3C` |
| `BuildGetDeviceInfo(FwVersion)` | `0E 04 02` | `3E 0C 00 00 00 00 02 04 02 14 3C` |
| `BuildGetDeviceInfo(SeriesAndColorInfo)` | `0E 04 03` | `3E 0C 00 00 00 00 02 04 03 15 3C` |
| `BuildGetDeviceInfo(InstructionGuide)` | `0E 04 04` | `3E 0C 00 00 00 00 02 04 04 16 3C` |
| `BuildGetSupportFunction()` | `0E 06 00` | `3E 0C 00 00 00 00 02 06 00 14 3C` |
| `BuildGetBatteryStatus(Battery)` | `0E 22 00` | `3E 0C 00 00 00 00 02 22 00 30 3C` |
| `BuildGetBatteryStatus(LeftRightBattery)` | `0E 22 01` | `3E 0C 00 00 00 00 02 22 01 31 3C` |
| `BuildGetBatteryStatus(CradleBattery)` | `0E 22 02` | `3E 0C 00 00 00 00 02 22 02 32 3C` |
| `BuildPowerOff()` | `0E 24 03 01` | `3E 0C 00 00 00 00 03 24 03 01 37 3C` |
| `BuildGetAutoPowerOff(0x05)` | `0E 26 05` | `3E 0C 00 00 00 00 02 26 05 39 3C` |
| `BuildSetAutoPowerOff(0x05, [0x01, 0x00])` | `0E 28 05 01 00` | `3E 0C 00 00 00 00 04 28 05 01 00 3D 2E 3C` |
| `BuildReplyAlertFixedMessage(7, true)` | `0E 98 00 07 01` | `3E 0C 00 00 00 00 04 98 00 07 01 B0 3C` |
| `BuildReplyAlertFixedMessage(7, false)` | `0E 98 00 07 00` | `3E 0C 00 00 00 00 04 98 00 07 00 AF 3C` |
| `BuildGetAudioCodecStatus()` | `0E 12 02` | `3E 0C 00 00 00 00 02 12 02 22 3C` |
| `BuildGetUpscalingEffectStatus()` | `0E 12 03` | `3E 0C 00 00 00 00 02 12 03 23 3C` |
| `BuildGetNcAsmCapability(0x17)` | `0E 60 17` | `3E 0C 00 00 00 00 02 60 17 85 3C` |
| `BuildGetNcAsmStatus(0x17)` | `0E 62 17` | `3E 0C 00 00 00 00 02 62 17 87 3C` |
| `BuildGetNcAsmParam(0x17)` | `0E 66 17` | `3E 0C 00 00 00 00 02 66 17 8B 3C` |
| `BuildSetNoiseControlMode(NoiseCancelling)`（默认 `0x17`） | `0E 68 17 01 01 00 00 0A` | `3E 0C 00 00 00 00 07 68 17 01 01 00 00 0A 9E 3C` |
| `BuildSetNoiseControlMode(AmbientSound, 15, Voice)`（`0x17`） | `0E 68 17 01 01 01 01 0F` | `3E 0C 00 00 00 00 07 68 17 01 01 01 01 0F A5 3C` |
| `BuildSetNoiseControlMode(Off)`（`0x17`） | `0E 68 17 01 00 00 00 0A` | `3E 0C 00 00 00 00 07 68 17 01 00 00 00 0A 9D 3C` |
| `BuildSetNoiseControlMode(NoiseCancelling, ncType=0x19, na=true, High)` | `0E 68 19 01 01 00 00 0A 01 01` | `3E 0C 00 00 00 00 09 68 19 01 01 00 00 0A 01 01 A4 3C` |
| `BuildSetNoiseControlMode(NoiseCancelling, ncType=0x15, wind=true)` | `0E 68 15 01 01 00 03 00 0A` | `3E 0C 00 00 00 00 08 68 15 01 01 00 03 00 0A A0 3C` |
| `BuildSetNoiseControlMode(NoiseCancelling, ncType=0x16, wind=true)` | `0E 68 16 01 01 00 01 00 0A` | `3E 0C 00 00 00 00 08 68 16 01 01 00 01 00 0A 9F 3C` |
| `BuildSetNoiseControlMode(NoiseCancelling, ncType=0x30)` | `0E 68 30 02` | `3E 0C 00 00 00 00 03 68 30 02 A9 3C` |
| `BuildSetNoiseControlMode(AmbientSound, ncType=0x30)` | `0E 68 30 01` | `3E 0C 00 00 00 00 03 68 30 01 A8 3C` |
| `BuildSetNoiseControlMode(Off, ncType=0x30)` | `0E 68 30 00` | `3E 0C 00 00 00 00 03 68 30 00 A7 3C` |
| `BuildSetNoiseControlMode(NoiseCancelling, ncType=0x01)` | `0E 68 01 01 01 01` | `3E 0C 00 00 00 00 05 68 01 01 01 01 7D 3C` |
| `BuildSetNoiseControlMode(AmbientSound, level=25, ncType=0x22)`（截断至 20） | `0E 68 22 01 01 00 14` | `3E 0C 00 00 00 00 06 68 22 01 01 00 14 B2 3C` |
| `BuildGetEqEbbCapability(PresetEq)` | `0E 50 00 00` | `3E 0C 00 00 00 00 03 50 00 00 5F 3C` |
| `BuildGetEqEbbStatus(PresetEq)` | `0E 52 00` | `3E 0C 00 00 00 00 02 52 00 60 3C` |
| `BuildGetEqEbbParam(PresetEq)` | `0E 56 00` | `3E 0C 00 00 00 00 02 56 00 64 3C` |
| `BuildGetEqEbbExtendedInfo(PresetEq)` | `0E 5A 00` | `3E 0C 00 00 00 00 02 5A 00 68 3C` |
| `BuildSetEqPreset(Bright, PresetEq, null)` | `0E 58 00 10 00` | `3E 0C 00 00 00 00 04 58 00 10 00 78 3C` |
| `BuildSetEqPreset(Custom, PresetEq, [10,12,10,8,10])` | `0E 58 00 A0 05 0A 0C 0A 08 0A` | `3E 0C 00 00 00 00 09 58 00 A0 05 0A 0C 0A 08 0A 44 3C` |
| `BuildSetEqPreset(Custom, CustomEq, [6,7,6,5,6])` | `0E 58 31 05 06 07 06 05 06` | `3E 0C 00 00 00 00 08 58 31 05 06 07 06 05 06 C0 3C` |
| `BuildSetEqPreset(Ult1, PresetEqAndUltMode, [10,10,10,10,10])` | `0E 58 03 41 00 05 0A 0A 0A 0A 0A` | `3E 0C 00 00 00 00 0A 58 03 41 00 05 0A 0A 0A 0A 0A E9 3C` |
| `BuildSetClearBass(-3)` | `0E 58 01 FD` | `3E 0C 00 00 00 00 03 58 01 FD 65 3C` |
| `BuildSetClearBass(5)` | `0E 58 01 05` | `3E 0C 00 00 00 00 03 58 01 05 6D 3C` |
| `BuildGetUpscaling(0x01)` | `0E E6 01` | `3E 0C 00 00 00 00 02 E6 01 F5 3C` |
| `BuildSetUpscaling(true, 0x01)` | `0E E8 01 01` | `3E 0C 00 00 00 00 03 E8 01 01 F9 3C` |
| `BuildSetUpscaling(false, 0x0B)` | `0E E8 0B 00` | `3E 0C 00 00 00 00 03 E8 0B 00 02 3C` |
| `BuildGetConnectionQuality(0x00)` | `0E E6 00` | `3E 0C 00 00 00 00 02 E6 00 F4 3C` |
| `BuildSetConnectionQuality(SoundQualityPrior, 0x00)` | `0E E8 00 00` | `3E 0C 00 00 00 00 03 E8 00 00 F7 3C` |
| `BuildSetConnectionQuality(ConnectionQualityPrior, 0x02)` | `0E E8 02 01` | `3E 0C 00 00 00 00 03 E8 02 01 FA 3C` |
| `BuildGetCinemaMode()` | `0E E6 20` | `3E 0C 00 00 00 00 02 E6 20 14 3C` |
| `BuildSetCinemaMode(true)` | `0E E8 20 00` | `3E 0C 00 00 00 00 03 E8 20 00 17 3C` |
| `BuildSetCinemaMode(false)` | `0E E8 20 01` | `3E 0C 00 00 00 00 03 E8 20 01 18 3C` |
| `BuildGetSpeakToChatStatus(0x01)` | `0E F2 01` | `3E 0C 00 00 00 00 02 F2 01 01 3C` |
| `BuildGetSpeakToChatParam(0x01)` | `0E F6 01` | `3E 0C 00 00 00 00 02 F6 01 05 3C` |
| `BuildSetSpeakToChatEnabled(true, 0x01)` | `0E F8 01 00 00` | `3E 0C 00 00 00 00 04 F8 01 00 00 09 3C` |
| `BuildSetSpeakToChatEnabled(false, 0x01)` | `0E F8 01 01 00` | `3E 0C 00 00 00 00 04 F8 01 01 00 0A 3C` |
| `BuildGetWearingStatus()` | `0E F6 04` | `3E 0C 00 00 00 00 02 F6 04 08 3C` |
| `BuildGetGeneralSettingCapability(0xD1)` | `0E D0 D1 01` | `3E 0C 00 00 00 00 03 D0 D1 01 B1 3C` |
| `BuildGetGeneralSettingParam(0xD1)` | `0E D6 D1` | `3E 0C 00 00 00 00 02 D6 D1 B5 3C` |
| `BuildSetGeneralSetting(0xD1, true)` | `0E D8 D1 00 00` | `3E 0C 00 00 00 00 04 D8 D1 00 00 B9 3C` |
| `BuildSetGeneralSetting(0xD1, false)` | `0E D8 D1 00 01` | `3E 0C 00 00 00 00 04 D8 D1 00 01 BA 3C` |
| `BuildGetPlaybackParam(MusicVolume)` | `0E A6 20` | `3E 0C 00 00 00 00 02 A6 20 D4 3C` |
| `BuildSetPlaybackVolume(50)` | `0E A8 20 32` | `3E 0C 00 00 00 00 03 A8 20 32 09 3C` |
| `BuildPlayback(Play)` | `0E A4 01 00 07` | `3E 0C 00 00 00 00 04 A4 01 00 07 BC 3C` |
| `BuildPlayback(Pause)` | `0E A4 01 00 01` | `3E 0C 00 00 00 00 04 A4 01 00 01 B6 3C` |
| `BuildPlayback(TrackUp)` | `0E A4 01 00 02` | `3E 0C 00 00 00 00 04 A4 01 00 02 B7 3C` |
| `BuildPlayback(TrackDown)` | `0E A4 01 00 03` | `3E 0C 00 00 00 00 04 A4 01 00 03 B8 3C` |
| `BuildPlayback(Stop)` | `0E A4 01 00 06` | `3E 0C 00 00 00 00 04 A4 01 00 06 BB 3C` |

## 3. 响应解析关键单测样例（`Table1.Parse` & `Table2.ParseTable2`）

| 解析场景 | 输入字节（Hex） | 期望解析结果 |
|---|---|---|
| ProtocolInfo 版本号 | `0E 01 00 03 00 30 33` | `ProtocolInfoResponse(Version = 0x03003033 = 50343987)` |
| 扩展电量 `0x09` 特判 | `0E 25 09 50 00 4B` | `BatteryResponse(Kind = LeftRightBattery, Levels = [80, 75])` |
| AudioCodec 1 字节 / 2 字节兼容 | `0E 13 02 10` 与 `0E 13 02 00 10` | 均解析为 `AudioCodecResponse(Codec = Ldac)` |
| Clear Bass 负数补码 | `0E 57 01 FD` | `EqEbbResponse(EqType = Ebb, ClearBass = -3)` |
| SupportFunction 按 order 排序 | `0E 07 00 02 61 05 20 01` | `SupportFunctionResponse([(0x20, 1), (0x61, 5)])` |
| Table2 带 CoD 多点设备表 | `0F 37 02 01 41 41 3A 42 42 3A 43 43 3A 44 44 3A 45 45 3A 46 46 01 04 04 24 04 4D 79 50 43 01` | `PeripheralDevicesResponse(Devices=[("AA:BB:CC:DD:EE:FF", 1, "MyPC", true)], PlaybackHolderOrder=1)` |
| 误喂本机请求（自喂回归） | `0E 02 00` | `UnknownResponse(DataType=0x0E, Command=0x02, Payload=[0x00])` |
