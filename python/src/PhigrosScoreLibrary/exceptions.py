"""本库的异常体系。"""

from __future__ import annotations


class PhigrosError(Exception):
    """所有异常的基类。"""


class PhigrosFormatError(PhigrosError):
    """存档字节流不符合预期格式。"""


class PhigrosDataError(PhigrosError):
    """定数表缺失曲目，或数据本身不合法。"""


class PhigrosApiError(PhigrosError):
    """LeanCloud 接口返回错误或响应无法解析。"""


class PhigrosLoginError(PhigrosError):
    """扫码登录流程中出现错误。"""
