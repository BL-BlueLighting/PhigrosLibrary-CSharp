using System.Text.Json.Serialization;

namespace PhigrosLibrary.Models;

/// <summary>
/// <c>user</c> 条目：玩家资料。
/// </summary>
public sealed class UserData
{
    [JsonPropertyName("showPlayerId")]
    public bool ShowPlayerId { get; set; }

    /// <summary>个性签名。</summary>
    [JsonPropertyName("selfIntro")]
    public string SelfIntro { get; set; } = string.Empty;

    /// <summary>头像 id。</summary>
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;

    /// <summary>名片背景 id。</summary>
    [JsonPropertyName("background")]
    public string Background { get; set; } = string.Empty;
}
