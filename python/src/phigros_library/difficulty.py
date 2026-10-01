"""定数表：曲目 id 到 EZ / HD / IN / AT 四个难度定数的映射。

数据来源为 ``difficulty.tsv``（见仓库 ``resources/NOTICE.md``）。
只有三个难度的曲目，AT 定数记为 0。
"""

from __future__ import annotations

from importlib.resources import files
from typing import IO, Iterable, Iterator

from .exceptions import PhigrosDataError
from .models import LEVEL_COUNT, SongDifficulty

#: 随包分发的定数表在包内的相对路径。
_BUNDLED_DIFFICULTY = ("data", "difficulty.tsv")


def bundled_difficulty_text() -> str:
    """读取随包分发的 ``difficulty.tsv`` 内容。

    这份数据与仓库根目录的 ``resources/difficulty.tsv`` 保持一致（有测试把关），
    但它随游戏版本更新，需要最新数据时请自行替换。
    """
    resource = files(__package__).joinpath(*_BUNDLED_DIFFICULTY)
    return resource.read_text(encoding="utf-8")


class DifficultyTable:
    """曲目定数表。"""

    __slots__ = ("_songs",)

    def __init__(self, songs: dict[str, list[float]]) -> None:
        self._songs = dict(songs)

    @classmethod
    def load(cls, path: str) -> DifficultyTable:
        """从 tsv 文件加载定数表。"""
        with open(path, encoding="utf-8") as handle:
            return cls.parse(handle)

    @classmethod
    def bundled(cls) -> DifficultyTable:
        """加载随包分发的定数表，省去自己去找 ``difficulty.tsv``::

            from phigros_library import DifficultyTable, RksCalculator

            calculator = RksCalculator(DifficultyTable.bundled())
        """
        return cls.parse(bundled_difficulty_text())

    @classmethod
    def parse(cls, source: str | Iterable[str] | IO[str]) -> DifficultyTable:
        """从文本内容或行序列解析定数表。

        每行格式为 ``曲目id\\tEZ\\tHD\\tIN[\\tAT]``。
        """
        if isinstance(source, str):
            lines: Iterable[str] = source.splitlines()
        else:
            lines = source

        songs: dict[str, list[float]] = {}
        for line_number, line in enumerate(lines, start=1):
            line = line.rstrip("\n")
            if not line.strip():
                continue

            parts = line.split("\t")
            if len(parts) < 2:
                raise PhigrosDataError(f"定数表第 {line_number} 行格式不正确：{line}")

            levels = [0.0] * LEVEL_COUNT
            for index, text in enumerate(parts[1 : LEVEL_COUNT + 1]):
                try:
                    levels[index] = float(text)
                except ValueError as exc:
                    raise PhigrosDataError(
                        f"定数表第 {line_number} 行的 '{text}' 不是合法数字。"
                    ) from exc

            songs[parts[0]] = levels

        if not songs:
            raise PhigrosDataError("定数表为空。")

        return cls(songs)

    def __len__(self) -> int:
        return len(self._songs)

    def __contains__(self, song_id: object) -> bool:
        return song_id in self._songs

    def __iter__(self) -> Iterator[str]:
        return iter(self._songs)

    def __getitem__(self, song_id: str) -> list[float]:
        """取某个曲目的四个难度定数，不存在时抛出 :class:`PhigrosDataError`。"""
        try:
            return list(self._songs[song_id])
        except KeyError as exc:
            raise PhigrosDataError(
                f"定数表中没有曲目 '{song_id}'，请更新 difficulty.tsv（见 resources/NOTICE.md）。"
            ) from exc

    @property
    def songs(self) -> dict[str, list[float]]:
        """所有曲目及其定数。"""
        return {song_id: list(levels) for song_id, levels in self._songs.items()}

    def get(self, song_id: str) -> list[float] | None:
        """取某个曲目的定数，不存在时返回 ``None``。"""
        levels = self._songs.get(song_id)
        return list(levels) if levels else None

    def difficulty_of(self, song_id: str, level: int | SongDifficulty) -> float:
        """取某曲某难度的定数。"""
        return self[song_id][int(level)]
