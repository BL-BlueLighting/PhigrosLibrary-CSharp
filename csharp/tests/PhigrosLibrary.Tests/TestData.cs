using PhigrosLibrary.Models;

namespace PhigrosLibrary.Tests;

/// <summary>测试共用的只读数据。</summary>
internal static class TestData
{
    private static readonly Lazy<DifficultyTable> LazyDifficulties = new(() => DifficultyTable.Load(TestPaths.DifficultyTable));

    /// <summary>仓库内真实的定数表。</summary>
    public static DifficultyTable Difficulties => LazyDifficulties.Value;

    /// <summary>一份便于手算的迷你定数表。</summary>
    public static DifficultyTable MiniTable { get; } = DifficultyTable.Parse(
        """
        A.Artist	1.0	5.0	10.0	15.0
        B.Artist	2.0	6.0	11.0	16.0
        C.Artist	3.0	7.0	12.0	17.0
        D.Artist	4.0	8.0	13.0
        """);

    /// <summary>构造成绩表的便捷方法：<c>("A.Artist", In, 90f, false)</c>。</summary>
    public static Dictionary<string, SongLevels> Records(
        params (string SongId, SongDifficulty Level, float Accuracy, bool FullCombo)[] entries)
    {
        var records = new Dictionary<string, SongLevels>(StringComparer.Ordinal);

        foreach ((string songId, SongDifficulty level, float accuracy, bool fullCombo) in entries)
        {
            if (!records.TryGetValue(songId, out var levels))
            {
                levels = new SongLevels();
                records[songId] = levels;
            }

            uint score = accuracy >= 100f ? LevelRecord.PerfectScore : (uint)(accuracy * 10_000);
            levels[level] = new LevelRecord(score, accuracy, fullCombo);
        }

        return records;
    }
}
