using System.Net;
using System.Text;

namespace PhigrosLibrary.Tests;

/// <summary>
/// 一次被记录的请求。
/// </summary>
/// <remarks>
/// 内容是当场读出来的快照：<see cref="HttpRequestMessage"/> 发完之后会被释放，
/// 事后再去读它的 <c>Content</c> 会抛异常。
/// </remarks>
internal sealed record StubRequest(
    HttpMethod Method,
    Uri Uri,
    string? Content,
    string? ContentType,
    IReadOnlyDictionary<string, string[]> Headers)
{
    /// <summary>取首个同名请求头的值。</summary>
    public string? Header(string name) => Headers.TryGetValue(name, out string[]? values) ? values.FirstOrDefault() : null;
}

/// <summary>
/// 按给定脚本应答的 <see cref="HttpMessageHandler"/>，让测试不产生任何真实网络请求。
/// </summary>
internal sealed class StubHttpHandler(Func<StubRequest, HttpResponseMessage> responder) : HttpMessageHandler
{
    /// <summary>按顺序记录收到的请求。</summary>
    public List<StubRequest> Requests { get; } = [];

    public HttpClient CreateClient() => new(this);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? content = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var recorded = new StubRequest(
            request.Method,
            request.RequestUri!,
            content,
            request.Content?.Headers.ContentType?.ToString(),
            request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase));

        Requests.Add(recorded);
        return responder(recorded);
    }
}

internal static class StubResponses
{
    /// <summary>构造一个 JSON 响应。</summary>
    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>构造一个纯文本响应。</summary>
    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
}
