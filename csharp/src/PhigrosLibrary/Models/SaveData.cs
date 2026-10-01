using System.Text.Json.Serialization;

namespace PhigrosLibrary.Models;

/// <summary>
/// 一份完整的云存档，对应 zip 中的五个条目。
/// </summary>
public sealed class SaveData
{
    /// <summary>
    /// 各曲目各难度的成绩。键为曲目 id（磁盘上带 <c>.0</c> 后缀，读取时已剥离）。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="Dictionary{TKey,TValue}"/> 以保持磁盘顺序：
    /// .NET 的字典在只增不删的前提下按插入顺序枚举，因此解析后再写回可以得到一致的字节序列。
    /// </remarks>
    [JsonPropertyName("gameRecord")]
    public Dictionary<string, SongLevels> GameRecord { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("gameKey")]
    public GameKey GameKey { get; set; } = new();

    [JsonPropertyName("gameProgress")]
    public GameProgress GameProgress { get; set; } = new();

    [JsonPropertyName("user")]
    public UserData User { get; set; } = new();

    [JsonPropertyName("settings")]
    public Settings Settings { get; set; } = new();
}
