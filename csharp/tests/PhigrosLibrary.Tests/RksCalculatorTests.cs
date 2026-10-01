using PhigrosLibrary.Models;
using PhigrosLibrary.Testing;

namespace PhigrosLibrary.Tests;

public sealed class RksCalculatorTests
{
    private static readonly RksCalculator Calculator = new(TestData.MiniTable);

    [Theory]
    [InlineData(15f, 100f, 15f)]     // 满分时单曲 RKS 等于定数
    [InlineData(15f, 55f, 0f)]       // 公式下界
    [InlineData(16f, 77.5f, 4f)]     // (77.5-55)/45 = 0.5，平方后乘 16
    [InlineData(10f, 91f, 6.4f)]     // (91-55)/45 = 0.8，平方后乘 10
    public void ComputesSingleRks(float difficulty, float accuracy, float expected)
    {
        Assert.Equal(expected, RksCalculator.ComputeRks(difficulty, accuracy), precision: 5);
    }

    [Theory]
    [InlineData(16f, 90f)]
    [InlineData(17f, 99.5f)]
    [InlineData(13f, 55f)]
    public void ExpectedAccuracyInvertsRks(float difficulty, float accuracy)
    {
        float rks = RksCalculator.ComputeRks(difficulty, accuracy);

        Assert.Equal(accuracy, RksCalculator.ComputeExpectedAccuracy(difficulty, rks), precision: 4);
    }

    [Fact]
    public void Best19CountsPhiTwiceWhenItIsAlsoTopRated()
    {
        var records = TestData.Records(
            ("A.Artist", SongDifficulty.In, 100f, true),   // 定数 10，满分
            ("B.Artist", SongDifficulty.At, 100f, true),   // 定数 16，满分，难度最高
            ("C.Artist", SongDifficulty.At, 90f, false));  // 定数 17，ACC 90

        var result = Calculator.ComputeBest19(records);

        Assert.NotNull(result.Phi);
        Assert.Equal("B.Artist", result.Phi.Id);
        Assert.Equal(16f, result.Phi.Rks, precision: 5);

        Assert.Equal(3, result.Best.Count);
        Assert.Equal(["B.Artist", "C.Artist", "A.Artist"], result.Best.Select(entry => entry.Id));

        // (φ 16 + B 16 + C 17×((90-55)/45)² + A 10) / 20
        float expected = (16f + 16f + 17f * MathF.Pow(35f / 45f, 2) + 10f) / 20f;
        Assert.Equal(expected, result.Rks, precision: 5);
    }

    [Fact]
    public void PhiIsHighestDifficultyAllPerfectNotHighestRks()
    {
        var records = TestData.Records(
            ("A.Artist", SongDifficulty.In, 100f, true),  // 定数 10 的 φ
            ("C.Artist", SongDifficulty.At, 99f, false)); // RKS 更高但不是满分

        var result = Calculator.ComputeBest19(records);

        Assert.NotNull(result.Phi);
        Assert.Equal("A.Artist", result.Phi.Id);

        // φ 之外的榜首应当是 C。
        Assert.Equal("C.Artist", result.Best[0].Id);
        Assert.True(result.Best[0].Rks > result.Phi.Rks);
    }

    [Fact]
    public void WithoutAllPerfectPhiIsNullAndCountsAsZero()
    {
        var records = TestData.Records(("A.Artist", SongDifficulty.Hd, 90f, false));

        var result = Calculator.ComputeBest19(records);

        Assert.Null(result.Phi);
        Assert.Single(result.Best);

        float expected = RksCalculator.ComputeRks(5f, 90f) / 20f;
        Assert.Equal(expected, result.Rks, precision: 5);
    }

    [Fact]
    public void IgnoresAccuracyBelowThreshold()
    {
        var records = TestData.Records(("A.Artist", SongDifficulty.In, 54.9f, false));

        var result = Calculator.ComputeBest19(records);

        Assert.Empty(result.Best);
        Assert.Null(result.Phi);
        Assert.Equal(0f, result.Rks);
    }

    [Fact]
    public void SkipsAtDifficultyOfSongsWithoutIt()
    {
        // D 只有三个难度，写在 AT 槽位上的成绩不应被计入。
        var records = TestData.Records(("D.Artist", SongDifficulty.At, 100f, true));

        var result = Calculator.ComputeBest19(records);

        Assert.Empty(result.Best);
        Assert.Null(result.Phi);
    }

    [Fact]
    public void ThrowsForSongMissingFromDifficultyTable()
    {
        var records = TestData.Records(("不存在.曲目", SongDifficulty.In, 100f, true));

        var exception = Assert.Throws<PhigrosDataException>(() => Calculator.ComputeBest19(records));
        Assert.Contains("不存在.曲目", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CountsProgressPerDifficulty()
    {
        var records = TestData.Records(
            ("A.Artist", SongDifficulty.Ez, 90f, false),
            ("A.Artist", SongDifficulty.Hd, 95f, true),
            ("A.Artist", SongDifficulty.In, 100f, true));

        int[] progress = Calculator.ComputeProgress(records);

        // 每三个一组：已游玩 / Full Combo / All Perfect
        Assert.Equal([1, 0, 0, 1, 1, 0, 1, 1, 1, 0, 0, 0], progress);
    }

    [Fact]
    public void ExpectExcludesRecordsAlreadyInBest19()
    {
        var records = TestData.Records(("A.Artist", SongDifficulty.In, 100f, true));

        var expect = Calculator.ComputeExpect(records);

        // 已经打过的那个难度不应出现在建议列表里，其余难度都要给出目标 ACC。
        Assert.DoesNotContain(expect, entry => entry.Level == SongDifficulty.In);
        Assert.Contains(expect, entry => entry.Level == SongDifficulty.Ez);
        Assert.Contains(expect, entry => entry.Level == SongDifficulty.At);
        Assert.DoesNotContain(expect, entry => entry.Id == "D.Artist" && entry.Level == SongDifficulty.At);
    }

    [Fact]
    public void ExpectTargetsTheSameThresholdForEveryEntry()
    {
        var save = SampleSaveFactory.Create(TestData.Difficulties, seed: 11, songCount: 40);
        var calculator = new RksCalculator(TestData.Difficulties);

        var expect = calculator.ComputeExpect(save.GameRecord);
        Assert.NotEmpty(expect);

        // 每条建议的 expect 都应当恰好把单曲 RKS 抬到同一个门槛值。
        float target = RksCalculator.ComputeRks(expect[0].Difficulty, expect[0].Expect);
        foreach (var entry in expect)
        {
            Assert.Equal(target, RksCalculator.ComputeRks(entry.Difficulty, entry.Expect), precision: 3);
            Assert.True(entry.Rks <= target + 1e-3f);
        }
    }

    [Fact]
    public void Best19OnSampleSaveIsConsistentWithItsParts()
    {
        var save = SampleSaveFactory.Create(TestData.Difficulties, seed: 3, songCount: 50);
        var calculator = new RksCalculator(TestData.Difficulties);

        var result = calculator.ComputeBest19(save.GameRecord);

        Assert.True(result.Best.Count <= RksCalculator.BestCount);
        Assert.NotNull(result.Phi);

        float sum = result.Phi.Rks + result.Best.Sum(entry => entry.Rks);
        Assert.Equal(sum / RksCalculator.RecordCount, result.Rks, precision: 5);

        // 定数表里的曲目都应当能被解析出成绩。
        Assert.All(result.Best, entry => Assert.True(entry.Difficulty > 0));
    }
}
