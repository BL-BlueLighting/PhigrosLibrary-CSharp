using System.Text.Json.Serialization;
using PhigrosLibrary.Serialization;

namespace PhigrosLibrary.Models;

/// <summary>
/// <c>gameKey</c> 条目：曲目解锁相关的密钥/开关。
/// </summary>
public sealed class GameKey
{
    /// <summary>
    /// 该条目的版本号，决定用到哪些字段表。
    /// 新建实例时默认为本库支持的最高版本，保证写回时不会丢掉已赋值的字段。
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = PhigrosSchemas.GameKey.Length;

    /// <summary>
    /// 前半段的键值表：键为章节或曲目名，值为 5 个 <see cref="byte"/>（磁盘上只保存非零项）。
    /// </summary>
    [JsonPropertyName("map")]
    public Dictionary<string, byte[]> Map { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Lanota 联动曲目的解锁标记。</summary>
    [JsonPropertyName("lanotaReadKeys")]
    public int LanotaReadKeys { get; set; }

    /// <summary>Camellia 联动曲目的解锁标记，v2 起存在。</summary>
    [JsonPropertyName("camelliaReadKey")]
    public bool CamelliaReadKey { get; set; }

    /// <summary>
    /// 本实现无法识别的尾部字节（游戏版本高于本库时出现），以 base64 原样保留，写回时原样写回。
    /// </summary>
    [JsonPropertyName("overflow")]
    public string? Overflow { get; set; }
}
