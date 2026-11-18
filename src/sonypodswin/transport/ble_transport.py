"""BLE GATT Tandem transport using bleak."""

import asyncio
from collections import deque
from dataclasses import dataclass
from typing import Callable

from bleak import BleakClient, BleakScanner

from ..protocol.constants import SonyGatt, SonySppFrameType
from ..protocol.framing import SonySppPayloadMapper, SonyTandemFraming


@dataclass
class DiscoveredDevice:
    name: str
    address: str
    rssi: int | None = None


class SonyBleTandemClient:
    """Asyncio BLE GATT client implementing Sony Tandem framing, fragmentation, and ACK/retry."""

    def __init__(
        self,
        on_payload: Callable[[bytes], None],
        on_log: Callable[[str], None] | None = None,
        on_disconnected: Callable[[], None] | None = None,
    ) -> None:
        self._on_payload = on_payload
        self._on_log = on_log or (lambda _msg: None)
        self._on_disconnected = on_disconnected or (lambda: None)

        self._client: BleakClient | None = None
        self._to_acc_uuid: str | None = None
        self._from_acc_uuid: str | None = None
        self._writable_len: int = 200

        self._rx_buf = bytearray()
        self._in_frame = False

        self._next_tx_seq = 0
        self._awaiting_ack: int | None = None
        self._awaiting_frame: bytes | None = None
        self._awaiting_policy: tuple[float, int] | None = None
        self._awaiting_retries = 0
        self._ack_event: asyncio.Event | None = None
        self._pending_writes: deque[tuple[SonySppFrameType, bytes]] = deque()
        self._tx_task: asyncio.Task[None] | None = None
        self._closed = True

    @property
    def is_connected(self) -> bool:
        return self._client is not None and self._client.is_connected and not self._closed

    @staticmethod
    async def scan_devices(timeout: float = 4.0) -> list[DiscoveredDevice]:
        by_addr: dict[str, DiscoveredDevice] = {}

        # 1. Include already-paired Sony BLE devices from Windows registry/WinRT
        try:
            from winrt.windows.devices.bluetooth import BluetoothLEDevice
            from winrt.windows.devices.enumeration import DeviceInformation

            selector = BluetoothLEDevice.get_device_selector_from_pairing_state(True)
            infos = await DeviceInformation.find_all_async(selector)
            for info in infos:
                name = info.name or ""
                if any(k in name.lower() for k in ("wh-1000", "wf-1000", "linkbuds", "sony", "ult")):
                    le_dev = await BluetoothLEDevice.from_id_async(info.id)
                    if le_dev is not None:
                        raw_addr = le_dev.bluetooth_address
                        addr = ":".join(f"{(raw_addr >> (8 * i)) & 0xFF:02X}" for i in range(5, -1, -1))
                        by_addr[addr] = DiscoveredDevice(name=name or addr, address=addr, rssi=None)
        except Exception:
            pass

        # 2. Active BLE advertisement scan
        devices = await BleakScanner.discover(timeout=timeout, return_adv=True)
        for dev, adv in devices.values():
            uuids = [u.lower() for u in (adv.service_uuids or [])]
            name = dev.name or adv.local_name or ""
            is_sony = (
                SonyGatt.TANDEM_V2_HPC_SERVICE in uuids
                or SonyGatt.TANDEM_V2_MC_SERVICE in uuids
                or SonyGatt.TANDEM_V1_MC_SERVICE in uuids
                or any(k in name.lower() for k in ("wh-1000", "wf-1000", "linkbuds", "sony", "ult"))
            )
            if is_sony:
                by_addr[dev.address.upper()] = DiscoveredDevice(
                    name=name or dev.address,
                    address=dev.address.upper(),
                    rssi=adv.rssi,
                )
        results = list(by_addr.values())
        results.sort(key=lambda d: d.rssi if d.rssi is not None else -1, reverse=True)
        return results

    async def connect(self, address: str) -> None:
        await self.disconnect()
        self._closed = False
        self._rx_buf.clear()
        self._in_frame = False
        self._next_tx_seq = 0
        self._awaiting_ack = None
        self._pending_writes.clear()
        self._ack_event = asyncio.Event()

        def _disc_cb(_client: BleakClient) -> None:
            if not self._closed:
                self._closed = True
                self._on_log("BLE disconnected")
                self._on_disconnected()

        client = BleakClient(address, disconnected_callback=_disc_cb)
        await client.connect()
        self._client = client

        services = client.services
        to_acc = None
        from_acc = None
        wlen_uuid = None

        for svc_uuid, t_uuid, f_uuid in (
            (SonyGatt.TANDEM_V2_HPC_SERVICE, SonyGatt.TANDEM_HPC_TO_ACC, SonyGatt.TANDEM_HPC_FROM_ACC),
            (SonyGatt.TANDEM_V2_MC_SERVICE, SonyGatt.TANDEM_MC_TO_ACC, SonyGatt.TANDEM_MC_FROM_ACC),
            (SonyGatt.TANDEM_V1_MC_SERVICE, SonyGatt.TANDEM_MC_TO_ACC, SonyGatt.TANDEM_MC_FROM_ACC),
        ):
            svc = services.get_service(svc_uuid)
            if svc is not None:
                c_to = svc.get_characteristic(t_uuid)
                c_from = svc.get_characteristic(f_uuid)
                if c_to and c_from:
                    to_acc = c_to.uuid
                    from_acc = c_from.uuid
                    c_wlen = svc.get_characteristic(SonyGatt.WRITABLE_VALUE_LENGTH)
                    if c_wlen:
                        wlen_uuid = c_wlen.uuid
                    break

        if not to_acc or not from_acc:
            await client.disconnect()
            raise RuntimeError("未找到 Sony Tandem GATT 服务特征")

        self._to_acc_uuid = to_acc
        self._from_acc_uuid = from_acc

        if wlen_uuid:
            try:
                raw_wlen = await client.read_gatt_char(wlen_uuid)
                if len(raw_wlen) >= 2:
                    val = (raw_wlen[0] << 8) | raw_wlen[1]
                    if val > 0:
                        self._writable_len = val
            except Exception as exc:
                self._on_log(f"读取 WRITABLE_VALUE_LENGTH 失败: {exc}")

        self._on_log(f"已连接 {address}, writable_len={self._writable_len}")
        await client.start_notify(from_acc, self._handle_notification)
        self._tx_task = asyncio.create_task(self._tx_loop())

    async def disconnect(self) -> None:
        self._closed = True
        if self._tx_task:
            self._tx_task.cancel()
            try:
                await self._tx_task
            except asyncio.CancelledError:
                pass
            self._tx_task = None
        if self._client:
            try:
                if self._client.is_connected:
                    await self._client.disconnect()
            except Exception:
                pass
            self._client = None

    def send(self, tandem_bytes: bytes) -> None:
        if self._closed:
            return
        ftype, payload = SonySppPayloadMapper.outbound_from_tandem_bytes(tandem_bytes)
        self._pending_writes.append((ftype, payload))

    async def _tx_loop(self) -> None:
        while not self._closed:
            if not self._pending_writes:
                await asyncio.sleep(0.02)
                continue
            ftype, payload = self._pending_writes.popleft()
            seq = self._next_tx_seq
            frame = SonyTandemFraming.encode(int(ftype), seq, payload)
            policy = ftype.retry_policy
            if policy is None:
                self._next_tx_seq = SonyTandemFraming.inverse_sequence(seq)
                await self._write_fragmented(frame)
                continue

            expected_ack = SonyTandemFraming.inverse_sequence(seq)
            self._awaiting_ack = expected_ack
            self._awaiting_frame = frame
            self._awaiting_policy = policy
            timeout_s, max_retries = policy
            if self._ack_event:
                self._ack_event.clear()

            for attempt in range(max_retries + 1):
                if self._closed:
                    return
                await self._write_fragmented(frame)
                if not self._ack_event:
                    break
                try:
                    await asyncio.wait_for(self._ack_event.wait(), timeout=timeout_s)
                    break
                except asyncio.TimeoutError:
                    if attempt >= max_retries:
                        self._on_log(f"ACK 超时 seq={seq}")
                        self._awaiting_ack = None
                        break
                    self._on_log(f"ACK 超时重试 {attempt + 1}/{max_retries} seq={seq}")

    async def _write_fragmented(self, data: bytes) -> None:
        if not self._client or not self._to_acc_uuid or self._closed:
            return
        limit = max(20, self._writable_len)
        for offset in range(0, len(data), limit):
            chunk = data[offset : offset + limit]
            await self._client.write_gatt_char(self._to_acc_uuid, chunk, response=False)

    def _handle_notification(self, _sender: object, data: bytearray) -> None:
        completed: list[bytes] = []
        for b in data:
            if b == SonyTandemFraming.FRAME_START:
                self._rx_buf.clear()
                self._in_frame = True
            elif b == SonyTandemFraming.FRAME_END:
                if self._in_frame:
                    completed.append(bytes(self._rx_buf))
                self._rx_buf.clear()
                self._in_frame = False
            elif self._in_frame:
                self._rx_buf.append(b)
        for body in completed:
            self._process_frame(body)

    def _process_frame(self, escaped_body: bytes) -> None:
        frame = SonyTandemFraming.decode(escaped_body)
        if frame is None:
            self._on_log("RX 帧校验失败")
            return
        if frame.frame_type == SonySppFrameType.ACK:
            if self._awaiting_ack == frame.sequence:
                self._next_tx_seq = frame.sequence
                self._awaiting_ack = None
                self._awaiting_frame = None
                if self._ack_event:
                    self._ack_event.set()
            return

        if frame.frame_type.ack_required:
            ack_seq = SonyTandemFraming.inverse_sequence(frame.sequence)
            ack_frame = SonyTandemFraming.encode(int(SonySppFrameType.ACK), ack_seq, b"")
            asyncio.create_task(self._write_fragmented(ack_frame))

        tandem_bytes = SonySppPayloadMapper.inbound_to_tandem_bytes(frame.frame_type, frame.payload)
        if tandem_bytes is not None:
            self._on_payload(tandem_bytes)
