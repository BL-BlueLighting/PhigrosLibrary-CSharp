using System.Net.Http.Headers;
using System.Text.Json;
using PhigrosLibrary.Models;

namespace PhigrosLibrary.Api;

/// <summary>
/// 与鸽游（TapTap 的 LeanCloud 服务）通信的客户端。
/// </summary>
/// <remarks>
/// <para>
/// 只做读操作：取昵称、取概要、下载存档。所有请求都只带 <c>X-LC-Session</c>（即玩家的 sessionToken），
/// 不会修改云端数据。
/// </para>
/// <para>
/// 上游项目明确要求不要大规模查分。如果要在机器人里高频调用，请设置
/// <see cref="MinimumRequestInterval"/> 做限流，避免给对方服务器造成压力。
/// </para>
/// </remarks>
public sealed class LeanCloudClient : IDisposable
{
    /// <summary>国服 LeanCloud 接口根地址。</summary>
    public const string ApiBaseUrl = LeanCloudApp.ChinaBaseUrl;

    /// <summary>国服 Phigros 在 LeanCloud 上的 AppId。</summary>
    public const string AppId = LeanCloudApp.ChinaAppId;

    /// <summary>国服 Phigros 在 LeanCloud 上的 AppKey。</summary>
    public const string AppKey = LeanCloudApp.ChinaAppKey;

    private const string GameSaveClass = "classes/_GameSave";
    private const string UserAgent = "LeanCloud-CSharp-SDK/1.0.3";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _throttleGate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private bool _disposed;

    /// <param name="httpClient">
    /// 外部提供的 <see cref="HttpClient"/>。传入时其 <see cref="HttpClient.Timeout"/> 等设置由调用方负责，
    /// 本类不会修改；为 <see langword="null"/> 时自行创建一个并负责释放。
    /// </param>
    /// <param name="app">区服对应的应用配置；为 <see langword="null"/> 时使用国服。</param>
    public LeanCloudClient(HttpClient? httpClient = null, LeanCloudApp? app = null)
    {
        App = app ?? LeanCloudApp.China;

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

    /// <summary>当前使用的应用配置。</summary>
    public LeanCloudApp App { get; }

    /// <summary>
    /// 两次请求之间的最小间隔，用于限流；默认为 <see cref="TimeSpan.Zero"/>（不限流）。
    /// </summary>
    public TimeSpan MinimumRequestInterval { get; set; } = TimeSpan.Zero;

    /// <summary>取玩家昵称。</summary>
    public async Task<string> GetNicknameAsync(
        string sessionToken, CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync("users/me", sessionToken, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        ThrowIfError(root);

        if (!root.TryGetProperty("nickname", out var nickname) || nickname.ValueKind != JsonValueKind.String)
            throw new PhigrosApiException("接口返回的数据中没有 nickname 字段。");

        return nickname.GetString() ?? string.Empty;
    }

    /// <summary>取玩家概要，同时带上存档下载地址等云端元数据。</summary>
    public async Task<Summary> GetSummaryAsync(
        string sessionToken, CancellationToken cancellationToken = default)
    {
        var record = await GetSaveRecordAsync(sessionToken, cancellationToken).ConfigureAwait(false);
        var summary = SaveCodec.ParseSummary(record.Summary);

        summary.ObjectId = record.ObjectId;
        summary.UserId = record.UserId;
        summary.FileId = record.FileId;
        summary.Url = record.Url;
        summary.UpdatedAt = record.UpdatedAt;
        return summary;
    }

    /// <summary>取玩家存档记录（含 base64 概要、存档下载地址等）。</summary>
    public async Task<CloudSaveRecord> GetSaveRecordAsync(
        string sessionToken, CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            $"{GameSaveClass}?limit=1", sessionToken, cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        ThrowIfError(root);

        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
        {
            throw new PhigrosApiException("该账号没有云存档，或 sessionToken 已失效。");
        }

        var result = results[0];

        string summary = GetRequiredString(result, "summary", "存档记录");
        string objectId = GetString(result, "objectId") ?? string.Empty;
        string? userId = result.TryGetProperty("user", out var user) ? GetString(user, "objectId") : null;

        string url = string.Empty;
        string? fileId = null;
        if (result.TryGetProperty("gameFile", out var gameFile))
        {
            fileId = GetString(gameFile, "objectId");
            url = GetString(gameFile, "url") ?? string.Empty;
        }

        if (url.Length == 0)
            throw new PhigrosApiException("存档记录中没有 gameFile.url 字段。");

        DateTimeOffset? updatedAt = null;
        if (result.TryGetProperty("updatedAt", out var updated)
            && updated.ValueKind == JsonValueKind.String
            && updated.TryGetDateTimeOffset(out var parsed))
        {
            updatedAt = parsed;
        }

        return new CloudSaveRecord(objectId, summary, fileId, url, updatedAt, userId);
    }

    /// <summary>下载存档 zip 的原始字节。</summary>
    public async Task<byte[]> DownloadSaveAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new PhigrosApiException(
                $"下载存档失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}。");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>取并解析玩家存档。</summary>
    public async Task<SaveData> GetSaveAsync(
        string sessionToken, CancellationToken cancellationToken = default)
    {
        var record = await GetSaveRecordAsync(sessionToken, cancellationToken).ConfigureAwait(false);
        byte[] zip = await DownloadSaveAsync(record.Url, cancellationToken).ConfigureAwait(false);
        return SaveCodec.Parse(zip);
    }

    private async Task<JsonDocument> GetJsonAsync(
        string relativeUrl, string sessionToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);

        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, App.BaseUrl + relativeUrl);
            request.Headers.Add("X-LC-Id", App.AppId);
            request.Headers.Add("X-LC-Key", App.AppKey);
            request.Headers.Add("X-LC-Session", sessionToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd(UserAgent);
            return request;
        }, cancellationToken).ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new PhigrosApiException(
                $"接口返回了非 JSON 内容：HTTP {(int)response.StatusCode}。", ex);
        }

        // LeanCloud 的错误响应形如 {"code":210,"error":"..."}，成功时也可能带 4xx 状态码之外的信息，
        // 因此这里先解析 body，再在调用方统一检查 error 字段。
        return document;
    }

    private static void ThrowIfError(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.String) return;

        string message = error.GetString() ?? "未知错误";
        string code = root.TryGetProperty("code", out var codeElement) ? $"（code {codeElement.GetRawText()}）" : string.Empty;
        throw new PhigrosApiException($"接口返回错误：{message}{code}");
    }

    private static string GetRequiredString(JsonElement element, string property, string context)
    {
        string? value = GetString(element, property);
        if (string.IsNullOrEmpty(value))
            throw new PhigrosApiException($"{context}中没有 {property} 字段。");

        return value;
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (MinimumRequestInterval > TimeSpan.Zero)
        {
            await _throttleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var elapsed = DateTimeOffset.UtcNow - _lastRequestAt;
                if (elapsed < MinimumRequestInterval)
                    await Task.Delay(MinimumRequestInterval - elapsed, cancellationToken).ConfigureAwait(false);

                _lastRequestAt = DateTimeOffset.UtcNow;
            }
            finally
            {
                _throttleGate.Release();
            }
        }

        try
        {
            return await _http.SendAsync(requestFactory(), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new PhigrosApiException("请求失败，请检查网络连接。", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PhigrosApiException("请求超时。", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsHttpClient) _http.Dispose();
        _throttleGate.Dispose();
    }
}
