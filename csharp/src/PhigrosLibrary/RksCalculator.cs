using PhigrosLibrary.Models;

namespace PhigrosLibrary;

/// <summary>
/// B19 / RKS / 期望 ACC 的计算。
/// </summary>
/// <remarks>
/// <para>单曲 RKS 的公式为 <c>定数 × ((ACC - 55) / 45)²</c>，ACC 为 100 时恰好等于定数。</para>
/// <para>
/// 总 RKS 取 19 首单曲 RKS 最高的成绩，加上一首难度最高的 φ（满分）曲目，共 20 项取平均。
/// 没有满分成绩时 φ 一项按 0 计入。
/// </para>
/// </remarks>
public sealed class RksCalculator
{
    /// <summary>成绩计入 B19 所需的最低 ACC。</summary>
    public const float MinimumAccuracy = 55f;

    /// <summary>B19 中除 φ 外的成绩数量。</summary>
    public const int BestCount = 19;

    /// <summary>组成总 RKS 的成绩总数：19 首最佳 + 1 首 φ。</summary>
    public const int RecordCount = BestCount + 1;

    private const float AccuracyFloor = 55f;
    private const float AccuracySpan = 45f;

    public RksCalculator(DifficultyTable difficulties)
    {
        ArgumentNullException.ThrowIfNull(difficulties);
        Difficulties = difficulties;
    }

    /// <summary>计算所用的定数表。</summary>
    public DifficultyTable Difficulties { get; }

    /// <summary>计算单曲 RKS。</summary>
    public static float ComputeRks(float difficulty, float accuracy)
    {
        float factor = (accuracy - AccuracyFloor) / AccuracySpan;
        return factor * factor * difficulty;
    }

    /// <summary>计算达到目标单曲 RKS 所需的 ACC，即 <see cref="ComputeRks"/> 的反函数。</summary>
    public static float ComputeExpectedAccuracy(float difficulty, float targetRks)
        => MathF.Sqrt(targetRks / difficulty) * AccuracySpan + AccuracyFloor;

    /// <summary>计算一份存档的 B19。</summary>
    public B19Result ComputeBest19(SaveData save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return ComputeBest19(save.GameRecord);
    }

    /// <summary>由成绩表计算 B19。</summary>
    public B19Result ComputeBest19(IReadOnlyDictionary<string, SongLevels> gameRecord)
    {
        ArgumentNullException.ThrowIfNull(gameRecord);

        var (phi, best) = Select(gameRecord);

        double sum = phi?.Rks ?? 0;
        foreach (var entry in best) sum += entry.Rks;

        return new B19Result
        {
            Rks = (float)(sum / RecordCount),
            Phi = phi,
            Best = best,
        };
    }

    /// <summary>计算把哪些成绩打进 B19 需要多少 ACC。</summary>
    public IReadOnlyList<ExpectEntry> ComputeExpect(IReadOnlyDictionary<string, SongLevels> gameRecord)
    {
        ArgumentNullException.ThrowIfNull(gameRecord);

        // 门槛取第 19 名的单曲 RKS：越过它才能挤进 B19。
        var (_, best) = Select(gameRecord);
        float threshold = best.Count >= BestCount ? best[BestCount - 1].Rks : 0f;

        var result = new List<ExpectEntry>();
        foreach (var candidate in Enumerate(gameRecord))
        {
            if (candidate.Difficulty < threshold) continue;

            float accuracy = candidate.Record?.Accuracy ?? 0f;
            float rks = accuracy > MinimumAccuracy ? ComputeRks(candidate.Difficulty, accuracy) : 0f;
            if (rks > threshold) continue;

            result.Add(new ExpectEntry
            {
                Id = candidate.SongId,
                Level = candidate.Level,
                Difficulty = candidate.Difficulty,
                Rks = rks,
                Accuracy = accuracy,
                Expect = ComputeExpectedAccuracy(candidate.Difficulty, threshold),
            });
        }

        return result;
    }

    /// <summary>
    /// 统计成绩表：每三个一组对应 EZ / HD / IN / AT，
    /// 依次为已游玩数、Full Combo 数、All Perfect 数，共 12 项。
    /// </summary>
    public int[] ComputeProgress(IReadOnlyDictionary<string, SongLevels> gameRecord)
    {
        ArgumentNullException.ThrowIfNull(gameRecord);

        var progress = new int[DifficultyTable.LevelCount * 3];
        foreach (var candidate in Enumerate(gameRecord))
        {
            if (candidate.Record is not { } record || record.Accuracy == 0) continue;

            int offset = 3 * (int)candidate.Level;
            progress[offset]++;
            if (record.IsAllPerfect)
            {
                progress[offset + 1]++;
                progress[offset + 2]++;
            }
            else if (record.FullCombo)
            {
                progress[offset + 1]++;
            }
        }

        return progress;
    }

    /// <summary>
    /// 选出 φ 与 19 首最佳成绩。返回的列表按单曲 RKS 从高到低排列。
    /// </summary>
    private (BestEntry? Phi, IReadOnlyList<BestEntry> Best) Select(
        IReadOnlyDictionary<string, SongLevels> gameRecord)
    {
        BestEntry? phi = null;
        var best = new BestEntry?[BestCount];
        int lowest = 0;

        foreach (var candidate in Enumerate(gameRecord))
        {
            float accuracy = candidate.Record?.Accuracy ?? 0f;
            if (accuracy < MinimumAccuracy) continue;

            // φ 取难度最高的满分成绩；单曲 RKS 恒不超过定数，先用定数做剪枝。
            bool isPhi = candidate.Record!.IsAllPerfect && candidate.Difficulty > (phi?.Difficulty ?? 0f);
            if (!isPhi && candidate.Difficulty <= (best[lowest]?.Rks ?? 0f)) continue;

            float rks = ComputeRks(candidate.Difficulty, accuracy);
            if (isPhi) phi = CreateEntry(candidate, rks);

            if (rks > (best[lowest]?.Rks ?? 0f))
            {
                best[lowest] = CreateEntry(candidate, rks);
                lowest = IndexOfLowest(best);
            }
        }

        var entries = best
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderByDescending(entry => entry.Rks)
            .ToList();

        return (phi, entries);
    }

    private static int IndexOfLowest(BestEntry?[] entries)
    {
        int lowest = 0;
        for (int i = 1; i < entries.Length; i++)
        {
            if ((entries[i]?.Rks ?? 0f) < (entries[lowest]?.Rks ?? 0f)) lowest = i;
        }

        return lowest;
    }

    private static BestEntry CreateEntry(Candidate candidate, float rks)
    {
        var record = candidate.Record!;
        return new BestEntry
        {
            Id = candidate.SongId,
            Level = candidate.Level,
            Difficulty = candidate.Difficulty,
            Rks = rks,
            Score = record.Score,
            Accuracy = record.Accuracy,
            FullCombo = record.FullCombo,
        };
    }

    /// <summary>
    /// 按存档顺序枚举所有"曲目 × 难度"，只展开该曲实际拥有的难度
    /// （没有 AT 的曲目不展开第 4 个难度）。
    /// </summary>
    private IEnumerable<Candidate> Enumerate(IReadOnlyDictionary<string, SongLevels> gameRecord)
    {
        foreach ((string songId, SongLevels levels) in gameRecord)
        {
            float[] difficulties = Difficulties.Get(songId);
            for (int level = 0; level < DifficultyTable.LevelCount; level++)
            {
                if (level == (int)SongDifficulty.At && difficulties[level] == 0) break;
                yield return new Candidate(songId, (SongDifficulty)level, difficulties[level], levels[level]);
            }
        }
    }

    private readonly record struct Candidate(
        string SongId, SongDifficulty Level, float Difficulty, LevelRecord? Record);
}
