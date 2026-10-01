using PhigrosLibrary.Models;

namespace PhigrosLibrary.Tests;

public sealed class DifficultyTableTests
{
    [Fact]
    public void ParsesFourLevels()
    {
        var table = DifficultyTable.Parse("Song.Artist\t1.0\t6.5\t12.6\t15.7");

        Assert.Equal(1, table.Count);
        Assert.Equal([1.0f, 6.5f, 12.6f, 15.7f], table["Song.Artist"]);
    }

    [Fact]
    public void MissingAtLevelBecomesZero()
    {
        var table = DifficultyTable.Parse("Song.Artist\t1.0\t6.5\t12.6");

        Assert.Equal(0f, table["Song.Artist"][(int)SongDifficulty.At]);
    }

    [Fact]
    public void IgnoresBlankLines()
    {
        var table = DifficultyTable.Parse("\nA\t1\t2\t3\n\nB\t4\t5\t6\n");

        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void RejectsMalformedLines()
    {
        Assert.Throws<PhigrosDataException>(() => DifficultyTable.Parse("只有一列"));
        Assert.Throws<PhigrosDataException>(() => DifficultyTable.Parse("A\t不是数字\t2\t3"));
    }

    [Fact]
    public void RejectsEmptyTable()
    {
        Assert.Throws<PhigrosDataException>(() => DifficultyTable.Parse("\n\n"));
    }

    [Fact]
    public void ThrowsForUnknownSong()
    {
        var table = DifficultyTable.Parse("A\t1\t2\t3");

        Assert.Throws<PhigrosDataException>(() => table.Get("不存在"));
        Assert.False(table.TryGet("不存在", out _));
    }

    [Fact]
    public void LoadsRealDifficultyTable()
    {
        var table = TestData.Difficulties;

        Assert.True(table.Count > 200, $"真实定数表只有 {table.Count} 首曲目。");
        Assert.Equal([1.0f, 6.5f, 12.6f, 0f], table["Glaciaxion.SunsetRay"]);
        Assert.Equal([4.5f, 10.4f, 13.6f, 15.7f], table["Credits.Frums"]);
    }
}
