namespace PhigrosLibrary.Serialization;

/// <summary>
/// 存档各部分的字段表。
/// </summary>
/// <remarks>
/// 字段顺序即磁盘顺序，与上游 C 实现中的 <c>Summary</c> / <c>GameKey</c> / <c>GameProgress</c> /
/// <c>User</c> / <c>Settings</c> 表一一对应。游戏更新后若追加字段，在对应数组尾部新增一个
/// <see cref="ObjectSchema"/> 即可（版本号来自存档自身的 version 字段）。
/// </remarks>
public static class PhigrosSchemas
{
    /// <summary>玩家概要，来源为 LeanCloud 返回的 base64 字段，不带版本号。</summary>
    public static readonly ObjectSchema Summary = new("Summary",
        new SchemaField("saveVersion", FieldKind.U8),
        new SchemaField("challengeModeRank", FieldKind.U16),
        new SchemaField("rankingScore", FieldKind.Single),
        new SchemaField("gameVersion", FieldKind.Varshort),
        new SchemaField("avatar", FieldKind.String),
        new SchemaField("progress", FieldKind.UInt16Array, 12));

    /// <summary><c>gameKey</c> 的 v1 段。</summary>
    public static readonly ObjectSchema GameKey1 = new("GameKey1",
        new SchemaField("lanotaReadKeys", FieldKind.U8));

    /// <summary><c>gameKey</c> 的 v2 段。</summary>
    public static readonly ObjectSchema GameKey2 = new("GameKey2",
        new SchemaField("camelliaReadKey", FieldKind.Bool));

    /// <summary><c>gameKey</c> 按版本排列的字段表。</summary>
    public static readonly ObjectSchema[] GameKey = [GameKey1, GameKey2];

    /// <summary><c>gameProgress</c> 的 v1 段。</summary>
    public static readonly ObjectSchema GameProgress1 = new("GameProgress1",
        new SchemaField("isFirstRun", FieldKind.Bool),
        new SchemaField("legacyChapterFinished", FieldKind.Bool),
        new SchemaField("alreadyShowCollectionTip", FieldKind.Bool),
        new SchemaField("alreadyShowAutoUnlockINTip", FieldKind.Bool),
        new SchemaField("completed", FieldKind.String),
        new SchemaField("songUpdateInfo", FieldKind.U8),
        new SchemaField("challengeModeRank", FieldKind.U16),
        new SchemaField("money", FieldKind.VarshortArray, 5),
        new SchemaField("unlockFlagOfSpasmodic", FieldKind.U8),
        new SchemaField("unlockFlagOfIgallta", FieldKind.U8),
        new SchemaField("unlockFlagOfRrharil", FieldKind.U8),
        new SchemaField("flagOfSongRecordKey", FieldKind.U8));

    /// <summary><c>gameProgress</c> 的 v2 段。</summary>
    public static readonly ObjectSchema GameProgress2 = new("GameProgress2",
        new SchemaField("randomVersionUnlocked", FieldKind.U8));

    /// <summary><c>gameProgress</c> 的 v3 段。</summary>
    public static readonly ObjectSchema GameProgress3 = new("GameProgress3",
        new SchemaField("chapter8UnlockBegin", FieldKind.Bool),
        new SchemaField("chapter8UnlockSecondPhase", FieldKind.Bool),
        new SchemaField("chapter8Passed", FieldKind.Bool),
        new SchemaField("chapter8SongUnlocked", FieldKind.U8));

    /// <summary><c>gameProgress</c> 按版本排列的字段表。</summary>
    public static readonly ObjectSchema[] GameProgress = [GameProgress1, GameProgress2, GameProgress3];

    /// <summary><c>user</c>，无版本号，结构固定。</summary>
    public static readonly ObjectSchema User = new("User",
        new SchemaField("showPlayerId", FieldKind.Bool),
        new SchemaField("selfIntro", FieldKind.String),
        new SchemaField("avatar", FieldKind.String),
        new SchemaField("background", FieldKind.String));

    /// <summary><c>settings</c>，无版本号，结构固定。</summary>
    public static readonly ObjectSchema Settings = new("Settings",
        new SchemaField("chordSupport", FieldKind.Bool),
        new SchemaField("fcAPIndicator", FieldKind.Bool),
        new SchemaField("enableHitSound", FieldKind.Bool),
        new SchemaField("lowResolutionMode", FieldKind.Bool),
        new SchemaField("deviceName", FieldKind.String),
        new SchemaField("bright", FieldKind.Single),
        new SchemaField("musicVolume", FieldKind.Single),
        new SchemaField("effectVolume", FieldKind.Single),
        new SchemaField("hitSoundVolume", FieldKind.Single),
        new SchemaField("soundOffset", FieldKind.Single),
        new SchemaField("noteScale", FieldKind.Single));
}
