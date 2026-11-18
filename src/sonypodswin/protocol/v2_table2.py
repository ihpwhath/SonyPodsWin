"""Sony Tandem V2 Table 2 (dataType 0x0F) protocol parsers."""

from dataclasses import dataclass

from .constants import SonyTandemDataType

PERI_RET_CAPABILITY = 0x31
PERI_RET_STATUS = 0x33
PERI_NTFY_STATUS = 0x35
PERI_RET_PARAM = 0x37
PERI_NTFY_PARAM = 0x39


@dataclass
class PairedDeviceInfo:
    address: str
    connected_order: int
    name: str
    is_connected: bool


@dataclass
class PeripheralDevicesResponse:
    devices: list[PairedDeviceInfo]
    playback_holder_order: int
    raw: bytes


def parse_table2(raw: bytes) -> PeripheralDevicesResponse | None:
    if len(raw) < 3 or raw[0] != SonyTandemDataType.DATA_MDR_NO2:
        return None
    cmd = raw[1]
    payload = raw[2:]
    if cmd not in (PERI_RET_PARAM, PERI_NTFY_PARAM) or not payload:
        return None
    inq_type = payload[0]
    if inq_type not in (0x00, 0x02):
        return None
    if len(payload) < 2:
        return None
    count = payload[1]
    idx = 2
    has_cod = inq_type == 0x02
    devices: list[PairedDeviceInfo] = []
    for _ in range(count):
        if idx + 18 > len(payload):
            break
        addr = payload[idx : idx + 17].decode("ascii", errors="ignore")
        idx += 17
        order = payload[idx]
        idx += 1
        if has_cod:
            if idx + 3 > len(payload):
                break
            idx += 3
        if idx >= len(payload):
            break
        name_len = payload[idx]
        idx += 1
        if idx + name_len > len(payload):
            break
        name = payload[idx : idx + name_len].decode("utf-8", errors="ignore").strip()
        idx += name_len
        devices.append(
            PairedDeviceInfo(
                address=addr,
                connected_order=order,
                name=name or addr,
                is_connected=(order > 0),
            )
        )
    holder = payload[idx] if idx < len(payload) else 0
    return PeripheralDevicesResponse(devices=devices, playback_holder_order=holder, raw=raw)
