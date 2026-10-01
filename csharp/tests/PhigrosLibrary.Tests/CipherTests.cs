using PhigrosLibrary.Crypto;

namespace PhigrosLibrary.Tests;

public sealed class CipherTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1000)]
    public void RoundTripsPlaintext(int length)
    {
        byte[] plaintext = CreatePlaintext(length);

        byte[] ciphertext = PhigrosCipher.Encrypt(plaintext);
        Assert.Equal(0, ciphertext.Length % PhigrosCipher.BlockSize);
        Assert.NotEqual(plaintext, ciphertext);
        Assert.Equal(plaintext, PhigrosCipher.Decrypt(ciphertext));
    }

    [Fact]
    public void PaddingAlwaysAddsABlock()
    {
        // PKCS7 在明文正好是整块时会再补一个整块，这是与上游实现一致的行为。
        Assert.Equal(32, PhigrosCipher.Encrypt(new byte[16]).Length);
        Assert.Equal(16, PhigrosCipher.Encrypt(new byte[15]).Length);
    }

    [Fact]
    public void IsDeterministic()
    {
        // IV 固定，因此同样的明文必然得到同样的密文——这正是存档能逐字节往返的前提。
        byte[] plaintext = CreatePlaintext(37);

        Assert.Equal(PhigrosCipher.Encrypt(plaintext), PhigrosCipher.Encrypt(plaintext));
    }

    [Fact]
    public void EntryKeepsVersionByteInClear()
    {
        byte[] plaintext = CreatePlaintext(20);

        byte[] entry = PhigrosCipher.EncryptEntry(3, plaintext);

        Assert.Equal(3, entry[0]);
        Assert.Equal(1 + PhigrosCipher.Encrypt(plaintext).Length, entry.Length);

        var (version, decrypted) = PhigrosCipher.DecryptEntry(entry);
        Assert.Equal(3, version);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void RejectsMisalignedCiphertext()
    {
        Assert.Throws<PhigrosFormatException>(() => PhigrosCipher.Decrypt(new byte[17]));
        Assert.Throws<PhigrosFormatException>(() => PhigrosCipher.Decrypt([]));
    }

    [Fact]
    public void RejectsCorruptedPadding()
    {
        // 密文长度合法但内容随机，PKCS7 填充几乎必然不合法。
        var corrupted = new byte[32];
        Array.Fill(corrupted, (byte)0xAB);

        Assert.Throws<PhigrosFormatException>(() => PhigrosCipher.Decrypt(corrupted));
    }

    [Fact]
    public void RejectsInvalidEntryVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PhigrosCipher.EncryptEntry(256, CreatePlaintext(8)));
    }

    private static byte[] CreatePlaintext(int length)
    {
        var plaintext = new byte[length];
        for (int i = 0; i < length; i++) plaintext[i] = (byte)(i * 31 + 7);
        return plaintext;
    }
}
