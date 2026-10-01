"""与鸽游（TapTap 的 LeanCloud 服务）通信的客户端。

只做读操作：取昵称、取概要、下载存档。所有请求都只带 ``X-LC-Session``（即玩家的
sessionToken），不会修改云端数据。

上游项目明确要求不要大规模查分。如果要在机器人里高频调用，请设置
``minimum_request_interval`` 做限流，避免给对方服务器造成压力。
"""

from __future__ import annotations

import json
import threading
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Callable, Mapping

from .accounts import LeanCloudApp
from .exceptions import PhigrosApiError
from .models import SaveData, Summary
from .save import parse as parse_save, parse_summary

#: 国服 LeanCloud 接口根地址。
API_BASE_URL = LeanCloudApp.CHINA_BASE_URL

#: 国服 Phigros 在 LeanCloud 上的 AppId。
APP_ID = LeanCloudApp.CHINA_APP_ID

#: 国服 Phigros 在 LeanCloud 上的 AppKey。
APP_KEY = LeanCloudApp.CHINA_APP_KEY

_USER_AGENT = "LeanCloud-CSharp-SDK/1.0.3"


@dataclass(slots=True)
class CloudSaveRecord:
    """LeanCloud ``_GameSave`` 表中的一条记录。"""

    object_id: str
    summary: str
    """base64 编码的玩家概要。"""

    url: str
    """存档文件下载地址。"""

    file_id: str | None = None
    updated_at: datetime | None = None
    user_id: str | None = None


class LeanCloudClient:
    """查分接口客户端。"""

    def __init__(
        self,
        timeout: float = 30.0,
        minimum_request_interval: float = 0.0,
        app: LeanCloudApp | None = None,
        opener: Callable[[urllib.request.Request, float], Any] | None = None,
    ) -> None:
        """
        :param timeout: 单次请求超时（秒）。
        :param minimum_request_interval: 两次请求之间的最小间隔（秒），用于限流，默认不限流。
        :param app: 区服对应的应用配置；为 ``None`` 时使用国服。
        :param opener: 自定义的请求函数，签名 ``(request, timeout) -> http.client.HTTPResponse``，
            便于测试时注入桩实现。
        """
        self.timeout = timeout
        self.minimum_request_interval = minimum_request_interval
        self.app = app or LeanCloudApp.china()
        self._opener = opener or (lambda request, timeout: urllib.request.urlopen(request, timeout=timeout))
        self._lock = threading.Lock()
        self._last_request_at = 0.0

    def get_nickname(self, session_token: str) -> str:
        """取玩家昵称。"""
        payload = self._get_json("users/me", session_token)
        nickname = payload.get("nickname")
        if not isinstance(nickname, str):
            raise PhigrosApiError("接口返回的数据中没有 nickname 字段。")

        return nickname

    def get_save_record(self, session_token: str) -> CloudSaveRecord:
        """取玩家存档记录（含 base64 概要、存档下载地址等）。"""
        payload = self._get_json("classes/_GameSave?limit=1", session_token)

        results = payload.get("results")
        if not isinstance(results, list) or not results:
            raise PhigrosApiError("该账号没有云存档，或 sessionToken 已失效。")

        result = results[0]
        summary = result.get("summary")
        if not isinstance(summary, str) or not summary:
            raise PhigrosApiError("存档记录中没有 summary 字段。")

        game_file = result.get("gameFile") or {}
        url = game_file.get("url") if isinstance(game_file, Mapping) else None
        if not isinstance(url, str) or not url:
            raise PhigrosApiError("存档记录中没有 gameFile.url 字段。")

        user = result.get("user") or {}

        return CloudSaveRecord(
            object_id=_as_str(result.get("objectId")),
            summary=summary,
            url=url,
            file_id=_as_str(game_file.get("objectId")) or None,
            updated_at=_parse_datetime(result.get("updatedAt")),
            user_id=(_as_str(user.get("objectId")) or None) if isinstance(user, Mapping) else None,
        )

    def get_summary(self, session_token: str) -> Summary:
        """取玩家概要，同时带上存档下载地址等云端元数据。"""
        record = self.get_save_record(session_token)
        summary = parse_summary(record.summary)

        summary.object_id = record.object_id or None
        summary.user_id = record.user_id
        summary.file_id = record.file_id
        summary.url = record.url
        summary.updated_at = record.updated_at
        return summary

    def download_save(self, url: str) -> bytes:
        """下载存档 zip 的原始字节。"""
        response = self._send(urllib.request.Request(url, method="GET"))
        try:
            return response.read()
        finally:
            response.close()

    def get_save(self, session_token: str) -> SaveData:
        """取并解析玩家存档。"""
        record = self.get_save_record(session_token)
        return parse_save(self.download_save(record.url))

    def _get_json(self, relative_url: str, session_token: str) -> dict[str, Any]:
        if not session_token:
            raise ValueError("session_token 不能为空")

        request = urllib.request.Request(self.app.base_url + relative_url, method="GET")
        request.add_header("X-LC-Id", self.app.app_id)
        request.add_header("X-LC-Key", self.app.app_key)
        request.add_header("X-LC-Session", session_token)
        request.add_header("Accept", "application/json")
        request.add_header("User-Agent", _USER_AGENT)

        response = self._send(request)
        try:
            body = response.read()
        except OSError as exc:
            raise PhigrosApiError("读取接口响应失败。") from exc
        finally:
            response.close()

        try:
            payload = json.loads(body)
        except (json.JSONDecodeError, UnicodeDecodeError) as exc:
            raise PhigrosApiError("接口返回了非 JSON 内容。") from exc

        if not isinstance(payload, dict):
            raise PhigrosApiError("接口返回的不是 JSON 对象。")

        # LeanCloud 的错误响应形如 {"code":210,"error":"..."}。
        error = payload.get("error")
        if isinstance(error, str):
            code = payload.get("code")
            suffix = f"（code {code}）" if code is not None else ""
            raise PhigrosApiError(f"接口返回错误：{error}{suffix}")

        return payload

    def _send(self, request: urllib.request.Request) -> Any:
        self._throttle()

        try:
            return self._opener(request, self.timeout)
        except urllib.error.HTTPError as exc:
            raise PhigrosApiError(
                f"请求失败：HTTP {exc.code} {exc.reason}。"
            ) from exc
        except urllib.error.URLError as exc:
            raise PhigrosApiError("请求失败，请检查网络连接。") from exc
        except TimeoutError as exc:
            raise PhigrosApiError("请求超时。") from exc

    def _throttle(self) -> None:
        if self.minimum_request_interval <= 0:
            return

        with self._lock:
            elapsed = time.monotonic() - self._last_request_at
            remaining = self.minimum_request_interval - elapsed
            if remaining > 0:
                time.sleep(remaining)

            self._last_request_at = time.monotonic()


def _as_str(value: object) -> str:
    return value if isinstance(value, str) else ""


def _parse_datetime(value: object) -> datetime | None:
    if not isinstance(value, str):
        return None

    try:
        # LeanCloud 返回的是 ISO 8601，形如 2024-05-06T07:08:09.000Z。
        return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)
    except ValueError:
        return None
