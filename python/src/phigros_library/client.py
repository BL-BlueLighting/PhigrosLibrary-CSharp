"""面向调用方的门面：用一个 sessionToken 完成"查昵称 / 查概要 / 拉存档 / 算 B19"。

与上游 C 实现的 handle 一样，概要和存档会缓存在实例上，同一实例内多次调用不会重复请求。
需要重新拉取时调用 :meth:`PhigrosClient.invalidate_cache`。
"""

from __future__ import annotations

import asyncio

from .accounts import LeanCloudApp
from .api import LeanCloudClient
from .difficulty import DifficultyTable
from .exceptions import PhigrosDataError
from .models import B19Result, ExpectEntry, SaveData, Summary
from .rks import RksCalculator
from .save import parse_file


class PhigrosClient:
    """查分门面（同步）。"""

    def __init__(
        self,
        session_token: str,
        difficulties: DifficultyTable | None = None,
        api: LeanCloudClient | None = None,
        app: LeanCloudApp | None = None,
    ) -> None:
        """
        :param session_token: 玩家 sessionToken，可通过
            :class:`~phigros_library.login.PhigrosLogin` 扫码获取。
        :param difficulties: 定数表，计算 B19 / 期望 ACC 时必需。
        :param api: 自定义的接口客户端；给出时 ``app`` 会被忽略。
        :param app: 区服对应的应用配置；为 ``None`` 时使用国服。
        """
        if not session_token:
            raise ValueError("session_token 不能为空")

        self.session_token = session_token
        self.difficulties = difficulties
        self.api = api or LeanCloudClient(app=app)
        self._summary: Summary | None = None
        self._save: SaveData | None = None

    @classmethod
    def from_save_file(
        cls, path: str, difficulties: DifficultyTable | None = None
    ) -> PhigrosClient:
        """构造一个只读本地存档、不做任何网络请求的实例。"""
        client = cls.__new__(cls)
        client.session_token = ""
        client.difficulties = difficulties
        client.api = None  # type: ignore[assignment]
        client._summary = None
        client._save = parse_file(path)
        return client

    def load_difficulties(self, path: str) -> PhigrosClient:
        """加载定数表文件。"""
        self.difficulties = DifficultyTable.load(path)
        return self

    def get_nickname(self) -> str:
        """取玩家昵称。"""
        return self._require_api().get_nickname(self.session_token)

    def get_summary(self) -> Summary:
        """取玩家概要。结果会被缓存。"""
        if self._summary is None:
            self._summary = self._require_api().get_summary(self.session_token)
        return self._summary

    def get_save(self) -> SaveData:
        """取玩家存档。结果会被缓存。"""
        if self._save is None:
            self._save = self._require_api().get_save(self.session_token)
        return self._save

    def get_best19(self) -> B19Result:
        """计算 B19，需要先设置 ``difficulties``。"""
        return self.create_calculator().compute_best19(self.get_save().game_record)

    def get_expect(self) -> list[ExpectEntry]:
        """计算推分所需 ACC，需要先设置 ``difficulties``。"""
        return self.create_calculator().compute_expect(self.get_save().game_record)

    def create_calculator(self) -> RksCalculator:
        """创建一个绑定当前定数表的计算器。"""
        if self.difficulties is None:
            raise PhigrosDataError(
                "尚未设置定数表，无法计算 RKS。"
                "请通过构造函数传入，或调用 load_difficulties 加载 difficulty.tsv。"
            )

        return RksCalculator(self.difficulties)

    def invalidate_cache(self) -> None:
        """清空概要 / 存档缓存，下次调用会重新请求。"""
        self._summary = None
        self._save = None

    def _require_api(self) -> LeanCloudClient:
        if self.api is None:
            raise PhigrosDataError("本实例只读本地存档，没有配置接口客户端。")
        return self.api


class AsyncPhigrosClient:
    """异步版门面。

    本库不依赖任何异步 HTTP 库，这里用 :func:`asyncio.to_thread` 把同步请求挪到线程池执行，
    在异步机器人里可以直接 ``await``。
    """

    def __init__(self, client: PhigrosClient) -> None:
        self._client = client

    @classmethod
    def from_save_file(
        cls, path: str, difficulties: DifficultyTable | None = None
    ) -> AsyncPhigrosClient:
        return cls(PhigrosClient.from_save_file(path, difficulties))

    @property
    def client(self) -> PhigrosClient:
        """底层的同步客户端。"""
        return self._client

    async def get_nickname(self) -> str:
        return await asyncio.to_thread(self._client.get_nickname)

    async def get_summary(self) -> Summary:
        return await asyncio.to_thread(self._client.get_summary)

    async def get_save(self) -> SaveData:
        return await asyncio.to_thread(self._client.get_save)

    async def get_best19(self) -> B19Result:
        return await asyncio.to_thread(self._client.get_best19)

    async def get_expect(self) -> list[ExpectEntry]:
        return await asyncio.to_thread(self._client.get_expect)

    def invalidate_cache(self) -> None:
        self._client.invalidate_cache()
