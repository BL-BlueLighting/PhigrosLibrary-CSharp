using PhigrosLibrary.Binary;

namespace PhigrosLibrary.Tests;

public sealed class VarshortTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(200)]
    [InlineData(300)]
    [InlineData(2047)]
    [InlineData(ByteWriter.VarshortMax)]
    public void RoundTrips(int value)
    {
        var writer = new ByteWriter();
        writer.WriteVarshort(value);

        var reader = new ByteReader(writer.ToArray());
        Assert.Equal(value, reader.ReadVarshort());
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 2)]
    [InlineData(ByteWriter.VarshortMax, 2)]
    public void UsesOneByteBelow128(int value, int expectedBytes)
    {
        var writer = new ByteWriter();
        writer.WriteVarshort(value);

        Assert.Equal(expectedBytes, writer.Length);
    }

    [Fact]
    public void MaxIsTwoBytesOfSevenPlusEightBits()
    {
        // 首字节给低 7 位，次字节给高 8 位，因此上限是 0x7F7F。
        Assert.Equal(0x7F7F, ByteWriter.VarshortMax);

        var writer = new ByteWriter();
        writer.WriteVarshort(ByteWriter.VarshortMax);

        Assert.Equal(new byte[] { 0xFF, 0xFE }, writer.ToArray());
        Assert.Equal(ByteWriter.VarshortMax, new ByteReader(writer.ToArray()).ReadVarshort());
    }

    [Fact]
    public void EncodesTwoByteFormLowBitsFirst()
    {
        var writer = new ByteWriter();
        writer.WriteVarshort(200);

        // 200 = 0b1100_1000：低 7 位 0x48 放进首字节并置最高位，剩余 0x01 放进第二字节。
        Assert.Equal(new byte[] { 0xC8, 0x01 }, writer.ToArray());
    }

    [Fact]
    public void RejectsValuesBeyondMax()
    {
        var writer = new ByteWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteVarshort(ByteWriter.VarshortMax + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteVarshort(-1));
    }

    [Fact]
    public void ReadsStringsWithSuffixInLength()
    {
        var writer = new ByteWriter();
        writer.WriteString("Chapter8", ".0"u8);

        // 长度字段包含 ".0" 两个字节，读回时应剥离。
        var reader = new ByteReader(writer.ToArray());
        Assert.Equal("Chapter8", reader.ReadString(suffixLength: 2));
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ReadsStringsWithoutSuffix()
    {
        var writer = new ByteWriter();
        writer.WriteString("sunset");

        var reader = new ByteReader(writer.ToArray());
        Assert.Equal("sunset", reader.ReadString());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ReadsMultiByteUtf8()
    {
        var writer = new ByteWriter();
        writer.WriteString("尊師");

        var reader = new ByteReader(writer.ToArray());
        Assert.Equal("尊師", reader.ReadString());
    }

    [Fact]
    public void ThrowsWhenReadingPastEnd()
    {
        var reader = new ByteReader([0x01, 0x02]);
        reader.ReadUInt16();

        Assert.Throws<PhigrosFormatException>(() => reader.ReadByte());
    }
}
