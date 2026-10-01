using System.Text.Json.Serialization;
using PhigrosLibrary.Serialization;

namespace PhigrosLibrary.Models;

/// <summary>
/// <c>gameProgress</c> 条目：主线进度、解锁状态与货币。
/// </summary>
public sealed class GameProgress
{
    /// <summary>
    /// 该条目的版本号，决定用到哪些字段表。
    /// 新建实例时默认为本库支持的最高版本，保证写回时不会丢掉已赋值的字段。
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = PhigrosSchemas.GameProgress.Length;

    [JsonPropertyName("isFirstRun")]
    public bool IsFirstRun { get; set; }

    [JsonPropertyName("legacyChapterFinished")]
    public bool LegacyChapterFinished { get; set; }

    [JsonPropertyName("alreadyShowCollectionTip")]
    public bool AlreadyShowCollectionTip { get; set; }

    [JsonPropertyName("alreadyShowAutoUnlockINTip")]
    public bool AlreadyShowAutoUnlockINTip { get; set; }

    /// <summary>已完成曲目的 id 列表，以某种分隔符拼接的字符串。</summary>
    [JsonPropertyName("completed")]
    public string Completed { get; set; } = string.Empty;

    [JsonPropertyName("songUpdateInfo")]
    public int SongUpdateInfo { get; set; }

    [JsonPropertyName("challengeModeRank")]
    public int ChallengeModeRank { get; set; }

    /// <summary>五种货币的数量。</summary>
    [JsonPropertyName("money")]
    public int[] Money { get; set; } = new int[5];

    /// <summary>《Spasmodic》解锁标记。</summary>
    [JsonPropertyName("unlockFlagOfSpasmodic")]
    public int UnlockFlagOfSpasmodic { get; set; }

    /// <summary>《Igallta》解锁标记。</summary>
    [JsonPropertyName("unlockFlagOfIgallta")]
    public int UnlockFlagOfIgallta { get; set; }

    /// <summary>《Rrhar'il》解锁标记。</summary>
    [JsonPropertyName("unlockFlagOfRrharil")]
    public int UnlockFlagOfRrharil { get; set; }

    [JsonPropertyName("flagOfSongRecordKey")]
    public int FlagOfSongRecordKey { get; set; }

    /// <summary>v2 起存在。</summary>
    [JsonPropertyName("randomVersionUnlocked")]
    public int RandomVersionUnlocked { get; set; }

    /// <summary>v3 起存在：第八章解锁流程是否开始。</summary>
    [JsonPropertyName("chapter8UnlockBegin")]
    public bool Chapter8UnlockBegin { get; set; }

    /// <summary>v3 起存在：第八章解锁是否进入第二阶段。</summary>
    [JsonPropertyName("chapter8UnlockSecondPhase")]
    public bool Chapter8UnlockSecondPhase { get; set; }

    /// <summary>v3 起存在：第八章是否通关。</summary>
    [JsonPropertyName("chapter8Passed")]
    public bool Chapter8Passed { get; set; }

    /// <summary>v3 起存在：第八章已解锁曲目数。</summary>
    [JsonPropertyName("chapter8SongUnlocked")]
    public int Chapter8SongUnlocked { get; set; }

    /// <summary>
    /// 本实现无法识别的尾部字节，以 base64 原样保留，写回时原样写回。
    /// </summary>
    [JsonPropertyName("overflow")]
    public string? Overflow { get; set; }
}
