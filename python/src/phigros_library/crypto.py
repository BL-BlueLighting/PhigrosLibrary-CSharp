"""存档条目的 AES-256-CBC 加解密。

存档 zip 中每个条目（``gameRecord`` / ``gameKey`` / ``gameProgress`` / ``user`` / ``settings``）
的格式为::

    +---------+--------------------------------------+
    | version | AES-256-CBC(明文 + PKCS7 填充)        |
    | 1 字节  | 剩余全部字节                          |
    +---------+--------------------------------------+

首个字节是明文版本号，不参与加密，也不属于密文长度。
"""

from __future__ import annotations

import base64

from cryptography.hazmat.primitives import padding
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

from .exceptions import PhigrosFormatError

#: AES-256 密钥，32 字节。
KEY = base64.b64decode("6Jaa0qVAJZuXkZCLiOa/Ax5tIZVu+taKUN1V1nqwkks=")

#: AES-CBC 初始向量，16 字节。
IV = base64.b64decode("Kk/wisgNYwcAV8WVGMgyUw==")

#: AES 分组大小（字节），即 PKCS7 的填充粒度。
BLOCK_SIZE = 16

_PADDING = padding.PKCS7(BLOCK_SIZE * 8)


def decrypt(ciphertext: bytes) -> bytes:
    """解密一段 AES-256-CBC 密文并去除 PKCS7 填充。"""
    if not ciphertext or len(ciphertext) % BLOCK_SIZE != 0:
        raise PhigrosFormatError(
            f"密文长度 {len(ciphertext)} 不是 {BLOCK_SIZE} 的正整数倍，数据可能已损坏。"
        )

    decryptor = Cipher(algorithms.AES(KEY), modes.CBC(IV)).decryptor()
    padded = decryptor.update(ciphertext) + decryptor.finalize()

    try:
        unpadder = _PADDING.unpadder()
        return unpadder.update(padded) + unpadder.finalize()
    except ValueError as exc:
        raise PhigrosFormatError("解密失败：填充不合法，密钥或存档内容不匹配。") from exc


def encrypt(plaintext: bytes) -> bytes:
    """加密一段明文，返回 AES-256-CBC 密文（含 PKCS7 填充）。"""
    padder = _PADDING.padder()
    padded = padder.update(plaintext) + padder.finalize()

    encryptor = Cipher(algorithms.AES(KEY), modes.CBC(IV)).encryptor()
    return encryptor.update(padded) + encryptor.finalize()


def decrypt_entry(entry: bytes) -> tuple[int, bytes]:
    """解析存档条目：首字节为版本号，其余为密文。"""
    if not entry:
        raise PhigrosFormatError("存档条目为空。")

    return entry[0], decrypt(entry[1:])


def encrypt_entry(version: int, plaintext: bytes) -> bytes:
    """按存档条目的格式打包：``version`` 作为首字节明文写入，其余加密。"""
    if not 0 <= version <= 0xFF:
        raise ValueError(f"版本号 {version} 必须在 0..255 之间。")

    return bytes([version]) + encrypt(plaintext)
