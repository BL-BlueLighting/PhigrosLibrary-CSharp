namespace PhigrosLibrary;

/// <summary>
/// 账号所在的服务区。
/// </summary>
/// <remarks>
/// 国服与国际服使用两套独立的 TapTap 与 LeanCloud 端点，账号数据不互通。
/// </remarks>
public enum TapTapRegion
{
    /// <summary>国服（accounts.tapapis.cn / rak3ffdi.cloud.tds1.tapapis.cn）。</summary>
    China,

    /// <summary>国际服（accounts.tapapis.com / kviehlel.cloud.ap-sg.tapapis.com）。</summary>
    Global,
}
