using System.Security.Cryptography;

namespace PhigrosLibrary.Crypto;

/// <summary>
/// 存档条目的 AES-256-CBC 加解密。
/// </summary>
/// <remarks>
/// 存档 zip 中每个条目（<c>gameRecord</c> / <c>gameKey</c> / <c>gameProgress</c> / <c>user</c> / <c>settings</c>）
/// 的格式为：
/// <code>
/// +---------+--------------------------------------+
/// | version | AES-256-CBC(明文 + PKCS7 填充)        |
/// | 1 字节  | 剩余全部字节                          |
/// +---------+--------------------------------------+
/// </code>
/// 首个字节是明文版本号，不参与加密，也不属于密文长度。
/// </remarks>
public static class PhigrosCipher
{
    /// <summary>AES-256 密钥，32 字节。</summary>
    public static readonly byte[] Key =
        Convert.FromBase64String("6Jaa0qVAJZuXkZCLiOa/Ax5tIZVu+taKUN1V1nqwkks=");

    /// <summary>AES-CBC 初始向量，16 字节。</summary>
    public static readonly byte[] Iv =
        Convert.FromBase64String("Kk/wisgNYwcAV8WVGMgyUw==");

    /// <summary>AES 分组大小（字节），即 PKCS7 的填充粒度。</summary>
    public const int BlockSize = 16;

    /// <summary>解密一段 AES-256-CBC 密文并去除 PKCS7 填充。</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        if (ciphertext.Length == 0 || ciphertext.Length % BlockSize != 0)
        {
            throw new PhigrosFormatException(
                $"密文长度 {ciphertext.Length} 不是 {BlockSize} 的正整数倍，数据可能已损坏。");
        }

        using var aes = Aes.Create();
        aes.Key = Key;
        aes.Mode = CipherMode.CBC;

        try
        {
            return aes.DecryptCbc(ciphertext, Iv, PaddingMode.PKCS7);
        }
        catch (CryptographicException ex)
        {
            throw new PhigrosFormatException("解密失败：填充不合法，密钥或存档内容不匹配。", ex);
        }
    }

    /// <summary>加密一段明文，返回 AES-256-CBC 密文（含 PKCS7 填充）。</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        return aes.EncryptCbc(plaintext, Iv, PaddingMode.PKCS7);
    }

    /// <summary>
    /// 解析存档条目：首字节为版本号，其余为密文。
    /// </summary>
    /// <returns>版本号与解密后的明文。</returns>
    public static (int Version, byte[] Plaintext) DecryptEntry(ReadOnlySpan<byte> entry)
    {
        if (entry.Length < 1)
            throw new PhigrosFormatException("存档条目为空。");

        return (entry[0], Decrypt(entry[1..]));
    }

    /// <summary>
    /// 按存档条目的格式打包：<paramref name="version"/> 作为首字节明文写入，其余加密。
    /// </summary>
    public static byte[] EncryptEntry(int version, ReadOnlySpan<byte> plaintext)
    {
        if (version is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(version), version, "版本号必须在 0..255 之间。");

        byte[] ciphertext = Encrypt(plaintext);
        byte[] entry = new byte[ciphertext.Length + 1];
        entry[0] = (byte)version;
        ciphertext.CopyTo(entry, 1);
        return entry;
    }
}
