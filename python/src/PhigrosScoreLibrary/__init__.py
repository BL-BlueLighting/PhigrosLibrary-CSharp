"""Phigros 云存档解析库的 Python 实现。

包含三部分：

* **离线解析**：:func:`parse_save` / :func:`write_save` 在存档 zip 与模型之间转换，
  往返可做到逐字节一致；
* **成绩计算**：:class:`RksCalculator` 提供 B19、总 RKS 与期望 ACC；
* **扫码登录**：:class:`PhigrosLogin` 走完 TapTap 设备码流程，换回 sessionToken；
* **在线查分**：:class:`PhigrosClient` / :class:`AsyncPhigrosClient` 用一个 sessionToken
  完成"查昵称 / 查概要 / 拉存档 / 算 B19"。

只有 AES 依赖第三方库（``cryptography``），其余全部基于标准库。

快速上手::

    from PhigrosScoreLibrary import DifficultyTable, PhigrosClient, PhigrosLogin

    # 没有 sessionToken 时先扫码登录
    with PhigrosLogin() as login:
        result = login.login(lambda qr: print("请扫码：", qr.url))

    client = PhigrosClient(result.session_token, DifficultyTable.load("difficulty.tsv"))
    print(client.get_nickname())
    print(client.get_best19().rks)
"""

from __future__ import annotations

from .accounts import LeanCloudApp, TapTapRegion
from .api import API_BASE_URL, APP_ID, APP_KEY, CloudSaveRecord, LeanCloudClient
from .client import AsyncPhigrosClient, PhigrosClient
from .difficulty import DifficultyTable, bundled_difficulty_text
from .exceptions import (
    PhigrosApiError,
    PhigrosDataError,
    PhigrosError,
    PhigrosFormatError,
    PhigrosLoginError,
)
from .login import (
    DEFAULT_PERMISSIONS,
    MAC_ALGORITHM,
    AsyncPhigrosLogin,
    LoginResult,
    PhigrosLogin,
    QrCodeData,
    TapTapEndpoints,
    TapTapLoginClient,
    TapTapToken,
    TokenPollResult,
    TokenPollStatus,
    create_authorization_header,
    create_nonce,
)
from .models import (
    B19Result,
    BestEntry,
    ExpectEntry,
    GameKey,
    GameProgress,
    LEVEL_COUNT,
    LevelRecord,
    SaveData,
    Settings,
    SongDifficulty,
    SongLevels,
    Summary,
    UserData,
)
from .rks import (
    BEST_COUNT,
    MINIMUM_ACCURACY,
    RECORD_COUNT,
    RksCalculator,
    compute_expected_accuracy,
    compute_rks,
)
from .save import (
    ENTRY_GAME_KEY,
    ENTRY_GAME_PROGRESS,
    ENTRY_GAME_RECORD,
    ENTRY_SETTINGS,
    ENTRY_USER,
    parse as parse_save,
    parse_file as parse_save_file,
    parse_summary,
    write as write_save,
    write_file as write_save_file,
    write_summary,
)
from .testing import create_sample_save

#: 版本号。这里是唯一来源，pyproject.toml 通过 hatchling 动态读取它。
__version__ = "1.0.1"

__all__ = [
    # 存档读写
    "parse_save",
    "parse_save_file",
    "write_save",
    "write_save_file",
    "parse_summary",
    "write_summary",
    "ENTRY_GAME_RECORD",
    "ENTRY_GAME_KEY",
    "ENTRY_GAME_PROGRESS",
    "ENTRY_USER",
    "ENTRY_SETTINGS",
    # 模型
    "SaveData",
    "Summary",
    "GameKey",
    "GameProgress",
    "UserData",
    "Settings",
    "SongLevels",
    "LevelRecord",
    "SongDifficulty",
    "LEVEL_COUNT",
    "BestEntry",
    "B19Result",
    "ExpectEntry",
    # 计算
    "DifficultyTable",
    "bundled_difficulty_text",
    "RksCalculator",
    "compute_rks",
    "compute_expected_accuracy",
    "MINIMUM_ACCURACY",
    "BEST_COUNT",
    "RECORD_COUNT",
    # 接口
    "LeanCloudClient",
    "CloudSaveRecord",
    "PhigrosClient",
    "AsyncPhigrosClient",
    "API_BASE_URL",
    "APP_ID",
    "APP_KEY",
    "LeanCloudApp",
    "TapTapRegion",
    # 扫码登录
    "PhigrosLogin",
    "AsyncPhigrosLogin",
    "TapTapLoginClient",
    "TapTapEndpoints",
    "QrCodeData",
    "TapTapToken",
    "TokenPollResult",
    "TokenPollStatus",
    "LoginResult",
    "create_authorization_header",
    "create_nonce",
    "DEFAULT_PERMISSIONS",
    "MAC_ALGORITHM",
    # 异常
    "PhigrosError",
    "PhigrosFormatError",
    "PhigrosDataError",
    "PhigrosApiError",
    "PhigrosLoginError",
    # 其他
    "create_sample_save",
    "__version__",
]
