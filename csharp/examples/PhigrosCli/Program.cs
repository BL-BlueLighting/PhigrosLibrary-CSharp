using System.Text.Json;
using System.Text.Json.Serialization;
using PhigrosLibrary;
using PhigrosLibrary.Login;
using PhigrosLibrary.Models;
using PhigrosLibrary.Testing;

namespace PhigrosCli;

/// <summary>
/// PhigrosLibrary 的命令行示例。
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return args[0] switch
            {
                "parse" => ParseCommand(Options.Parse(args[1..])),
                "query" => await QueryCommandAsync(Options.Parse(args[1..])).ConfigureAwait(false),
                "sample" => SampleCommand(Options.Parse(args[1..])),
                "login" => await LoginCommandAsync(Options.Parse(args[1..])).ConfigureAwait(false),
                _ => Fail($"未知命令 '{args[0]}'。"),
            };
        }
        catch (PhigrosException ex)
        {
            return Fail(ex.Message);
        }
        catch (IOException ex)
        {
            return Fail($"读写文件失败：{ex.Message}");
        }
    }

    /// <summary>解析本地存档，可选输出 B19 / 期望 ACC / 概要。</summary>
    private static int ParseCommand(Options options)
    {
        string path = options.Require("save");
        var save = SaveCodec.ParseFile(path);

        if (options.Get("rewrite") is { } outputPath)
        {
            SaveCodec.WriteFile(outputPath, save);
            Console.Error.WriteLine($"已重新写出存档：{outputPath}");
        }

        return PrintSave(save, options);
    }

    /// <summary>用 sessionToken 在线查分。</summary>
    private static async Task<int> QueryCommandAsync(Options options)
    {
        string token = options.Require("token");
        using var client = new PhigrosClient(token, LoadDifficulties(options));

        string nickname = await client.GetNicknameAsync().ConfigureAwait(false);
        Console.Error.WriteLine($"玩家：{nickname}");

        var summary = await client.GetSummaryAsync().ConfigureAwait(false);
        Console.Error.WriteLine($"RKS：{summary.RankingScore:F4}  更新时间：{summary.UpdatedAt:yyyy-MM-dd HH:mm:ss}");

        var save = await client.GetSaveAsync().ConfigureAwait(false);
        return PrintSave(save, options);
    }

    /// <summary>生成一份结构完整的示例存档，便于在没有账号的情况下试用本库。</summary>
    private static int SampleCommand(Options options)
    {
        string path = options.Get("out") ?? "sample.save";

        DifficultyTable? difficulties = LoadDifficulties(options);
        if (difficulties is null)
            return Fail("生成示例存档需要定数表，请用 --difficulty 指定 difficulty.tsv 的路径。");

        var save = SampleSaveFactory.Create(difficulties, seed: options.GetInt("seed", 2024));
        SaveCodec.WriteFile(path, save);

        Console.Error.WriteLine($"已生成示例存档：{path}");
        Console.Error.WriteLine(
            $"校验：解析回来得到 {SaveCodec.ParseFile(path).GameRecord.Count} 首曲目的成绩。");

        return PrintSave(save, options);
    }

    /// <summary>扫码登录，把换到的 sessionToken 打印（或写入文件）后用于 query。</summary>
    private static async Task<int> LoginCommandAsync(Options options)
    {
        var region = options.Get("region") is "global" or "gb" or "国际服"
            ? TapTapRegion.Global
            : TapTapRegion.China;

        using var login = new PhigrosLogin(region);

        Console.Error.WriteLine($"区服：{(region == TapTapRegion.Global ? "国际服" : "国服")}");

        var result = await login.LoginAsync(qrCode =>
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("请用 TapTap App 扫描下面的二维码（或把链接生成二维码后扫描）：");
            Console.Error.WriteLine();
            Console.WriteLine(qrCode.Url);
            Console.Error.WriteLine();
            Console.Error.WriteLine($"有效期 {qrCode.ExpiresInSeconds} 秒，正在等待扫码...");
        }).ConfigureAwait(false);

        Console.Error.WriteLine();
        Console.Error.WriteLine($"登录成功：{result.Nickname ?? result.UserId ?? "未知玩家"}");

        if (options.Get("out") is { } path)
        {
            await File.WriteAllTextAsync(path, result.SessionToken).ConfigureAwait(false);
            Console.Error.WriteLine($"sessionToken 已写入：{path}");
        }
        else
        {
            Console.WriteLine(result.SessionToken);
        }

        return 0;
    }

    private static int PrintSave(SaveData save, Options options)
    {
        DifficultyTable? difficulties = LoadDifficulties(options);
        var calculator = difficulties is null ? null : new RksCalculator(difficulties);

        bool printedSomething = false;

        if (options.Has("summary") || options.Has("progress"))
        {
            int[] progress = calculator?.ComputeProgress(save.GameRecord)
                ?? new int[12];
            Console.WriteLine(JsonSerializer.Serialize(new { progress }, OutputOptions));
            printedSomething = true;
        }

        if (options.Has("b19"))
        {
            if (calculator is null) return Fail("计算 B19 需要定数表，请用 --difficulty 指定。");

            Console.WriteLine(JsonSerializer.Serialize(calculator.ComputeBest19(save), OutputOptions));
            printedSomething = true;
        }

        if (options.Has("expect"))
        {
            if (calculator is null) return Fail("计算期望 ACC 需要定数表，请用 --difficulty 指定。");

            Console.WriteLine(JsonSerializer.Serialize(
                new { expect = calculator.ComputeExpect(save.GameRecord) }, OutputOptions));
            printedSomething = true;
        }

        if (!printedSomething)
        {
            Console.WriteLine(JsonSerializer.Serialize(save, OutputOptions));
        }

        return 0;
    }

    private static DifficultyTable? LoadDifficulties(Options options)
    {
        string? path = options.Get("difficulty") ?? FindDefaultDifficulty();
        return path is null ? null : DifficultyTable.Load(path);
    }

    /// <summary>依次尝试几个常见位置，让示例在仓库内外都能直接运行。</summary>
    private static string? FindDefaultDifficulty()
    {
        string[] candidates =
        [
            "difficulty.tsv",
            "resources/difficulty.tsv",
            "../resources/difficulty.tsv",
            "../../resources/difficulty.tsv",
            "../../../resources/difficulty.tsv",
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"错误：{message}");
        return 1;
    }

    private static void PrintUsage() => Console.WriteLine(
        """
        PhigrosLibrary 命令行示例

        用法:
          PhigrosCli login  [--region china|global] [--out <令牌文件>]
          PhigrosCli parse  --save <存档文件> [输出选项]
          PhigrosCli query  --token <sessionToken> [输出选项]
          PhigrosCli sample --out <存档文件> [--seed <整数>] [输出选项]

        参数:
          -s, --save <文件>        本地存档 zip 的路径
          -t, --token <字符串>     玩家 sessionToken
          -d, --difficulty <文件>  定数表路径，默认自动在若干常见位置查找
          -o, --out <文件>         sample 的输出路径，或 login 的令牌保存路径
              --region <区域>      login 的区服：china（默认）或 global

        输出选项（可组合，都不给时输出完整存档）:
              --b19        输出 B19（含总 RKS、φ、19 首最佳成绩）
              --expect     输出打进 B19 所需的 ACC
              --summary    输出 12 项游玩统计
              --progress   同 --summary

        其他:
              --rewrite <文件>      解析后重新写出存档，用于验证往返一致性
          -h, --help                显示本帮助

        示例:
          PhigrosCli login  --out token.txt                # 扫码登录，保存 sessionToken
          PhigrosCli query  --token <token> --b19          # 用令牌查分
          PhigrosCli sample --out demo.save --b19          # 无需账号，生成示例存档
          PhigrosCli parse  --save demo.save --b19 --expect
        """);

    /// <summary>极简的命令行参数解析，支持 <c>--name value</c> 与开关式的 <c>--flag</c>。</summary>
    private sealed class Options
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public static Options Parse(string[] args)
        {
            var options = new Options();

            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                if (!argument.StartsWith("--", StringComparison.Ordinal)
                    && !argument.StartsWith('-'))
                {
                    throw new PhigrosException($"无法识别的参数 '{argument}'。");
                }

                string name = argument.TrimStart('-');

                // 开关式参数：后面没有值，或紧跟另一个选项。
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith('-');
                options._values[name] = hasValue ? args[++i] : null;
            }

            return options;
        }

        public bool Has(string name) => _values.ContainsKey(name);

        public string? Get(string name) => _values.GetValueOrDefault(name);

        public int GetInt(string name, int fallback)
            => _values.TryGetValue(name, out string? value)
               && int.TryParse(value, out int parsed)
                ? parsed
                : fallback;

        public string Require(string name)
            => Get(name) ?? throw new PhigrosException($"缺少必需的参数 --{name}。");
    }
}
