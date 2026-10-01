"""B19 / RKS / 期望 ACC 的计算测试。"""

from __future__ import annotations

import pytest

from PhigrosScoreLibrary import (
    BEST_COUNT,
    RECORD_COUNT,
    LevelRecord,
    RksCalculator,
    SongDifficulty,
    SongLevels,
    compute_expected_accuracy,
    compute_rks,
    create_sample_save,
)
from PhigrosScoreLibrary.exceptions import PhigrosDataError


def make_records(*entries: tuple[str, SongDifficulty, float, bool]) -> dict[str, SongLevels]:
    """构造成绩表：``("A.Artist", SongDifficulty.IN, 90.0, False)``。"""
    records: dict[str, SongLevels] = {}

    for song_id, level, accuracy, full_combo in entries:
        levels = records.setdefault(song_id, SongLevels())
        score = LevelRecord.PERFECT_SCORE if accuracy >= 100 else int(accuracy * 10_000)
        levels[level] = LevelRecord(score, accuracy, full_combo)

    return records


@pytest.fixture
def calculator(mini_table) -> RksCalculator:
    return RksCalculator(mini_table)


@pytest.mark.parametrize(
    ("difficulty", "accuracy", "expected"),
    [
        (15.0, 100.0, 15.0),  # 满分时单曲 RKS 等于定数
        (15.0, 55.0, 0.0),    # 公式下界
        (16.0, 77.5, 4.0),    # (77.5-55)/45 = 0.5，平方后乘 16
        (10.0, 91.0, 6.4),    # (91-55)/45 = 0.8，平方后乘 10
    ],
)
def test_computes_single_rks(difficulty: float, accuracy: float, expected: float) -> None:
    assert compute_rks(difficulty, accuracy) == pytest.approx(expected, abs=1e-6)


@pytest.mark.parametrize(
    ("difficulty", "accuracy"), [(16.0, 90.0), (17.0, 99.5), (13.0, 55.0)]
)
def test_expected_accuracy_inverts_rks(difficulty: float, accuracy: float) -> None:
    rks = compute_rks(difficulty, accuracy)

    assert compute_expected_accuracy(difficulty, rks) == pytest.approx(accuracy, abs=1e-4)


def test_best19_counts_phi_twice_when_it_is_also_top_rated(calculator) -> None:
    records = make_records(
        ("A.Artist", SongDifficulty.IN, 100.0, True),   # 定数 10，满分
        ("B.Artist", SongDifficulty.AT, 100.0, True),   # 定数 16，满分，难度最高
        ("C.Artist", SongDifficulty.AT, 90.0, False),   # 定数 17，ACC 90
    )

    result = calculator.compute_best19(records)

    assert result.phi is not None
    assert result.phi.id == "B.Artist"
    assert result.phi.rks == pytest.approx(16.0)

    assert [entry.id for entry in result.best] == ["B.Artist", "C.Artist", "A.Artist"]

    # (φ 16 + B 16 + C 17×((90-55)/45)² + A 10) / 20
    expected = (16.0 + 16.0 + 17.0 * ((90.0 - 55.0) / 45.0) ** 2 + 10.0) / 20.0
    assert result.rks == pytest.approx(expected)


def test_phi_is_highest_difficulty_all_perfect_not_highest_rks(calculator) -> None:
    records = make_records(
        ("A.Artist", SongDifficulty.IN, 100.0, True),  # 定数 10 的 φ
        ("C.Artist", SongDifficulty.AT, 99.0, False),  # RKS 更高但不是满分
    )

    result = calculator.compute_best19(records)

    assert result.phi is not None
    assert result.phi.id == "A.Artist"
    # φ 之外的榜首应当是 C。
    assert result.best[0].id == "C.Artist"
    assert result.best[0].rks > result.phi.rks


def test_without_all_perfect_phi_is_none_and_counts_as_zero(calculator) -> None:
    records = make_records(("A.Artist", SongDifficulty.HD, 90.0, False))

    result = calculator.compute_best19(records)

    assert result.phi is None
    assert len(result.best) == 1
    assert result.rks == pytest.approx(compute_rks(5.0, 90.0) / 20.0)


def test_ignores_accuracy_below_threshold(calculator) -> None:
    records = make_records(("A.Artist", SongDifficulty.IN, 54.9, False))

    result = calculator.compute_best19(records)

    assert result.best == []
    assert result.phi is None
    assert result.rks == 0.0


def test_skips_at_difficulty_of_songs_without_it(calculator) -> None:
    # D 只有三个难度，写在 AT 槽位上的成绩不应被计入。
    records = make_records(("D.Artist", SongDifficulty.AT, 100.0, True))

    result = calculator.compute_best19(records)

    assert result.best == []
    assert result.phi is None


def test_throws_for_song_missing_from_difficulty_table(calculator) -> None:
    records = make_records(("不存在.曲目", SongDifficulty.IN, 100.0, True))

    with pytest.raises(PhigrosDataError, match="不存在.曲目"):
        calculator.compute_best19(records)


def test_counts_progress_per_difficulty(calculator) -> None:
    records = make_records(
        ("A.Artist", SongDifficulty.EZ, 90.0, False),
        ("A.Artist", SongDifficulty.HD, 95.0, True),
        ("A.Artist", SongDifficulty.IN, 100.0, True),
    )

    assert calculator.compute_progress(records) == [1, 0, 0, 1, 1, 0, 1, 1, 1, 0, 0, 0]


def test_expect_excludes_records_already_in_best19(calculator) -> None:
    records = make_records(("A.Artist", SongDifficulty.IN, 100.0, True))

    expect = calculator.compute_expect(records)

    # 已经打过的那个难度不应出现在建议列表里，其余难度都要给出目标 ACC。
    assert all(entry.level is not SongDifficulty.IN for entry in expect)
    assert any(entry.level is SongDifficulty.EZ for entry in expect)
    assert any(entry.level is SongDifficulty.AT for entry in expect)
    assert not any(entry.id == "D.Artist" and entry.level is SongDifficulty.AT for entry in expect)


def test_expect_targets_the_same_threshold_for_every_entry(difficulties) -> None:
    save = create_sample_save(difficulties, seed=11, song_count=40)
    calculator = RksCalculator(difficulties)

    expect = calculator.compute_expect(save.game_record)
    assert expect

    # 每条建议的 expect 都应当恰好把单曲 RKS 抬到同一个门槛值。
    target = compute_rks(expect[0].difficulty, expect[0].expect)
    for entry in expect:
        assert compute_rks(entry.difficulty, entry.expect) == pytest.approx(target, abs=1e-3)
        assert entry.rks <= target + 1e-3


def test_best19_on_sample_save_is_consistent_with_its_parts(difficulties) -> None:
    save = create_sample_save(difficulties, seed=3, song_count=50)
    calculator = RksCalculator(difficulties)

    result = calculator.compute_best19(save.game_record)

    assert len(result.best) <= BEST_COUNT
    assert result.phi is not None

    total = result.phi.rks + sum(entry.rks for entry in result.best)
    assert result.rks == pytest.approx(total / RECORD_COUNT)
    assert all(entry.difficulty > 0 for entry in result.best)


def test_best_entries_are_sorted_descending(difficulties) -> None:
    save = create_sample_save(difficulties, seed=5, song_count=60)

    result = RksCalculator(difficulties).compute_best19(save.game_record)

    rks_values = [entry.rks for entry in result.best]
    assert rks_values == sorted(rks_values, reverse=True)
