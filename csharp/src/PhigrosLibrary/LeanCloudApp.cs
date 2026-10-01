namespace PhigrosLibrary;

/// <summary>
/// 一个区服对应的 LeanCloud 应用配置。
/// </summary>
/// <param name="AppId">LeanCloud AppId，作为 <c>X-LC-Id</c> 发送。</param>
/// <param name="AppKey">LeanCloud AppKey，用于计算 <c>X-LC-Sign</c>。</param>
/// <param name="BaseUrl">接口根地址，末尾带斜杠。</param>
/// <param name="Region">该应用所属的区服。</param>
public sealed record LeanCloudApp(string AppId, string AppKey, string BaseUrl, TapTapRegion Region)
{
    /// <summary>国服 Phigros 的 AppId。</summary>
    public const string ChinaAppId = "rAK3FfdieFob2Nn8Am";

    /// <summary>国服 Phigros 的 AppKey。</summary>
    public const string ChinaAppKey = "Qr9AEqtuoSVS3zeD6iVbM4ZC0AtkJcQ89tywVyi0";

    /// <summary>国服接口根地址。</summary>
    public const string ChinaBaseUrl = "https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/";

    /// <summary>国际服 Phigros 的 AppId。</summary>
    public const string GlobalAppId = "kviehleldgxsagpozb";

    /// <summary>国际服 Phigros 的 AppKey。</summary>
    public const string GlobalAppKey = "tG9CTm0LDD736k9HMM9lBZrbeBGRmUkjSfNLDNib";

    /// <summary>国际服接口根地址。</summary>
    public const string GlobalBaseUrl = "https://kviehlel.cloud.ap-sg.tapapis.com/1.1/";

    /// <summary>国服。</summary>
    public static LeanCloudApp China { get; } = new(ChinaAppId, ChinaAppKey, ChinaBaseUrl, TapTapRegion.China);

    /// <summary>国际服。</summary>
    public static LeanCloudApp Global { get; } = new(GlobalAppId, GlobalAppKey, GlobalBaseUrl, TapTapRegion.Global);

    /// <summary>取指定区服的应用配置。</summary>
    public static LeanCloudApp For(TapTapRegion region)
        => region == TapTapRegion.Global ? Global : China;
}
