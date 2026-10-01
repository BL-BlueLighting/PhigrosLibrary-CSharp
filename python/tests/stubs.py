"""测试共用的 HTTP 桩，保证所有用例都不产生真实网络请求。"""

from __future__ import annotations

import json
import urllib.request
from typing import Any, Callable


class StubResponse:
    """最小可用的 http 响应桩。"""

    def __init__(self, body: bytes, status: int = 200) -> None:
        self._body = body
        self.status = status
        self.closed = False

    def read(self) -> bytes:
        return self._body

    def close(self) -> None:
        self.closed = True


class StubOpener:
    """记录请求并返回预设响应的桩，签名与 ``urlopen`` 一致。"""

    def __init__(self, responder: Callable[[urllib.request.Request], StubResponse]) -> None:
        self._responder = responder
        self.requests: list[urllib.request.Request] = []

    def __call__(self, request: urllib.request.Request, timeout: float) -> StubResponse:
        self.requests.append(request)
        return self._responder(request)

    @property
    def last(self) -> urllib.request.Request:
        """最后一条请求。"""
        return self.requests[-1]


def json_response(payload: object, status: int = 200) -> StubResponse:
    """构造一个 JSON 响应。"""
    return StubResponse(json.dumps(payload).encode("utf-8"), status)


def header_of(request: urllib.request.Request, name: str) -> str | None:
    """按大小写不敏感的方式取请求头。"""
    for key, value in request.headers.items():
        if key.lower() == name.lower():
            return value
    return None


def body_text(request: urllib.request.Request) -> str:
    """取请求体文本。"""
    data: Any = request.data
    if data is None:
        return ""
    return data.decode("utf-8") if isinstance(data, bytes) else str(data)
