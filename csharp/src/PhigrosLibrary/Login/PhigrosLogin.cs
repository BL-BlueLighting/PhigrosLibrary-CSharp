using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhigrosLibrary.Login;

/// <summary>
/// 扫码登录，换取可长期使用的 LeanCloud sessionToken。
/// </summary>
/// <remarks>
/// <para>流程：</para>
/// <list type="number">
/// <item>向 TapTap 请求设备码，把 <see cref="QrCodeData.Url"/> 作为二维码展示给用户；</item>
/// <item>用户用 TapTap App 扫码确认，<see cref="WaitForTokenAsync"/> 拿到访问令牌；</item>
/// <item>用令牌拉取账号资料；</item>
/// <item>把资料作为 <c>authData.taptap</c> 提交给 LeanCloud，换回 sessionToken。</item>
/// </list>
/// <para>
/// 最省事的用法是 <see cref="LoginAsync"/>，它把四步串起来，只把二维码回调出来。
/// </para>
/// <para>
/// 需要留意的是最后一步会在 LeanCloud 上创建或登录一个用户，
/// 这是本库唯一会写入云端数据的操作。
/// </para>
/// </remarks>
public sealed class PhigrosLogin : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    /// <param name="region">账号所在区服。</param>
    /// <param name="app">覆盖默认的应用配置；为 <see langword="null"/> 时按区服自动选择。</param>
    /// <param name="httpClient">自定义 HTTP 客户端；为 <see langword="null"/> 时自行创建并负责释放。</param>
    /// <param name="timeProvider">时间源，便于测试。</param>
    public PhigrosLogin(
        TapTapRegion region = TapTapRegion.China,
        LeanCloudApp? app = null,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (httpClient is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _ownsHttpClient = true;
        }
        else
        {
            _http = httpClient;
        }

        Region = region;
        App = app ?? LeanCloudApp.For(region);
        TapTap = new TapTapLoginClient(region, _http, _timeProvider);
    }

    /// <summary>当前区服。</summary>
    public TapTapRegion Region { get; }

    /// <summary>当前使用的 LeanCloud 应用配置。</summary>
    public LeanCloudApp App { get; }

    /// <summary>底层的 TapTap 登录客户端，可单独调用其分步接口。</summary>
    public TapTapLoginClient TapTap { get; }

    /// <summary>请求一次扫码登录。</summary>
    public Task<QrCodeData> RequestQrCodeAsync(CancellationToken cancellationToken = default)
        => TapTap.RequestQrCodeAsync(null, cancellationToken);

    /// <summary>等待用户扫码确认。</summary>
    public Task<TapTapToken> WaitForTokenAsync(
        QrCodeData data, Action<TokenPollResult>? progress = null, CancellationToken cancellationToken = default)
        => TapTap.WaitForTokenAsync(data, progress, cancellationToken);

    /// <summary>一次走完全流程，二维码地址通过 <paramref name="onQrCodeReady"/> 交给调用方展示。</summary>
    public async Task<LoginResult> LoginAsync(
        Action<QrCodeData> onQrCodeReady, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onQrCodeReady);

        var data = await RequestQrCodeAsync(cancellationToken).ConfigureAwait(false);
        onQrCodeReady(data);

        var token = await WaitForTokenAsync(data, null, cancellationToken).ConfigureAwait(false);
        return await CompleteAsync(token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>用已拿到的令牌完成最后两步：取资料并换取 sessionToken。</summary>
    public async Task<LoginResult> CompleteAsync(
        TapTapToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var profile = await TapTap.GetProfileAsync(token, cancellationToken).ConfigureAwait(false);
        return await ExchangeSessionTokenAsync(profile, token.Payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 把 TapTap 账号资料提交给 LeanCloud，换回 sessionToken。
    /// </summary>
    /// <param name="profile">账号资料，来自 <see cref="TapTapLoginClient.GetProfileAsync"/>。</param>
    /// <param name="tokenPayload">令牌原文，来自 <see cref="TapTapToken.Payload"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<LoginResult> ExchangeSessionTokenAsync(
        JsonObject profile, JsonObject tokenPayload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(tokenPayload);

        // 上游把资料与令牌字段合并后一起作为 authData.taptap 提交。
        var authData = new JsonObject();
        foreach ((string key, JsonNode? value) in profile) authData[key] = value?.DeepClone();
        foreach ((string key, JsonNode? value) in tokenPayload) authData[key] = value?.DeepClone();

        var requestBody = new JsonObject { ["authData"] = new JsonObject { ["taptap"] = authData } };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.BaseUrl}users")
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-LC-Id", App.AppId);
        request.Headers.TryAddWithoutValidation("X-LC-Sign", CreateSign());

        var body = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        string? sessionToken = GetString(body, "sessionToken");
        if (string.IsNullOrEmpty(sessionToken))
            throw new PhigrosLoginException("LeanCloud 没有返回 sessionToken，登录失败。");

        return new LoginResult
        {
            SessionToken = sessionToken,
            UserId = GetString(body, "objectId"),
            Username = GetString(body, "username"),
            Nickname = GetString(profile, "name") ?? GetString(profile, "nickname"),
            Profile = profile,
            Region = Region,
            App = App,
        };
    }

    /// <summary>
    /// 计算 LeanCloud 的 <c>X-LC-Sign</c>：<c>md5(时间戳 + AppKey) + "," + 时间戳</c>，时间戳为秒。
    /// </summary>
    private string CreateSign()
    {
        long timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        string hash = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(
                string.Concat(timestamp.ToString(CultureInfo.InvariantCulture), App.AppKey))));

        return $"{hash.ToLowerInvariant()},{timestamp.ToString(CultureInfo.InvariantCulture)}";
    }

    private async Task<JsonObject> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new PhigrosLoginException("请求 LeanCloud 失败，请检查网络连接。", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PhigrosLoginException("请求 LeanCloud 超时。", ex);
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            JsonObject body;
            try
            {
                body = JsonNode.Parse(text) as JsonObject
                    ?? throw new PhigrosLoginException("LeanCloud 返回的不是 JSON 对象。");
            }
            catch (JsonException ex)
            {
                throw new PhigrosLoginException(
                    $"LeanCloud 返回了非 JSON 内容：HTTP {(int)response.StatusCode}。", ex);
            }

            if (GetString(body, "error") is { } error)
            {
                string code = GetString(body, "code") is { } value ? $"（code {value}）" : string.Empty;
                throw new PhigrosLoginException($"登录被拒绝：{error}{code}");
            }

            return body;
        }
    }

    private static string? GetString(JsonObject body, string name)
    {
        if (!body.TryGetPropertyValue(name, out var node) || node is not JsonValue value) return null;

        if (value.TryGetValue(out string? text)) return text;
        if (value.TryGetValue(out int number)) return number.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue(out long big)) return big.ToString(CultureInfo.InvariantCulture);

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        TapTap.Dispose();
        if (_ownsHttpClient) _http.Dispose();
    }
}
