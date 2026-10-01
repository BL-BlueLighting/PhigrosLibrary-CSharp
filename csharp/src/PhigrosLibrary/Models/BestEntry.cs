using System.Text.Json.Serialization;

namespace PhigrosLibrary.Models;

/// <summary>
/// B19 中的一首曲目成绩。
/// </summary>
public sealed class BestEntry
{
    /// <summary>曲目 id。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>难度。</summary>
    [JsonPropertyName("level")]
    public SongDifficulty Level { get; set; }

    /// <summary>该难度的定数。</summary>
    [JsonPropertyName("difficulty")]
    public float Difficulty { get; set; }

    /// <summary>该成绩贡献的单曲 RKS。</summary>
    [JsonPropertyName("rks")]
    public float Rks { get; set; }

    /// <summary>分数。</summary>
    [JsonPropertyName("score")]
    public uint Score { get; set; }

    /// <summary>准确率。</summary>
    [JsonPropertyName("acc")]
    public float Accuracy { get; set; }

    /// <summary>是否 Full Combo。</summary>
    [JsonPropertyName("fc")]
    public bool FullCombo { get; set; }
}

/// <summary>
/// B19 计算结果：19 首最高 RKS 的成绩，外加一首难度最高的 φ（满分）曲目。
/// </summary>
public sealed class B19Result
{
    /// <summary>
    /// 总 RKS，即 20 个成绩的单曲 RKS 平均值。
    /// 没有 φ 曲目时该位置按 0 计入。
    /// </summary>
    [JsonPropertyName("rks")]
    public float Rks { get; set; }

    /// <summary>难度最高的满分曲目，没有满分成绩时为 <see langword="null"/>。</summary>
    [JsonPropertyName("phi")]
    public BestEntry? Phi { get; set; }

    /// <summary>其余 19 首成绩，按单曲 RKS 从高到低排列。</summary>
    [JsonPropertyName("best")]
    public IReadOnlyList<BestEntry> Best { get; set; } = [];
}

/// <summary>
/// 推分建议：某个难度需要打到多少 ACC 才能进入 B19。
/// </summary>
public sealed class ExpectEntry
{
    /// <summary>曲目 id。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>难度。</summary>
    [JsonPropertyName("level")]
    public SongDifficulty Level { get; set; }

    /// <summary>该难度的定数。</summary>
    [JsonPropertyName("difficulty")]
    public float Difficulty { get; set; }

    /// <summary>当前成绩的单曲 RKS，未游玩为 0。</summary>
    [JsonPropertyName("rks")]
    public float Rks { get; set; }

    /// <summary>当前 ACC，未游玩为 0。</summary>
    [JsonPropertyName("acc")]
    public float Accuracy { get; set; }

    /// <summary>进入 B19 所需的 ACC。</summary>
    [JsonPropertyName("expect")]
    public float Expect { get; set; }
}
