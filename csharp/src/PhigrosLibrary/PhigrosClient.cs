using PhigrosLibrary.Api;
using PhigrosLibrary.Models;

namespace PhigrosLibrary;

/// <summary>
/// 面向调用方的门面：用一个 sessionToken 完成"查昵称 / 查概要 / 拉存档 / 算 B19"。
/// </summary>
/// <remarks>
/// 与上游 C 实现的 handle 一样，概要和存档会缓存在实例上，同一实例内多次调用不会重复请求。
/// 需要重新拉取时调用 <see cref="InvalidateCache"/>。
/// </remarks>
public sealed class PhigrosClient : IDisposable
{
    private readonly LeanCloudClient _api;
    private readonly bool _ownsApi;
    private Summary? _summary;
    private SaveData? _save;
    private bool _disposed;

    /// <param name="sessionToken">
    /// 玩家 sessionToken，可通过 <see cref="Login.PhigrosLogin"/> 扫码获取。
    /// </param>
    /// <param name="difficulties">定数表，计算 B19 / 期望 ACC 时必需。</param>
    /// <param name="httpClient">自定义的 HTTP 客户端，便于复用连接池或注入测试桩。</param>
    /// <param name="app">区服对应的应用配置；为 <see langword="null"/> 时使用国服。</param>
    public PhigrosClient(
        string sessionToken,
        DifficultyTable? difficulties = null,
        HttpClient? httpClient = null,
        LeanCloudApp? app = null)
        : this(sessionToken, new LeanCloudClient(httpClient, app), difficulties, ownsApi: true)
    {
    }

    /// <param name="sessionToken">玩家 sessionToken。</param>
    /// <param name="api">自定义的接口客户端。</param>
    /// <param name="difficulties">定数表，计算 B19 / 期望 ACC 时必需。</param>
    public PhigrosClient(string sessionToken, LeanCloudClient api, DifficultyTable? difficulties = null)
        : this(sessionToken, api, difficulties, ownsApi: false)
    {
    }

    private PhigrosClient(
        string sessionToken, LeanCloudClient api, DifficultyTable? difficulties, bool ownsApi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        ArgumentNullException.ThrowIfNull(api);

        SessionToken = sessionToken;
        _api = api;
        _ownsApi = ownsApi;
        Difficulties = difficulties;
    }

    /// <summary>当前实例使用的 sessionToken。</summary>
    public string SessionToken { get; }

    /// <summary>定数表，可以随时替换（例如游戏更新后重新加载）。</summary>
    public DifficultyTable? Difficulties { get; set; }

    /// <summary>底层接口客户端，可用于设置限流等。</summary>
    public LeanCloudClient Api => _api;

    /// <summary>加载定数表文件。</summary>
    public PhigrosClient LoadDifficulties(string path)
    {
        Difficulties = DifficultyTable.Load(path);
        return this;
    }

    /// <summary>取玩家昵称。</summary>
    public Task<string> GetNicknameAsync(CancellationToken cancellationToken = default)
        => _api.GetNicknameAsync(SessionToken, cancellationToken);

    /// <summary>取玩家概要。结果会被缓存。</summary>
    public async Task<Summary> GetSummaryAsync(CancellationToken cancellationToken = default)
        => _summary ??= await _api.GetSummaryAsync(SessionToken, cancellationToken).ConfigureAwait(false);

    /// <summary>取玩家存档。结果会被缓存。</summary>
    public async Task<SaveData> GetSaveAsync(CancellationToken cancellationToken = default)
        => _save ??= await _api.GetSaveAsync(SessionToken, cancellationToken).ConfigureAwait(false);

    /// <summary>计算 B19，需要先设置 <see cref="Difficulties"/>。</summary>
    public async Task<B19Result> GetBest19Async(CancellationToken cancellationToken = default)
    {
        var save = await GetSaveAsync(cancellationToken).ConfigureAwait(false);
        return CreateCalculator().ComputeBest19(save);
    }

    /// <summary>计算推分所需 ACC，需要先设置 <see cref="Difficulties"/>。</summary>
    public async Task<IReadOnlyList<ExpectEntry>> GetExpectAsync(CancellationToken cancellationToken = default)
    {
        var save = await GetSaveAsync(cancellationToken).ConfigureAwait(false);
        return CreateCalculator().ComputeExpect(save.GameRecord);
    }

    /// <summary>创建一个绑定当前定数表的计算器。</summary>
    public RksCalculator CreateCalculator()
    {
        if (Difficulties is null)
        {
            throw new PhigrosDataException(
                "尚未设置定数表，无法计算 RKS。请通过构造函数传入，或调用 LoadDifficulties 加载 difficulty.tsv。");
        }

        return new RksCalculator(Difficulties);
    }

    /// <summary>清空概要 / 存档缓存，下次调用会重新请求。</summary>
    public void InvalidateCache()
    {
        _summary = null;
        _save = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsApi) _api.Dispose();
    }
}
