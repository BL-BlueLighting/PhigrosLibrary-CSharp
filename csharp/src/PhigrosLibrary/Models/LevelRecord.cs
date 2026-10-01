namespace PhigrosLibrary.Models;

/// <summary>难度编号，与存档中的下标一致。</summary>
public enum SongDifficulty
{
    /// <summary>EZ</summary>
    Ez = 0,

    /// <summary>HD</summary>
    Hd = 1,

    /// <summary>IN</summary>
    In = 2,

    /// <summary>AT，部分曲目没有该难度。</summary>
    At = 3,
}

/// <summary>某一首曲目在单个难度下的成绩。</summary>
public sealed class LevelRecord
{
    /// <summary>满分成绩。</summary>
    public const uint PerfectScore = 1_000_000;

    public LevelRecord() { }

    public LevelRecord(uint score, float accuracy, bool fullCombo)
    {
        Score = score;
        Accuracy = accuracy;
        FullCombo = fullCombo;
    }

    /// <summary>分数，0 ~ 1000000。</summary>
    public uint Score { get; set; }

    /// <summary>准确率，百分比数值（如 99.5 表示 99.5%）。</summary>
    public float Accuracy { get; set; }

    /// <summary>是否取得 Full Combo。</summary>
    public bool FullCombo { get; set; }

    /// <summary>是否为满分（φ）。</summary>
    public bool IsAllPerfect => Score == PerfectScore;
}

/// <summary>一首曲目四个难度的成绩，<see langword="null"/> 表示该难度尚无成绩。</summary>
public sealed class SongLevels
{
    /// <summary>难度数量。</summary>
    public const int Count = 4;

    private readonly LevelRecord?[] _levels = new LevelRecord?[Count];

    public SongLevels() { }

    public SongLevels(IEnumerable<LevelRecord?> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        int index = 0;
        foreach (var level in levels)
        {
            if (index >= Count) break;
            _levels[index++] = level;
        }
    }

    /// <summary>按下标读写某个难度的成绩，下标含义见 <see cref="SongDifficulty"/>。</summary>
    public LevelRecord? this[int level]
    {
        get
        {
            if ((uint)level >= Count) throw new ArgumentOutOfRangeException(nameof(level), level, "难度下标必须在 0..3 之间。");
            return _levels[level];
        }
        set
        {
            if ((uint)level >= Count) throw new ArgumentOutOfRangeException(nameof(level), level, "难度下标必须在 0..3 之间。");
            _levels[level] = value;
        }
    }

    /// <summary>按下标读写某个难度的成绩。</summary>
    public LevelRecord? this[SongDifficulty level]
    {
        get => this[(int)level];
        set => this[(int)level] = value;
    }

    /// <summary>是否四个难度都没有成绩。</summary>
    public bool IsEmpty => _levels.All(level => level is null);

    /// <summary>所有已有成绩的难度。</summary>
    public IEnumerable<(SongDifficulty Difficulty, LevelRecord Record)> Played()
    {
        for (int i = 0; i < Count; i++)
        {
            if (_levels[i] is { } record) yield return ((SongDifficulty)i, record);
        }
    }
}
