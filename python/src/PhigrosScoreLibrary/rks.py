"""B19 / RKS / 期望 ACC 的计算。

单曲 RKS 的公式为 ``定数 × ((ACC - 55) / 45)²``，ACC 为 100 时恰好等于定数。

总 RKS 取 19 首单曲 RKS 最高的成绩，加上一首难度最高的 φ（满分）曲目，共 20 项取平均。
没有满分成绩时 φ 一项按 0 计入。
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Iterator, Mapping

from .difficulty import DifficultyTable
from .models import B19Result, BestEntry, ExpectEntry, LEVEL_COUNT, LevelRecord, SongDifficulty, SongLevels

#: 成绩计入 B19 所需的最低 ACC。
MINIMUM_ACCURACY = 55.0

#: B19 中除 φ 外的成绩数量。
BEST_COUNT = 19

#: 组成总 RKS 的成绩总数：19 首最佳 + 1 首 φ。
RECORD_COUNT = BEST_COUNT + 1

_ACCURACY_FLOOR = 55.0
_ACCURACY_SPAN = 45.0


def compute_rks(difficulty: float, accuracy: float) -> float:
    """计算单曲 RKS。"""
    factor = (accuracy - _ACCURACY_FLOOR) / _ACCURACY_SPAN
    return factor * factor * difficulty


def compute_expected_accuracy(difficulty: float, target_rks: float) -> float:
    """计算达到目标单曲 RKS 所需的 ACC，即 :func:`compute_rks` 的反函数。"""
    return math.sqrt(target_rks / difficulty) * _ACCURACY_SPAN + _ACCURACY_FLOOR


@dataclass(slots=True)
class _Candidate:
    """一个"曲目 × 难度"的候选项。"""

    song_id: str
    level: SongDifficulty
    difficulty: float
    record: LevelRecord | None


class RksCalculator:
    """绑定一份定数表的计算器。"""

    __slots__ = ("difficulties",)

    def __init__(self, difficulties: DifficultyTable) -> None:
        self.difficulties = difficulties

    def compute_best19(self, game_record: Mapping[str, SongLevels]) -> B19Result:
        """计算 B19。"""
        phi, best = self._select(game_record)

        total = (phi.rks if phi else 0.0) + sum(entry.rks for entry in best)
        return B19Result(rks=total / RECORD_COUNT, phi=phi, best=best)

    def compute_expect(self, game_record: Mapping[str, SongLevels]) -> list[ExpectEntry]:
        """计算把哪些成绩打进 B19 需要多少 ACC。"""
        _, best = self._select(game_record)

        # 门槛取第 19 名的单曲 RKS：越过它才能挤进 B19。
        threshold = best[BEST_COUNT - 1].rks if len(best) >= BEST_COUNT else 0.0

        result: list[ExpectEntry] = []
        for candidate in self._enumerate(game_record):
            if candidate.difficulty < threshold:
                continue

            accuracy = candidate.record.accuracy if candidate.record else 0.0
            rks = compute_rks(candidate.difficulty, accuracy) if accuracy > MINIMUM_ACCURACY else 0.0
            if rks > threshold:
                continue

            result.append(
                ExpectEntry(
                    id=candidate.song_id,
                    level=candidate.level,
                    difficulty=candidate.difficulty,
                    rks=rks,
                    accuracy=accuracy,
                    expect=compute_expected_accuracy(candidate.difficulty, threshold),
                )
            )

        return result

    def compute_progress(self, game_record: Mapping[str, SongLevels]) -> list[int]:
        """统计成绩表：每三个一组对应 EZ / HD / IN / AT，依次为已游玩数、FC 数、AP 数。"""
        progress = [0] * (LEVEL_COUNT * 3)

        for candidate in self._enumerate(game_record):
            record = candidate.record
            if record is None or record.accuracy == 0:
                continue

            offset = 3 * int(candidate.level)
            progress[offset] += 1
            if record.is_all_perfect:
                progress[offset + 1] += 1
                progress[offset + 2] += 1
            elif record.full_combo:
                progress[offset + 1] += 1

        return progress

    def _select(self, game_record: Mapping[str, SongLevels]) -> tuple[BestEntry | None, list[BestEntry]]:
        """选出 φ 与 19 首最佳成绩。返回的列表按单曲 RKS 从高到低排列。"""
        phi: BestEntry | None = None
        best: list[BestEntry | None] = [None] * BEST_COUNT
        lowest = 0
        lowest_rks = 0.0

        for candidate in self._enumerate(game_record):
            record = candidate.record
            accuracy = record.accuracy if record else 0.0
            if accuracy < MINIMUM_ACCURACY:
                continue

            # φ 取难度最高的满分成绩；单曲 RKS 恒不超过定数，先用定数做剪枝。
            is_phi = bool(record and record.is_all_perfect) and candidate.difficulty > (
                phi.difficulty if phi else 0.0
            )
            if not is_phi and candidate.difficulty <= lowest_rks:
                continue

            rks = compute_rks(candidate.difficulty, accuracy)
            if is_phi:
                phi = self._make_entry(candidate, rks)

            if rks > lowest_rks:
                best[lowest] = self._make_entry(candidate, rks)
                lowest = self._index_of_lowest(best)
                lowest_rks = best[lowest].rks if best[lowest] else 0.0

        entries = sorted(
            (entry for entry in best if entry is not None),
            key=lambda entry: entry.rks,
            reverse=True,
        )
        return phi, entries

    @staticmethod
    def _index_of_lowest(entries: list[BestEntry | None]) -> int:
        lowest = 0
        for index in range(1, len(entries)):
            current = entries[index].rks if entries[index] else 0.0
            reference = entries[lowest].rks if entries[lowest] else 0.0
            if current < reference:
                lowest = index

        return lowest

    @staticmethod
    def _make_entry(candidate: _Candidate, rks: float) -> BestEntry:
        record = candidate.record
        assert record is not None
        return BestEntry(
            id=candidate.song_id,
            level=candidate.level,
            difficulty=candidate.difficulty,
            rks=rks,
            score=record.score,
            accuracy=record.accuracy,
            full_combo=record.full_combo,
        )

    def _enumerate(self, game_record: Mapping[str, SongLevels]) -> Iterator[_Candidate]:
        """按存档顺序枚举所有"曲目 × 难度"，只展开该曲实际拥有的难度。"""
        for song_id, levels in game_record.items():
            difficulties = self.difficulties[song_id]
            for level in range(LEVEL_COUNT):
                if level == int(SongDifficulty.AT) and difficulties[level] == 0:
                    break
                yield _Candidate(song_id, SongDifficulty(level), difficulties[level], levels[level])
