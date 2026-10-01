# phigros-score-library

[PhigrosLibrary](../README.md) 的 Python 实现：Phigros 云存档的解析与序列化、
B19 / RKS / 期望 ACC 计算、TapTap 扫码登录，以及基于 LeanCloud 的查分接口。

* 发行名 `phigros-score-library`，导入名 **`PhigrosScoreLibrary`**。
* 除 AES 依赖 [`cryptography`](https://pypi.org/project/cryptography/) 外，其余功能全部基于标准库。
* 支持 Python 3.10 及以上。

## 安装

```bash
pip install phigros-score-library
```

从源码安装：

```bash
pip install .          # 或 pip install -e ".[dev]" 装开发依赖
```

## 快速开始

### 第一步：拿到 sessionToken

没有 sessionToken 时先扫码登录，用 TapTap App 扫一次即可，拿到的令牌可以长期复用：

```python
from PhigrosScoreLibrary import PhigrosLogin

with PhigrosLogin() as login:                       # 国际服传 TapTapRegion.GLOBAL
    result = login.login(lambda qr: print("请扫码：", qr.url))
    print(result.session_token)                     # 保存下来，下次直接用
```

异步环境下用 `AsyncPhigrosLogin`。二维码回调是在**事件循环线程**里触发的，
所以可以直接在回调里 `await`（比如把二维码图片发到群里）：

```python
import asyncio
from PhigrosScoreLibrary import AsyncPhigrosLogin, PhigrosLogin

async def main() -> None:
    login = AsyncPhigrosLogin(PhigrosLogin())       # 国际服传 TapTapRegion.GLOBAL

    async def on_qr(qr):
        await send_qr_image_to_chat(qr.url)         # 回调里可以安全地 await
        print(f"有效期 {qr.expires_in_seconds} 秒")

    result = await login.login(on_qr)
    print(result.session_token)

asyncio.run(main())
```

需要先发二维码、稍后再回来等结果时，可以分步调用：

```python
data = await login.request_qr_code()                # 拿二维码地址
token = await login.wait_for_token(data)            # 阻塞式轮询，已挪到线程池
result = await login.complete(token)                # 取资料并换 sessionToken
```

`AsyncPhigrosLogin` 内部用 `asyncio.to_thread` 把阻塞的网络请求挪到线程池，
因此不依赖任何异步 HTTP 库；手上已有 `PhigrosLogin` 实例时，
用 `AsyncPhigrosLogin(login)` 包一层即可。

### 第二步：查分

```python
from PhigrosScoreLibrary import DifficultyTable, PhigrosClient

client = PhigrosClient("<sessionToken>", DifficultyTable.bundled())

print(client.get_nickname())

summary = client.get_summary()
print(summary.ranking_score, summary.updated_at)

result = client.get_best19()
print(result.rks)
for entry in result.best:
    print(entry.id, entry.level, entry.rks, entry.accuracy)
```

异步环境（例如 Yunzai / NoneBot 插件）可以直接用异步门面：

```python
from PhigrosScoreLibrary import AsyncPhigrosClient, DifficultyTable, PhigrosClient

client = AsyncPhigrosClient(PhigrosClient("<sessionToken>", DifficultyTable.bundled()))

print(await client.get_nickname())
summary = await client.get_summary()
result = await client.get_best19()
```

### 离线解析

`DifficultyTable.bundled()` 读取随包分发的定数表，省去自己找文件：

```python
from PhigrosScoreLibrary import DifficultyTable, RksCalculator, parse_save_file

save = parse_save_file("cloud.save")
print(len(save.game_record), "首曲目")

calculator = RksCalculator(DifficultyTable.bundled())
print(calculator.compute_best19(save.game_record).rks)
print(calculator.compute_progress(save.game_record))   # 12 项统计
```

需要最新定数时用 `DifficultyTable.load("difficulty.tsv")` 指定自己的文件。

解析后再写出时，五个条目的**内容逐字节一致**：

```python
import io, zipfile
from PhigrosScoreLibrary import parse_save, write_save

original = open("cloud.save", "rb").read()
rewritten = write_save(parse_save(original))

with zipfile.ZipFile(io.BytesIO(original)) as before, zipfile.ZipFile(io.BytesIO(rewritten)) as after:
    for name in before.namelist():
        assert before.read(name) == after.read(name)
```

（zip 容器本身会被重新生成，时间戳等元数据可能与游戏产出的文件不同；条目内容不受影响。
本库自己写出的存档则可以做到整个文件逐字节一致，测试里就是这么断言的。）

### 命令行示例

仓库内附了一个示例 CLI：

```bash
cd python

python examples/cli.py login  --out token.txt        # 扫码登录，保存 sessionToken
python examples/cli.py query  --token <token> --b19  # 用令牌查分
python examples/cli.py sample --out demo.save --b19  # 无需账号，生成示例存档
python examples/cli.py parse  --save demo.save --b19 --expect
```

## 主要接口

| 接口 | 说明 |
| --- | --- |
| `parse_save(bytes)` / `parse_save_file(path)` | 解析存档 zip |
| `write_save(save)` / `write_save_file(path, save)` | 生成存档 zip |
| `parse_summary(base64)` / `write_summary(summary)` | 解析 / 生成 LeanCloud 的 summary |
| `DifficultyTable.load(path)` / `.bundled()` | 读取定数表 |
| `RksCalculator.compute_best19(record)` | B19（19 首最佳 + φ）与总 RKS |
| `RksCalculator.compute_expect(record)` | 打进 B19 所需的 ACC |
| `RksCalculator.compute_progress(record)` | 12 项游玩统计 |
| `PhigrosLogin` / `AsyncPhigrosLogin` | 扫码登录，换取 sessionToken |
| `PhigrosClient` / `AsyncPhigrosClient` | 查昵称 / 概要 / 存档 / B19 |
| `LeanCloudClient` | 底层接口客户端，可单独使用或注入到门面 |
| `create_sample_save(table)` | 生成示例存档，便于离线试用 |

## 数据结构

`SaveData` 对应存档 zip 中的五个条目：

```python
save.game_record      # dict[str, SongLevels]，键为曲目 id
save.game_key         # GameKey
save.game_progress    # GameProgress
save.user             # UserData
save.settings         # Settings
```

每个 `SongLevels` 有四个槽位（EZ / HD / IN / AT），`None` 表示该难度没有成绩：

```python
from PhigrosScoreLibrary import SongDifficulty

record = save.game_record["Credits.Frums"][SongDifficulty.AT]
record.score, record.accuracy, record.full_combo, record.is_all_perfect
```

所有模型都提供 `to_dict()`，输出的字段名与上游 C 实现保持一致，便于直接替换。

## 注意事项

* 存档中的 `acc` 与各项音量都是 **float32**，写回时会被截断到单精度；
  模型里保持 Python 的 `float`，需要精确比较时请留出精度余量。
* `LevelRecord.accuracy` 为 0 表示该难度没有成绩。
* 定数表缺失曲目时会抛出 `PhigrosDataError`。内置的那份随游戏版本更新，
  需要最新数据时按 [`resources/NOTICE.md`](../resources/NOTICE.md) 自行提取并 `load()`。
* 登录流程的最后一步会在 LeanCloud 上创建或登录一个用户，是本库**唯一**的写操作。
* 请勿大规模查分。需要高频调用时设置限流：

  ```python
  from PhigrosScoreLibrary import LeanCloudClient, PhigrosClient

  api = LeanCloudClient(minimum_request_interval=0.5)  # 每次请求至少间隔 0.5 秒
  client = PhigrosClient("<sessionToken>", difficulties, api=api)
  ```
