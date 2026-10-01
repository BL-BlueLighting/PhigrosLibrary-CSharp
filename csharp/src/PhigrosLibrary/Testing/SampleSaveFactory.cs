using PhigrosLibrary.Models;

namespace PhigrosLibrary.Testing;

/// <summary>
/// 按给定随机种子生成一份结构与真实云存档一致的示例存档。
/// </summary>
/// <remarks>
/// 用于在没有真实账号的情况下试用本库、写单元测试，或验证"写出 → 读回"的往返一致性。
/// 生成的数据是伪造的，与任何真实玩家无关。
/// </remarks>
public static class SampleSaveFactory
{
    /// <summary>生成一份示例存档。</summary>
    /// <param name="difficulties">曲目来源，生成的成绩只覆盖表中存在的难度。</param>
    /// <param name="seed">随机种子，相同种子生成完全相同的结果。</param>
    /// <param name="songCount">最多取多少首曲目。</param>
    public static SaveData Create(DifficultyTable difficulties, int seed = 2024, int songCount = 80)
    {
        ArgumentNullException.ThrowIfNull(difficulties);

        var random = new Random(seed);
        var save = new SaveData();

        var songs = difficulties.Songs.Take(songCount).ToList();
        for (int index = 0; index < songs.Count; index++)
        {
            (string songId, float[] levels) = songs[index];
            var song = new SongLevels();

            // 第一首曲目的最高难度固定为满分，保证 B19 里一定有一个 φ。
            int perfectLevel = index == 0 ? HighestLevel(levels) : -1;

            for (int level = 0; level < DifficultyTable.LevelCount; level++)
            {
                if (levels[level] == 0) continue;          // 该曲没有这个难度

                bool forcePerfect = level == perfectLevel;
                if (!forcePerfect && random.NextDouble() < 0.15) continue; // 假装这个难度还没打

                song[level] = CreateRecord(random, forcePerfect);
            }

            if (!song.IsEmpty) save.GameRecord[songId] = song;
        }

        save.GameKey.Map = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["Chapter8"] = [1, 1, 0, 0, 0],
        };
        save.GameKey.LanotaReadKeys = (byte)random.Next(0, 2);
        save.GameKey.CamelliaReadKey = random.Next(0, 2) == 1;

        save.GameProgress.Completed = string.Join('|', save.GameRecord.Keys.Take(10));
        // money 的每一项都是变长整数，上限 32767。
        save.GameProgress.Money = [0, 12, 340, 5600, 12000];
        save.GameProgress.ChallengeModeRank = random.Next(0, 100);
        save.GameProgress.LegacyChapterFinished = true;
        save.GameProgress.SongUpdateInfo = (byte)random.Next(0, 4);

        save.User.Avatar = "Introduction.0";
        save.User.Background = "Introduction.0";
        save.User.SelfIntro = "由 SampleSaveFactory 生成的示例数据";
        save.User.ShowPlayerId = false;

        save.Settings.DeviceName = "PhigrosLibrary";
        save.Settings.Bright = 0.8f;
        save.Settings.MusicVolume = 0.7f;
        save.Settings.EffectVolume = 0.7f;
        save.Settings.HitSoundVolume = 0.5f;
        save.Settings.NoteScale = 1f;

        return save;
    }

    private static LevelRecord CreateRecord(Random random, bool forcePerfect)
    {
        if (forcePerfect)
        {
            return new LevelRecord(LevelRecord.PerfectScore, 100f, fullCombo: true);
        }

        // ACC 集中在 90 以上，偶尔出现高 ACC 成绩。
        float accuracy = random.NextDouble() switch
        {
            < 0.3 => 90f + (float)random.NextDouble() * 5f,
            < 0.8 => 95f + (float)random.NextDouble() * 4f,
            _ => 99f + (float)random.NextDouble(),
        };

        // 真实计分中满分对应 ACC 100，其余线性折算已足够用来说明问题。
        uint score = (uint)Math.Clamp(Math.Round(accuracy * 10_000), 0, LevelRecord.PerfectScore);
        bool fullCombo = accuracy >= 97f || random.NextDouble() < 0.1;

        return new LevelRecord(score, accuracy, fullCombo);
    }

    private static int HighestLevel(float[] levels)
    {
        int highest = 0;
        for (int level = 1; level < levels.Length; level++)
        {
            if (levels[level] > 0) highest = level;
        }

        return highest;
    }
}
