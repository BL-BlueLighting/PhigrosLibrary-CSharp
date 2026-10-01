using System.IO.Compression;
using PhigrosLibrary.Crypto;
using PhigrosLibrary.Models;
using PhigrosLibrary.Testing;

namespace PhigrosLibrary.Tests;

public sealed class SaveRoundTripTests
{
    private static DifficultyTable Difficulties => TestData.Difficulties;

    [Fact]
    public void SampleSaveSurvivesByteExactRoundTrip()
    {
        // 固定 IV 让加密结果可复现，因此"写出 → 解析 → 再写出"应当逐字节一致。
        byte[] written = SaveCodec.Write(SampleSaveFactory.Create(Difficulties));

        byte[] rewritten = SaveCodec.Write(SaveCodec.Parse(written));

        Assert.Equal(written, rewritten);
    }

    [Fact]
    public void ParsedModelMatchesOriginal()
    {
        var original = SampleSaveFactory.Create(Difficulties, seed: 7);
        var parsed = SaveCodec.Parse(SaveCodec.Write(original));

        Assert.Equal(original.GameRecord.Keys, parsed.GameRecord.Keys);

        foreach ((string songId, SongLevels levels) in original.GameRecord)
        {
            SongLevels actualLevels = parsed.GameRecord[songId];
            for (int level = 0; level < SongLevels.Count; level++)
            {
                LevelRecord? expected = levels[level];
                LevelRecord? actual = actualLevels[level];

                if (expected is null)
                {
                    Assert.Null(actual);
                    continue;
                }

                Assert.NotNull(actual);
                Assert.Equal(expected.Score, actual.Score);
                Assert.Equal(expected.Accuracy, actual.Accuracy);
                Assert.Equal(expected.FullCombo, actual.FullCombo);
            }
        }

        Assert.Equal(original.GameKey.Map.Keys, parsed.GameKey.Map.Keys);
        foreach ((string key, byte[] values) in original.GameKey.Map)
        {
            Assert.Equal(values, parsed.GameKey.Map[key]);
        }

        Assert.Equal(original.GameKey.LanotaReadKeys, parsed.GameKey.LanotaReadKeys);
        Assert.Equal(original.GameKey.CamelliaReadKey, parsed.GameKey.CamelliaReadKey);

        Assert.Equal(original.GameProgress.Completed, parsed.GameProgress.Completed);
        Assert.Equal(original.GameProgress.Money, parsed.GameProgress.Money);
        Assert.Equal(original.GameProgress.LegacyChapterFinished, parsed.GameProgress.LegacyChapterFinished);

        Assert.Equal(original.User.SelfIntro, parsed.User.SelfIntro);
        Assert.Equal(original.User.Avatar, parsed.User.Avatar);

        Assert.Equal(original.Settings.DeviceName, parsed.Settings.DeviceName);
        Assert.Equal(original.Settings.MusicVolume, parsed.Settings.MusicVolume);
        Assert.Equal(original.Settings.NoteScale, parsed.Settings.NoteScale);
    }

    [Fact]
    public void KeepsEntryVersions()
    {
        var save = SampleSaveFactory.Create(Difficulties);
        save.GameKey.Version = 1;       // 模拟旧版本存档
        save.GameProgress.Version = 2;

        var parsed = SaveCodec.Parse(SaveCodec.Write(save));

        Assert.Equal(1, parsed.GameKey.Version);
        Assert.Equal(2, parsed.GameProgress.Version);
    }

    [Fact]
    public void OlderVersionDropsFieldsAddedLater()
    {
        var save = SampleSaveFactory.Create(Difficulties);
        save.GameKey.CamelliaReadKey = true;
        save.GameProgress.Chapter8Passed = true;

        save.GameKey.Version = 1;       // v1 没有 camelliaReadKey
        save.GameProgress.Version = 2;  // v2 还没有 chapter8 系列字段

        var parsed = SaveCodec.Parse(SaveCodec.Write(save));

        Assert.False(parsed.GameKey.CamelliaReadKey);
        Assert.False(parsed.GameProgress.Chapter8Passed);
        // 同一版本内的字段不受影响。
        Assert.Equal(save.GameKey.LanotaReadKeys, parsed.GameKey.LanotaReadKeys);
        Assert.Equal(save.GameProgress.RandomVersionUnlocked, parsed.GameProgress.RandomVersionUnlocked);
    }

    [Fact]
    public void NewModelWritesEveryFieldItExposes()
    {
        // 回归测试：版本号默认为 0 会让写回时一个字段段都不写，字段被静默丢弃。
        var save = new SaveData();
        save.GameProgress.Completed = "第一章";
        save.GameProgress.Money = [1, 2, 3, 4, 5];
        save.GameKey.LanotaReadKeys = 9;

        var parsed = SaveCodec.Parse(SaveCodec.Write(save));

        Assert.Equal("第一章", parsed.GameProgress.Completed);
        Assert.Equal([1, 2, 3, 4, 5], parsed.GameProgress.Money);
        Assert.Equal(9, parsed.GameKey.LanotaReadKeys);
    }

    [Fact]
    public void PreservesUnknownTrailingBytesAsOverflow()
    {
        byte[] zip = SaveCodec.Write(SampleSaveFactory.Create(Difficulties));
        byte[] unknown = [0xDE, 0xAD, 0xBE, 0xEF];

        byte[] extended = ReplaceEntry(zip, SaveCodec.EntryGameProgress, entry =>
        {
            var (version, plaintext) = PhigrosCipher.DecryptEntry(entry);
            return PhigrosCipher.EncryptEntry(version, [.. plaintext, .. unknown]);
        });

        var save = SaveCodec.Parse(extended);
        Assert.Equal(unknown, Convert.FromBase64String(save.GameProgress.Overflow!));

        // 写回时这几个字节必须原样出现在 gameProgress 条目末尾。
        byte[] rewritten = SaveCodec.Write(save);
        Assert.Equal(GetEntry(extended, SaveCodec.EntryGameProgress), GetEntry(rewritten, SaveCodec.EntryGameProgress));
        Assert.Equal(extended, rewritten);
    }

    [Fact]
    public void HandlesSaveWithoutRecords()
    {
        var save = new SaveData();

        byte[] written = SaveCodec.Write(save);
        var parsed = SaveCodec.Parse(written);

        Assert.Empty(parsed.GameRecord);
        Assert.Equal(string.Empty, parsed.User.SelfIntro);
        Assert.Equal(0f, parsed.Settings.MusicVolume);
    }

    [Fact]
    public void KeepsSongsWithoutAnyRecord()
    {
        var save = new SaveData();
        save.GameRecord["Glaciaxion.SunsetRay"] = new SongLevels();

        var parsed = SaveCodec.Parse(SaveCodec.Write(save));

        Assert.True(parsed.GameRecord["Glaciaxion.SunsetRay"].IsEmpty);
    }

    [Fact]
    public void RejectsZipWithoutRequiredEntry()
    {
        byte[] zip = SaveCodec.Write(SampleSaveFactory.Create(Difficulties));
        byte[] broken = ReplaceEntry(zip, SaveCodec.EntryGameKey, _ => null!);

        var exception = Assert.Throws<PhigrosFormatException>(() => SaveCodec.Parse(broken));
        Assert.Contains(SaveCodec.EntryGameKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNonZipData()
    {
        Assert.Throws<PhigrosFormatException>(() => SaveCodec.Parse("not a zip"u8.ToArray()));
    }

    [Fact]
    public void WritesThroughFileApi()
    {
        string path = Path.Combine(Path.GetTempPath(), $"phigros-{Guid.NewGuid():N}.save");
        try
        {
            var save = SampleSaveFactory.Create(Difficulties, songCount: 5);
            SaveCodec.WriteFile(path, save);

            var parsed = SaveCodec.ParseFile(path);
            Assert.Equal(save.GameRecord.Keys, parsed.GameRecord.Keys);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] GetEntry(byte[] zip, string name)
    {
        using var stream = new MemoryStream(zip);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = archive.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        entry.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>重写 zip 中某个条目，<paramref name="transform"/> 返回 <see langword="null"/> 表示删除该条目。</summary>
    private static byte[] ReplaceEntry(byte[] zip, string name, Func<byte[], byte[]?> transform)
    {
        using var input = new MemoryStream(zip);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();

        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);

                byte[]? data = entry.FullName == name ? transform(buffer.ToArray()) : buffer.ToArray();
                if (data is null) continue;

                using var created = target.CreateEntry(entry.FullName, CompressionLevel.NoCompression).Open();
                created.Write(data);
            }
        }

        return output.ToArray();
    }
}
