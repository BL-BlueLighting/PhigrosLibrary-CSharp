using PhigrosLibrary.Binary;
using PhigrosLibrary.Models;

namespace PhigrosLibrary.Tests;

public sealed class SummaryTests
{
    [Fact]
    public void ParsesKnownLayout()
    {
        // 手工按磁盘顺序拼一份负载，验证字段顺序与各类型的编码。
        var writer = new ByteWriter();
        writer.WriteByte(3);          // saveVersion: u8
        writer.WriteUInt16(300);      // challengeModeRank: u16
        writer.WriteSingle(15.5f);    // rankingScore: f32
        writer.WriteVarshort(100);    // gameVersion: varshort（100 ≥ 128，占两字节）
        writer.WriteString("abc");    // avatar: 变长字符串
        for (int i = 0; i < 12; i++) writer.WriteUInt16((ushort)i); // progress: 12 × u16

        var summary = SaveCodec.ParseSummary(Convert.ToBase64String(writer.ToArray()));

        Assert.Equal(3, summary.SaveVersion);
        Assert.Equal(300, summary.ChallengeModeRank);
        Assert.Equal(15.5f, summary.RankingScore);
        Assert.Equal(100, summary.GameVersion);
        Assert.Equal("abc", summary.Avatar);
        Assert.Equal(Enumerable.Range(0, 12).ToArray(), summary.Progress);
    }

    [Fact]
    public void RoundTrips()
    {
        var summary = new Summary
        {
            SaveVersion = 4,
            ChallengeModeRank = 1234,
            RankingScore = 16.7234f,
            GameVersion = 87,
            Avatar = "Introduction.0",
            Progress = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
        };

        var parsed = SaveCodec.ParseSummary(SaveCodec.WriteSummary(summary));

        Assert.Equal(summary.SaveVersion, parsed.SaveVersion);
        Assert.Equal(summary.ChallengeModeRank, parsed.ChallengeModeRank);
        Assert.Equal(summary.RankingScore, parsed.RankingScore);
        Assert.Equal(summary.GameVersion, parsed.GameVersion);
        Assert.Equal(summary.Avatar, parsed.Avatar);
        Assert.Equal(summary.Progress, parsed.Progress);
    }

    [Fact]
    public void ThrowsForInvalidBase64()
    {
        Assert.Throws<PhigrosFormatException>(() => SaveCodec.ParseSummary("这显然不是 base64!!"));
    }

    [Fact]
    public void ThrowsForTruncatedPayload()
    {
        var writer = new ByteWriter();
        writer.WriteByte(1);
        writer.WriteUInt16(0);

        Assert.Throws<PhigrosFormatException>(
            () => SaveCodec.ParseSummary(Convert.ToBase64String(writer.ToArray())));
    }
}
