# 资源文件说明

本目录下的所有数据文件均来自上游项目 [7aGiven/PhigrosLibrary](https://github.com/7aGiven/PhigrosLibrary)（GPL-3.0），
属于游戏《Phigros》的数据表，本仓库仅作移植与重新分发。

| 文件 | 用途 | 来源 |
| --- | --- | --- |
| `difficulty.tsv` | 定数表，B19 / RKS / 期望 ACC 计算必需 | 上游仓库根目录 |
| `info.tsv` | 曲目信息（曲名、曲师、谱师等） | 上游仓库根目录 |
| `collection.tsv` | 收藏品 id 对照表 | 上游仓库根目录 |
| `avatar.txt` | 头像 id 对照表 | 上游仓库根目录 |
| `illustration.txt` | 曲绘 id 对照表 | 上游仓库根目录 |
| `single.txt` | 单曲列表 | 上游仓库根目录 |

## 更新方式

资源文件随游戏版本更新，判断版本请查看文件的修改时间。

* **定数表**：使用 [Phigros_Resource](https://github.com/7aGiven/Phigros_Resource/) 从 apk 中提取。
  安装 `UnityPy==1.10.18` 后运行 `python gameInformation.py Phigros.apk`，
  会在 `./info` 下生成 `difficulty.tsv`，替换本目录内的同名文件即可。
* **头像 / 收藏品 id**：直接替换 `avatar.txt` 与 `collection.tsv`。

## 文件格式

### difficulty.tsv

以制表符分隔，首列为曲目 id，其后为该曲 EZ / HD / IN / AT 四个难度的定数。
AT 难度可能缺失（该曲只有三个难度），缺失时按 `0` 处理。

```
Glaciaxion.SunsetRay	1.0	6.5	12.6
Credits.Frums	4.5	10.4	13.6	15.7
```

### 其他文件

`avatar.txt` / `illustration.txt` / `single.txt` 每行一项，`collection.tsv` 与 `info.tsv` 为制表符分隔的多列文本，
均按行顺序与游戏内 id 对应。本仓库的库代码只依赖 `difficulty.tsv`，其余文件供调用方自行取用。
