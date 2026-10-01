using System.IO.Compression;
using PhigrosLibrary.Binary;
using PhigrosLibrary.Crypto;
using PhigrosLibrary.Models;
using PhigrosLibrary.Serialization;

namespace PhigrosLibrary;

/// <summary>
/// 云存档 zip 的解析与生成。
/// </summary>
/// <remarks>
/// 存档是一个 zip，包含 <c>gameRecord</c> / <c>gameKey</c> / <c>gameProgress</c> / <c>user</c> / <c>settings</c>
/// 五个条目，每个条目首字节为明文版本号，其余为 AES-256-CBC 密文（见 <see cref="PhigrosCipher"/>）。
/// </remarks>
public static class SaveCodec
{
    public const string EntryGameRecord = "gameRecord";
    public const string EntryGameKey = "gameKey";
    public const string EntryGameProgress = "gameProgress";
    public const string EntryUser = "user";
    public const string EntrySettings = "settings";

    /// <summary>结构固定、不随版本变化的条目所使用的版本号。</summary>
    private const int FixedEntryVersion = 1;

    /// <summary>解析一份存档 zip。</summary>
    public static SaveData Parse(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        try
        {
            return ParseCore(zipBytes);
        }
        catch (InvalidDataException ex)
        {
            throw new PhigrosFormatException("存档不是合法的 zip 数据。", ex);
        }
    }

    private static SaveData ParseCore(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var save = new SaveData();

        var gameRecordReader = new ByteReader(PhigrosCipher.DecryptEntry(ReadEntry(archive, EntryGameRecord)).Plaintext);
        save.GameRecord = GameRecordCodec.Read(gameRecordReader);

        var gameKey = PhigrosCipher.DecryptEntry(ReadEntry(archive, EntryGameKey));
        var gameKeyReader = new ByteReader(gameKey.Plaintext);
        save.GameKey.Version = gameKey.Version;
        save.GameKey.Map = ByteKeyMapCodec.Read(gameKeyReader);
        var gameKeyFields = BinarySerializer.DeserializeVersioned(
            gameKeyReader, PhigrosSchemas.GameKey, gameKey.Version);
        save.GameKey.LanotaReadKeys = GetInt(gameKeyFields, "lanotaReadKeys");
        save.GameKey.CamelliaReadKey = GetBool(gameKeyFields, "camelliaReadKey");
        save.GameKey.Overflow = ReadOverflow(gameKeyReader);

        var gameProgress = PhigrosCipher.DecryptEntry(ReadEntry(archive, EntryGameProgress));
        var gameProgressReader = new ByteReader(gameProgress.Plaintext);
        save.GameProgress.Version = gameProgress.Version;
        var progressFields = BinarySerializer.DeserializeVersioned(
            gameProgressReader, PhigrosSchemas.GameProgress, gameProgress.Version);
        ApplyGameProgress(save.GameProgress, progressFields);
        save.GameProgress.Overflow = ReadOverflow(gameProgressReader);

        var userReader = new ByteReader(PhigrosCipher.DecryptEntry(ReadEntry(archive, EntryUser)).Plaintext);
        var userFields = BinarySerializer.Deserialize(userReader, PhigrosSchemas.User);
        save.User.ShowPlayerId = GetBool(userFields, "showPlayerId");
        save.User.SelfIntro = GetString(userFields, "selfIntro");
        save.User.Avatar = GetString(userFields, "avatar");
        save.User.Background = GetString(userFields, "background");

        var settingsReader = new ByteReader(PhigrosCipher.DecryptEntry(ReadEntry(archive, EntrySettings)).Plaintext);
        var settingsFields = BinarySerializer.Deserialize(settingsReader, PhigrosSchemas.Settings);
        ApplySettings(save.Settings, settingsFields);

        return save;
    }

    /// <summary>解析本地存档文件。</summary>
    public static SaveData ParseFile(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>生成一份存档 zip。</summary>
    public static byte[] Write(SaveData save)
    {
        ArgumentNullException.ThrowIfNull(save);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var gameKeyWriter = new ByteWriter();
            ByteKeyMapCodec.Write(gameKeyWriter, save.GameKey.Map);
            gameKeyWriter.WriteBytes(SerializeNodes(
                ToGameKeyFields(save.GameKey), PhigrosSchemas.GameKey, save.GameKey.Version));
            WriteOverflow(gameKeyWriter, save.GameKey.Overflow);
            AddEntry(archive, EntryGameKey, PhigrosCipher.EncryptEntry(save.GameKey.Version, gameKeyWriter.ToArray()));

            var gameProgressWriter = new ByteWriter();
            gameProgressWriter.WriteBytes(SerializeNodes(
                ToGameProgressFields(save.GameProgress), PhigrosSchemas.GameProgress, save.GameProgress.Version));
            WriteOverflow(gameProgressWriter, save.GameProgress.Overflow);
            AddEntry(archive, EntryGameProgress,
                PhigrosCipher.EncryptEntry(save.GameProgress.Version, gameProgressWriter.ToArray()));

            var gameRecordWriter = new ByteWriter();
            GameRecordCodec.Write(gameRecordWriter, save.GameRecord);
            AddEntry(archive, EntryGameRecord,
                PhigrosCipher.EncryptEntry(FixedEntryVersion, gameRecordWriter.ToArray()));

            var settingsWriter = new ByteWriter();
            BinarySerializer.Serialize(settingsWriter, ToSettingsFields(save.Settings), PhigrosSchemas.Settings);
            AddEntry(archive, EntrySettings,
                PhigrosCipher.EncryptEntry(FixedEntryVersion, settingsWriter.ToArray()));

            var userWriter = new ByteWriter();
            BinarySerializer.Serialize(userWriter, ToUserFields(save.User), PhigrosSchemas.User);
            AddEntry(archive, EntryUser,
                PhigrosCipher.EncryptEntry(FixedEntryVersion, userWriter.ToArray()));
        }

        return stream.ToArray();
    }

    /// <summary>把存档写入本地文件。</summary>
    public static void WriteFile(string path, SaveData save) => File.WriteAllBytes(path, Write(save));

    /// <summary>解析 LeanCloud 返回的 base64 形式的 summary。</summary>
    public static Summary ParseSummary(string base64)
    {
        ArgumentNullException.ThrowIfNull(base64);

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new PhigrosFormatException("summary 不是合法的 base64 字符串。", ex);
        }

        var fields = BinarySerializer.Deserialize(new ByteReader(payload), PhigrosSchemas.Summary);
        return new Summary
        {
            SaveVersion = GetInt(fields, "saveVersion"),
            ChallengeModeRank = GetInt(fields, "challengeModeRank"),
            RankingScore = GetSingle(fields, "rankingScore"),
            GameVersion = GetInt(fields, "gameVersion"),
            Avatar = GetString(fields, "avatar"),
            Progress = ToIntArray(GetUInt16Array(fields, "progress")),
        };
    }

    /// <summary>把 summary 序列化为 LeanCloud 需要的 base64 字符串。</summary>
    public static string WriteSummary(Summary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var fields = new FieldMap();
        fields.Set("saveVersion", (byte)summary.SaveVersion);
        fields.Set("challengeModeRank", (ushort)summary.ChallengeModeRank);
        fields.Set("rankingScore", summary.RankingScore);
        fields.Set("gameVersion", summary.GameVersion);
        fields.Set("avatar", summary.Avatar);
        fields.Set("progress", ToUInt16Array(summary.Progress));

        var writer = new ByteWriter();
        BinarySerializer.Serialize(writer, fields, PhigrosSchemas.Summary);
        return Convert.ToBase64String(writer.ToArray());
    }

    private static byte[] ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name)
            ?? throw new PhigrosFormatException($"存档中缺少条目 '{name}'。");

        using var source = entry.Open();
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] payload)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var target = entry.Open();
        target.Write(payload, 0, payload.Length);
    }

    private static string? ReadOverflow(ByteReader reader)
        => reader.Remaining > 0 ? Convert.ToBase64String(reader.RemainingSpan) : null;

    private static void WriteOverflow(ByteWriter writer, string? overflow)
    {
        if (string.IsNullOrEmpty(overflow)) return;
        writer.WriteBytes(Convert.FromBase64String(overflow));
    }

    private static byte[] SerializeNodes(FieldMap fields, ObjectSchema[] schemas, int version)
    {
        var writer = new ByteWriter();
        BinarySerializer.SerializeVersioned(writer, fields, schemas, version);
        return writer.ToArray();
    }

    private static void ApplyGameProgress(GameProgress progress, FieldMap fields)
    {
        progress.IsFirstRun = GetBool(fields, "isFirstRun");
        progress.LegacyChapterFinished = GetBool(fields, "legacyChapterFinished");
        progress.AlreadyShowCollectionTip = GetBool(fields, "alreadyShowCollectionTip");
        progress.AlreadyShowAutoUnlockINTip = GetBool(fields, "alreadyShowAutoUnlockINTip");
        progress.Completed = GetString(fields, "completed");
        progress.SongUpdateInfo = GetInt(fields, "songUpdateInfo");
        progress.ChallengeModeRank = GetInt(fields, "challengeModeRank");
        progress.Money = GetVarshortArray(fields, "money");
        progress.UnlockFlagOfSpasmodic = GetInt(fields, "unlockFlagOfSpasmodic");
        progress.UnlockFlagOfIgallta = GetInt(fields, "unlockFlagOfIgallta");
        progress.UnlockFlagOfRrharil = GetInt(fields, "unlockFlagOfRrharil");
        progress.FlagOfSongRecordKey = GetInt(fields, "flagOfSongRecordKey");
        progress.RandomVersionUnlocked = GetInt(fields, "randomVersionUnlocked");
        progress.Chapter8UnlockBegin = GetBool(fields, "chapter8UnlockBegin");
        progress.Chapter8UnlockSecondPhase = GetBool(fields, "chapter8UnlockSecondPhase");
        progress.Chapter8Passed = GetBool(fields, "chapter8Passed");
        progress.Chapter8SongUnlocked = GetInt(fields, "chapter8SongUnlocked");
    }

    private static FieldMap ToGameProgressFields(GameProgress progress)
    {
        var fields = new FieldMap();
        fields.Set("isFirstRun", progress.IsFirstRun);
        fields.Set("legacyChapterFinished", progress.LegacyChapterFinished);
        fields.Set("alreadyShowCollectionTip", progress.AlreadyShowCollectionTip);
        fields.Set("alreadyShowAutoUnlockINTip", progress.AlreadyShowAutoUnlockINTip);
        fields.Set("completed", progress.Completed);
        fields.Set("songUpdateInfo", (byte)progress.SongUpdateInfo);
        fields.Set("challengeModeRank", (ushort)progress.ChallengeModeRank);
        fields.Set("money", progress.Money);
        fields.Set("unlockFlagOfSpasmodic", (byte)progress.UnlockFlagOfSpasmodic);
        fields.Set("unlockFlagOfIgallta", (byte)progress.UnlockFlagOfIgallta);
        fields.Set("unlockFlagOfRrharil", (byte)progress.UnlockFlagOfRrharil);
        fields.Set("flagOfSongRecordKey", (byte)progress.FlagOfSongRecordKey);
        fields.Set("randomVersionUnlocked", (byte)progress.RandomVersionUnlocked);
        fields.Set("chapter8UnlockBegin", progress.Chapter8UnlockBegin);
        fields.Set("chapter8UnlockSecondPhase", progress.Chapter8UnlockSecondPhase);
        fields.Set("chapter8Passed", progress.Chapter8Passed);
        fields.Set("chapter8SongUnlocked", (byte)progress.Chapter8SongUnlocked);
        return fields;
    }

    private static FieldMap ToGameKeyFields(GameKey gameKey)
    {
        var fields = new FieldMap();
        fields.Set("lanotaReadKeys", (byte)gameKey.LanotaReadKeys);
        fields.Set("camelliaReadKey", gameKey.CamelliaReadKey);
        return fields;
    }

    private static void ApplySettings(Settings settings, FieldMap fields)
    {
        settings.ChordSupport = GetBool(fields, "chordSupport");
        settings.FcApIndicator = GetBool(fields, "fcAPIndicator");
        settings.EnableHitSound = GetBool(fields, "enableHitSound");
        settings.LowResolutionMode = GetBool(fields, "lowResolutionMode");
        settings.DeviceName = GetString(fields, "deviceName");
        settings.Bright = GetSingle(fields, "bright");
        settings.MusicVolume = GetSingle(fields, "musicVolume");
        settings.EffectVolume = GetSingle(fields, "effectVolume");
        settings.HitSoundVolume = GetSingle(fields, "hitSoundVolume");
        settings.SoundOffset = GetSingle(fields, "soundOffset");
        settings.NoteScale = GetSingle(fields, "noteScale");
    }

    private static FieldMap ToSettingsFields(Settings settings)
    {
        var fields = new FieldMap();
        fields.Set("chordSupport", settings.ChordSupport);
        fields.Set("fcAPIndicator", settings.FcApIndicator);
        fields.Set("enableHitSound", settings.EnableHitSound);
        fields.Set("lowResolutionMode", settings.LowResolutionMode);
        fields.Set("deviceName", settings.DeviceName);
        fields.Set("bright", settings.Bright);
        fields.Set("musicVolume", settings.MusicVolume);
        fields.Set("effectVolume", settings.EffectVolume);
        fields.Set("hitSoundVolume", settings.HitSoundVolume);
        fields.Set("soundOffset", settings.SoundOffset);
        fields.Set("noteScale", settings.NoteScale);
        return fields;
    }

    private static FieldMap ToUserFields(UserData user)
    {
        var fields = new FieldMap();
        fields.Set("showPlayerId", user.ShowPlayerId);
        fields.Set("selfIntro", user.SelfIntro);
        fields.Set("avatar", user.Avatar);
        fields.Set("background", user.Background);
        return fields;
    }

    private static bool GetBool(FieldMap fields, string name) => fields.Get(name) as bool? ?? false;

    private static int GetInt(FieldMap fields, string name) => fields.Get(name) switch
    {
        byte value => value,
        ushort value => value,
        int value => value,
        _ => 0,
    };

    private static float GetSingle(FieldMap fields, string name) => fields.Get(name) as float? ?? 0f;

    private static string GetString(FieldMap fields, string name) => fields.Get(name) as string ?? string.Empty;

    private static ushort[] GetUInt16Array(FieldMap fields, string name) => fields.Get(name) as ushort[] ?? new ushort[12];

    private static int[] GetVarshortArray(FieldMap fields, string name) => fields.Get(name) as int[] ?? new int[5];

    private static int[] ToIntArray(ushort[] values)
    {
        var result = new int[values.Length];
        for (int i = 0; i < values.Length; i++) result[i] = values[i];
        return result;
    }

    private static ushort[] ToUInt16Array(int[] values)
    {
        var result = new ushort[values.Length];
        for (int i = 0; i < values.Length; i++) result[i] = (ushort)values[i];
        return result;
    }
}
