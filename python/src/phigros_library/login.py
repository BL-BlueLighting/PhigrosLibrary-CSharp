"""扫码登录，换取可长期使用的 LeanCloud sessionToken。

流程分四步：

1. 向 TapTap 请求设备码，把 :attr:`QrCodeData.url` 作为二维码展示给用户；
2. 用户用 TapTap App 扫码确认，:meth:`PhigrosLogin.wait_for_token` 拿到访问令牌；
3. 用令牌拉取账号资料（请求需要 MAC 签名，见 :func:`create_authorization_header`）；
4. 把资料作为 ``authData.taptap`` 提交给 LeanCloud，换回 sessionToken。

最省事的用法是 :meth:`PhigrosLogin.login`，它把四步串起来，只把二维码回调出来::

    from phigros_library import PhigrosLogin

    with PhigrosLogin() as login:
        result = login.login(lambda qr: print("请扫码：", qr.url))
        print(result.session_token)

需要留意的是最后一步会在 LeanCloud 上创建或登录一个用户，
这是本库唯一会写入云端数据的操作。
"""

from __future__ import annotations

import asyncio
import base64
import hashlib
import hmac
import json
import os
import time
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from enum import Enum
from typing import Any, Callable, Mapping
from urllib.parse import urlsplit

from .accounts import LeanCloudApp, TapTapRegion
from .difficulty import DifficultyTable
from .exceptions import PhigrosLoginError

#: 默认申请的权限。
DEFAULT_PERMISSIONS = ("public_profile",)

_SDK_VERSION = "2.1"
_TOKEN_VERSION = "1.0"
_PLATFORM = "unity"
_MULTIPART_BOUNDARY = "----PhigrosLibraryBoundary"

#: MAC 签名算法名。
MAC_ALGORITHM = "hmac-sha-1"


@dataclass(frozen=True, slots=True)
class TapTapEndpoints:
    """一个区服对应的 TapTap 登录端点。"""

    web_host: str
    """账号服务地址，负责设备码与令牌。"""

    api_host: str
    """开放平台地址，负责账号资料。"""

    client_id: str
    """OAuth 客户端 id，即 Phigros 在 TapTap 上的应用 id。"""

    @property
    def device_code_url(self) -> str:
        """请求设备码的地址。"""
        return f"{self.web_host}/oauth2/v1/device/code"

    @property
    def token_url(self) -> str:
        """用设备码换取令牌的地址。"""
        return f"{self.web_host}/oauth2/v1/token"

    @property
    def profile_url(self) -> str:
        """拉取账号资料的地址。"""
        return f"{self.api_host}/account/profile/v1?client_id={self.client_id}"

    @classmethod
    def china(cls) -> TapTapEndpoints:
        """国服端点。"""
        return cls("https://accounts.tapapis.cn", "https://open.tapapis.cn", LeanCloudApp.CHINA_APP_ID)

    @classmethod
    def global_(cls) -> TapTapEndpoints:
        """国际服端点。

        上游实现中国际服仅切换了域名，客户端 id 仍沿用国服的，这里保持一致；
        若 TapTap 后续启用独立的国际服应用，直接构造本类型覆盖即可。
        """
        return cls("https://accounts.tapapis.com", "https://open.tapapis.com", LeanCloudApp.CHINA_APP_ID)

    @classmethod
    def for_region(cls, region: TapTapRegion) -> TapTapEndpoints:
        """取指定区服的端点。"""
        return cls.global_() if region is TapTapRegion.GLOBAL else cls.china()


@dataclass(slots=True)
class QrCodeData:
    """一次扫码登录的设备码信息。"""

    device_code: str
    """设备码，用于轮询令牌。"""

    device_id: str
    """本次登录的随机设备标识，轮询时要一并回传。"""

    url: str
    """二维码内容，用 TapTap App 扫描或在手机上打开。"""

    expires_in_seconds: int
    """有效期（秒）。"""

    interval_seconds: int
    """建议的轮询间隔（秒）。"""

    created_at: datetime
    """发起时间，用于判断是否过期。"""

    def expires_at(self) -> datetime:
        """过期时刻。"""
        return self.created_at + timedelta(seconds=self.expires_in_seconds)

    def is_expired(self, now: datetime) -> bool:
        """在给定时刻是否已过期。"""
        return now >= self.expires_at()

    def remaining_lifetime(self, now: datetime) -> timedelta:
        """剩余有效时间，已过期时为 0。"""
        remaining = self.expires_at() - now
        return remaining if remaining > timedelta(0) else timedelta(0)


@dataclass(slots=True)
class TapTapToken:
    """扫码成功后拿到的 TapTap 访问令牌。

    字段名沿用 TapTap 的响应，``payload`` 保存原始对象，
    换取 LeanCloud sessionToken 时需要连同账号资料一起提交。
    """

    kid: str
    """密钥 id，用于计算请求签名。"""

    access_token: str
    mac_key: str
    """MAC 签名密钥。"""

    payload: dict[str, Any]
    """服务端返回的原始对象。"""

    token_type: str = "mac"
    mac_algorithm: str = MAC_ALGORITHM
    scope: str = ""
    """授权范围，以逗号分隔。"""

    @property
    def has_profile_scope(self) -> bool:
        """是否包含读取账号资料所需的权限。

        服务端返回的 scope 可能是逗号分隔，也可能是空格分隔
        （实际见到的是 ``"public_profile compliance"``），两者都接受。
        """
        return "public_profile" in self.scope.replace(",", " ").split()


class TokenPollStatus(Enum):
    """轮询状态。"""

    PENDING = "pending"
    """用户尚未扫码或尚未确认，继续轮询。"""

    SLOW_DOWN = "slow_down"
    """轮询过快，需要降低频率。"""

    SUCCEEDED = "succeeded"
    """已拿到令牌。"""


@dataclass(frozen=True, slots=True)
class TokenPollResult:
    """一次轮询的结果。"""

    status: TokenPollStatus
    token: TapTapToken | None = None

    @property
    def is_succeeded(self) -> bool:
        """是否已拿到令牌。"""
        return self.status is TokenPollStatus.SUCCEEDED


@dataclass(slots=True)
class LoginResult:
    """一次登录的产物。"""

    session_token: str
    """LeanCloud 的 sessionToken，用于后续查分。"""

    profile: dict[str, Any]
    """账号资料原文。"""

    region: TapTapRegion
    app: LeanCloudApp
    user_id: str | None = None
    """LeanCloud 用户 objectId。"""

    username: str | None = None
    """LeanCloud 用户名，通常是自动生成的。"""

    nickname: str | None = None
    """TapTap 昵称，取自账号资料，可能为空（游戏会使用自定义昵称）。"""

    def create_client(self, difficulties: DifficultyTable | None = None):
        """用本次登录结果构造一个查分客户端。"""
        from .client import PhigrosClient

        return PhigrosClient(self.session_token, difficulties, app=self.app)


def create_nonce() -> str:
    """生成一个随机串作为 MAC 签名的 ``nonce``。"""
    return base64.b64encode(os.urandom(16)).decode("ascii")


def create_authorization_header(
    url: str,
    method: str,
    key_id: str,
    mac_key: str,
    timestamp: datetime,
    nonce: str,
) -> str:
    """按 TapTap 开放平台的规则生成 ``Authorization`` 头的值。

    格式为 ``MAC id="{kid}", ts="{时间戳}", nonce="{随机串}", mac="{签名}"``，
    其中签名是对下面这段文本做 HMAC-SHA1（密钥为 ``mac_key``）后取 base64::

        {时间戳}\\n{随机串}\\n{方法}\\n{路径与查询串}\\n{主机名}\\n{端口}\\n\\n

    时间戳为秒，左侧补零到 10 位；端口在未显式指定时按协议取 443 / 80。
    """
    parts = urlsplit(url)
    time_text = str(int(timestamp.timestamp())).rjust(10, "0")
    port = parts.port or (443 if parts.scheme == "https" else 80)
    path_and_query = parts.path + (f"?{parts.query}" if parts.query else "")

    signature_base = (
        f"{time_text}\n{nonce}\n{method.upper()}\n{path_and_query}\n{parts.hostname}\n{port}\n\n"
    )
    mac = base64.b64encode(
        hmac.new(mac_key.encode("utf-8"), signature_base.encode("utf-8"), hashlib.sha1).digest()
    ).decode("ascii")

    return f'MAC id="{key_id}", ts="{time_text}", nonce="{nonce}", mac="{mac}"'


class TapTapLoginClient:
    """TapTap 扫码登录（OAuth2 设备码流程）。

    三个步骤都可以单独调用，便于把二维码先展示给用户；
    拿到资料后还需要换成 LeanCloud 的 sessionToken 才能查分，见 :class:`PhigrosLogin`。
    """

    def __init__(
        self,
        region: TapTapRegion = TapTapRegion.CHINA,
        timeout: float = 30.0,
        clock: Callable[[], datetime] | None = None,
        sleep: Callable[[float], None] | None = None,
        opener: Callable[[urllib.request.Request, float], Any] | None = None,
    ) -> None:
        """
        :param region: 账号所在区服。
        :param timeout: 单次请求超时（秒）。
        :param clock: 返回当前 UTC 时间的函数，便于测试时控制超时判断。
        :param sleep: 等待函数，便于测试时避免真的等待。
        :param opener: 自定义的请求函数，签名 ``(request, timeout) -> http.client.HTTPResponse``。
        """
        self.region = region
        self.endpoints = TapTapEndpoints.for_region(region)
        self.timeout = timeout
        self._clock = clock or (lambda: datetime.now(timezone.utc))
        self._sleep = sleep or time.sleep
        self._opener = opener or (
            lambda request, timeout: urllib.request.urlopen(request, timeout=timeout)
        )

    def request_qr_code(self, permissions: tuple[str, ...] | None = None) -> QrCodeData:
        """请求一次扫码登录，返回二维码地址与轮询所需的设备码。"""
        device_id = uuid.uuid4().hex

        payload = self._post_form(
            self.endpoints.device_code_url,
            {
                "client_id": self.endpoints.client_id,
                "response_type": "device_code",
                "scope": ",".join(permissions or DEFAULT_PERMISSIONS),
                "version": _SDK_VERSION,
                "platform": _PLATFORM,
                "info": json.dumps({"device_id": device_id}, separators=(",", ":")),
            },
            "TapTap",
        )

        if error := _as_str(payload.get("error")):
            raise PhigrosLoginError(f"TapTap 拒绝了设备码请求：{error}")

        # 设备码同样包在 data 信封里，但为兼容起见两种形状都接受。
        inner = payload.get("data")
        source = inner if isinstance(inner, dict) else payload

        device_code = _as_str(source.get("device_code"))
        url = _as_str(source.get("qrcode_url"))
        if not device_code or not url:
            raise PhigrosLoginError(f"TapTap 没有返回设备码，请稍后重试。响应：{_preview(payload)}")

        return QrCodeData(
            device_code=device_code,
            device_id=device_id,
            url=url,
            expires_in_seconds=_as_int(source.get("expires_in"), 300),
            interval_seconds=max(_as_int(source.get("interval"), 2), 1),
            created_at=self._clock(),
        )

    def poll_token(self, data: QrCodeData) -> TokenPollResult:
        """轮询一次令牌。

        用户尚未确认时返回 :attr:`TokenPollStatus.PENDING`，
        由调用方按 :attr:`QrCodeData.interval_seconds` 决定下一次调用时机。
        """
        payload = self._post_form(
            self.endpoints.token_url,
            {
                "grant_type": "device_token",
                "client_id": self.endpoints.client_id,
                "secret_type": MAC_ALGORITHM,
                "code": data.device_code,
                "version": _TOKEN_VERSION,
                "platform": _PLATFORM,
                "info": json.dumps({"device_id": data.device_id}, separators=(",", ":")),
            },
            "TapTap",
        )

        token = _read_token(payload)
        if token is not None:
            return TokenPollResult(TokenPollStatus.SUCCEEDED, token)

        error = _as_str(payload.get("error")) or _as_str(payload.get("error_description"))
        if error:
            if "slow_down" in error.lower():
                return TokenPollResult(TokenPollStatus.SLOW_DOWN)
            if _is_terminal_error(error):
                raise PhigrosLoginError(f"TapTap 登录失败：{error}")

        return TokenPollResult(TokenPollStatus.PENDING)

    def wait_for_token(
        self,
        data: QrCodeData,
        on_poll: Callable[[TokenPollResult], None] | None = None,
    ) -> TapTapToken:
        """轮询直到用户扫码确认、二维码过期。

        :param data: 设备码信息。
        :param on_poll: 每次轮询后的回调，可用于输出提示。
        """
        interval_seconds = data.interval_seconds

        while True:
            if data.is_expired(self._clock()):
                raise PhigrosLoginError("二维码已过期，请重新发起登录。")

            result = self.poll_token(data)
            if on_poll is not None:
                on_poll(result)

            if result.token is not None:
                return result.token

            if result.status is TokenPollStatus.SLOW_DOWN:
                interval_seconds += 1

            # 等待间隔与剩余有效期取较小值，避免二维码过期后还多等一轮。
            delay = min(interval_seconds, data.remaining_lifetime(self._clock()).total_seconds())
            self._sleep(max(delay, 0.0))

    def get_profile(self, token: TapTapToken) -> dict[str, Any]:
        """用令牌拉取账号资料。"""
        if not token.has_profile_scope:
            raise PhigrosLoginError(
                f"令牌缺少 public_profile 权限（当前为 '{token.scope}'），无法读取账号资料。"
            )

        request = urllib.request.Request(self.endpoints.profile_url, method="GET")
        request.add_header(
            "Authorization",
            create_authorization_header(
                self.endpoints.profile_url,
                "GET",
                token.kid,
                token.mac_key,
                self._clock(),
                create_nonce(),
            ),
        )

        payload = _request_json(self._opener, request, self.timeout, "TapTap")

        error = _as_str(payload.get("error")) or _as_str(payload.get("error_description"))
        if error:
            raise PhigrosLoginError(f"TapTap 拒绝读取账号资料：{error}（响应：{_preview(payload)}）")

        # 资料可能直接放在 data 里，也可能平铺在顶层，两者都兼容。
        inner = payload.get("data")
        return inner if isinstance(inner, dict) else payload

    def _post_form(self, url: str, fields: Mapping[str, str], context: str) -> dict[str, Any]:
        body = _encode_multipart(fields)
        request = urllib.request.Request(url, data=body, method="POST")
        request.add_header("Content-Type", f"multipart/form-data; boundary={_MULTIPART_BOUNDARY}")

        return _request_json(self._opener, request, self.timeout, context)


class PhigrosLogin:
    """扫码登录并把 TapTap 账号换成 LeanCloud sessionToken。"""

    def __init__(
        self,
        region: TapTapRegion = TapTapRegion.CHINA,
        app: LeanCloudApp | None = None,
        timeout: float = 30.0,
        clock: Callable[[], datetime] | None = None,
        sleep: Callable[[float], None] | None = None,
        opener: Callable[[urllib.request.Request, float], Any] | None = None,
    ) -> None:
        """
        :param region: 账号所在区服。
        :param app: 覆盖默认的应用配置；为 ``None`` 时按区服自动选择。
        :param timeout: 单次请求超时（秒）。
        :param clock: 返回当前 UTC 时间的函数，便于测试。
        :param sleep: 等待函数，便于测试。
        :param opener: 自定义的请求函数，便于测试时注入桩实现。
        """
        self.region = region
        self.app = app or LeanCloudApp.for_region(region)
        self.timeout = timeout
        self._clock = clock or (lambda: datetime.now(timezone.utc))
        self._opener = opener or (
            lambda request, timeout: urllib.request.urlopen(request, timeout=timeout)
        )

        self.tap_tap = TapTapLoginClient(
            region,
            timeout=timeout,
            clock=self._clock,
            sleep=sleep,
            opener=self._opener,
        )

    def request_qr_code(self) -> QrCodeData:
        """请求一次扫码登录。"""
        return self.tap_tap.request_qr_code()

    def wait_for_token(
        self,
        data: QrCodeData,
        on_poll: Callable[[TokenPollResult], None] | None = None,
    ) -> TapTapToken:
        """等待用户扫码确认。"""
        return self.tap_tap.wait_for_token(data, on_poll)

    def login(self, on_qr_code_ready: Callable[[QrCodeData], None]) -> LoginResult:
        """一次走完全流程，二维码地址通过 ``on_qr_code_ready`` 交给调用方展示。"""
        data = self.request_qr_code()
        on_qr_code_ready(data)

        token = self.wait_for_token(data)
        return self.complete(token)

    def complete(self, token: TapTapToken) -> LoginResult:
        """用已拿到的令牌完成最后两步：取资料并换取 sessionToken。"""
        profile = self.tap_tap.get_profile(token)
        return self.exchange_session_token(profile, token.payload)

    def exchange_session_token(
        self,
        profile: Mapping[str, Any],
        token_payload: Mapping[str, Any],
    ) -> LoginResult:
        """把 TapTap 账号资料提交给 LeanCloud，换回 sessionToken。

        :param profile: 账号资料，来自 :meth:`TapTapLoginClient.get_profile`。
        :param token_payload: 令牌原文，来自 :attr:`TapTapToken.payload`。
        """
        # 上游把资料与令牌字段合并后一起作为 authData.taptap 提交。
        auth_data = {**dict(profile), **dict(token_payload)}
        body = json.dumps({"authData": {"taptap": auth_data}}).encode("utf-8")

        request = urllib.request.Request(f"{self.app.base_url}users", data=body, method="POST")
        request.add_header("Content-Type", "application/json")
        request.add_header("X-LC-Id", self.app.app_id)
        # 签名是 md5(时间戳 + AppKey) + "," + 时间戳，时间戳为秒。
        request.add_header("X-LC-Sign", self._create_sign())

        payload = _request_json(self._opener, request, self.timeout, "LeanCloud")

        if error := _as_str(payload.get("error")):
            code = payload.get("code")
            suffix = f"（code {code}）" if code is not None else ""
            raise PhigrosLoginError(f"登录被拒绝：{error}{suffix}")

        session_token = _as_str(payload.get("sessionToken"))
        if not session_token:
            raise PhigrosLoginError("LeanCloud 没有返回 sessionToken，登录失败。")

        return LoginResult(
            session_token=session_token,
            profile=dict(profile),
            region=self.region,
            app=self.app,
            user_id=_as_str(payload.get("objectId")) or None,
            username=_as_str(payload.get("username")) or None,
            nickname=_as_str(profile.get("name")) or _as_str(profile.get("nickname")) or None,
        )

    def _create_sign(self) -> str:
        """计算 LeanCloud 的 ``X-LC-Sign``。"""
        timestamp = int(self._clock().timestamp())
        digest = hashlib.md5(f"{timestamp}{self.app.app_key}".encode("utf-8")).hexdigest()

        return f"{digest},{timestamp}"

    def __enter__(self) -> PhigrosLogin:
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        return None


class AsyncPhigrosLogin:
    """异步版登录门面。

    本库不依赖任何异步 HTTP 库，这里用 :func:`asyncio.to_thread` 把阻塞的轮询挪到线程池，
    二维码回调仍在事件循环线程里触发，可以直接在里面 ``await`` 后续逻辑。
    """

    def __init__(self, login: PhigrosLogin) -> None:
        self._login = login

    @property
    def login_client(self) -> PhigrosLogin:
        """底层的同步门面。"""
        return self._login

    async def request_qr_code(self) -> QrCodeData:
        return await asyncio.to_thread(self._login.request_qr_code)

    async def wait_for_token(self, data: QrCodeData) -> TapTapToken:
        return await asyncio.to_thread(self._login.wait_for_token, data)

    async def complete(self, token: TapTapToken) -> LoginResult:
        return await asyncio.to_thread(self._login.complete, token)

    async def login(self, on_qr_code_ready: Callable[[QrCodeData], None]) -> LoginResult:
        """一次走完全流程，二维码地址通过 ``on_qr_code_ready`` 交给调用方展示。"""
        data = await self.request_qr_code()
        on_qr_code_ready(data)

        token = await self.wait_for_token(data)
        return await self.complete(token)


def _encode_multipart(fields: Mapping[str, str]) -> bytes:
    """拼装 multipart/form-data 请求体。

    TapTap 的登录接口接收的是 ``FormData`` 形式的多部分表单，
    这里按同样的线格式拼装。
    """
    parts: list[str] = []
    for name, value in fields.items():
        parts.append(f"--{_MULTIPART_BOUNDARY}\r\n")
        parts.append(f'Content-Disposition: form-data; name="{name}"\r\n\r\n')
        parts.append(f"{value}\r\n")
    parts.append(f"--{_MULTIPART_BOUNDARY}--\r\n")

    return "".join(parts).encode("utf-8")


def _is_terminal_error(error: str) -> bool:
    lowered = error.lower()
    return any(word in lowered for word in ("expired", "denied", "invalid", "unauthorized"))


def _read_token(payload: dict[str, Any]) -> TapTapToken | None:
    """从轮询响应里取出令牌，成功响应可能是 ``{success, data}`` 信封，也可能平铺。"""
    inner = payload.get("data")
    source = inner if isinstance(inner, dict) else payload

    access_token = _as_str(source.get("access_token"))
    mac_key = _as_str(source.get("mac_key"))
    if not access_token or not mac_key:
        return None

    return TapTapToken(
        kid=_as_str(source.get("kid")),
        access_token=access_token,
        mac_key=mac_key,
        payload=source,
        token_type=_as_str(source.get("token_type")) or "mac",
        mac_algorithm=_as_str(source.get("mac_algorithm")) or MAC_ALGORITHM,
        scope=_as_str(source.get("scope")),
    )


def _request_json(
    opener: Callable[[urllib.request.Request, float], Any],
    request: urllib.request.Request,
    timeout: float,
    context: str,
) -> dict[str, Any]:
    """发送请求并解析 JSON 对象；HTTP 错误响应里的 JSON 也会被解析出来交给调用方判断。"""
    try:
        response = opener(request, timeout)
    except urllib.error.HTTPError as exc:
        payload = _read_error_payload(exc)
        if payload is not None:
            return payload
        raise PhigrosLoginError(f"请求{context}失败：HTTP {exc.code} {exc.reason}。") from exc
    except urllib.error.URLError as exc:
        raise PhigrosLoginError(f"请求{context}失败，请检查网络连接。") from exc
    except TimeoutError as exc:
        raise PhigrosLoginError(f"请求{context}超时。") from exc

    try:
        body = response.read()
    finally:
        response.close()

    try:
        payload = json.loads(body)
    except (json.JSONDecodeError, UnicodeDecodeError) as exc:
        raise PhigrosLoginError(f"{context}返回了非 JSON 内容。") from exc

    if not isinstance(payload, dict):
        raise PhigrosLoginError(f"{context}返回的不是 JSON 对象。")

    return payload


def _read_error_payload(exc: urllib.error.HTTPError) -> dict[str, Any] | None:
    try:
        body = exc.read()
    except OSError:
        return None

    try:
        payload = json.loads(body)
    except (json.JSONDecodeError, UnicodeDecodeError):
        return None

    return payload if isinstance(payload, dict) and payload else None


def _as_str(value: object) -> str:
    if isinstance(value, str):
        return value
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return str(value)
    return ""


def _preview(payload: Mapping[str, Any], limit: int = 300) -> str:
    """把响应截断成一段便于放进异常信息的文本。"""
    try:
        text = json.dumps(payload, ensure_ascii=False)
    except (TypeError, ValueError):
        text = repr(payload)

    return text if len(text) <= limit else f"{text[:limit]}..."


def _as_int(value: object, fallback: int) -> int:
    if isinstance(value, bool):
        return fallback
    if isinstance(value, int):
        return value
    if isinstance(value, str):
        try:
            return int(value)
        except ValueError:
            return fallback
    return fallback
