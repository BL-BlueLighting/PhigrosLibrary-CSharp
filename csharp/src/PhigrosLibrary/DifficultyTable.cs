using System.Globalization;
using PhigrosLibrary.Models;

namespace PhigrosLibrary;

/// <summary>
/// 定数表：曲目 id 到 EZ / HD / IN / AT 四个难度定数的映射。
/// </summary>
/// <remarks>
/// 数据来源为 <c>difficulty.tsv</c>（见 <c>resources/NOTICE.md</c>）。
/// 只有三个难度的曲目，AT 定数记为 0。
/// </remarks>
public sealed class DifficultyTable
{
    private readonly Dictionary<string, float[]> _songs;

    private DifficultyTable(Dictionary<string, float[]> songs) => _songs = songs;

    /// <summary>难度数量。</summary>
    public const int LevelCount = SongLevels.Count;

    /// <summary>收录的曲目数量。</summary>
    public int Count => _songs.Count;

    /// <summary>所有曲目及其定数。</summary>
    public IReadOnlyDictionary<string, float[]> Songs => _songs;

    /// <summary>从 tsv 文件加载定数表。</summary>
    public static DifficultyTable Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var reader = new StreamReader(path);
        return Parse(reader);
    }

    /// <summary>从文本内容解析定数表。</summary>
    public static DifficultyTable Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var reader = new StringReader(content);
        return Parse(reader);
    }

    /// <summary>从文本流解析定数表，每行格式为 <c>曲目id\tEZ\tHD\tIN[\tAT]</c>。</summary>
    public static DifficultyTable Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var songs = new Dictionary<string, float[]>(StringComparer.Ordinal);
        string? line;
        int lineNumber = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] parts = line.Split('\t');
            if (parts.Length < 2)
                throw new PhigrosDataException($"定数表第 {lineNumber} 行格式不正确：{line}");

            var levels = new float[LevelCount];
            for (int i = 1; i < parts.Length && i <= LevelCount; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out levels[i - 1]))
                    throw new PhigrosDataException($"定数表第 {lineNumber} 行的 '{parts[i]}' 不是合法数字。");
            }

            songs[parts[0]] = levels;
        }

        if (songs.Count == 0)
            throw new PhigrosDataException("定数表为空。");

        return new DifficultyTable(songs);
    }

    /// <summary>尝试取某个曲目的四个难度定数。</summary>
    public bool TryGet(string songId, out float[] levels) => _songs.TryGetValue(songId, out levels!);

    /// <summary>取某个曲目的四个难度定数，不存在时抛出 <see cref="PhigrosDataException"/>。</summary>
    public float[] this[string songId] => Get(songId);

    /// <summary>取某个曲目的四个难度定数，不存在时抛出 <see cref="PhigrosDataException"/>。</summary>
    public float[] Get(string songId)
    {
        if (_songs.TryGetValue(songId, out var levels)) return levels;

        throw new PhigrosDataException(
            $"定数表中没有曲目 '{songId}'，请更新 difficulty.tsv（见 resources/NOTICE.md）。");
    }
}
