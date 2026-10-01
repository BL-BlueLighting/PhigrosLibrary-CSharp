# PhigrosLibrary（C# 实现）

[PhigrosLibrary](../README.md) 的 C# 实现：Phigros 云存档的解析、序列化，
B19 / RKS / 期望 ACC 计算，以及基于 LeanCloud 的查分接口。

目标框架 `net8.0`，**没有任何第三方依赖**——zip 用 `System.IO.Compression`，
AES 用 `System.Security.Cryptography`，HTTP 用 `HttpClient`。

## 构建与测试

```bash
dotnet build csharp/PhigrosLibrary.sln
dotnet test  csharp/tests/PhigrosLibrary.Tests/PhigrosLibrary.Tests.csproj
```

## 快速开始

### 在线查分

```csharp
using PhigrosLibrary;

using var client = new PhigrosClient("<sessionToken>", DifficultyTable.Load("resources/difficulty.tsv"));

Console.WriteLine(await client.GetNicknameAsync());

var summary = await client.GetSummaryAsync();
Console.WriteLine($"{summary.RankingScore} {summary.UpdatedAt}");

var result = await client.GetBest19Async();
Console.WriteLine(result.Rks);
foreach (var entry in result.Best)
{
    Console.WriteLine($"{entry.Id} {entry.Level} {entry.Rks} {entry.Accuracy}");
}
```

`PhigrosClient` 与上游 C 实现的 handle 一样会缓存概要和存档，需要重新拉取时调用
`InvalidateCache()`。

### 离线解析

```csharp
using PhigrosLibrary;

var save = SaveCodec.ParseFile("cloud.save");
Console.WriteLine($"{save.GameRecord.Count} 首曲目");

var calculator = new RksCalculator(DifficultyTable.Load("difficulty.tsv"));
Console.WriteLine(calculator.ComputeBest19(save).Rks);
Console.WriteLine(string.Join(", ", calculator.ComputeProgress(save.GameRecord)));  // 12 项统计
```

解析后再写出时，五个条目的**内容逐字节一致**：

```csharp
byte[] original = File.ReadAllBytes("cloud.save");
byte[] rewritten = SaveCodec.Write(SaveCodec.Parse(original));
// 逐条目比较内容即可；zip 容器本身会被重新生成，时间戳等元数据可能不同。
// 本库自己写出的存档可以做到整个文件逐字节一致，测试里就是这么断言的。
```

### 命令行示例

```bash
dotnet run --project csharp/examples/PhigrosCli -- sample --out demo.save --b19
dotnet run --project csharp/examples/PhigrosCli -- parse  --save demo.save --b19 --expect
dotnet run --project csharp/examples/PhigrosCli -- query  --token <sessionToken> --b19
dotnet run --project csharp/examples/PhigrosCli -- --help
```

## 主要类型

| 类型 | 说明 |
| --- | --- |
| `SaveCodec` | `Parse` / `Write` / `ParseFile` / `WriteFile`，以及 summary 的 base64 转换 |
| `DifficultyTable` | 定数表，`Load(path)` / `Parse(text)` |
| `RksCalculator` | `ComputeBest19` / `ComputeExpect` / `ComputeProgress` |
| `PhigrosClient` | 门面：`GetNicknameAsync` / `GetSummaryAsync` / `GetSaveAsync` / `GetBest19Async` |
| `LeanCloudClient` | 底层接口客户端，可单独使用或自定义 `HttpClient` |
| `SampleSaveFactory` | 生成示例存档，便于离线试用 |

## 数据结构

`SaveData` 对应存档 zip 中的五个条目：

```csharp
save.GameRecord    // Dictionary<string, SongLevels>，键为曲目 id
save.GameKey       // GameKey
save.GameProgress  // GameProgress
save.User          // UserData
save.Settings      // Settings
```

每个 `SongLevels` 有四个槽位（EZ / HD / IN / AT），`null` 表示该难度没有成绩：

```csharp
var record = save.GameRecord["Credits.Frums"][SongDifficulty.At];
// record.Score / record.Accuracy / record.FullCombo / record.IsAllPerfect
```

模型默认可以直接用 `System.Text.Json` 序列化，字段名与上游 C 实现保持一致，
`GameKey.Version` / `GameProgress.Version` 默认取本库支持的最高版本，
所以在旧版本存档上解析、再写回时会保留原版本号。

## 注意事项

* 定数表缺失曲目时会抛出 `PhigrosDataException`，请按 [`resources/NOTICE.md`](../resources/NOTICE.md) 更新。
* `LevelRecord.Accuracy` 为 0 表示该难度没有成绩。
* 请勿大规模查分。需要高频调用时设置限流：

  ```csharp
  using var api = new LeanCloudClient { MinimumRequestInterval = TimeSpan.FromMilliseconds(500) };
  using var client = new PhigrosClient("<sessionToken>", api, difficulties);
  ```

* 未实现的接口：存档上传回写（上游 `re8` 那段被注释掉的流程）。
