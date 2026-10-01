using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhigrosLibrary.Login;

/// <summary>
/// TapTap 扫码登录（OAuth2 设备码流程）。
/// </summary>
/// <remarks>
/// <para>流程分三步，每一步都可以单独调用，便于把二维码先展示给用户：</para>
/// <list type="number">
/// <item><see cref="RequestQrCodeAsync"/> 取设备码与二维码地址；</item>
/// <item><see cref="WaitForTokenAsync"/> 轮询直到用户扫码确认，拿到访问令牌；</item>
/// <item><see cref="GetProfileAsync"/> 用令牌拉取账号资料。</item>
/// </list>
/// <para>
/// 拿到资料后还需要换成 LeanCloud 的 sessionToken 才能查分，见 <see cref="PhigrosLogin"/>。
/// </para>
/// </remarks>
public sealed class TapTapLoginClient : IDisposable
{
    private const string DeviceCodeGrantType = "device_code";
    private const string DeviceTokenGrantType = "device_token";
    private const string SdkVersion = "2.1";
    private const string TokenVersion = "1.0";
    private const string Platform = "unity";

    /// <summary>默认申请的权限。</summary>
    public static readonly string[] DefaultPermissions = ["public_profile"];

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    /// <param name="region">账号所在区服。</param>
    /// <param name="httpClient">自定义 HTTP 客户端；为 <see langword="null"/> 时自行创建并负责释放。</param>
    /// <param name="timeProvider">时间源，便于测试时控制超时判断。</param>
    public TapTapLoginClient(
        TapTapRegion region = TapTapRegion.China,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        Region = region;
        Endpoints = TapTapEndpoints.For(region);
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
    }

    /// <summary>当前区服。</summary>
    public TapTapRegion Region { get; }

    /// <summary>当前区服使用的端点。</summary>
    public TapTapEndpoints Endpoints { get; }

    /// <summary>
    /// 请求一次扫码登录，返回二维码地址与轮询所需的设备码。
    /// </summary>
    /// <param name="permissions">申请的权限，默认只需要 <c>public_profile</c>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<QrCodeData> RequestQrCodeAsync(
        IEnumerable<string>? permissions = null, CancellationToken cancellationToken = default)
    {
        string deviceId = Guid.NewGuid().ToString("N");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = Endpoints.ClientId,
            ["response_type"] = DeviceCodeGrantType,
            ["scope"] = string.Join(',', permissions ?? DefaultPermissions),
            ["version"] = SdkVersion,
            ["platform"] = Platform,
            ["info"] = $"{{\"device_id\":\"{deviceId}\"}}",
        };

        var body = await PostFormAsync(Endpoints.DeviceCodeUrl, fields, cancellationToken).ConfigureAwait(false);

        if (GetString(body, "error") is { } error)
            throw new PhigrosLoginException($"TapTap 拒绝了设备码请求：{error}");

        // 设备码同样包在 data 信封里，但为兼容起见两种形状都接受。
        var source = AsObject(body["data"]) ?? body;

        string? deviceCode = GetString(source, "device_code");
        string? url = GetString(source, "qrcode_url");
        if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(url))
        {
            throw new PhigrosLoginException(
                $"TapTap 没有返回设备码，请稍后重试。响应：{Preview(body)}");
        }

        return new QrCodeData
        {
            DeviceCode = deviceCode,
            DeviceId = deviceId,
            Url = url,
            ExpiresInSeconds = GetInt(source, "expires_in", 300),
            IntervalSeconds = Math.Max(GetInt(source, "interval", 2), 1),
            CreatedAt = _timeProvider.GetUtcNow(),
        };
    }

    /// <summary>
    /// 轮询一次令牌。用户尚未确认时返回 <see cref="TokenPollStatus.Pending"/>，
    /// 由调用方按 <see cref="QrCodeData.IntervalSeconds"/> 决定下一次调用时机。
    /// </summary>
    public async Task<TokenPollResult> PollTokenAsync(
        QrCodeData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = DeviceTokenGrantType,
            ["client_id"] = Endpoints.ClientId,
            ["secret_type"] = TapTapAuthorization.Algorithm,
            ["code"] = data.DeviceCode,
            ["version"] = TokenVersion,
            ["platform"] = Platform,
            ["info"] = $"{{\"device_id\":\"{data.DeviceId}\"}}",
        };

        var body = await PostFormAsync(Endpoints.TokenUrl, fields, cancellationToken).ConfigureAwait(false);

        if (TryReadToken(body, out var token)) return new TokenPollResult(TokenPollStatus.Succeeded, token);

        string? error = GetString(body, "error") ?? GetString(body, "error_description");
        if (error is not null && error.Contains("slow_down", StringComparison.OrdinalIgnoreCase))
            return new TokenPollResult(TokenPollStatus.SlowDown, null);

        if (error is not null && IsTerminalError(error))
            throw new PhigrosLoginException($"TapTap 登录失败：{error}");

        return new TokenPollResult(TokenPollStatus.Pending, null);
    }

    /// <summary>
    /// 轮询直到用户扫码确认、二维码过期或被取消。
    /// </summary>
    /// <param name="data">设备码信息。</param>
    /// <param name="progress">每次轮询后的回调，可用于输出提示。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<TapTapToken> WaitForTokenAsync(
        QrCodeData data,
        Action<TokenPollResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        int intervalSeconds = data.IntervalSeconds;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = _timeProvider.GetUtcNow();
            if (data.IsExpired(now))
                throw new PhigrosLoginException("二维码已过期，请重新发起登录。");

            var result = await PollTokenAsync(data, cancellationToken).ConfigureAwait(false);
            progress?.Invoke(result);

            if (result.Token is { } token) return token;

            if (result.Status == TokenPollStatus.SlowDown) intervalSeconds++;

            // 等待间隔与剩余有效期取较小值，避免二维码过期后还多等一轮。
            var delay = TimeSpan.FromSeconds(intervalSeconds);
            var remaining = data.RemainingLifetime(_timeProvider.GetUtcNow());
            if (remaining < delay) delay = remaining;

            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>用令牌拉取账号资料。</summary>
    public async Task<JsonObject> GetProfileAsync(
        TapTapToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (!token.HasProfileScope)
        {
            throw new PhigrosLoginException(
                $"令牌缺少 public_profile 权限（当前为 '{token.Scope}'），无法读取账号资料。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoints.ProfileUrl);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            TapTapAuthorization.CreateHeader(
                Endpoints.ProfileUrl,
                "GET",
                token.Kid,
                token.MacKey,
                _timeProvider.GetUtcNow(),
                TapTapAuthorization.CreateNonce()));

        var body = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        string? error = GetString(body, "error") ?? GetString(body, "error_description");
        if (error is not null)
            throw new PhigrosLoginException($"TapTap 拒绝读取账号资料：{error}（响应：{Preview(body)}）");

        // 资料可能直接放在 data 里，也可能平铺在顶层，两者都兼容。
        return AsObject(body["data"]) ?? body;
    }

    /// <summary>
    /// 一次走完"请求二维码 → 等待扫码 → 取资料"，
    /// 二维码地址通过 <paramref name="onQrCodeReady"/> 交给调用方展示。
    /// </summary>
    public async Task<JsonObject> LoginAsync(
        Action<QrCodeData> onQrCodeReady,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onQrCodeReady);

        var data = await RequestQrCodeAsync(null, cancellationToken).ConfigureAwait(false);
        onQrCodeReady(data);

        var token = await WaitForTokenAsync(data, null, cancellationToken).ConfigureAwait(false);
        return await GetProfileAsync(token, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTerminalError(string error) =>
        error.Contains("expired", StringComparison.OrdinalIgnoreCase)
        || error.Contains("denied", StringComparison.OrdinalIgnoreCase)
        || error.Contains("invalid", StringComparison.OrdinalIgnoreCase)
        || error.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadToken(JsonObject body, out TapTapToken token)
    {
        token = null!;

        // 成功响应可能是 {success, data:{...}} 信封，也可能直接平铺。
        var payload = AsObject(body["data"]) ?? body;
        if (GetString(payload, "access_token") is not { } accessToken || accessToken.Length == 0) return false;

        string macKey = GetString(payload, "mac_key") ?? string.Empty;
        if (macKey.Length == 0) return false;

        token = new TapTapToken
        {
            Kid = GetString(payload, "kid") ?? string.Empty,
            AccessToken = accessToken,
            TokenType = GetString(payload, "token_type") ?? "mac",
            MacKey = macKey,
            MacAlgorithm = GetString(payload, "mac_algorithm") ?? TapTapAuthorization.Algorithm,
            Scope = GetString(payload, "scope") ?? string.Empty,
            Payload = payload,
        };

        return true;
    }

    private async Task<JsonObject> PostFormAsync(
        string url, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = MultipartForm.Build(fields),
        };

        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
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
            throw new PhigrosLoginException("请求 TapTap 失败，请检查网络连接。", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PhigrosLoginException("请求 TapTap 超时。", ex);
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return JsonNode.Parse(text) as JsonObject
                    ?? throw new PhigrosLoginException("TapTap 返回的不是 JSON 对象。");
            }
            catch (JsonException ex)
            {
                throw new PhigrosLoginException(
                    $"TapTap 返回了非 JSON 内容：HTTP {(int)response.StatusCode}。", ex);
            }
        }
    }

    private static JsonObject? AsObject(JsonNode? node) => node as JsonObject;

    /// <summary>把响应截断成一段便于放进异常信息的文本。</summary>
    private static string Preview(JsonObject body)
    {
        string text = body.ToJsonString();
        return text.Length <= 300 ? text : $"{text[..300]}...";
    }

    private static string? GetString(JsonObject body, string name)
        => body.TryGetPropertyValue(name, out var node) && node is JsonValue value
           && value.TryGetValue(out string? text)
            ? text
            : null;

    private static int GetInt(JsonObject body, string name, int fallback)
    {
        if (!body.TryGetPropertyValue(name, out var node) || node is not JsonValue value) return fallback;

        if (value.TryGetValue(out int number)) return number;
        if (value.TryGetValue(out string? text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        return fallback;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsHttpClient) _http.Dispose();
    }
}

/// <summary>
/// 手工拼装 multipart/form-data 请求体。
/// </summary>
/// <remarks>
/// TapTap 的登录接口接收的是 <c>FormData</c> 形式的多部分表单，
/// 这里按同样的线格式拼装，避免 <see cref="MultipartFormDataContent"/>
/// 给每个字段附加上游没有的 <c>Content-Type</c>。
/// </remarks>
internal static class MultipartForm
{
    private const string Boundary = "----PhigrosLibraryBoundary";

    public static HttpContent Build(IReadOnlyDictionary<string, string> fields)
    {
        var builder = new StringBuilder();

        foreach ((string name, string value) in fields)
        {
            builder.Append("--").Append(Boundary).Append("\r\n");
            builder.Append("Content-Disposition: form-data; name=\"").Append(name).Append("\"\r\n\r\n");
            builder.Append(value).Append("\r\n");
        }

        builder.Append("--").Append(Boundary).Append("--\r\n");

        var content = new StringContent(builder.ToString(), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data")
        {
            Parameters = { new NameValueHeaderValue("boundary", Boundary) },
        };

        return content;
    }
}
