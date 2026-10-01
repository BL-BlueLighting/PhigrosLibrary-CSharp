"""存档各部分的字段表。

字段顺序即磁盘顺序，与上游 C 实现中的 ``Summary`` / ``GameKey`` / ``GameProgress`` /
``User`` / ``Settings`` 表一一对应。游戏更新后若追加字段，在对应元组尾部新增一个
:class:`ObjectSchema` 即可（版本号来自存档自身的 version 字段）。
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class FieldKind(Enum):
    """存档二进制结构中支持的字段类型。"""

    BOOL = "bool"
    """布尔值。连续的布尔值按低位在前打包进同一个字节，被其他类型打断时对齐到下一字节。"""

    U8 = "u8"
    U16 = "u16"
    F32 = "f32"
    STRING = "string"
    """变长整数长度前缀的 UTF-8 字符串。"""

    VARSHORT = "varshort"
    U16_ARRAY = "u16_array"
    VARSHORT_ARRAY = "varshort_array"


@dataclass(frozen=True, slots=True)
class SchemaField:
    """描述存档二进制结构中的一个字段。"""

    name: str
    kind: FieldKind
    count: int = 0
    """数组类型的元素个数，非数组类型忽略。"""


@dataclass(frozen=True, slots=True)
class ObjectSchema:
    """描述存档二进制结构中的一段字段序列。"""

    name: str
    fields: tuple[SchemaField, ...]


#: 玩家概要，来源为 LeanCloud 返回的 base64 字段，不带版本号。
SUMMARY = ObjectSchema(
    "Summary",
    (
        SchemaField("saveVersion", FieldKind.U8),
        SchemaField("challengeModeRank", FieldKind.U16),
        SchemaField("rankingScore", FieldKind.F32),
        SchemaField("gameVersion", FieldKind.VARSHORT),
        SchemaField("avatar", FieldKind.STRING),
        SchemaField("progress", FieldKind.U16_ARRAY, 12),
    ),
)

#: ``gameKey`` 的 v1 段。
GAME_KEY_1 = ObjectSchema("GameKey1", (SchemaField("lanotaReadKeys", FieldKind.U8),))

#: ``gameKey`` 的 v2 段。
GAME_KEY_2 = ObjectSchema("GameKey2", (SchemaField("camelliaReadKey", FieldKind.BOOL),))

#: ``gameKey`` 按版本排列的字段表。
GAME_KEY = (GAME_KEY_1, GAME_KEY_2)

#: ``gameProgress`` 的 v1 段。
GAME_PROGRESS_1 = ObjectSchema(
    "GameProgress1",
    (
        SchemaField("isFirstRun", FieldKind.BOOL),
        SchemaField("legacyChapterFinished", FieldKind.BOOL),
        SchemaField("alreadyShowCollectionTip", FieldKind.BOOL),
        SchemaField("alreadyShowAutoUnlockINTip", FieldKind.BOOL),
        SchemaField("completed", FieldKind.STRING),
        SchemaField("songUpdateInfo", FieldKind.U8),
        SchemaField("challengeModeRank", FieldKind.U16),
        SchemaField("money", FieldKind.VARSHORT_ARRAY, 5),
        SchemaField("unlockFlagOfSpasmodic", FieldKind.U8),
        SchemaField("unlockFlagOfIgallta", FieldKind.U8),
        SchemaField("unlockFlagOfRrharil", FieldKind.U8),
        SchemaField("flagOfSongRecordKey", FieldKind.U8),
    ),
)

#: ``gameProgress`` 的 v2 段。
GAME_PROGRESS_2 = ObjectSchema(
    "GameProgress2", (SchemaField("randomVersionUnlocked", FieldKind.U8),)
)

#: ``gameProgress`` 的 v3 段。
GAME_PROGRESS_3 = ObjectSchema(
    "GameProgress3",
    (
        SchemaField("chapter8UnlockBegin", FieldKind.BOOL),
        SchemaField("chapter8UnlockSecondPhase", FieldKind.BOOL),
        SchemaField("chapter8Passed", FieldKind.BOOL),
        SchemaField("chapter8SongUnlocked", FieldKind.U8),
    ),
)

#: ``gameProgress`` 按版本排列的字段表。
GAME_PROGRESS = (GAME_PROGRESS_1, GAME_PROGRESS_2, GAME_PROGRESS_3)

#: ``user``，无版本号，结构固定。
USER = ObjectSchema(
    "User",
    (
        SchemaField("showPlayerId", FieldKind.BOOL),
        SchemaField("selfIntro", FieldKind.STRING),
        SchemaField("avatar", FieldKind.STRING),
        SchemaField("background", FieldKind.STRING),
    ),
)

#: ``settings``，无版本号，结构固定。
SETTINGS = ObjectSchema(
    "Settings",
    (
        SchemaField("chordSupport", FieldKind.BOOL),
        SchemaField("fcAPIndicator", FieldKind.BOOL),
        SchemaField("enableHitSound", FieldKind.BOOL),
        SchemaField("lowResolutionMode", FieldKind.BOOL),
        SchemaField("deviceName", FieldKind.STRING),
        SchemaField("bright", FieldKind.F32),
        SchemaField("musicVolume", FieldKind.F32),
        SchemaField("effectVolume", FieldKind.F32),
        SchemaField("hitSoundVolume", FieldKind.F32),
        SchemaField("soundOffset", FieldKind.F32),
        SchemaField("noteScale", FieldKind.F32),
    ),
)

#: 各结构当前支持的最高版本号。
HIGHEST_GAME_KEY_VERSION = len(GAME_KEY)
HIGHEST_GAME_PROGRESS_VERSION = len(GAME_PROGRESS)
