"""存档二进制数据的顺序读写。

所有多字节数值均为小端序，与游戏内 Unity 的序列化结果一致。
"""

from __future__ import annotations

import struct

from .exceptions import PhigrosDataError, PhigrosFormatError

_U16 = struct.Struct("<H")
_U32 = struct.Struct("<I")
_F32 = struct.Struct("<f")

#: 变长整数能表示的最大值：首字节 7 位 + 次字节 8 位。
VARSHORT_MAX = 0x7F7F


class ByteReader:
    """在 bytes 上按顺序读取。"""

    __slots__ = ("_data", "_position")

    def __init__(self, data: bytes, position: int = 0) -> None:
        if not isinstance(data, (bytes, bytearray, memoryview)):
            raise TypeError("data 必须是 bytes 类型")
        if not 0 <= position <= len(data):
            raise ValueError(f"position {position} 超出数据范围 0..{len(data)}")

        self._data = bytes(data)
        self._position = position

    @property
    def position(self) -> int:
        """当前读取位置。"""
        return self._position

    @position.setter
    def position(self, value: int) -> None:
        if not 0 <= value <= len(self._data):
            raise ValueError(f"position {value} 超出数据范围 0..{len(self._data)}")
        self._position = value

    @property
    def length(self) -> int:
        """缓冲区总长度。"""
        return len(self._data)

    @property
    def remaining(self) -> int:
        """尚未读取的字节数。"""
        return len(self._data) - self._position

    @property
    def remaining_bytes(self) -> bytes:
        """剩余字节，不移动读取位置。"""
        return self._data[self._position :]

    def _ensure(self, count: int) -> None:
        if self._position + count > len(self._data):
            raise PhigrosFormatError(
                f"存档数据在偏移 {self._position} 处提前结束："
                f"还需要 {count} 字节，实际只剩 {self.remaining} 字节。"
            )

    def peek_u8(self) -> int:
        """读取当前位置的字节但不移动读取位置。"""
        self._ensure(1)
        return self._data[self._position]

    def read_u8(self) -> int:
        self._ensure(1)
        value = self._data[self._position]
        self._position += 1
        return value

    def read_u16(self) -> int:
        self._ensure(2)
        (value,) = _U16.unpack_from(self._data, self._position)
        self._position += 2
        return value

    def read_u32(self) -> int:
        self._ensure(4)
        (value,) = _U32.unpack_from(self._data, self._position)
        self._position += 4
        return value

    def read_f32(self) -> float:
        self._ensure(4)
        (value,) = _F32.unpack_from(self._data, self._position)
        self._position += 4
        return value

    def skip(self, count: int) -> None:
        if count < 0:
            raise ValueError("count 不能为负数")
        self._ensure(count)
        self._position += count

    def read_varshort(self) -> int:
        """读取变长整数：最高位为 0 时占 1 字节，为 1 时占 2 字节（低位在前）。"""
        self._ensure(1)
        first = self._data[self._position]
        if first < 0x80:
            self._position += 1
            return first

        self._ensure(2)
        second = self._data[self._position + 1]
        self._position += 2
        return (first & 0x7F) | (second << 7)

    def read_string(self, suffix_length: int = 0) -> str:
        """读取变长整数长度的字符串。

        ``suffix_length`` 是长度字段中包含、但不属于字符串内容的尾部字节数。
        ``gameRecord`` 的键在磁盘上带有 ``.0`` 后缀，需要传 2。
        """
        length = self.read_varshort()
        if length < suffix_length:
            raise PhigrosFormatError(
                f"字符串长度字段 {length} 小于应有的后缀长度 {suffix_length}，数据可能已损坏。"
            )

        self._ensure(length)
        raw = self._data[self._position : self._position + length - suffix_length]
        self._position += length
        return raw.decode("utf-8", errors="replace")


class ByteWriter:
    """以字节为单位顺序写出，与 :class:`ByteReader` 互逆。"""

    __slots__ = ("_buffer",)

    def __init__(self) -> None:
        self._buffer = bytearray()

    def __len__(self) -> int:
        return len(self._buffer)

    def write_u8(self, value: int) -> None:
        self._buffer.append(value & 0xFF)

    def write_u16(self, value: int) -> None:
        self._buffer += _U16.pack(value & 0xFFFF)

    def write_u32(self, value: int) -> None:
        self._buffer += _U32.pack(value & 0xFFFFFFFF)

    def write_f32(self, value: float) -> None:
        self._buffer += _F32.pack(value)

    def write_bytes(self, value: bytes) -> None:
        self._buffer += value

    def write_varshort(self, value: int) -> None:
        """写入变长整数。小于 128 的值占 1 字节，其余占 2 字节（首位补 1）。"""
        if not 0 <= value <= VARSHORT_MAX:
            raise PhigrosDataError(f"变长整数的值 {value} 超出可表示范围 0..{VARSHORT_MAX}。")

        if value < 0x80:
            self._buffer.append(value)
            return

        self._buffer.append((value & 0x7F) | 0x80)
        self._buffer.append(value >> 7)

    def write_string(self, value: str, suffix: bytes = b"") -> None:
        """写入变长整数长度的字符串，``suffix`` 计入长度但不属于内容。"""
        raw = value.encode("utf-8")
        self.write_varshort(len(raw) + len(suffix))
        self._buffer += raw
        self._buffer += suffix

    def to_bytes(self) -> bytes:
        return bytes(self._buffer)
