#!/usr/bin/env python3
"""PhigrosLibrary 的命令行示例。

用法::

    python cli.py login  --out token.txt               # 扫码登录，保存 sessionToken
    python cli.py query  --token <sessionToken> --b19  # 用令牌查分
    python cli.py sample --out demo.save --b19         # 无需账号，生成示例存档
    python cli.py parse  --save demo.save --b19 --expect

定数表默认会在若干常见位置自动查找，也可以用 ``--difficulty`` 显式指定。
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# 允许直接从源码树运行，无需先安装本包。
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from phigros_library import (  # noqa: E402
    AsyncPhigrosClient,
    DifficultyTable,
    PhigrosClient,
    PhigrosError,
    PhigrosLogin,
    QrCodeData,
    RksCalculator,
    TapTapRegion,
    TokenPollStatus,
    create_sample_save,
    parse_save,
    parse_save_file,
    write_save,
    write_save_file,
)

#: 定数表的候选位置，让示例在仓库内外都能直接运行。
_DIFFICULTY_CANDIDATES = (
    "difficulty.tsv",
    "resources/difficulty.tsv",
    "../resources/difficulty.tsv",
    "../../resources/difficulty.tsv",
    "../../../resources/difficulty.tsv",
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="phigros-cli",
        description="PhigrosLibrary 命令行示例",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    def add_common(sub: argparse.ArgumentParser) -> None:
        sub.add_argument(
            "-d", "--difficulty", help="定数表路径，默认自动在若干常见位置查找"
        )
        sub.add_argument("--b19", action="store_true", help="输出 B19")
        sub.add_argument("--expect", action="store_true", help="输出打进 B19 所需的 ACC")
        sub.add_argument("--summary", "--progress", action="store_true", help="输出 12 项游玩统计")

    parse_command = subparsers.add_parser("parse", help="解析本地存档")
    parse_command.add_argument("-s", "--save", required=True, help="存档 zip 的路径")
    parse_command.add_argument("--rewrite", help="解析后重新写出到该路径，用于验证往返一致性")
    add_common(parse_command)

    query_command = subparsers.add_parser("query", help="用 sessionToken 在线查分")
    query_command.add_argument("-t", "--token", required=True, help="玩家 sessionToken")
    add_common(query_command)

    sample_command = subparsers.add_parser("sample", help="生成一份示例存档")
    sample_command.add_argument("-o", "--out", default="sample.save", help="输出路径")
    sample_command.add_argument("--seed", type=int, default=2024, help="随机种子")
    add_common(sample_command)

    login_command = subparsers.add_parser("login", help="扫码登录，换取 sessionToken")
    login_command.add_argument("--region", default="china", help="区服：china（默认）或 global")
    login_command.add_argument("-o", "--out", help="把 sessionToken 写入该文件，不给则打印到标准输出")

    return parser


def load_difficulties(path: str | None) -> DifficultyTable | None:
    if path:
        return DifficultyTable.load(path)

    for candidate in _DIFFICULTY_CANDIDATES:
        if Path(candidate).is_file():
            return DifficultyTable.load(candidate)

    return None


def print_save(save, options: argparse.Namespace, difficulties: DifficultyTable | None) -> int:
    calculator = RksCalculator(difficulties) if difficulties else None
    printed = False

    if options.summary:
        progress = calculator.compute_progress(save.game_record) if calculator else [0] * 12
        print(json.dumps({"progress": progress}, ensure_ascii=False, indent=2))
        printed = True

    if options.b19:
        if calculator is None:
            return fail("计算 B19 需要定数表，请用 --difficulty 指定。")
        print(json.dumps(calculator.compute_best19(save.game_record).to_dict(), ensure_ascii=False, indent=2))
        printed = True

    if options.expect:
        if calculator is None:
            return fail("计算期望 ACC 需要定数表，请用 --difficulty 指定。")
        entries = [entry.to_dict() for entry in calculator.compute_expect(save.game_record)]
        print(json.dumps({"expect": entries}, ensure_ascii=False, indent=2))
        printed = True

    if not printed:
        print(json.dumps(save.to_dict(), ensure_ascii=False, indent=2))

    return 0


def run_parse(options: argparse.Namespace) -> int:
    save = parse_save_file(options.save)

    if options.rewrite:
        write_save_file(options.rewrite, save)
        print(f"已重新写出存档：{options.rewrite}", file=sys.stderr)

    return print_save(save, options, load_difficulties(options.difficulty))


async def run_query(options: argparse.Namespace) -> int:
    client = AsyncPhigrosClient(PhigrosClient(options.token, load_difficulties(options.difficulty)))

    print(f"玩家：{await client.get_nickname()}", file=sys.stderr)
    summary = await client.get_summary()
    updated = summary.updated_at.strftime("%Y-%m-%d %H:%M:%S") if summary.updated_at else "未知"
    print(f"RKS：{summary.ranking_score:.4f}  更新时间：{updated}", file=sys.stderr)

    save = await client.get_save()
    return print_save(save, options, load_difficulties(options.difficulty))


def run_sample(options: argparse.Namespace) -> int:
    difficulties = load_difficulties(options.difficulty)
    if difficulties is None:
        return fail("生成示例存档需要定数表，请用 --difficulty 指定 difficulty.tsv 的路径。")

    save = create_sample_save(difficulties, seed=options.seed)
    write_save_file(options.out, save)

    print(f"已生成示例存档：{options.out}", file=sys.stderr)
    print(
        f"校验：解析回来得到 {len(parse_save(write_save(save)).game_record)} 首曲目的成绩。",
        file=sys.stderr,
    )

    return print_save(save, options, difficulties)


def run_login(options: argparse.Namespace) -> int:
    region = TapTapRegion.parse(options.region)
    print(f"区服：{'国际服' if region is TapTapRegion.GLOBAL else '国服'}", file=sys.stderr)

    with PhigrosLogin(region) as login:
        # 分步调用可以先把二维码展示出来，再等用户扫码。
        data = login.request_qr_code()
        print_qr_code(data)

        token = login.wait_for_token(data, on_poll=_report_poll)
        print("\n扫码成功，正在换取 sessionToken...", file=sys.stderr)

        result = login.complete(token)

    print(f"登录成功：{result.nickname or result.user_id or '未知玩家'}", file=sys.stderr)

    if options.out:
        Path(options.out).write_text(result.session_token, encoding="utf-8")
        print(f"sessionToken 已写入：{options.out}", file=sys.stderr)
    else:
        print(result.session_token)

    return 0


def print_qr_code(data: QrCodeData) -> None:
    print(file=sys.stderr)
    print("请用 TapTap App 扫描下面的二维码（或把链接生成二维码后扫描）：", file=sys.stderr)
    print(file=sys.stderr)
    print(data.url)
    print(file=sys.stderr)
    print(f"有效期 {data.expires_in_seconds} 秒，正在等待扫码...", file=sys.stderr)


def _report_poll(result) -> None:
    if result.status is TokenPollStatus.SLOW_DOWN:
        print(".", end="", file=sys.stderr, flush=True)
    elif result.status is TokenPollStatus.PENDING:
        print(".", end="", file=sys.stderr, flush=True)


def fail(message: str) -> int:
    print(f"错误：{message}", file=sys.stderr)
    return 1


def main(argv: list[str] | None = None) -> int:
    import asyncio

    options = build_parser().parse_args(argv)

    try:
        if options.command == "parse":
            return run_parse(options)
        if options.command == "query":
            return asyncio.run(run_query(options))
        if options.command == "login":
            return run_login(options)
        return run_sample(options)
    except PhigrosError as error:
        return fail(str(error))
    except OSError as error:
        return fail(f"读写文件失败：{error}")


if __name__ == "__main__":
    raise SystemExit(main())
