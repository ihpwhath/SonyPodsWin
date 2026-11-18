"""Unit tests for Sony Tandem framing, V2 Table 1, and V2 Table 2 protocols."""

import unittest

from sonypodswin.protocol import (
    AmbientSoundMode,
    DeviceInfoType,
    EqEbbInquiredType,
    EqPresetId,
    NcAsmInquiredType,
    NoiseControlMode,
    PowerInquiredType,
    SonySppFrameType,
    SonyTandemFraming,
    v2_table1,
)
from sonypodswin.protocol.v2_table1 import (
    BatteryResponse,
    DeviceInfoResponse,
    EqEbbResponse,
    NoiseControlResponse,
    ProtocolInfoResponse,
    SupportFunctionResponse,
)
from sonypodswin.protocol.v2_table2 import PeripheralDevicesResponse


class TestFraming(unittest.TestCase):
    def test_roundtrip_with_escape_bytes(self) -> None:
        payload = bytes([0x00, 0x3C, 0x3D, 0x3E, 0x55, 0xAA])
        encoded = SonyTandemFraming.encode(int(SonySppFrameType.DATA_MDR), 0, payload)
        self.assertEqual(encoded[0], SonyTandemFraming.FRAME_START)
        self.assertEqual(encoded[-1], SonyTandemFraming.FRAME_END)
        decoded = SonyTandemFraming.decode(encoded[1:-1])
        self.assertIsNotNone(decoded)
        assert decoded is not None
        self.assertEqual(decoded.frame_type, SonySppFrameType.DATA_MDR)
        self.assertEqual(decoded.sequence, 0)
        self.assertEqual(decoded.payload, payload)

    def test_bad_checksum_rejected(self) -> None:
        encoded = bytearray(SonyTandemFraming.encode(int(SonySppFrameType.ACK), 1, b""))
        encoded[-2] ^= 0xFF
        self.assertIsNone(SonyTandemFraming.decode(bytes(encoded[1:-1])))


class TestV2Table1AndTable2(unittest.TestCase):
    def test_parse_real_xm6_protocol_and_device_info(self) -> None:
        p = v2_table1.parse(b"\x0e\x01\x00\x03\x0002\x00\x00")
        self.assertIsInstance(p, ProtocolInfoResponse)

        m = v2_table1.parse(b"\x0e\x05\x01\nWH-1000XM6")
        self.assertIsInstance(m, DeviceInfoResponse)
        assert isinstance(m, DeviceInfoResponse)
        self.assertEqual(m.info_type, DeviceInfoType.MODEL_NAME)
        self.assertEqual(m.text, "WH-1000XM6")

        b = v2_table1.parse(b"\x0e#\x007\x00")
        self.assertIsInstance(b, BatteryResponse)
        assert isinstance(b, BatteryResponse)
        self.assertEqual(b.kind, PowerInquiredType.BATTERY)
        self.assertEqual(b.levels, [55])

    def test_build_and_parse_ncasm_na(self) -> None:
        cmd = v2_table1.build_set_noise_control_mode(
            control_mode=NoiseControlMode.AMBIENT_SOUND,
            ambient_level=15,
            ambient_mode=AmbientSoundMode.VOICE,
            nc_type=NcAsmInquiredType.MODE_NC_ASM_DUAL_NC_MODE_SWITCH_AND_ASM_SEAMLESS_NA,
            noise_adaptive=True,
        )
        ret_raw = bytes([cmd[0], 0x67]) + cmd[2:]
        parsed = v2_table1.parse(ret_raw)
        self.assertIsInstance(parsed, NoiseControlResponse)
        assert isinstance(parsed, NoiseControlResponse)
        self.assertEqual(parsed.control_mode, NoiseControlMode.AMBIENT_SOUND)
        self.assertEqual(parsed.ambient_level, 15)
        self.assertEqual(parsed.ambient_mode, AmbientSoundMode.VOICE)
        self.assertTrue(parsed.noise_adaptive)

    def test_build_and_parse_eq(self) -> None:
        cmd = v2_table1.build_set_eq_preset(
            EqPresetId.CUSTOM,
            EqEbbInquiredType.PRESET_EQ_AND_ERRORCODE,
            [12, 10, 8, 11, 14],
        )
        ret_raw = bytes([cmd[0], 0x57]) + cmd[2:]
        parsed = v2_table1.parse(ret_raw)
        self.assertIsInstance(parsed, EqEbbResponse)
        assert isinstance(parsed, EqEbbResponse)
        self.assertEqual(parsed.preset, EqPresetId.CUSTOM)
        self.assertEqual(parsed.band_steps, [12, 10, 8, 11, 14])

    def test_parse_support_function(self) -> None:
        parsed = v2_table1.parse(b"\x0e\x07\x00\x02\x6d\x0b\x71\x02")
        self.assertIsInstance(parsed, SupportFunctionResponse)
        assert isinstance(parsed, SupportFunctionResponse)
        self.assertEqual(parsed.functions, [(0x71, 2), (0x6D, 11)])

    def test_parse_table2_peripheral(self) -> None:
        raw = b"\x0f9\x02\x018C:E9:EE:81:D5:25\x01*A\x0c\x10HAODONG\xe7\x9a\x84WINDOW\x01"
        parsed = v2_table1.parse(raw)
        self.assertIsInstance(parsed, PeripheralDevicesResponse)
        assert isinstance(parsed, PeripheralDevicesResponse)
        self.assertEqual(len(parsed.devices), 1)
        self.assertEqual(parsed.devices[0].address, "8C:E9:EE:81:D5:25")
        self.assertTrue(parsed.devices[0].is_connected)


if __name__ == "__main__":
    unittest.main()
