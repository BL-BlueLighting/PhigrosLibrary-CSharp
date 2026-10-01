namespace PhigrosLibrary.Login;

/// <summary>
/// 一个区服对应的 TapTap 登录端点。
/// </summary>
/// <param name="WebHost">账号服务地址，负责设备码与令牌。</param>
/// <param name="ApiHost">开放平台地址，负责账号资料。</param>
/// <param name="ClientId">OAuth 客户端 id，即 Phigros 在 TapTap 上的应用 id。</param>
public sealed record TapTapEndpoints(string WebHost, string ApiHost, string ClientId)
{
    /// <summary>国服端点。</summary>
    public static TapTapEndpoints China { get; } = new(
        "https://accounts.tapapis.cn", "https://open.tapapis.cn", "rAK3FfdieFob2Nn8Am");

    /// <summary>国际服端点。</summary>
    /// <remarks>
    /// 上游实现中国际服仅切换了域名，客户端 id 仍沿用国服的，这里保持一致；
    /// 若 TapTap 后续启用独立的国际服应用，可通过本类型的构造函数覆盖。
    /// </remarks>
    public static TapTapEndpoints Global { get; } = new(
        "https://accounts.tapapis.com", "https://open.tapapis.com", "rAK3FfdieFob2Nn8Am");

    /// <summary>取指定区服的端点。</summary>
    public static TapTapEndpoints For(TapTapRegion region)
        => region == TapTapRegion.Global ? Global : China;

    /// <summary>请求设备码的地址。</summary>
    public string DeviceCodeUrl => $"{WebHost}/oauth2/v1/device/code";

    /// <summary>用设备码换取令牌的地址。</summary>
    public string TokenUrl => $"{WebHost}/oauth2/v1/token";

    /// <summary>拉取账号资料的地址。</summary>
    public string ProfileUrl => $"{ApiHost}/account/profile/v1?client_id={ClientId}";
}
