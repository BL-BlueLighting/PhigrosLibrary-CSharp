using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PhigrosLibrary.Login;

/// <summary>
/// TapTap 开放平台的 MAC 签名。
/// </summary>
/// <remarks>
/// 拉取账号资料时需要在 <c>Authorization</c> 头里带上签名，格式为：
/// <code>
/// MAC id="{kid}", ts="{时间戳}", nonce="{随机串}", mac="{签名}"
/// </code>
/// 其中签名是对下面这段文本做 HMAC-SHA1（密钥为 <c>mac_key</c>）后取 base64：
/// <code>
/// {时间戳}\n{随机串}\n{方法}\n{路径与查询串}\n{主机名}\n{端口}\n\n
/// </code>
/// 时间戳为秒，左侧补零到 10 位；端口在未显式指定时按协议取 443 / 80。
/// </remarks>
public static class TapTapAuthorization
{
    /// <summary>签名算法名。</summary>
    public const string Algorithm = "hmac-sha-1";

    /// <summary>按上述规则生成 <c>Authorization</c> 头的值。</summary>
    /// <param name="url">完整请求地址。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="keyId">令牌中的 <c>kid</c>。</param>
    /// <param name="macKey">令牌中的 <c>mac_key</c>。</param>
    /// <param name="timestamp">签名时间戳。</param>
    /// <param name="nonce">随机串，每次请求都应当不同。</param>
    public static string CreateHeader(
        string url, string method, string keyId, string macKey, DateTimeOffset timestamp, string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(nonce);

        var uri = new Uri(url, UriKind.Absolute);
        string time = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture).PadLeft(10, '0');
        string port = uri.IsDefaultPort
            ? (uri.Scheme == Uri.UriSchemeHttps ? "443" : "80")
            : uri.Port.ToString(CultureInfo.InvariantCulture);

        string signatureBase = string.Concat(
            time, "\n",
            nonce, "\n",
            method.ToUpperInvariant(), "\n",
            uri.PathAndQuery, "\n",
            uri.Host, "\n",
            port, "\n",
            "\n");

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(macKey));
        string mac = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureBase)));

        return $"MAC id=\"{keyId}\", ts=\"{time}\", nonce=\"{nonce}\", mac=\"{mac}\"";
    }

    /// <summary>生成一个随机串作为 <c>nonce</c>。</summary>
    public static string CreateNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
}
