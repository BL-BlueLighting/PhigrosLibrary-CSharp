"""按 :class:`~phigros_library.schemas.ObjectSchema` 描述的结构，在字节流与字段表之间转换。

字段表是 schema 与强类型模型之间的中间表示：存档结构带有版本号，解析时会把多个版本的
schema 依次作用在同一张字段表上，缺少的字段由模型侧取默认值；序列化时再按同样的顺序写回。
"""

from __future__ import annotations

from typing import Any, Iterable, Mapping, Sequence

from ._binary import VARSHORT_MAX, ByteReader, ByteWriter
from .exceptions import PhigrosDataError, PhigrosFormatError
from .schemas import FieldKind, ObjectSchema, SchemaField

#: 连续布尔最多能塞进一个字节。
_MAX_PACKED_BOOLS = 8


class FieldMap:
    """二进制解码后的字段集合，保留写入顺序。"""

    __slots__ = ("_values",)

    def __init__(self, values: Mapping[str, Any] | None = None) -> None:
        self._values: dict[str, Any] = dict(values) if values else {}

    def __contains__(self, name: object) -> bool:
        return name in self._values

    def __len__(self) -> int:
        return len(self._values)

    def __getitem__(self, name: str) -> Any:
        return self._values[name]

    def get(self, name: str, default: Any = None) -> Any:
        return self._values.get(name, default)

    def set(self, name: str, value: Any) -> None:
        self._values[name] = value

    def keys(self) -> Iterable[str]:
        return self._values.keys()

    def items(self) -> Iterable[tuple[str, Any]]:
        return self._values.items()

    def __repr__(self) -> str:  # pragma: no cover - 仅用于调试
        return f"FieldMap({self._values!r})"


def _read_value(reader: ByteReader, field: SchemaField) -> Any:
    kind = field.kind
    if kind is FieldKind.U8:
        return reader.read_u8()
    if kind is FieldKind.U16:
        return reader.read_u16()
    if kind is FieldKind.F32:
        return reader.read_f32()
    if kind is FieldKind.STRING:
        return reader.read_string()
    if kind is FieldKind.VARSHORT:
        return reader.read_varshort()
    if kind is FieldKind.U16_ARRAY:
        return [reader.read_u16() for _ in range(field.count)]
    if kind is FieldKind.VARSHORT_ARRAY:
        return [reader.read_varshort() for _ in range(field.count)]

    raise PhigrosFormatError(f"未知字段类型 {kind}。")


def deserialize(reader: ByteReader, schema: ObjectSchema) -> FieldMap:
    """按 schema 顺序读取一段字段。"""
    fields = FieldMap()
    packed = 0
    bit = 0

    for field in schema.fields:
        if field.kind is FieldKind.BOOL:
            # 连续布尔共用一个字节：第一个布尔只窥视不消费，直到遇到非布尔字段才跳过该字节。
            if bit == 0:
                packed = reader.peek_u8()
            if bit >= _MAX_PACKED_BOOLS:
                raise PhigrosFormatError(
                    f"结构 {schema.name} 中 '{field.name}' 之前连续出现了超过 "
                    f"{_MAX_PACKED_BOOLS} 个布尔字段。"
                )

            fields.set(field.name, bool((packed >> bit) & 1))
            bit += 1
            continue

        if bit:
            bit = 0
            reader.skip(1)

        fields.set(field.name, _read_value(reader, field))

    if bit:
        reader.skip(1)

    return fields


def _write_value(writer: ByteWriter, field: SchemaField, value: Any) -> None:
    kind = field.kind
    if kind is FieldKind.U8:
        writer.write_u8(int(value or 0))
    elif kind is FieldKind.U16:
        writer.write_u16(int(value or 0))
    elif kind is FieldKind.F32:
        writer.write_f32(float(value or 0.0))
    elif kind is FieldKind.STRING:
        writer.write_string(value or "")
    elif kind is FieldKind.VARSHORT:
        writer.write_varshort(_checked_varshort(field, value))
    elif kind is FieldKind.U16_ARRAY:
        for item in _checked_array(field, value):
            writer.write_u16(int(item))
    elif kind is FieldKind.VARSHORT_ARRAY:
        for item in _checked_array(field, value):
            writer.write_varshort(_checked_varshort(field, item))
    else:
        raise PhigrosFormatError(f"未知字段类型 {kind}。")


def serialize(writer: ByteWriter, fields: FieldMap, schema: ObjectSchema) -> None:
    """按 schema 顺序写出一段字段。"""
    packed = 0
    bit = 0

    for field in schema.fields:
        if field.kind is FieldKind.BOOL:
            if bit >= _MAX_PACKED_BOOLS:
                raise PhigrosFormatError(
                    f"结构 {schema.name} 中 '{field.name}' 之前连续出现了超过 "
                    f"{_MAX_PACKED_BOOLS} 个布尔字段。"
                )

            if bool(fields.get(field.name, False)):
                packed |= 1 << bit
            bit += 1
            continue

        if bit:
            writer.write_u8(packed)
            bit = 0
            packed = 0

        _write_value(writer, field, fields.get(field.name))

    if bit:
        writer.write_u8(packed)


def deserialize_versioned(
    reader: ByteReader, schemas: Sequence[ObjectSchema], version: int
) -> FieldMap:
    """依次应用前 ``version`` 个 schema 读取字段，用于带版本的存档结构。"""
    fields = FieldMap()
    for schema in schemas[: max(version, 0)]:
        for name, value in deserialize(reader, schema).items():
            fields.set(name, value)

    return fields


def serialize_versioned(
    writer: ByteWriter, fields: FieldMap, schemas: Sequence[ObjectSchema], version: int
) -> None:
    """依次应用前 ``version`` 个 schema 写出字段，与 :func:`deserialize_versioned` 互逆。"""
    for schema in schemas[: max(version, 0)]:
        serialize(writer, fields, schema)


def _checked_varshort(field: SchemaField, value: Any) -> int:
    number = int(value or 0)
    if not 0 <= number <= VARSHORT_MAX:
        raise PhigrosDataError(
            f"字段 '{field.name}' 的值 {number} 超出变长整数能表示的范围 0..{VARSHORT_MAX}。"
        )

    return number


def _checked_array(field: SchemaField, value: Any) -> list[Any]:
    if value is None:
        return [0] * field.count

    items = list(value)
    if field.count and len(items) != field.count:
        raise PhigrosDataError(
            f"字段 '{field.name}' 需要 {field.count} 个元素，实际为 {len(items)} 个。"
        )

    return items
