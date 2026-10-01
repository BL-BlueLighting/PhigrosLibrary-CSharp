"""云存档 zip 的解析与生成。

存档是一个 zip，包含 ``gameRecord`` / ``gameKey`` / ``gameProgress`` / ``user`` / ``settings``
五个条目，每个条目首字节为明文版本号，其余为 AES-256-CBC 密文（见 :mod:`.crypto`）。
"""

from __future__ import annotations

import base64
import io
import zipfile

from ._binary import ByteReader, ByteWriter
from .crypto import decrypt_entry, encrypt_entry
from .exceptions import PhigrosFormatError
from .models import GameKey, GameProgress, LevelRecord, SaveData, Settings, SongLevels, Summary, UserData
from .schemas import GAME_KEY, GAME_PROGRESS, SETTINGS, SUMMARY, USER, ObjectSchema
from .serialization import FieldMap, deserialize, deserialize_versioned, serialize, serialize_versioned

#: zip 中的条目名。
ENTRY_GAME_RECORD = "gameRecord"
ENTRY_GAME_KEY = "gameKey"
ENTRY_GAME_PROGRESS = "gameProgress"
ENTRY_USER = "user"
ENTRY_SETTINGS = "settings"

#: 结构固定、不随版本变化的条目所使用的版本号。
_FIXED_ENTRY_VERSION = 1

#: ``gameRecord`` 的键在磁盘上携带的后缀。
_KEY_SUFFIX = b".0"

#: :meth:`ByteKeyMapCodec.read` / :meth:`ByteKeyMapCodec.write` 中值的个数。
KEY_MAP_VALUE_COUNT = 5


class GameRecordCodec:
    """``gameRecord`` 条目的读写。

    磁盘结构：先一个变长整数表示曲目数量，随后每首曲目为::

        +----------------------+------------+------------------------------------------+
        | 变长长度 + 曲目id.0  | 块长度(1B) | exist(1B) + fc(1B) + [分数(4B) + acc(4B)]* |
        +----------------------+------------+------------------------------------------+

    ``exist`` 的第 n 位表示第 n 个难度有成绩，``fc`` 的第 n 位表示该难度取得 Full Combo；
    只有被 ``exist`` 标记的难度才会写入 8 字节的成绩。块长度用于跳过本实现不认识的尾部字段。
    """

    @staticmethod
    def read(reader: ByteReader) -> dict[str, SongLevels]:
        count = reader.read_varshort()
        records: dict[str, SongLevels] = {}

        for _ in range(count):
            song_id = reader.read_string(suffix_length=len(_KEY_SUFFIX))

            block_length = reader.read_u8()
            next_position = reader.position + block_length
            if next_position > reader.length:
                raise PhigrosFormatError(
                    f"曲目 '{song_id}' 的块长度 {block_length} 越界，数据可能已损坏。"
                )

            exist = reader.read_u8()
            full_combo = reader.read_u8()

            levels = SongLevels()
            for level in range(len(levels.levels)):
                if not (exist >> level) & 1:
                    continue

                score = reader.read_u32()
                accuracy = reader.read_f32()
                levels[level] = LevelRecord(score, accuracy, bool((full_combo >> level) & 1))

            reader.position = next_position
            records[song_id] = levels

        return records

    @staticmethod
    def write(writer: ByteWriter, records: dict[str, SongLevels]) -> None:
        writer.write_varshort(len(records))

        for song_id, levels in records.items():
            writer.write_string(song_id, _KEY_SUFFIX)

            exist = 0
            full_combo = 0
            block_length = 2  # exist + fc

            for level, record in enumerate(levels.levels):
                if record is None:
                    continue

                exist |= 1 << level
                block_length += 8  # 分数 + acc
                if record.full_combo:
                    full_combo |= 1 << level

            writer.write_u8(block_length)
            writer.write_u8(exist)
            writer.write_u8(full_combo)

            for record in levels.levels:
                if record is None:
                    continue

                writer.write_u32(record.score)
                writer.write_f32(record.accuracy)


class ByteKeyMapCodec:
    """``gameKey`` 前半段的键值表读写：值是 5 个整数，磁盘上只保存非零项。

    磁盘结构：先一个变长整数表示条目数量，随后每条为::

        +-----------------+------------+---------------------------------------+
        | 变长长度 + 键名 | 块长度(1B) | exist(1B) + 只写非零项的各 1 字节值  |
        +-----------------+------------+---------------------------------------+
    """

    @staticmethod
    def read(reader: ByteReader) -> dict[str, list[int]]:
        count = reader.read_varshort()
        result: dict[str, list[int]] = {}

        for _ in range(count):
            key = reader.read_string()

            block_length = reader.read_u8()
            next_position = reader.position + block_length
            if next_position > reader.length:
                raise PhigrosFormatError(f"键 '{key}' 的块长度 {block_length} 越界，数据可能已损坏。")

            exist = reader.read_u8()
            values = [0] * KEY_MAP_VALUE_COUNT
            for index in range(KEY_MAP_VALUE_COUNT):
                if (exist >> index) & 1:
                    values[index] = reader.read_u8()

            reader.position = next_position
            result[key] = values

        return result

    @staticmethod
    def write(writer: ByteWriter, map_: dict[str, list[int]]) -> None:
        writer.write_varshort(len(map_))

        for key, values in map_.items():
            if len(values) != KEY_MAP_VALUE_COUNT:
                raise PhigrosFormatError(
                    f"键 '{key}' 需要 {KEY_MAP_VALUE_COUNT} 个值，实际为 {len(values)} 个。"
                )

            writer.write_string(key)

            exist = 0
            for index, value in enumerate(values):
                if value:
                    exist |= 1 << index

            writer.write_u8(exist.bit_count() + 1)  # exist 自身占 1 字节
            writer.write_u8(exist)

            for value in values:
                if value:
                    writer.write_u8(value)


def _read_entry(archive: zipfile.ZipFile, name: str) -> bytes:
    try:
        return archive.read(name)
    except KeyError as exc:
        raise PhigrosFormatError(f"存档中缺少条目 '{name}'。") from exc


def _serialize_nodes(fields: FieldMap, schemas: tuple[ObjectSchema, ...], version: int) -> bytes:
    writer = ByteWriter()
    serialize_versioned(writer, fields, schemas, version)
    return writer.to_bytes()


def _read_overflow(reader: ByteReader) -> str | None:
    if reader.remaining <= 0:
        return None
    return base64.b64encode(reader.remaining_bytes).decode("ascii")


def _write_overflow(writer: ByteWriter, overflow: str | None) -> None:
    if not overflow:
        return
    writer.write_bytes(base64.b64decode(overflow))


def parse(zip_bytes: bytes) -> SaveData:
    """解析一份存档 zip。"""
    try:
        archive = zipfile.ZipFile(io.BytesIO(zip_bytes))
    except zipfile.BadZipFile as exc:
        raise PhigrosFormatError("存档不是合法的 zip 数据。") from exc

    with archive:
        save = SaveData()

        _, plaintext = decrypt_entry(_read_entry(archive, ENTRY_GAME_RECORD))
        save.game_record = GameRecordCodec.read(ByteReader(plaintext))

        version, plaintext = decrypt_entry(_read_entry(archive, ENTRY_GAME_KEY))
        reader = ByteReader(plaintext)
        game_key = GameKey(version=version)
        game_key.map = ByteKeyMapCodec.read(reader)
        fields = deserialize_versioned(reader, GAME_KEY, version)
        game_key.lanota_read_keys = _as_int(fields.get("lanotaReadKeys"))
        game_key.camellia_read_key = bool(fields.get("camelliaReadKey", False))
        game_key.overflow = _read_overflow(reader)
        save.game_key = game_key

        version, plaintext = decrypt_entry(_read_entry(archive, ENTRY_GAME_PROGRESS))
        reader = ByteReader(plaintext)
        fields = deserialize_versioned(reader, GAME_PROGRESS, version)
        save.game_progress = _game_progress_from_fields(fields, version)
        save.game_progress.overflow = _read_overflow(reader)

        _, plaintext = decrypt_entry(_read_entry(archive, ENTRY_USER))
        fields = deserialize(ByteReader(plaintext), USER)
        save.user = UserData(
            show_player_id=bool(fields.get("showPlayerId", False)),
            self_intro=_as_str(fields.get("selfIntro")),
            avatar=_as_str(fields.get("avatar")),
            background=_as_str(fields.get("background")),
        )

        _, plaintext = decrypt_entry(_read_entry(archive, ENTRY_SETTINGS))
        fields = deserialize(ByteReader(plaintext), SETTINGS)
        save.settings = _settings_from_fields(fields)

        return save


def parse_file(path: str) -> SaveData:
    """解析本地存档文件。"""
    with open(path, "rb") as handle:
        return parse(handle.read())


def write(save: SaveData) -> bytes:
    """生成一份存档 zip。"""
    buffer = io.BytesIO()

    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_STORED) as archive:
        game_key_writer = ByteWriter()
        ByteKeyMapCodec.write(game_key_writer, save.game_key.map)
        game_key_writer.write_bytes(
            _serialize_nodes(_game_key_fields(save.game_key), GAME_KEY, save.game_key.version)
        )
        _write_overflow(game_key_writer, save.game_key.overflow)
        archive.writestr(ENTRY_GAME_KEY, encrypt_entry(save.game_key.version, game_key_writer.to_bytes()))

        progress_writer = ByteWriter()
        progress_writer.write_bytes(
            _serialize_nodes(
                _game_progress_fields(save.game_progress), GAME_PROGRESS, save.game_progress.version
            )
        )
        _write_overflow(progress_writer, save.game_progress.overflow)
        archive.writestr(
            ENTRY_GAME_PROGRESS,
            encrypt_entry(save.game_progress.version, progress_writer.to_bytes()),
        )

        record_writer = ByteWriter()
        GameRecordCodec.write(record_writer, save.game_record)
        archive.writestr(ENTRY_GAME_RECORD, encrypt_entry(_FIXED_ENTRY_VERSION, record_writer.to_bytes()))

        settings_writer = ByteWriter()
        serialize(settings_writer, _settings_fields(save.settings), SETTINGS)
        archive.writestr(ENTRY_SETTINGS, encrypt_entry(_FIXED_ENTRY_VERSION, settings_writer.to_bytes()))

        user_writer = ByteWriter()
        serialize(user_writer, _user_fields(save.user), USER)
        archive.writestr(ENTRY_USER, encrypt_entry(_FIXED_ENTRY_VERSION, user_writer.to_bytes()))

    return buffer.getvalue()


def write_file(path: str, save: SaveData) -> None:
    """把存档写入本地文件。"""
    with open(path, "wb") as handle:
        handle.write(write(save))


def parse_summary(base64_text: str) -> Summary:
    """解析 LeanCloud 返回的 base64 形式的 summary。"""
    try:
        payload = base64.b64decode(base64_text, validate=True)
    except (ValueError, TypeError) as exc:
        raise PhigrosFormatError("summary 不是合法的 base64 字符串。") from exc

    fields = deserialize(ByteReader(payload), SUMMARY)
    progress = fields.get("progress") or [0] * 12

    return Summary(
        save_version=_as_int(fields.get("saveVersion")),
        challenge_mode_rank=_as_int(fields.get("challengeModeRank")),
        ranking_score=_as_float(fields.get("rankingScore")),
        game_version=_as_int(fields.get("gameVersion")),
        avatar=_as_str(fields.get("avatar")),
        progress=[int(value) for value in progress],
    )


def write_summary(summary: Summary) -> str:
    """把 summary 序列化为 LeanCloud 需要的 base64 字符串。"""
    fields = FieldMap(
        {
            "saveVersion": summary.save_version,
            "challengeModeRank": summary.challenge_mode_rank,
            "rankingScore": summary.ranking_score,
            "gameVersion": summary.game_version,
            "avatar": summary.avatar,
            "progress": list(summary.progress),
        }
    )

    writer = ByteWriter()
    serialize(writer, fields, SUMMARY)
    return base64.b64encode(writer.to_bytes()).decode("ascii")


def _game_key_fields(game_key: GameKey) -> FieldMap:
    return FieldMap(
        {
            "lanotaReadKeys": game_key.lanota_read_keys,
            "camelliaReadKey": game_key.camellia_read_key,
        }
    )


def _game_progress_from_fields(fields: FieldMap, version: int) -> GameProgress:
    return GameProgress(
        version=version,
        is_first_run=bool(fields.get("isFirstRun", False)),
        legacy_chapter_finished=bool(fields.get("legacyChapterFinished", False)),
        already_show_collection_tip=bool(fields.get("alreadyShowCollectionTip", False)),
        already_show_auto_unlock_in_tip=bool(fields.get("alreadyShowAutoUnlockINTip", False)),
        completed=_as_str(fields.get("completed")),
        song_update_info=_as_int(fields.get("songUpdateInfo")),
        challenge_mode_rank=_as_int(fields.get("challengeModeRank")),
        money=[int(value) for value in (fields.get("money") or [0] * 5)],
        unlock_flag_of_spasmodic=_as_int(fields.get("unlockFlagOfSpasmodic")),
        unlock_flag_of_igallta=_as_int(fields.get("unlockFlagOfIgallta")),
        unlock_flag_of_rrharil=_as_int(fields.get("unlockFlagOfRrharil")),
        flag_of_song_record_key=_as_int(fields.get("flagOfSongRecordKey")),
        random_version_unlocked=_as_int(fields.get("randomVersionUnlocked")),
        chapter8_unlock_begin=bool(fields.get("chapter8UnlockBegin", False)),
        chapter8_unlock_second_phase=bool(fields.get("chapter8UnlockSecondPhase", False)),
        chapter8_passed=bool(fields.get("chapter8Passed", False)),
        chapter8_song_unlocked=_as_int(fields.get("chapter8SongUnlocked")),
    )


def _game_progress_fields(progress: GameProgress) -> FieldMap:
    return FieldMap(
        {
            "isFirstRun": progress.is_first_run,
            "legacyChapterFinished": progress.legacy_chapter_finished,
            "alreadyShowCollectionTip": progress.already_show_collection_tip,
            "alreadyShowAutoUnlockINTip": progress.already_show_auto_unlock_in_tip,
            "completed": progress.completed,
            "songUpdateInfo": progress.song_update_info,
            "challengeModeRank": progress.challenge_mode_rank,
            "money": list(progress.money),
            "unlockFlagOfSpasmodic": progress.unlock_flag_of_spasmodic,
            "unlockFlagOfIgallta": progress.unlock_flag_of_igallta,
            "unlockFlagOfRrharil": progress.unlock_flag_of_rrharil,
            "flagOfSongRecordKey": progress.flag_of_song_record_key,
            "randomVersionUnlocked": progress.random_version_unlocked,
            "chapter8UnlockBegin": progress.chapter8_unlock_begin,
            "chapter8UnlockSecondPhase": progress.chapter8_unlock_second_phase,
            "chapter8Passed": progress.chapter8_passed,
            "chapter8SongUnlocked": progress.chapter8_song_unlocked,
        }
    )


def _settings_from_fields(fields: FieldMap) -> Settings:
    return Settings(
        chord_support=bool(fields.get("chordSupport", False)),
        fc_ap_indicator=bool(fields.get("fcAPIndicator", False)),
        enable_hit_sound=bool(fields.get("enableHitSound", False)),
        low_resolution_mode=bool(fields.get("lowResolutionMode", False)),
        device_name=_as_str(fields.get("deviceName")),
        bright=_as_float(fields.get("bright")),
        music_volume=_as_float(fields.get("musicVolume")),
        effect_volume=_as_float(fields.get("effectVolume")),
        hit_sound_volume=_as_float(fields.get("hitSoundVolume")),
        sound_offset=_as_float(fields.get("soundOffset")),
        note_scale=_as_float(fields.get("noteScale")),
    )


def _settings_fields(settings: Settings) -> FieldMap:
    return FieldMap(
        {
            "chordSupport": settings.chord_support,
            "fcAPIndicator": settings.fc_ap_indicator,
            "enableHitSound": settings.enable_hit_sound,
            "lowResolutionMode": settings.low_resolution_mode,
            "deviceName": settings.device_name,
            "bright": settings.bright,
            "musicVolume": settings.music_volume,
            "effectVolume": settings.effect_volume,
            "hitSoundVolume": settings.hit_sound_volume,
            "soundOffset": settings.sound_offset,
            "noteScale": settings.note_scale,
        }
    )


def _user_fields(user: UserData) -> FieldMap:
    return FieldMap(
        {
            "showPlayerId": user.show_player_id,
            "selfIntro": user.self_intro,
            "avatar": user.avatar,
            "background": user.background,
        }
    )


def _as_int(value: object) -> int:
    return int(value) if isinstance(value, (int, float)) else 0


def _as_float(value: object) -> float:
    return float(value) if isinstance(value, (int, float)) else 0.0


def _as_str(value: object) -> str:
    return value if isinstance(value, str) else ""
