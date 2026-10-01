using System.Net;
using PhigrosLibrary.Api;
using PhigrosLibrary.Models;

namespace PhigrosLibrary.Tests;

public sealed class LeanCloudClientTests
{
    private const string SessionToken = "fake-session-token";

    [Fact]
    public async Task ParsesSaveRecordFromResponse()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""
            {
              "results": [
                {
                  "objectId": "save-1",
                  "summary": "c3VtbWFyeQ==",
                  "updatedAt": "2024-05-06T07:08:09.000Z",
                  "gameFile": { "objectId": "file-1", "url": "https://example.invalid/save.zip" },
                  "user": { "__type": "Pointer", "objectId": "user-1" }
                }
              ]
            }
            """));
        using var client = new LeanCloudClient(handler.CreateClient());

        var record = await client.GetSaveRecordAsync(SessionToken);

        Assert.Equal("save-1", record.ObjectId);
        Assert.Equal("c3VtbWFyeQ==", record.Summary);
        Assert.Equal("file-1", record.FileId);
        Assert.Equal("https://example.invalid/save.zip", record.Url);
        Assert.Equal("user-1", record.UserId);
        Assert.Equal(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero), record.UpdatedAt);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/classes/_GameSave?limit=1",
            request.Uri.ToString());
        Assert.Equal(SessionToken, request.Header("X-LC-Session"));
        Assert.Equal(LeanCloudClient.AppId, request.Header("X-LC-Id"));
        Assert.Equal(LeanCloudClient.AppKey, request.Header("X-LC-Key"));
    }

    [Fact]
    public async Task ParsesNickname()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "nickname": "鸽子" }"""));
        using var client = new LeanCloudClient(handler.CreateClient());

        Assert.Equal("鸽子", await client.GetNicknameAsync(SessionToken));

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/users/me", request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttachesCloudMetadataToSummary()
    {
        var summary = new Summary
        {
            SaveVersion = 1,
            ChallengeModeRank = 7,
            RankingScore = 16.5f,
            GameVersion = 87,
            Avatar = "Introduction.0",
            Progress = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
        };

        var handler = new StubHttpHandler(_ => StubResponses.Json($$"""
            {
              "results": [
                {
                  "objectId": "save-1",
                  "summary": "{{SaveCodec.WriteSummary(summary)}}",
                  "updatedAt": "2024-05-06T07:08:09.000Z",
                  "gameFile": { "objectId": "file-1", "url": "https://example.invalid/save.zip" },
                  "user": { "objectId": "user-1" }
                }
              ]
            }
            """));
        using var client = new LeanCloudClient(handler.CreateClient());

        var parsed = await client.GetSummaryAsync(SessionToken);

        Assert.Equal(16.5f, parsed.RankingScore);
        Assert.Equal("save-1", parsed.ObjectId);
        Assert.Equal("user-1", parsed.UserId);
        Assert.Equal("file-1", parsed.FileId);
        Assert.Equal("https://example.invalid/save.zip", parsed.Url);
    }

    [Fact]
    public async Task ThrowsWithServerErrorMessage()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "code": 210, "error": "sessionToken 无效" }"""));
        using var client = new LeanCloudClient(handler.CreateClient());

        var exception = await Assert.ThrowsAsync<PhigrosApiException>(
            () => client.GetNicknameAsync(SessionToken));

        Assert.Contains("sessionToken 无效", exception.Message, StringComparison.Ordinal);
        Assert.Contains("210", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrowsWhenNoSaveExists()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "results": [] }"""));
        using var client = new LeanCloudClient(handler.CreateClient());

        await Assert.ThrowsAsync<PhigrosApiException>(() => client.GetSaveRecordAsync(SessionToken));
    }

    [Fact]
    public async Task ThrowsForNonJsonBody()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Text("<html>502</html>", HttpStatusCode.BadGateway));
        using var client = new LeanCloudClient(handler.CreateClient());

        await Assert.ThrowsAsync<PhigrosApiException>(() => client.GetSaveRecordAsync(SessionToken));
    }

    [Fact]
    public async Task DownloadsSaveBytes()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var handler = new StubHttpHandler(request => request.Uri.Host == "example.invalid"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }
            : StubResponses.Json("{}"));
        using var client = new LeanCloudClient(handler.CreateClient());

        Assert.Equal(payload, await client.DownloadSaveAsync("https://example.invalid/save.zip"));
    }

    [Fact]
    public async Task ThrowsWhenDownloadFails()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new LeanCloudClient(handler.CreateClient());

        await Assert.ThrowsAsync<PhigrosApiException>(
            () => client.DownloadSaveAsync("https://example.invalid/missing.zip"));
    }

    [Fact]
    public async Task DoesNotDisposeInjectedHttpClient()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "nickname": "鸽子" }"""));
        var http = handler.CreateClient();

        using (var client = new LeanCloudClient(http))
        {
            Assert.Equal("鸽子", await client.GetNicknameAsync(SessionToken));
        }

        // 注入的 HttpClient 归调用方所有，客户端析构后仍可继续使用。
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("https://example.invalid/")).StatusCode);
    }

    [Fact]
    public async Task UsesRegionSpecificAppConfiguration()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "results": [] }"""));
        using var client = new LeanCloudClient(handler.CreateClient(), LeanCloudApp.Global);

        await Assert.ThrowsAsync<PhigrosApiException>(() => client.GetSaveRecordAsync(SessionToken));

        var request = Assert.Single(handler.Requests);
        Assert.StartsWith(LeanCloudApp.GlobalBaseUrl, request.Uri.ToString(), StringComparison.Ordinal);
        Assert.Equal(LeanCloudApp.GlobalAppId, request.Header("X-LC-Id"));
        Assert.Equal(LeanCloudApp.GlobalAppKey, request.Header("X-LC-Key"));
    }
}
