"""Sony Tandem framing codec (0x3E ... 0x3C) and payload mapper."""

from dataclasses import dataclass

from .constants import SonySppFrameType, SonyTandemDataType


@dataclass(frozen=True)
class TandemFrame:
    frame_type: SonySppFrameType
    sequence: int
    payload: bytes


class SonyTandemFraming:
    FRAME_START = 0x3E
    FRAME_END = 0x3C
    ESCAPE = 0x3D
    HEADER_SIZE = 6
    CHECKSUM_SIZE = 1
    MIN_FRAME_SIZE = HEADER_SIZE + CHECKSUM_SIZE

    @classmethod
    def encode(cls, frame_type: int, sequence: int, payload: bytes) -> bytes:
        length = len(payload)
        body = bytearray(cls.HEADER_SIZE + length + cls.CHECKSUM_SIZE)
        body[0] = frame_type & 0xFF
        body[1] = sequence & 0xFF
        body[2] = (length >> 24) & 0xFF
        body[3] = (length >> 16) & 0xFF
        body[4] = (length >> 8) & 0xFF
        body[5] = length & 0xFF
        body[cls.HEADER_SIZE : cls.HEADER_SIZE + length] = payload
        body[-1] = cls.checksum(body[:-1])
        return bytes([cls.FRAME_START]) + cls.escape(bytes(body)) + bytes([cls.FRAME_END])

    @classmethod
    def decode(cls, escaped_body: bytes) -> TandemFrame | None:
        body = cls.unescape(escaped_body)
        if len(body) < cls.MIN_FRAME_SIZE:
            return None
        expected = cls.checksum(body[:-1])
        actual = body[-1]
        if expected != actual:
            return None
        length = (body[2] << 24) | (body[3] << 16) | (body[4] << 8) | body[5]
        if length < 0 or len(body) != cls.HEADER_SIZE + length + cls.CHECKSUM_SIZE:
            return None
        try:
            ftype = SonySppFrameType(body[0])
        except ValueError:
            ftype = SonySppFrameType.UNKNOWN
        return TandemFrame(
            frame_type=ftype,
            sequence=body[1],
            payload=bytes(body[cls.HEADER_SIZE : cls.HEADER_SIZE + length]),
        )

    @classmethod
    def escape(cls, data: bytes) -> bytes:
        out = bytearray()
        for b in data:
            if b == cls.FRAME_END:
                out.extend((cls.ESCAPE, 0x2C))
            elif b == cls.ESCAPE:
                out.extend((cls.ESCAPE, 0x2D))
            elif b == cls.FRAME_START:
                out.extend((cls.ESCAPE, 0x2E))
            else:
                out.append(b)
        return bytes(out)

    @classmethod
    def unescape(cls, data: bytes) -> bytes:
        out = bytearray()
        i = 0
        n = len(data)
        while i < n:
            b = data[i]
            if b == cls.ESCAPE and i + 1 < n:
                i += 1
                out.append(data[i] | 0x10)
            else:
                out.append(b)
            i += 1
        return bytes(out)

    @staticmethod
    def checksum(data: bytes | bytearray) -> int:
        return sum(data) & 0xFF

    @staticmethod
    def inverse_sequence(sequence: int) -> int:
        return (1 - sequence) & 0xFF


class SonySppPayloadMapper:
    @staticmethod
    def outbound_from_tandem_bytes(data: bytes) -> tuple[SonySppFrameType, bytes]:
        if not data:
            return (SonySppFrameType.DATA_MDR, data)
        if data[0] == SonyTandemDataType.DATA_MDR:
            return (SonySppFrameType.DATA_MDR, data[1:])
        if data[0] == SonyTandemDataType.DATA_MDR_NO2:
            return (SonySppFrameType.DATA_MDR_NO2, data[1:])
        return (SonySppFrameType.DATA_MDR, data)

    @staticmethod
    def inbound_to_tandem_bytes(frame_type: SonySppFrameType, payload: bytes) -> bytes | None:
        if frame_type in (
            SonySppFrameType.DATA_MDR,
            SonySppFrameType.SHOT_MDR,
            SonySppFrameType.LARGE_DATA_MDR,
        ):
            return bytes([SonyTandemDataType.DATA_MDR]) + payload
        if frame_type in (
            SonySppFrameType.DATA_MDR_NO2,
            SonySppFrameType.SHOT_MDR_NO2,
        ):
            return bytes([SonyTandemDataType.DATA_MDR_NO2]) + payload
        return None
