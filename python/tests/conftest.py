"""测试共用的夹具。"""

from __future__ import annotations

from pathlib import Path

import pytest

from phigros_library import DifficultyTable

#: 仓库根目录（含 resources 的那一层）。
REPO_ROOT = Path(__file__).resolve().parents[2]


@pytest.fixture(scope="session")
def difficulty_path() -> Path:
    """真实定数表的路径。"""
    return REPO_ROOT / "resources" / "difficulty.tsv"


@pytest.fixture(scope="session")
def difficulties(difficulty_path: Path) -> DifficultyTable:
    """仓库内真实的定数表。"""
    return DifficultyTable.load(str(difficulty_path))


@pytest.fixture(scope="session")
def mini_table() -> DifficultyTable:
    """一份便于手算的迷你定数表。"""
    return DifficultyTable.parse(
        "A.Artist\t1.0\t5.0\t10.0\t15.0\n"
        "B.Artist\t2.0\t6.0\t11.0\t16.0\n"
        "C.Artist\t3.0\t7.0\t12.0\t17.0\n"
        "D.Artist\t4.0\t8.0\t13.0\n"
    )
