"""存档与计算结果的模型。

命名沿用 Python 习惯的 snake_case；``to_dict()`` 输出的是与上游 C 实现一致的字段名，
方便已有调用方直接替换。
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from enum import IntEnum
from typing import Any, Iterator


class SongDifficulty(IntEnum):
    """难度编号，与存档中的下标一致。"""

    EZ = 0
    HD = 1
    IN = 2
    AT = 3
    """AT，部分曲目没有该难度。"""


#: 难度数量。
LEVEL_COUNT = len(SongDifficulty)


@dataclass(slots=True)
class LevelRecord:
    """某一首曲目在单个难度下的成绩。"""

    #: 满分成绩。
    PERFECT_SCORE = 1_000_000

    score: int = 0
    """分数，0 ~ 1000000。"""

    accuracy: float = 0.0
    """准确率，百分比数值（如 99.5 表示 99.5%）。

    磁盘上是单精度浮点，写回时会被截断到 float32；模型里保持 Python 的 ``float``。
    """

    full_combo: bool = False

    @property
    def is_all_perfect(self) -> bool:
        """是否为满分（φ）。"""
        return self.score == self.PERFECT_SCORE

    def to_dict(self) -> dict[str, Any]:
        return {
            "score": self.score,
            "acc": self.accuracy,
            "fc": self.full_combo,
        }


@dataclass(slots=True)
class SongLevels:
    """一首曲目四个难度的成绩，``None`` 表示该难度尚无成绩。"""

    levels: list[LevelRecord | None] = field(default_factory=lambda: [None] * LEVEL_COUNT)

    def __getitem__(self, level: int | SongDifficulty) -> LevelRecord | None:
        return self.levels[self._index(level)]

    def __setitem__(self, level: int | SongDifficulty, value: LevelRecord | None) -> None:
        self.levels[self._index(level)] = value

    def __iter__(self) -> Iterator[LevelRecord | None]:
        return iter(self.levels)

    @staticmethod
    def _index(level: int | SongDifficulty) -> int:
        index = int(level)
        if not 0 <= index < LEVEL_COUNT:
            raise IndexError(f"难度下标 {index} 必须在 0..{LEVEL_COUNT - 1} 之间。")
        return index

    @property
    def is_empty(self) -> bool:
        """是否四个难度都没有成绩。"""
        return all(level is None for level in self.levels)

    def played(self) -> Iterator[tuple[SongDifficulty, LevelRecord]]:
        """所有已有成绩的难度。"""
        for index, record in enumerate(self.levels):
            if record is not None:
                yield SongDifficulty(index), record

    def to_dict(self) -> list[Any]:
        return [record.to_dict() if record else None for record in self.levels]


@dataclass(slots=True)
class GameKey:
    """``gameKey`` 条目：曲目解锁相关的密钥/开关。"""

    version: int = 2
    """该条目的版本号，决定用到哪些字段表。默认取本库支持的最高版本。"""

    map: dict[str, list[int]] = field(default_factory=dict)
    """键为章节或曲目名，值为 5 个整数（磁盘上只保存非零项）。"""

    lanota_read_keys: int = 0
    camellia_read_key: bool = False

    overflow: str | None = None
    """无法识别的尾部字节（base64），写回时原样保留。"""

    def to_dict(self) -> dict[str, Any]:
        result: dict[str, Any] = {
            "version": self.version,
            "map": {key: list(value) for key, value in self.map.items()},
            "lanotaReadKeys": self.lanota_read_keys,
            "camelliaReadKey": self.camellia_read_key,
        }
        if self.overflow is not None:
            result["overflow"] = self.overflow
        return result


@dataclass(slots=True)
class GameProgress:
    """``gameProgress`` 条目：主线进度、解锁状态与货币。"""

    version: int = 3
    """该条目的版本号，决定用到哪些字段表。默认取本库支持的最高版本。"""

    is_first_run: bool = False
    legacy_chapter_finished: bool = False
    already_show_collection_tip: bool = False
    already_show_auto_unlock_in_tip: bool = False
    completed: str = ""
    """已完成曲目的 id 列表，以某种分隔符拼接的字符串。"""

    song_update_info: int = 0
    challenge_mode_rank: int = 0
    money: list[int] = field(default_factory=lambda: [0] * 5)
    unlock_flag_of_spasmodic: int = 0
    unlock_flag_of_igallta: int = 0
    unlock_flag_of_rrharil: int = 0
    flag_of_song_record_key: int = 0

    random_version_unlocked: int = 0
    """v2 起存在。"""

    chapter8_unlock_begin: bool = False
    chapter8_unlock_second_phase: bool = False
    chapter8_passed: bool = False
    chapter8_song_unlocked: int = 0

    overflow: str | None = None
    """无法识别的尾部字节（base64），写回时原样保留。"""

    def to_dict(self) -> dict[str, Any]:
        result: dict[str, Any] = {
            "version": self.version,
            "isFirstRun": self.is_first_run,
            "legacyChapterFinished": self.legacy_chapter_finished,
            "alreadyShowCollectionTip": self.already_show_collection_tip,
            "alreadyShowAutoUnlockINTip": self.already_show_auto_unlock_in_tip,
            "completed": self.completed,
            "songUpdateInfo": self.song_update_info,
            "challengeModeRank": self.challenge_mode_rank,
            "money": list(self.money),
            "unlockFlagOfSpasmodic": self.unlock_flag_of_spasmodic,
            "unlockFlagOfIgallta": self.unlock_flag_of_igallta,
            "unlockFlagOfRrharil": self.unlock_flag_of_rrharil,
            "flagOfSongRecordKey": self.flag_of_song_record_key,
            "randomVersionUnlocked": self.random_version_unlocked,
            "chapter8UnlockBegin": self.chapter8_unlock_begin,
            "chapter8UnlockSecondPhase": self.chapter8_unlock_second_phase,
            "chapter8Passed": self.chapter8_passed,
            "chapter8SongUnlocked": self.chapter8_song_unlocked,
        }
        if self.overflow is not None:
            result["overflow"] = self.overflow
        return result


@dataclass(slots=True)
class UserData:
    """``user`` 条目：玩家资料。"""

    show_player_id: bool = False
    self_intro: str = ""
    avatar: str = ""
    background: str = ""

    def to_dict(self) -> dict[str, Any]:
        return {
            "showPlayerId": self.show_player_id,
            "selfIntro": self.self_intro,
            "avatar": self.avatar,
            "background": self.background,
        }


@dataclass(slots=True)
class Settings:
    """``settings`` 条目：游戏内设置。结构固定，不带版本号。"""

    chord_support: bool = False
    fc_ap_indicator: bool = False
    enable_hit_sound: bool = False
    low_resolution_mode: bool = False
    device_name: str = ""
    bright: float = 0.0
    music_volume: float = 0.0
    effect_volume: float = 0.0
    hit_sound_volume: float = 0.0
    sound_offset: float = 0.0
    note_scale: float = 0.0

    def to_dict(self) -> dict[str, Any]:
        return {
            "chordSupport": self.chord_support,
            "fcAPIndicator": self.fc_ap_indicator,
            "enableHitSound": self.enable_hit_sound,
            "lowResolutionMode": self.low_resolution_mode,
            "deviceName": self.device_name,
            "bright": self.bright,
            "musicVolume": self.music_volume,
            "effectVolume": self.effect_volume,
            "hitSoundVolume": self.hit_sound_volume,
            "soundOffset": self.sound_offset,
            "noteScale": self.note_scale,
        }


@dataclass(slots=True)
class SaveData:
    """一份完整的云存档，对应 zip 中的五个条目。"""

    game_record: dict[str, SongLevels] = field(default_factory=dict)
    """各曲目各难度的成绩。键为曲目 id（磁盘上带 ``.0`` 后缀，读取时已剥离）。"""

    game_key: GameKey = field(default_factory=GameKey)
    game_progress: GameProgress = field(default_factory=GameProgress)
    user: UserData = field(default_factory=UserData)
    settings: Settings = field(default_factory=Settings)

    def to_dict(self) -> dict[str, Any]:
        return {
            "gameRecord": {song_id: levels.to_dict() for song_id, levels in self.game_record.items()},
            "gameKey": self.game_key.to_dict(),
            "gameProgress": self.game_progress.to_dict(),
            "user": self.user.to_dict(),
            "settings": self.settings.to_dict(),
        }


@dataclass(slots=True)
class Summary:
    """玩家概要，对应 LeanCloud ``_GameSave`` 记录中的 base64 ``summary`` 字段。"""

    save_version: int = 0
    challenge_mode_rank: int = 0
    ranking_score: float = 0.0
    game_version: int = 0
    avatar: str = ""
    progress: list[int] = field(default_factory=lambda: [0] * 12)
    """12 项统计，每三个一组对应 EZ / HD / IN / AT：已游玩数、Full Combo 数、All Perfect 数。"""

    object_id: str | None = None
    user_id: str | None = None
    file_id: str | None = None
    url: str | None = None
    updated_at: datetime | None = None
    """以上为 LeanCloud 侧的元数据，不属于二进制结构。"""

    def to_dict(self) -> dict[str, Any]:
        result: dict[str, Any] = {
            "saveVersion": self.save_version,
            "challengeModeRank": self.challenge_mode_rank,
            "rankingScore": self.ranking_score,
            "gameVersion": self.game_version,
            "avatar": self.avatar,
            "progress": list(self.progress),
        }
        if self.object_id is not None:
            result["objectId"] = self.object_id
        if self.user_id is not None:
            result["userId"] = self.user_id
        if self.file_id is not None:
            result["fileId"] = self.file_id
        if self.url is not None:
            result["url"] = self.url
        if self.updated_at is not None:
            result["updatedAt"] = self.updated_at.isoformat()
        return result


@dataclass(slots=True)
class BestEntry:
    """B19 中的一首曲目成绩。"""

    id: str = ""
    level: SongDifficulty = SongDifficulty.EZ
    difficulty: float = 0.0
    rks: float = 0.0
    score: int = 0
    accuracy: float = 0.0
    full_combo: bool = False

    def to_dict(self) -> dict[str, Any]:
        return {
            "id": self.id,
            "level": int(self.level),
            "difficulty": self.difficulty,
            "rks": self.rks,
            "score": self.score,
            "acc": self.accuracy,
            "fc": self.full_combo,
        }


@dataclass(slots=True)
class B19Result:
    """B19 计算结果：19 首最高 RKS 的成绩，外加一首难度最高的 φ（满分）曲目。"""

    rks: float = 0.0
    """总 RKS，即 20 个成绩的单曲 RKS 平均值；没有 φ 时该位置按 0 计入。"""

    phi: BestEntry | None = None
    best: list[BestEntry] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return {
            "rks": self.rks,
            "phi": self.phi.to_dict() if self.phi else None,
            "best": [entry.to_dict() for entry in self.best],
        }


@dataclass(slots=True)
class ExpectEntry:
    """推分建议：某个难度需要打到多少 ACC 才能进入 B19。"""

    id: str = ""
    level: SongDifficulty = SongDifficulty.EZ
    difficulty: float = 0.0
    rks: float = 0.0
    accuracy: float = 0.0
    expect: float = 0.0

    def to_dict(self) -> dict[str, Any]:
        return {
            "id": self.id,
            "level": int(self.level),
            "difficulty": self.difficulty,
            "rks": self.rks,
            "acc": self.accuracy,
            "expect": self.expect,
        }
