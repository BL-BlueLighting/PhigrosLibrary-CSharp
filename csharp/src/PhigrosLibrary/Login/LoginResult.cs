using System.Text.Json.Nodes;

namespace PhigrosLibrary.Login;

/// <summary>
/// 一次登录的产物。
/// </summary>
/// <remarks>
/// <see cref="SessionToken"/> 就是 <see cref="PhigrosClient"/> 需要的那串凭证，
/// 有效期较长，建议由调用方自行保存，不必每次查分都重新扫码。
/// </remarks>
public sealed class LoginResult
{
    /// <summary>LeanCloud 的 sessionToken，用于后续查分。</summary>
    public required string SessionToken { get; init; }

    /// <summary>LeanCloud 用户 objectId。</summary>
    public string? UserId { get; init; }

    /// <summary>LeanCloud 用户名，通常是自动生成的。</summary>
    public string? Username { get; init; }

    /// <summary>TapTap 昵称，取自账号资料，可能为空（游戏会使用自定义昵称）。</summary>
    public string? Nickname { get; init; }

    /// <summary>账号资料原文。</summary>
    public required JsonObject Profile { get; init; }

    /// <summary>登录所用区服。</summary>
    public required TapTapRegion Region { get; init; }

    /// <summary>登录所用应用配置，可直接交给 <see cref="PhigrosClient"/>。</summary>
    public required LeanCloudApp App { get; init; }

    /// <summary>用本次登录结果构造一个查分客户端。</summary>
    /// <param name="difficulties">定数表，计算 B19 时必需。</param>
    /// <param name="httpClient">自定义 HTTP 客户端。</param>
    public PhigrosClient CreateClient(DifficultyTable? difficulties = null, HttpClient? httpClient = null)
        => new(SessionToken, difficulties, httpClient, App);
}
