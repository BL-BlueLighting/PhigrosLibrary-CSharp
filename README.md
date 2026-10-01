# PhigrosLibrary-C#

Phigros 云存档解析库的 **C# 与 Python 重写**：存档解密 / 序列化、B19 与期望 ACC 计算、
TapTap 扫码登录、基于 LeanCloud 的查分接口。

> [!IMPORTANT]
> 本项目是 [7aGiven/PhigrosLibrary](https://github.com/7aGiven/PhigrosLibrary)（GPL-3.0）的移植成果，
> 沿用上游逆向得到的存档格式与算法，未调用任何第三方接口。
> 原项目为 C/C++ 实现，本项目用 C# 与 Python 各自独立重写了一遍，功能对等、互不依赖。

> [!CAUTION]
> **严禁大规模查分对鸽游服务器进行 DDOS。**
> 需要高频调用时请打开本库内置的限流（见各语言 README），并自行控制调用频率。

## 目录结构

```
.
├── csharp/                    C# 实现（net8.0，零第三方依赖）
│   ├── src/PhigrosLibrary/      类库
│   ├── examples/PhigrosCli/     命令行示例
│   └── tests/PhigrosLibrary.Tests/   78 个单元测试
├── python/                    Python 实现（≥3.10，仅依赖 cryptography）
│   ├── src/phigros_library/     包
│   ├── examples/cli.py          命令行示例
│   └── tests/                   86 个单元测试
├── resources/                 数据表（定数表、曲目信息等，见 NOTICE.md）
└── LICENSE                    GPL-3.0
```

两个实现是**彼此独立**的：C# 不依赖 Python，Python 也不需要编译任何原生库
（不像上游那样通过 ctypes / ffi-napi 调用 `libphigros.so`）。两者的存档格式与计算结果完全一致，
可以互相读写对方生成的存档（见下文[测试](#测试)）。

## 快速开始

### C#

```csharp
using PhigrosLibrary;

using var client = new PhigrosClient("<sessionToken>", DifficultyTable.Load("resources/difficulty.tsv"));

Console.WriteLine(await client.GetNicknameAsync());   // 玩家昵称
Console.WriteLine((await client.GetSummaryAsync()).RankingScore);

var b19 = await client.GetBest19Async();
Console.WriteLine(b19.Rks);                            // 总 RKS
foreach (var entry in b19.Best)                        // 19 首最佳成绩
{
    Console.WriteLine($"{entry.Id} {entry.Level} {entry.Rks:F4} {entry.Accuracy:F2}");
}
```

```bash
dotnet build csharp/PhigrosLibrary.sln
dotnet run --project csharp/examples/PhigrosCli -- sample --out demo.save --b19
dotnet run --project csharp/examples/PhigrosCli -- parse  --save demo.save --b19 --expect
dotnet run --project csharp/examples/PhigrosCli -- query  --token <sessionToken> --b19
```

详见 [csharp/README.md](csharp/README.md)。

### Python

发行名 `phigros-score-library`，导入名 `phigros_library`：

```bash
pip install phigros-score-library
```

```python
from phigros_library import DifficultyTable, PhigrosClient, PhigrosLogin

# 没有 sessionToken 时先扫码登录
with PhigrosLogin() as login:
    result = login.login(lambda qr: print("请扫码：", qr.url))

client = PhigrosClient(result.session_token, DifficultyTable.bundled())

print(client.get_nickname())
print(client.get_summary().ranking_score)

b19 = client.get_best19()
print(b19.rks)
for entry in b19.best:
    print(entry.id, entry.level, entry.rks, entry.accuracy)
```

定数表现已随包分发（`DifficultyTable.bundled()`），无需自己找 `difficulty.tsv`。
异步环境可直接用 `AsyncPhigrosClient` / `AsyncPhigrosLogin`。
详见 [python/README.md](python/README.md)。

### 离线试用（无需账号）

两个实现都自带示例存档生成器，用固定随机种子造出一份结构完整的存档，
可以直接体验解析、B19、期望 ACC 的完整流程：

```bash
# C#
dotnet run --project csharp/examples/PhigrosCli -- sample --out demo.save --b19

# Python
.venv/bin/python python/examples/cli.py sample --out demo.save --b19
```

## 上游接口对照

| 上游 C API | C# | Python |
| --- | --- | --- |
| `get_handle(sessionToken)` | `new PhigrosClient(token, table)` | `PhigrosClient(token, table)` |
| `free_handle(handle)` | `Dispose()` | 交由 GC |
| `get_nickname(handle)` | `GetNicknameAsync()` | `get_nickname()` |
| `get_summary(handle)` | `GetSummaryAsync()` | `get_summary()` |
| `get_save(handle)` | `GetSaveAsync()` | `get_save()` |
| `load_difficulty(path)` | `DifficultyTable.Load(path)` | `DifficultyTable.load(path)` |
| `get_b19(handle)` | `GetBest19Async()` | `get_best19()` |

B19 的 JSON 字段名与上游保持一致（`rks` / `phi` / `best`，每项含
`id` / `level` / `difficulty` / `rks` / `score` / `acc` / `fc`），summary 的字段名同样沿用，
已有调用方可以直接替换。

## 与上游的差异

移植时有意做了以下调整：

1. **零原生依赖。** 上游是 C/C++ 核心（libzip + OpenSSL + cJSON）加语言包装器；
   本项目是两套纯语言实现，C# 只用 `System.IO.Compression` / `System.Security.Cryptography`，
   Python 只用 `zipfile` 与 `cryptography`，部署时不再需要 `.so` / `.dll`。

2. **变长整数的取值范围。** 上游的写入端实际只能表达 11 位（`n << 1 & 0xF00`），
   读取端却按 15 位解析，两者并不对称。本库统一为「首字节 7 位 + 次字节 8 位」，
   即 `0..0x7F7F`；写入超范围的值会抛出明确异常，而不是静默截断。
   真实存档中的取值都远小于该上限，因此解析结果不受影响。

3. **φ 的选取依据。** 上游用 `difficulty > phi->rks` 判断是否替换 φ；
   由于满分曲目的单曲 RKS 恰好等于定数，两者等价，本库直接比较定数，语义更直白。

4. **网络层。** 上游 C 版把存档下载地址去掉 `https://` 前缀后走明文 HTTP，
   Python 版依赖 aiohttp；本库统一使用完整 URL 与各自的标准 HTTP 栈
   （`HttpClient` / `urllib`），并内建可选的请求限流。

5. **未实现的部分。** 存档上传回写（上游源码中被注释掉的 `re8`）及其依赖的
   `update_summary` / `gen_save` 上传流程不在本次范围内；本地存档的解析与生成是完整的。

6. **新增的部分。** 强类型模型（不再是 JSON 字符串）、示例存档生成器、异常体系、
   统一的分层异常、以及两套完整的单元测试。

存档格式上的若干细节被完整保留，包括：每个条目首字节的明文版本号、
连续布尔打包进同一字节、`gameRecord` 键名在磁盘上携带 `.0` 后缀、
块长度字段用于跳过未知尾部字节，以及把识别不了的尾部字节以 base64
存进 `overflow` 并在写回时原样还原。

## 资源更新

`resources/` 下的数据表来自上游仓库，随游戏版本更新。
判断版本看文件修改时间，更新方式见 [resources/NOTICE.md](resources/NOTICE.md)。
其中只有 `difficulty.tsv`（定数表）是计算 B19 必需的，其余供调用方自行取用。

## 测试

```bash
# C#
dotnet test csharp/tests/PhigrosLibrary.Tests/PhigrosLibrary.Tests.csproj

# Python
cd python && .venv/bin/python -m pytest
```

两套测试都覆盖：

* 变长整数与字符串的编解码边界；
* AES-256-CBC 加解密的往返、填充规则与错误输入；
* 存档"写出 → 解析 → 再写出"的**逐字节一致性**，以及未知尾部字节的保留；
* Summary 的解析与生成；
* B19 / RKS / 期望 ACC 的公式用例（含 φ 选取、ACC 下限、无 AT 难度等边界）；
* 接口层的响应解析与错误处理（全部使用桩实现，不产生网络请求）。

此外两个实现之间做过互通验证：C# 能解析 Python 生成的存档并算出完全相同的 B19，
反之亦然；C# 重写 Python 存档时五个条目逐字节一致。

## 参考项目与致谢

本项目的存档格式、算法与登录流程均来自下面这些项目，在此致谢：

| 项目 | 作者 | 本仓库参考了什么 |
| --- | --- | --- |
| [**7aGiven/PhigrosLibrary**](https://github.com/7aGiven/PhigrosLibrary) | 7aGiven | **本项目的移植母本**。存档 zip 结构、AES-256-CBC 加解密、二进制序列化、Summary 格式、B19 / 期望 ACC 的算法与 LeanCloud 查分接口，全部来自这个 C/C++ 项目；`resources/` 下的数据表也取自这里 |
| [**Catrong/phi-plugin**](https://github.com/Catrong/phi-plugin) | Catrong | **登录模块的参考实现**。`lib/TapTap/` 下的 `TapTapHelper.js`（TapTap 设备码流程、账号资料的 MAC 签名）与 `LCHelper.js`（`X-LC-Sign` 计算、`authData.taptap` 换取 sessionToken）是本仓库 C# / Python 登录模块的依据 |
| [**7aGiven/Phigros_Resource**](https://github.com/7aGiven/Phigros_Resource) | 7aGiven | 从游戏 apk 中提取定数表等资源的工具，`resources/` 的更新方式 |

上游项目均为 GPL-3.0，本项目作为衍生作品沿用同一许可。

如果你要基于本库做机器人或工具，也请一并遵守上游作者的要求：
**不要大规模查分对鸽游服务器造成压力**。

## 许可

[GPL-3.0](LICENSE)。本项目是 7aGiven/PhigrosLibrary 的衍生作品，沿用同一许可。
