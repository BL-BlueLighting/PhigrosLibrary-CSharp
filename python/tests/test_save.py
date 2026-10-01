"""存档解析 / 生成的往返测试。"""

from __future__ import annotations

import base64
import io
import zipfile

import pytest

from phigros_library import (
    ENTRY_GAME_KEY,
    ENTRY_GAME_PROGRESS,
    GameProgress,
    LevelRecord,
    SaveData,
    SongLevels,
    create_sample_save,
    parse_save,
    parse_save_file,
    write_save,
    write_save_file,
)
from phigros_library.crypto import decrypt_entry, encrypt_entry
from phigros_library.exceptions import PhigrosFormatError


def test_sample_save_round_trips_byte_exactly(difficulties) -> None:
    # 固定 IV 让加密结果可复现，因此"写出 → 解析 → 再写出"应当逐字节一致。
    written = write_save(create_sample_save(difficulties))

    assert write_save(parse_save(written)) == written


def test_parsed_model_matches_original(difficulties) -> None:
    original = create_sample_save(difficulties, seed=7)

    assert parse_save(write_save(original)).to_dict() == original.to_dict()


def test_keeps_entry_versions(difficulties) -> None:
    save = create_sample_save(difficulties)
    save.game_key.version = 1
    save.game_progress.version = 2

    parsed = parse_save(write_save(save))

    assert parsed.game_key.version == 1
    assert parsed.game_progress.version == 2


def test_older_version_drops_fields_added_later(difficulties) -> None:
    save = create_sample_save(difficulties)
    save.game_key.camellia_read_key = True
    save.game_progress.chapter8_passed = True

    save.game_key.version = 1       # v1 没有 camelliaReadKey
    save.game_progress.version = 2  # v2 还没有 chapter8 系列字段

    parsed = parse_save(write_save(save))

    assert parsed.game_key.camellia_read_key is False
    assert parsed.game_progress.chapter8_passed is False
    # 同一版本内的字段不受影响。
    assert parsed.game_key.lanota_read_keys == save.game_key.lanota_read_keys
    assert parsed.game_progress.random_version_unlocked == save.game_progress.random_version_unlocked


def test_new_model_writes_every_field_it_exposes() -> None:
    # 回归测试：版本号默认为 0 会让写回时一个字段段都不写，字段被静默丢弃。
    save = SaveData()
    save.game_progress.completed = "第一章"
    save.game_progress.money = [1, 2, 3, 4, 5]
    save.game_key.lanota_read_keys = 9

    parsed = parse_save(write_save(save))

    assert parsed.game_progress.completed == "第一章"
    assert parsed.game_progress.money == [1, 2, 3, 4, 5]
    assert parsed.game_key.lanota_read_keys == 9


def test_preserves_unknown_trailing_bytes_as_overflow(difficulties) -> None:
    zip_bytes = write_save(create_sample_save(difficulties))
    unknown = b"\xde\xad\xbe\xef"

    def extend(entry: bytes) -> bytes:
        version, plaintext = decrypt_entry(entry)
        return encrypt_entry(version, plaintext + unknown)

    extended = _replace_entry(zip_bytes, ENTRY_GAME_PROGRESS, extend)

    save = parse_save(extended)
    assert base64.b64decode(save.game_progress.overflow) == unknown

    # 写回时这几个字节必须原样出现在 gameProgress 条目末尾。
    rewritten = write_save(save)
    assert _get_entry(rewritten, ENTRY_GAME_PROGRESS) == _get_entry(extended, ENTRY_GAME_PROGRESS)
    assert rewritten == extended


def test_handles_save_without_records() -> None:
    parsed = parse_save(write_save(SaveData()))

    assert parsed.game_record == {}
    assert parsed.user.self_intro == ""
    assert parsed.settings.music_volume == 0.0


def test_keeps_songs_without_any_record() -> None:
    save = SaveData()
    save.game_record["Glaciaxion.SunsetRay"] = SongLevels()

    parsed = parse_save(write_save(save))

    assert parsed.game_record["Glaciaxion.SunsetRay"].is_empty


def test_preserves_record_order(difficulties) -> None:
    save = create_sample_save(difficulties, song_count=20)

    parsed = parse_save(write_save(save))

    assert list(parsed.game_record) == list(save.game_record)


def test_rejects_zip_without_required_entry(difficulties) -> None:
    zip_bytes = write_save(create_sample_save(difficulties))
    broken = _replace_entry(zip_bytes, ENTRY_GAME_KEY, lambda _: None)

    with pytest.raises(PhigrosFormatError, match=ENTRY_GAME_KEY):
        parse_save(broken)


def test_rejects_non_zip_data() -> None:
    with pytest.raises(PhigrosFormatError):
        parse_save(b"not a zip")


def test_file_api(tmp_path, difficulties) -> None:
    path = tmp_path / "sample.save"
    save = create_sample_save(difficulties, song_count=5)
    write_save_file(str(path), save)

    assert list(parse_save_file(str(path)).game_record) == list(save.game_record)


def test_level_records_survive_round_trip(difficulties) -> None:
    save = create_sample_save(difficulties, seed=13, song_count=30)
    parsed = parse_save(write_save(save))

    for song_id, levels in save.game_record.items():
        for level in range(4):
            expected = levels[level]
            actual = parsed.game_record[song_id][level]
            if expected is None:
                assert actual is None
                continue

            assert actual is not None
            assert (actual.score, actual.accuracy, actual.full_combo) == (
                expected.score,
                expected.accuracy,
                expected.full_combo,
            )


def test_level_record_all_perfect() -> None:
    assert LevelRecord(LevelRecord.PERFECT_SCORE, 100.0, True).is_all_perfect
    assert not LevelRecord(999_999, 99.9, True).is_all_perfect


def _get_entry(zip_bytes: bytes, name: str) -> bytes:
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as archive:
        return archive.read(name)


def _replace_entry(zip_bytes: bytes, name: str, transform) -> bytes:
    """重写 zip 中某个条目，``transform`` 返回 ``None`` 表示删除该条目。"""
    buffer = io.BytesIO()

    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as source:
        with zipfile.ZipFile(buffer, "w", zipfile.ZIP_STORED) as target:
            for info in source.infolist():
                data = source.read(info.filename)
                if info.filename == name:
                    data = transform(data)
                    if data is None:
                        continue
                target.writestr(info.filename, data)

    return buffer.getvalue()
