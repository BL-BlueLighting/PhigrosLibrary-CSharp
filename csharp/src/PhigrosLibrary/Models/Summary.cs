using System.Text.Json.Serialization;

namespace PhigrosLibrary.Models;

/// <summary>
/// 玩家概要，对应 LeanCloud <c>_GameSave</c> 记录中的 base64 <c>summary</c> 字段。
/// </summary>
/// <remarks>
/// 后面的 <c>objectId</c> / <c>userId</c> / <c>fileId</c> / <c>url</c> / <c>updatedAt</c>
/// 不属于二进制结构，是上游实现从 LeanCloud 响应里附加进来的元数据，
/// 便于调用方拿到存档下载地址，这里保持同样的字段名以兼容既有用法。
/// </remarks>
public sealed class Summary
{
    /// <summary>存档版本号。</summary>
    [JsonPropertyName("saveVersion")]
    public int SaveVersion { get; set; }

    /// <summary>课题模式段位。</summary>
    [JsonPropertyName("challengeModeRank")]
    public int ChallengeModeRank { get; set; }

    /// <summary>RKS。</summary>
    [JsonPropertyName("rankingScore")]
    public float RankingScore { get; set; }

    /// <summary>游戏版本号。</summary>
    [JsonPropertyName("gameVersion")]
    public int GameVersion { get; set; }

    /// <summary>头像 id。</summary>
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;

    /// <summary>
    /// 12 项统计，每三个一组对应 EZ / HD / IN / AT：
    /// 依次为已游玩数、Full Combo 数、All Perfect 数。
    /// </summary>
    [JsonPropertyName("progress")]
    public int[] Progress { get; set; } = new int[12];

    /// <summary>LeanCloud 中 <c>_GameSave</c> 记录的 objectId。</summary>
    [JsonPropertyName("objectId")]
    public string? ObjectId { get; set; }

    /// <summary>LeanCloud 用户 objectId。</summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    /// <summary>存档文件在 LeanCloud <c>_File</c> 中的 objectId。</summary>
    [JsonPropertyName("fileId")]
    public string? FileId { get; set; }

    /// <summary>存档文件下载地址。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>存档最后更新时间。</summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }
}
