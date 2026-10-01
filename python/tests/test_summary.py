"""Summary 的解析与生成测试。"""

from __future__ import annotations

import base64

import pytest

from PhigrosScoreLibrary import Summary, parse_summary, write_summary
from PhigrosScoreLibrary._binary import ByteWriter
from PhigrosScoreLibrary.exceptions import PhigrosFormatError


def test_parses_known_layout() -> None:
    # 手工按磁盘顺序拼一份负载，验证字段顺序与各类型的编码。
    writer = ByteWriter()
    writer.write_u8(3)          # saveVersion: u8
    writer.write_u16(300)       # challengeModeRank: u16
    writer.write_f32(15.5)      # rankingScore: f32
    writer.write_varshort(100)  # gameVersion: varshort（100 ≥ 128，占两字节）
    writer.write_string("abc")  # avatar: 变长字符串
    for index in range(12):     # progress: 12 × u16
        writer.write_u16(index)

    summary = parse_summary(base64.b64encode(writer.to_bytes()).decode("ascii"))

    assert summary.save_version == 3
    assert summary.challenge_mode_rank == 300
    assert summary.ranking_score == 15.5
    assert summary.game_version == 100
    assert summary.avatar == "abc"
    assert summary.progress == list(range(12))


def test_round_trips() -> None:
    summary = Summary(
        save_version=4,
        challenge_mode_rank=1234,
        ranking_score=16.7234,
        game_version=87,
        avatar="Introduction.0",
        progress=list(range(12)),
    )

    parsed = parse_summary(write_summary(summary))

    assert parsed.save_version == summary.save_version
    assert parsed.challenge_mode_rank == summary.challenge_mode_rank
    # rankingScore 存的是单精度浮点，比较时留出精度余量。
    assert parsed.ranking_score == pytest.approx(summary.ranking_score, abs=1e-6)
    assert parsed.game_version == summary.game_version
    assert parsed.avatar == summary.avatar
    assert parsed.progress == summary.progress


def test_rejects_invalid_base64() -> None:
    with pytest.raises(PhigrosFormatError):
        parse_summary("这显然不是 base64!!")


def test_rejects_truncated_payload() -> None:
    writer = ByteWriter()
    writer.write_u8(1)
    writer.write_u16(0)

    with pytest.raises(PhigrosFormatError):
        parse_summary(base64.b64encode(writer.to_bytes()).decode("ascii"))


def test_to_dict_omits_absent_cloud_metadata() -> None:
    result = Summary(save_version=1, avatar="a").to_dict()

    assert result["saveVersion"] == 1
    assert "objectId" not in result
    assert "updatedAt" not in result
