"""字节层与加解密的单元测试。"""

from __future__ import annotations

import pytest

from PhigrosScoreLibrary._binary import VARSHORT_MAX, ByteReader, ByteWriter
from PhigrosScoreLibrary.crypto import decrypt, decrypt_entry, encrypt, encrypt_entry
from PhigrosScoreLibrary.exceptions import PhigrosDataError, PhigrosFormatError


@pytest.mark.parametrize("value", [0, 1, 127, 128, 200, 300, 2047, VARSHORT_MAX])
def test_varshort_round_trips(value: int) -> None:
    writer = ByteWriter()
    writer.write_varshort(value)

    reader = ByteReader(writer.to_bytes())
    assert reader.read_varshort() == value
    assert reader.remaining == 0


def test_varshort_max_is_two_bytes_of_seven_plus_eight_bits() -> None:
    # 首字节给低 7 位，次字节给高 8 位，因此上限是 0x7F7F。
    assert VARSHORT_MAX == 0x7F7F

    writer = ByteWriter()
    writer.write_varshort(VARSHORT_MAX)
    assert writer.to_bytes() == bytes([0xFF, 0xFE])
    assert ByteReader(writer.to_bytes()).read_varshort() == VARSHORT_MAX


@pytest.mark.parametrize(("value", "expected_bytes"), [(0, 1), (127, 1), (128, 2), (VARSHORT_MAX, 2)])
def test_varshort_width(value: int, expected_bytes: int) -> None:
    writer = ByteWriter()
    writer.write_varshort(value)

    assert len(writer) == expected_bytes


def test_varshort_two_byte_layout() -> None:
    writer = ByteWriter()
    writer.write_varshort(200)

    # 200 = 0b1100_1000：低 7 位 0x48 放进首字节并置最高位，剩余 0x01 放进第二字节。
    assert writer.to_bytes() == bytes([0xC8, 0x01])


def test_varshort_rejects_out_of_range() -> None:
    writer = ByteWriter()
    with pytest.raises(PhigrosDataError):
        writer.write_varshort(VARSHORT_MAX + 1)
    with pytest.raises(PhigrosDataError):
        writer.write_varshort(-1)


def test_string_with_suffix_in_length() -> None:
    writer = ByteWriter()
    writer.write_string("Chapter8", b".0")

    reader = ByteReader(writer.to_bytes())
    assert reader.read_string(suffix_length=2) == "Chapter8"
    assert reader.remaining == 0


def test_string_round_trips_utf8() -> None:
    writer = ByteWriter()
    writer.write_string("尊師")

    assert ByteReader(writer.to_bytes()).read_string() == "尊師"


def test_reader_rejects_truncated_data() -> None:
    reader = ByteReader(b"\x01\x02")
    reader.read_u16()

    with pytest.raises(PhigrosFormatError):
        reader.read_u8()


def test_reader_position_and_slices() -> None:
    reader = ByteReader(b"\x01\x02\x03\x04")
    reader.skip(2)

    assert reader.position == 2
    assert reader.remaining == 2
    assert reader.remaining_bytes == b"\x03\x04"
    assert reader.peek_u8() == 3
    assert reader.position == 2


@pytest.mark.parametrize("length", [1, 15, 16, 17, 1000])
def test_cipher_round_trips(length: int) -> None:
    plaintext = bytes((index * 31 + 7) & 0xFF for index in range(length))

    ciphertext = encrypt(plaintext)
    assert len(ciphertext) % 16 == 0
    assert ciphertext != plaintext
    assert decrypt(ciphertext) == plaintext


def test_cipher_padding_always_adds_a_block() -> None:
    # PKCS7 在明文正好是整块时会再补一个整块，这是与上游实现一致的行为。
    assert len(encrypt(bytes(16))) == 32
    assert len(encrypt(bytes(15))) == 16


def test_cipher_is_deterministic() -> None:
    # IV 固定，因此同样的明文必然得到同样的密文——这正是存档能逐字节往返的前提。
    plaintext = bytes(range(37))

    assert encrypt(plaintext) == encrypt(plaintext)


def test_entry_keeps_version_byte_in_clear() -> None:
    plaintext = bytes(range(20))
    entry = encrypt_entry(3, plaintext)

    assert entry[0] == 3
    assert len(entry) == 1 + len(encrypt(plaintext))

    version, decrypted = decrypt_entry(entry)
    assert version == 3
    assert decrypted == plaintext


def test_cipher_rejects_bad_input() -> None:
    with pytest.raises(PhigrosFormatError):
        decrypt(bytes(17))
    with pytest.raises(PhigrosFormatError):
        decrypt(b"")

    # 长度合法但内容随机，PKCS7 填充几乎必然不合法。
    with pytest.raises(PhigrosFormatError):
        decrypt(b"\xab" * 32)

    with pytest.raises(PhigrosFormatError):
        decrypt_entry(b"")


def test_encrypt_entry_rejects_bad_version() -> None:
    with pytest.raises(ValueError):
        encrypt_entry(256, b"x")
