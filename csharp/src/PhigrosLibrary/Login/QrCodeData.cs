using System.Text.Json.Nodes;

namespace PhigrosLibrary.Login;

/// <summary>
/// 一次扫码登录的设备码信息。
/// </summary>
public sealed class QrCodeData
{
    /// <summary>设备码，用于轮询令牌。</summary>
    public required string DeviceCode { get; init; }

    /// <summary>本次登录的随机设备标识，轮询时要一并回传。</summary>
    public required string DeviceId { get; init; }

    /// <summary>二维码内容，用 TapTap App 扫描或在手机上打开。</summary>
    public required string Url { get; init; }

    /// <summary>有效期（秒）。</summary>
    public int ExpiresInSeconds { get; init; }

    /// <summary>建议的轮询间隔（秒）。</summary>
    public int IntervalSeconds { get; init; }

    /// <summary>发起时间，用于判断是否过期。</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>在给定时刻是否已过期。</summary>
    public bool IsExpired(DateTimeOffset now) =>
        now >= CreatedAt.AddSeconds(ExpiresInSeconds);

    /// <summary>剩余有效时间。</summary>
    public TimeSpan RemainingLifetime(DateTimeOffset now)
    {
        var remaining = CreatedAt.AddSeconds(ExpiresInSeconds) - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}

/// <summary>
/// 扫码成功后拿到的 TapTap 访问令牌。
/// </summary>
/// <remarks>
/// 字段名沿用 TapTap 的响应，<see cref="Payload"/> 保存原始 JSON，
/// 换取 LeanCloud sessionToken 时需要连同账号资料一起提交。
/// </remarks>
public sealed class TapTapToken
{
    /// <summary>密钥 id，用于计算请求签名。</summary>
    public required string Kid { get; init; }

    /// <summary>访问令牌。</summary>
    public required string AccessToken { get; init; }

    /// <summary>令牌类型，正常为 <c>mac</c>。</summary>
    public string TokenType { get; init; } = "mac";

    /// <summary>MAC 签名密钥。</summary>
    public required string MacKey { get; init; }

    /// <summary>签名算法，正常为 <c>hmac-sha-1</c>。</summary>
    public string MacAlgorithm { get; init; } = "hmac-sha-1";

    /// <summary>授权范围，以逗号分隔。</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>服务端返回的原始对象。</summary>
    public required JsonObject Payload { get; init; }

    /// <summary>
    /// 是否包含读取账号资料所需的权限。
    /// </summary>
    /// <remarks>
    /// 服务端返回的 scope 可能是逗号分隔，也可能是空格分隔
    /// （实际见到的是 <c>"public_profile compliance"</c>），两者都接受。
    /// </remarks>
    public bool HasProfileScope =>
        Scope.Split(
                [',', ' ', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains("public_profile", StringComparer.Ordinal);
}

/// <summary>轮询令牌的结果。</summary>
public enum TokenPollStatus
{
    /// <summary>用户尚未扫码或尚未确认，继续轮询。</summary>
    Pending,

    /// <summary>轮询过快，需要降低频率。</summary>
    SlowDown,

    /// <summary>已拿到令牌。</summary>
    Succeeded,
}

/// <summary>一次轮询的结果。</summary>
/// <param name="Status">轮询状态。</param>
/// <param name="Token">成功时的令牌，否则为 <see langword="null"/>。</param>
public sealed record TokenPollResult(TokenPollStatus Status, TapTapToken? Token)
{
    /// <summary>是否已拿到令牌。</summary>
    public bool IsSucceeded => Status == TokenPollStatus.Succeeded;
}
