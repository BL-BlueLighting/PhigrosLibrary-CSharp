"""按给定随机种子生成一份结构与真实云存档一致的示例存档。

用于在没有真实账号的情况下试用本库、写单元测试，或验证"写出 → 读回"的往返一致性。
生成的数据是伪造的，与任何真实玩家无关。
"""

from __future__ import annotations

import random
import struct

from .difficulty import DifficultyTable
from .models import (
    GameKey,
    GameProgress,
    LEVEL_COUNT,
    LevelRecord,
    SaveData,
    Settings,
    SongLevels,
    UserData,
)


def create_sample_save(
    difficulties: DifficultyTable, seed: int = 2024, song_count: int = 80
) -> SaveData:
    """生成一份示例存档。

    :param difficulties: 曲目来源，生成的成绩只覆盖表中存在的难度。
    :param seed: 随机种子，相同种子生成完全相同的结果。
    :param song_count: 最多取多少首曲目。
    """
    rng = random.Random(seed)
    save = SaveData()

    songs = list(difficulties.songs.items())[:song_count]
    for index, (song_id, levels) in enumerate(songs):
        song = SongLevels()

        # 第一首曲目的最高难度固定为满分，保证 B19 里一定有一个 φ。
        perfect_level = _highest_level(levels) if index == 0 else -1

        for level in range(LEVEL_COUNT):
            if levels[level] == 0:
                continue  # 该曲没有这个难度

            force_perfect = level == perfect_level
            if not force_perfect and rng.random() < 0.15:
                continue  # 假装这个难度还没打

            song[level] = _create_record(rng, force_perfect)

        if not song.is_empty:
            save.game_record[song_id] = song

    save.game_key = GameKey(
        version=2,
        map={"Chapter8": [1, 1, 0, 0, 0]},
        lanota_read_keys=rng.randrange(2),
        camellia_read_key=bool(rng.randrange(2)),
    )

    save.game_progress = GameProgress(
        # money 的每一项都是变长整数，上限 32639。
        money=[0, 12, 340, 5600, 12000],
        completed="|".join(list(save.game_record)[:10]),
        challenge_mode_rank=rng.randrange(100),
        legacy_chapter_finished=True,
        song_update_info=rng.randrange(4),
    )

    save.user = UserData(
        show_player_id=False,
        self_intro="由 create_sample_save 生成的示例数据",
        avatar="Introduction.0",
        background="Introduction.0",
    )

    save.settings = Settings(
        device_name="PhigrosLibrary",
        bright=_float32(0.8),
        music_volume=_float32(0.7),
        effect_volume=_float32(0.7),
        hit_sound_volume=_float32(0.5),
        note_scale=1.0,
    )

    return save


def _float32(value: float) -> float:
    """把数值截到单精度。

    磁盘上的 acc 与各项音量都是 float32，先截断可以保证"模型 → 存档 → 模型"完全一致，
    不会出现 0.7 读回来变成 0.699999988079071 这类意外。
    """
    return struct.unpack("<f", struct.pack("<f", value))[0]


def _create_record(rng: random.Random, force_perfect: bool) -> LevelRecord:
    if force_perfect:
        return LevelRecord(LevelRecord.PERFECT_SCORE, 100.0, full_combo=True)

    # ACC 集中在 90 以上，偶尔出现高 ACC 成绩。
    roll = rng.random()
    if roll < 0.3:
        accuracy = 90.0 + rng.random() * 5.0
    elif roll < 0.8:
        accuracy = 95.0 + rng.random() * 4.0
    else:
        accuracy = 99.0 + rng.random()

    # 真实计分中满分对应 ACC 100，其余线性折算已足够用来说明问题。
    score = min(round(accuracy * 10_000), LevelRecord.PERFECT_SCORE)
    full_combo = accuracy >= 97.0 or rng.random() < 0.1

    return LevelRecord(score, _float32(accuracy), full_combo)


def _highest_level(levels: list[float]) -> int:
    highest = 0
    for level, difficulty in enumerate(levels):
        if difficulty > 0:
            highest = level

    return highest
