"""定数表的解析测试。"""

from __future__ import annotations

from pathlib import Path

import pytest

from PhigrosScoreLibrary import DifficultyTable, SongDifficulty, bundled_difficulty_text
from PhigrosScoreLibrary.exceptions import PhigrosDataError

#: 随包分发的副本在源码树中的位置。
BUNDLED_DIFFICULTY = (
    Path(__file__).resolve().parents[1] / "src" / "PhigrosScoreLibrary" / "data" / "difficulty.tsv"
)


def test_parses_four_levels() -> None:
    table = DifficultyTable.parse("Song.Artist\t1.0\t6.5\t12.6\t15.7")

    assert len(table) == 1
    assert table["Song.Artist"] == [1.0, 6.5, 12.6, 15.7]


def test_missing_at_level_becomes_zero() -> None:
    table = DifficultyTable.parse("Song.Artist\t1.0\t6.5\t12.6")

    assert table["Song.Artist"][SongDifficulty.AT] == 0.0


def test_ignores_blank_lines() -> None:
    table = DifficultyTable.parse("\nA\t1\t2\t3\n\nB\t4\t5\t6\n")

    assert len(table) == 2


def test_accepts_line_iterables() -> None:
    table = DifficultyTable.parse(["A\t1\t2\t3", "B\t4\t5\t6"])

    assert len(table) == 2


def test_rejects_malformed_lines() -> None:
    with pytest.raises(PhigrosDataError):
        DifficultyTable.parse("只有一列")

    with pytest.raises(PhigrosDataError):
        DifficultyTable.parse("A\t不是数字\t2\t3")


def test_rejects_empty_table() -> None:
    with pytest.raises(PhigrosDataError):
        DifficultyTable.parse("\n\n")


def test_throws_for_unknown_song() -> None:
    table = DifficultyTable.parse("A\t1\t2\t3")

    with pytest.raises(PhigrosDataError):
        _ = table["不存在"]

    assert table.get("不存在") is None


def test_loads_real_difficulty_table(difficulties: DifficultyTable) -> None:
    assert len(difficulties) > 200
    assert difficulties["Glaciaxion.SunsetRay"] == [1.0, 6.5, 12.6, 0.0]
    assert difficulties["Credits.Frums"] == [4.5, 10.4, 13.6, 15.7]
    assert difficulties.difficulty_of("Credits.Frums", SongDifficulty.AT) == 15.7


def test_songs_property_is_a_copy(difficulties: DifficultyTable) -> None:
    songs = difficulties.songs
    songs["Glaciaxion.SunsetRay"][0] = 99.0

    assert difficulties["Glaciaxion.SunsetRay"][0] == 1.0


def test_bundled_copy_matches_repository_resources(difficulty_path: Path) -> None:
    # 包内副本必须和仓库根目录的资源保持一致，否则发布出去的就是旧数据。
    assert BUNDLED_DIFFICULTY.read_bytes() == difficulty_path.read_bytes()


def test_bundled_table_loads(difficulties: DifficultyTable) -> None:
    bundled = DifficultyTable.bundled()

    assert len(bundled) == len(difficulties)
    assert bundled["Glaciaxion.SunsetRay"] == difficulties["Glaciaxion.SunsetRay"]
    assert bundled["Credits.Frums"] == difficulties["Credits.Frums"]


def test_bundled_text_is_utf8_tsv() -> None:
    text = bundled_difficulty_text()

    assert text.startswith("Glaciaxion.SunsetRay\t1.0\t6.5\t12.6")
