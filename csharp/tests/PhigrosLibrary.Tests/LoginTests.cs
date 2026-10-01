using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PhigrosLibrary.Login;

namespace PhigrosLibrary.Tests;

public sealed class LoginTests
{
    private const string Kid = "1/abcDEF";
    private const string MacKey = "zCgtfVWxajWHl2MYVoFMNdPn0E2YXrV4mTjWjLKp";

    private static readonly DateTimeOffset FixedNow = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private static readonly DateTimeOffset ExpiryCheckEpoch = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    // ---------- MAC 签名 ----------

    [Fact]
    public void AuthorizationHeaderMatchesTapTapSpec()
    {
        const string url = "https://open.tapapis.cn/account/profile/v1?client_id=rAK3FfdieFob2Nn8Am";
        const string nonce = "MTIzNDU2Nzg5MDEyMzQ1Ng==";
        string time = "1700000000";

        // 按协议描述独立拼出期望值：时间戳\n随机串\n方法\n路径与查询串\n主机名\n端口\n\n
        string signatureBase = $"{time}\n{nonce}\nGET\n/account/profile/v1?client_id=rAK3FfdieFob2Nn8Am\nopen.tapapis.cn\n443\n\n";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(MacKey));
        string mac = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureBase)));

        string header = TapTapAuthorization.CreateHeader(url, "GET", Kid, MacKey, FixedNow, nonce);

        Assert.Equal($"MAC id=\"{Kid}\", ts=\"{time}\", nonce=\"{nonce}\", mac=\"{mac}\"", header);
    }

    [Fact]
    public void AuthorizationTimestampIsPaddedToTenDigits()
    {
        var early = DateTimeOffset.FromUnixTimeSeconds(123);

        string header = TapTapAuthorization.CreateHeader(
            "https://open.tapapis.cn/x", "GET", Kid, MacKey, early, "n");

        Assert.Contains("ts=\"0000000123\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public void NonceIsRandomPerCall()
    {
        var nonces = Enumerable.Range(0, 8).Select(_ => TapTapAuthorization.CreateNonce()).ToList();

        Assert.Equal(nonces.Count, nonces.Distinct(StringComparer.Ordinal).Count());
        // 16 字节的 base64 固定为 24 个字符。
        Assert.All(nonces, nonce => Assert.Equal(24, nonce.Length));
    }

    // ---------- 设备码 ----------

    [Fact]
    public async Task RequestQrCodeSendsMultipartFormAndParsesResponse()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""
            {
              "device_code": "device-123",
              "expires_in": 300,
              "interval": 2,
              "qrcode_url": "https://accounts.tapapis.cn/qr/device-123"
            }
            """));
        using var client = new TapTapLoginClient(TapTapRegion.China, handler.CreateClient(), new FakeTimeProvider(FixedNow));

        var data = await client.RequestQrCodeAsync();

        Assert.Equal("device-123", data.DeviceCode);
        Assert.Equal("https://accounts.tapapis.cn/qr/device-123", data.Url);
        Assert.Equal(300, data.ExpiresInSeconds);
        Assert.Equal(2, data.IntervalSeconds);
        Assert.Equal(FixedNow, data.CreatedAt);
        Assert.Equal(32, data.DeviceId.Length);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://accounts.tapapis.cn/oauth2/v1/device/code", request.Uri.ToString());
        Assert.StartsWith("multipart/form-data", request.ContentType, StringComparison.Ordinal);

        string body = request.Content!;
        Assert.Contains("name=\"client_id\"", body, StringComparison.Ordinal);
        Assert.Contains(TapTapEndpoints.China.ClientId, body, StringComparison.Ordinal);
        Assert.Contains("name=\"response_type\"", body, StringComparison.Ordinal);
        Assert.Contains("device_code", body, StringComparison.Ordinal);
        Assert.Contains("name=\"scope\"", body, StringComparison.Ordinal);
        Assert.Contains("public_profile", body, StringComparison.Ordinal);
        Assert.Contains("name=\"platform\"", body, StringComparison.Ordinal);
        Assert.Contains(data.DeviceId, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestQrCodeAcceptsWrappedEnvelope()
    {
        // 真实接口把设备码包在 data 信封里，上游用的也是 data.qrcode_url。
        var handler = new StubHttpHandler(_ => StubResponses.Json("""
            {
              "success": true,
              "data": {
                "device_code": "device-123",
                "expires_in": 300,
                "interval": 2,
                "qrcode_url": "https://accounts.tapapis.cn/qr/device-123"
              }
            }
            """));
        using var client = new TapTapLoginClient(TapTapRegion.China, handler.CreateClient());

        var data = await client.RequestQrCodeAsync();

        Assert.Equal("device-123", data.DeviceCode);
        Assert.Equal("https://accounts.tapapis.cn/qr/device-123", data.Url);
        Assert.Equal(300, data.ExpiresInSeconds);
        Assert.Equal(2, data.IntervalSeconds);
    }

    [Fact]
    public async Task RequestQrCodeReportsServerError()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "success": false, "error": "invalid_client" }"""));
        using var client = new TapTapLoginClient(TapTapRegion.China, handler.CreateClient());

        var exception = await Assert.ThrowsAsync<PhigrosLoginException>(() => client.RequestQrCodeAsync());
        Assert.Contains("invalid_client", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestQrCodeErrorIncludesRawResponse()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "unexpected": "shape" }"""));
        using var client = new TapTapLoginClient(TapTapRegion.China, handler.CreateClient());

        var exception = await Assert.ThrowsAsync<PhigrosLoginException>(() => client.RequestQrCodeAsync());
        Assert.Contains("unexpected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestQrCodeUsesGlobalEndpointsForGlobalRegion()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""
            { "device_code": "d", "expires_in": 1, "interval": 1, "qrcode_url": "https://example.invalid/qr" }
            """));
        using var client = new TapTapLoginClient(TapTapRegion.Global, handler.CreateClient());

        await client.RequestQrCodeAsync();

        Assert.Equal("https://accounts.tapapis.com/oauth2/v1/device/code", Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task RequestQrCodeThrowsWhenDeviceCodeMissing()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "error": "invalid_request" }"""));
        using var client = new TapTapLoginClient(TapTapRegion.China, handler.CreateClient());

        await Assert.ThrowsAsync<PhigrosLoginException>(() => client.RequestQrCodeAsync());
    }

    // ---------- 轮询令牌 ----------

    [Fact]
    public async Task PollTokenReturnsPendingWhileUserHasNotScanned()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "success": false, "error": "authorization_pending" }"""));
        using var client = CreateLoginClient(handler);

        var result = await client.PollTokenAsync(CreateQrCodeData());

        Assert.Equal(TokenPollStatus.Pending, result.Status);
        Assert.Null(result.Token);
        Assert.False(result.IsSucceeded);
    }

    [Fact]
    public async Task PollTokenReportsSlowDown()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "error": "slow_down" }"""));
        using var client = CreateLoginClient(handler);

        var result = await client.PollTokenAsync(CreateQrCodeData());

        Assert.Equal(TokenPollStatus.SlowDown, result.Status);
    }

    [Theory]
    [InlineData("expired_token")]
    [InlineData("access_denied")]
    [InlineData("invalid_grant")]
    public async Task PollTokenThrowsOnTerminalErrors(string error)
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json($$"""{ "error": "{{error}}" }"""));
        using var client = CreateLoginClient(handler);

        var exception = await Assert.ThrowsAsync<PhigrosLoginException>(
            () => client.PollTokenAsync(CreateQrCodeData()));

        Assert.Contains(error, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollTokenReadsTokenFromEnvelopeAndKeepsRawPayload()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json($$"""
            {
              "success": true,
              "data": {
                "kid": "{{Kid}}",
                "access_token": "1/abc",
                "token_type": "mac",
                "mac_key": "{{MacKey}}",
                "mac_algorithm": "hmac-sha-1",
                "scope": "public_profile"
              }
            }
            """));
        using var client = CreateLoginClient(handler);

        var result = await client.PollTokenAsync(CreateQrCodeData());

        Assert.Equal(TokenPollStatus.Succeeded, result.Status);
        var token = result.Token!;
        Assert.Equal(Kid, token.Kid);
        Assert.Equal("1/abc", token.AccessToken);
        Assert.Equal(MacKey, token.MacKey);
        Assert.True(token.HasProfileScope);

        // 原文要完整保留，换 sessionToken 时需要连同账号资料一起提交。
        Assert.Equal("1/abc", token.Payload["access_token"]!.GetValue<string>());

        string body = Assert.Single(handler.Requests).Content!;
        Assert.Contains("grant_type", body, StringComparison.Ordinal);
        Assert.Contains("device_token", body, StringComparison.Ordinal);
        Assert.Contains("hmac-sha-1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollTokenAcceptsFlatResponse()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json($$"""
            { "kid": "{{Kid}}", "access_token": "1/abc", "mac_key": "{{MacKey}}", "scope": "public_profile" }
            """));
        using var client = CreateLoginClient(handler);

        var result = await client.PollTokenAsync(CreateQrCodeData());

        Assert.Equal(TokenPollStatus.Succeeded, result.Status);
        Assert.Equal("1/abc", result.Token!.AccessToken);
    }

    [Fact]
    public async Task WaitForTokenPollsUntilTheUserConfirms()
    {
        int calls = 0;
        var handler = new StubHttpHandler(_ =>
        {
            calls++;
            return calls < 3
                ? StubResponses.Json("""{ "success": false }""")
                : StubResponses.Json($$"""
                    { "success": true, "data": { "kid": "{{Kid}}", "access_token": "1/abc",
                      "mac_key": "{{MacKey}}", "scope": "public_profile" } }
                    """);
        });
        using var client = CreateLoginClient(handler);

        var token = await client.WaitForTokenAsync(CreateQrCodeData());

        Assert.Equal("1/abc", token.AccessToken);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task WaitForTokenStopsWhenQrCodeExpired()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "success": false }"""));
        var time = new FakeTimeProvider(FixedNow);
        using var client = new TapTapLoginClient(TapTapRegion.China, handler.CreateClient(), time);

        var data = CreateQrCodeData();          // 已在 FixedNow 生成，有效期 300 秒
        time.Now = FixedNow.AddSeconds(301);    // 时间快进到过期之后

        var exception = await Assert.ThrowsAsync<PhigrosLoginException>(() => client.WaitForTokenAsync(data));

        Assert.Contains("过期", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    // ---------- 账号资料 ----------

    [Fact]
    public async Task GetProfileSendsMacAuthorizationAndUnwrapsData()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""
            { "success": true, "data": { "openid": "open-1", "name": "鸽子" } }
            """));
        using var client = CreateLoginClient(handler);

        var profile = await client.GetProfileAsync(CreateToken());

        Assert.Equal("open-1", profile["openid"]!.GetValue<string>());
        Assert.Equal("鸽子", profile["name"]!.GetValue<string>());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://open.tapapis.cn/account/profile/v1?client_id=rAK3FfdieFob2Nn8Am", request.Uri.ToString());

        string authorization = request.Header("Authorization")!;
        Assert.StartsWith($"MAC id=\"{Kid}\", ts=\"", authorization, StringComparison.Ordinal);
        Assert.Contains("nonce=\"", authorization, StringComparison.Ordinal);
        Assert.Contains("mac=\"", authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProfileRequiresPublicProfileScope()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("{}"));
        using var client = CreateLoginClient(handler);

        var token = CreateToken(scope: "user_info");

        var exception = await Assert.ThrowsAsync<PhigrosLoginException>(() => client.GetProfileAsync(token));
        Assert.Contains("public_profile", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    // ---------- 换取 sessionToken ----------

    [Fact]
    public async Task ExchangeSessionTokenPostsAuthDataAndSignsRequest()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""
            { "objectId": "user-1", "username": "phi_abc", "sessionToken": "session-token-1" }
            """));
        var time = new FakeTimeProvider(FixedNow);
        using var login = new PhigrosLogin(TapTapRegion.China, null, handler.CreateClient(), time);

        var profile = new JsonObject { ["openid"] = "open-1", ["name"] = "鸽子" };
        var payload = new JsonObject { ["access_token"] = "1/abc", ["mac_key"] = "k" };

        var result = await login.ExchangeSessionTokenAsync(profile, payload);

        Assert.Equal("session-token-1", result.SessionToken);
        Assert.Equal("user-1", result.UserId);
        Assert.Equal("phi_abc", result.Username);
        Assert.Equal("鸽子", result.Nickname);
        Assert.Equal(TapTapRegion.China, result.Region);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/users", request.Uri.ToString());
        Assert.Equal(LeanCloudApp.ChinaAppId, request.Header("X-LC-Id"));

        // 签名是 md5(时间戳 + AppKey) + "," + 时间戳，时间戳取自注入的时钟。
        long timestamp = FixedNow.ToUnixTimeSeconds();
        string expectedHash = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes($"{timestamp}{LeanCloudApp.ChinaAppKey}"))).ToLowerInvariant();
        Assert.Equal($"{expectedHash},{timestamp}", request.Header("X-LC-Sign"));

        var body = JsonNode.Parse(request.Content!)!;
        var authData = body["authData"]!["taptap"]!;
        Assert.Equal("open-1", authData["openid"]!.GetValue<string>());
        Assert.Equal("1/abc", authData["access_token"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExchangeSessionTokenUsesGlobalApp()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "sessionToken": "t" }"""));
        using var login = new PhigrosLogin(TapTapRegion.Global, null, handler.CreateClient());

        await login.ExchangeSessionTokenAsync(new JsonObject(), new JsonObject());

        var request = Assert.Single(handler.Requests);
        Assert.StartsWith(LeanCloudApp.GlobalBaseUrl, request.Uri.ToString(), StringComparison.Ordinal);
        Assert.Equal(LeanCloudApp.GlobalAppId, request.Header("X-LC-Id"));
    }

    [Fact]
    public async Task ExchangeSessionTokenThrowsOnErrorPayload()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "code": 210, "error": "invalid authData" }"""));
        using var login = new PhigrosLogin(TapTapRegion.China, null, handler.CreateClient());

        var exception = await Assert.ThrowsAsync<PhigrosLoginException>(
            () => login.ExchangeSessionTokenAsync(new JsonObject(), new JsonObject()));

        Assert.Contains("invalid authData", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeSessionTokenThrowsWhenTokenMissing()
    {
        var handler = new StubHttpHandler(_ => StubResponses.Json("""{ "objectId": "user-1" }"""));
        using var login = new PhigrosLogin(TapTapRegion.China, null, handler.CreateClient());

        await Assert.ThrowsAsync<PhigrosLoginException>(
            () => login.ExchangeSessionTokenAsync(new JsonObject(), new JsonObject()));
    }

    [Fact]
    public async Task ExchangeSessionTokenReportsNetworkFailure()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("no route to host"));
        using var login = new PhigrosLogin(TapTapRegion.China, null, handler.CreateClient());

        await Assert.ThrowsAsync<PhigrosLoginException>(
            () => login.ExchangeSessionTokenAsync(new JsonObject(), new JsonObject()));
    }

    // ---------- 整体流程 ----------

    [Fact]
    public async Task LoginRunsTheWholeFlow()
    {
        var handler = new StubHttpHandler(request => request.Uri.AbsolutePath switch
        {
            "/oauth2/v1/device/code" => StubResponses.Json("""
                { "device_code": "device-123", "expires_in": 300, "interval": 1,
                  "qrcode_url": "https://accounts.tapapis.cn/qr/device-123" }
                """),
            "/oauth2/v1/token" => StubResponses.Json($$"""
                { "success": true, "data": { "kid": "{{Kid}}", "access_token": "1/abc",
                  "mac_key": "{{MacKey}}", "scope": "public_profile" } }
                """),
            "/account/profile/v1" => StubResponses.Json("""{ "success": true, "data": { "openid": "open-1", "name": "鸽子" } }"""),
            "/1.1/users" => StubResponses.Json("""{ "objectId": "user-1", "sessionToken": "session-token-1" }"""),
            _ => StubResponses.Json("{}", HttpStatusCode.NotFound),
        });

        using var login = new PhigrosLogin(TapTapRegion.China, null, handler.CreateClient());
        QrCodeData? shown = null;

        var result = await login.LoginAsync(qr => shown = qr);

        Assert.Equal("session-token-1", result.SessionToken);
        Assert.Equal("鸽子", result.Nickname);
        Assert.Equal("https://accounts.tapapis.cn/qr/device-123", shown!.Url);
        Assert.Equal(4, handler.Requests.Count);

        // 用登录结果直接构造查分客户端时，应用配置会被带上。
        using var client = result.CreateClient();
        Assert.Equal(LeanCloudApp.China, client.Api.App);
    }

    // ---------- 辅助 ----------

    private static QrCodeData CreateQrCodeData() => new()
    {
        DeviceCode = "device-123",
        DeviceId = "device-id",
        Url = "https://accounts.tapapis.cn/qr/device-123",
        ExpiresInSeconds = 300,
        IntervalSeconds = 1,
        CreatedAt = FixedNow,
    };

    private static TapTapToken CreateToken(string scope = "public_profile") => new()
    {
        Kid = Kid,
        AccessToken = "1/abc",
        MacKey = MacKey,
        Scope = scope,
        Payload = new JsonObject { ["access_token"] = "1/abc" },
    };

    private static TapTapLoginClient CreateLoginClient(StubHttpHandler handler)
        => new(TapTapRegion.China, handler.CreateClient(), new FakeTimeProvider(FixedNow));
}
