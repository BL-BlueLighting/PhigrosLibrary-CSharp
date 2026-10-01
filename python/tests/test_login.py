"""扫码登录流程的测试，全部使用桩实现，不产生任何网络请求。"""

from __future__ import annotations

import base64
import hashlib
import hmac
import io
import json
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

import pytest
from stubs import StubOpener, StubResponse, body_text, header_of, json_response

from PhigrosScoreLibrary import (
    LeanCloudApp,
    PhigrosLogin,
    PhigrosLoginError,
    QrCodeData,
    TapTapEndpoints,
    TapTapLoginClient,
    TapTapRegion,
    TapTapToken,
    TokenPollStatus,
    create_authorization_header,
    create_nonce,
)

KID = "1/abcDEF"
MAC_KEY = "zCgtfVWxajWHl2MYVoFMNdPn0E2YXrV4mTjWjLKp"
FIXED_NOW = datetime.fromtimestamp(1_700_000_000, tz=timezone.utc)

QR_RESPONSE = {
    "device_code": "device-123",
    "expires_in": 300,
    "interval": 1,
    "qrcode_url": "https://accounts.tapapis.cn/qr/device-123",
}
TOKEN_RESPONSE = {
    "success": True,
    "data": {
        "kid": KID,
        "access_token": "1/abc",
        "token_type": "mac",
        "mac_key": MAC_KEY,
        "mac_algorithm": "hmac-sha-1",
        "scope": "public_profile",
    },
}
PROFILE_RESPONSE = {"success": True, "data": {"openid": "open-1", "name": "鸽子"}}


class Clock:
    """可手动推进的时钟。"""

    def __init__(self, now: datetime) -> None:
        self.now = now

    def __call__(self) -> datetime:
        return self.now

    def advance(self, seconds: float) -> None:
        self.now += timedelta(seconds=seconds)


def make_client(opener, clock: Clock | None = None, sleeps: list[float] | None = None):
    return TapTapLoginClient(
        TapTapRegion.CHINA,
        clock=clock or Clock(FIXED_NOW),
        sleep=(sleeps.append if sleeps is not None else (lambda _: None)),
        opener=opener,
    )


def make_qr_data(created_at: datetime = FIXED_NOW) -> QrCodeData:
    return QrCodeData(
        device_code="device-123",
        device_id="device-id",
        url="https://accounts.tapapis.cn/qr/device-123",
        expires_in_seconds=300,
        interval_seconds=1,
        created_at=created_at,
    )


def make_token(scope: str = "public_profile") -> TapTapToken:
    return TapTapToken(
        kid=KID,
        access_token="1/abc",
        mac_key=MAC_KEY,
        payload={"access_token": "1/abc"},
        scope=scope,
    )


# ---------- MAC 签名 ----------


def test_authorization_header_matches_tap_tap_spec() -> None:
    nonce = "MTIzNDU2Nzg5MDEyMzQ1Ng=="
    url = "https://open.tapapis.cn/account/profile/v1?client_id=rAK3FfdieFob2Nn8Am"

    # 按协议描述独立拼出期望值：时间戳\n随机串\n方法\n路径与查询串\n主机名\n端口\n\n
    signature_base = (
        f"1700000000\n{nonce}\nGET\n/account/profile/v1?client_id=rAK3FfdieFob2Nn8Am\n"
        "open.tapapis.cn\n443\n\n"
    )
    mac = base64.b64encode(
        hmac.new(MAC_KEY.encode(), signature_base.encode(), hashlib.sha1).digest()
    ).decode()

    header = create_authorization_header(url, "GET", KID, MAC_KEY, FIXED_NOW, nonce)

    assert header == f'MAC id="{KID}", ts="1700000000", nonce="{nonce}", mac="{mac}"'


def test_authorization_timestamp_is_padded_to_ten_digits() -> None:
    early = datetime.fromtimestamp(123, tz=timezone.utc)

    header = create_authorization_header("https://open.tapapis.cn/x", "GET", KID, MAC_KEY, early, "n")

    assert 'ts="0000000123"' in header


def test_authorization_uses_protocol_default_port_when_absent() -> None:
    nonce = "n"
    # http 未显式指定端口时按协议取 80。
    signature_base = f"1700000000\n{nonce}\nGET\n/x\nopen.tapapis.cn\n80\n\n"
    mac = base64.b64encode(
        hmac.new(MAC_KEY.encode(), signature_base.encode(), hashlib.sha1).digest()
    ).decode()

    header = create_authorization_header(
        "http://open.tapapis.cn/x", "GET", KID, MAC_KEY, FIXED_NOW, nonce
    )

    assert header == f'MAC id="{KID}", ts="1700000000", nonce="{nonce}", mac="{mac}"'


def test_authorization_uses_explicit_port_when_present() -> None:
    nonce = "n"
    signature_base = f"1700000000\n{nonce}\nGET\n/x\nopen.tapapis.cn\n8443\n\n"
    mac = base64.b64encode(
        hmac.new(MAC_KEY.encode(), signature_base.encode(), hashlib.sha1).digest()
    ).decode()

    header = create_authorization_header(
        "https://open.tapapis.cn:8443/x", "GET", KID, MAC_KEY, FIXED_NOW, nonce
    )

    assert header == f'MAC id="{KID}", ts="1700000000", nonce="{nonce}", mac="{mac}"'


def test_nonce_is_random_per_call() -> None:
    nonces = {create_nonce() for _ in range(8)}

    assert len(nonces) == 8
    # 16 字节的 base64 固定为 24 个字符。
    assert all(len(nonce) == 24 for nonce in nonces)


# ---------- 设备码 ----------


def test_request_qr_code_sends_multipart_form_and_parses_response() -> None:
    opener = StubOpener(lambda _: json_response(QR_RESPONSE))
    client = make_client(opener)

    data = client.request_qr_code()

    assert data.device_code == "device-123"
    assert data.url == "https://accounts.tapapis.cn/qr/device-123"
    assert data.expires_in_seconds == 300
    assert data.interval_seconds == 1
    assert data.created_at == FIXED_NOW
    assert len(data.device_id) == 32
    assert not data.is_expired(FIXED_NOW)
    assert data.expires_at() == FIXED_NOW + timedelta(seconds=300)

    request = opener.last
    assert request.method == "POST"
    assert request.full_url == "https://accounts.tapapis.cn/oauth2/v1/device/code"
    assert header_of(request, "Content-Type").startswith("multipart/form-data")

    body = body_text(request)
    assert 'name="client_id"' in body
    assert TapTapEndpoints.china().client_id in body
    assert 'name="response_type"' in body
    assert "device_code" in body
    assert 'name="scope"' in body
    assert "public_profile" in body
    assert 'name="platform"' in body
    assert data.device_id in body


def test_request_qr_code_accepts_wrapped_envelope() -> None:
    # 真实接口把设备码包在 data 信封里，上游用的也是 data.qrcode_url。
    opener = StubOpener(lambda _: json_response({"success": True, "data": QR_RESPONSE}))
    client = make_client(opener)

    data = client.request_qr_code()

    assert data.device_code == "device-123"
    assert data.url == "https://accounts.tapapis.cn/qr/device-123"
    assert data.expires_in_seconds == 300
    assert data.interval_seconds == 1


def test_request_qr_code_reports_server_error() -> None:
    opener = StubOpener(lambda _: json_response({"success": False, "error": "invalid_client"}))
    client = make_client(opener)

    with pytest.raises(PhigrosLoginError, match="invalid_client"):
        client.request_qr_code()


def test_request_qr_code_error_message_includes_response() -> None:
    opener = StubOpener(lambda _: json_response({"unexpected": "shape"}))
    client = make_client(opener)

    with pytest.raises(PhigrosLoginError, match="unexpected"):
        client.request_qr_code()


def test_request_qr_code_uses_global_endpoints_for_global_region() -> None:
    opener = StubOpener(lambda _: json_response(QR_RESPONSE))
    client = TapTapLoginClient(TapTapRegion.GLOBAL, opener=opener)

    client.request_qr_code()

    assert opener.last.full_url == "https://accounts.tapapis.com/oauth2/v1/device/code"


def test_request_qr_code_raises_when_device_code_missing() -> None:
    opener = StubOpener(lambda _: json_response({"error": "invalid_request"}))
    client = make_client(opener)

    with pytest.raises(PhigrosLoginError):
        client.request_qr_code()


# ---------- 轮询令牌 ----------


def test_poll_token_returns_pending_while_user_has_not_scanned() -> None:
    opener = StubOpener(lambda _: json_response({"success": False, "error": "authorization_pending"}))
    client = make_client(opener)

    result = client.poll_token(make_qr_data())

    assert result.status is TokenPollStatus.PENDING
    assert result.token is None
    assert not result.is_succeeded


def test_poll_token_reports_slow_down() -> None:
    opener = StubOpener(lambda _: json_response({"error": "slow_down"}))
    client = make_client(opener)

    assert client.poll_token(make_qr_data()).status is TokenPollStatus.SLOW_DOWN


@pytest.mark.parametrize("error", ["expired_token", "access_denied", "invalid_grant"])
def test_poll_token_raises_on_terminal_errors(error: str) -> None:
    opener = StubOpener(lambda _: json_response({"error": error}))
    client = make_client(opener)

    with pytest.raises(PhigrosLoginError, match=error):
        client.poll_token(make_qr_data())


def test_poll_token_reads_token_from_envelope_and_keeps_raw_payload() -> None:
    opener = StubOpener(lambda _: json_response(TOKEN_RESPONSE))
    client = make_client(opener)

    result = client.poll_token(make_qr_data())

    assert result.status is TokenPollStatus.SUCCEEDED
    token = result.token
    assert token is not None
    assert token.kid == KID
    assert token.access_token == "1/abc"
    assert token.mac_key == MAC_KEY
    assert token.has_profile_scope
    # 原文要完整保留，换 sessionToken 时需要连同账号资料一起提交。
    assert token.payload["access_token"] == "1/abc"

    body = body_text(opener.last)
    assert "grant_type" in body
    assert "device_token" in body
    assert "hmac-sha-1" in body


def test_poll_token_accepts_flat_response() -> None:
    opener = StubOpener(
        lambda _: json_response({"kid": KID, "access_token": "1/abc", "mac_key": MAC_KEY})
    )
    client = make_client(opener)

    result = client.poll_token(make_qr_data())

    assert result.status is TokenPollStatus.SUCCEEDED
    assert result.token.access_token == "1/abc"


def test_poll_token_parses_json_body_of_http_error() -> None:
    # 有些错误响应的状态码不是 200，但响应体里仍带着可判断的错误码。
    def raise_http_error(request: urllib.request.Request, timeout: float) -> StubResponse:
        body = io.BytesIO(json.dumps({"error": "authorization_pending"}).encode())
        raise urllib.error.HTTPError(request.full_url, 400, "Bad Request", {}, body)

    client = make_client(raise_http_error)

    assert client.poll_token(make_qr_data()).status is TokenPollStatus.PENDING


def test_poll_token_raises_on_http_error_without_json_body() -> None:
    def raise_http_error(request: urllib.request.Request, timeout: float) -> StubResponse:
        raise urllib.error.HTTPError(request.full_url, 502, "Bad Gateway", {}, None)

    client = make_client(raise_http_error)

    with pytest.raises(PhigrosLoginError, match="502"):
        client.poll_token(make_qr_data())


def test_wait_for_token_polls_until_the_user_confirms() -> None:
    calls = 0

    def responder(_: urllib.request.Request) -> StubResponse:
        nonlocal calls
        calls += 1
        return json_response({"success": False}) if calls < 3 else json_response(TOKEN_RESPONSE)

    client = make_client(StubOpener(responder))

    token = client.wait_for_token(make_qr_data())

    assert token.access_token == "1/abc"
    assert calls == 3


def test_wait_for_token_slow_down_increases_interval() -> None:
    sleeps: list[float] = []
    calls = 0

    def responder(_: urllib.request.Request) -> StubResponse:
        nonlocal calls
        calls += 1
        if calls == 1:
            return json_response({"error": "slow_down"})
        if calls == 2:
            return json_response({"success": False})
        return json_response(TOKEN_RESPONSE)

    client = make_client(StubOpener(responder), sleeps=sleeps)

    client.wait_for_token(make_qr_data())

    # 第一次间隔被 slow_down 加一，之后恢复正常。
    assert sleeps == [2, 2]


def test_wait_for_token_stops_when_qr_code_expired() -> None:
    opener = StubOpener(lambda _: json_response({"success": False}))
    clock = Clock(FIXED_NOW)
    client = make_client(opener, clock)

    data = make_qr_data()
    clock.advance(301)

    with pytest.raises(PhigrosLoginError, match="过期"):
        client.wait_for_token(data)

    assert opener.requests == []


def test_qr_code_expiry_helpers() -> None:
    data = make_qr_data()

    assert not data.is_expired(FIXED_NOW + timedelta(seconds=299))
    assert data.is_expired(FIXED_NOW + timedelta(seconds=300))
    assert data.remaining_lifetime(FIXED_NOW) == timedelta(seconds=300)
    assert data.remaining_lifetime(FIXED_NOW + timedelta(seconds=400)) == timedelta(0)


# ---------- 账号资料 ----------


def test_get_profile_sends_mac_authorization_and_unwraps_data() -> None:
    opener = StubOpener(lambda _: json_response(PROFILE_RESPONSE))
    client = make_client(opener)

    profile = client.get_profile(make_token())

    assert profile["openid"] == "open-1"
    assert profile["name"] == "鸽子"

    request = opener.last
    assert request.full_url == "https://open.tapapis.cn/account/profile/v1?client_id=rAK3FfdieFob2Nn8Am"
    assert request.method == "GET"

    authorization = header_of(request, "Authorization")
    assert authorization.startswith(f'MAC id="{KID}", ts="')
    assert 'nonce="' in authorization
    assert 'mac="' in authorization


@pytest.mark.parametrize(
    "scope",
    ["public_profile", "public_profile,compliance", "public_profile compliance", "compliance public_profile"],
)
def test_token_recognizes_profile_scope_in_both_separators(scope: str) -> None:
    # 真实返回的 scope 是空格分隔的 "public_profile compliance"。
    assert make_token(scope=scope).has_profile_scope


def test_token_rejects_scope_without_public_profile() -> None:
    assert not make_token(scope="compliance").has_profile_scope
    assert not make_token(scope="").has_profile_scope


def test_get_profile_reports_server_error() -> None:
    opener = StubOpener(lambda _: json_response({"success": False, "error": "invalid_token"}))
    client = make_client(opener)

    with pytest.raises(PhigrosLoginError, match="invalid_token"):
        client.get_profile(make_token())


def test_get_profile_requires_public_profile_scope() -> None:
    opener = StubOpener(lambda _: json_response(PROFILE_RESPONSE))
    client = make_client(opener)

    with pytest.raises(PhigrosLoginError, match="public_profile"):
        client.get_profile(make_token(scope="user_info"))

    assert opener.requests == []


# ---------- 换取 sessionToken ----------


def test_exchange_session_token_posts_auth_data_and_signs_request() -> None:
    opener = StubOpener(
        lambda _: json_response({"objectId": "user-1", "username": "phi_abc", "sessionToken": "session-token-1"})
    )
    clock = Clock(FIXED_NOW)
    login_client = PhigrosLogin(TapTapRegion.CHINA, clock=clock, opener=opener)

    result = login_client.exchange_session_token(
        {"openid": "open-1", "name": "鸽子"},
        {"access_token": "1/abc", "mac_key": "k"},
    )

    assert result.session_token == "session-token-1"
    assert result.user_id == "user-1"
    assert result.username == "phi_abc"
    assert result.nickname == "鸽子"
    assert result.region is TapTapRegion.CHINA

    request = opener.last
    assert request.full_url == "https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/users"
    assert header_of(request, "X-LC-Id") == LeanCloudApp.CHINA_APP_ID

    # 签名是 md5(时间戳 + AppKey) + "," + 时间戳，时间戳取自注入的时钟。
    timestamp = int(FIXED_NOW.timestamp())
    digest = hashlib.md5(f"{timestamp}{LeanCloudApp.CHINA_APP_KEY}".encode()).hexdigest()
    assert header_of(request, "X-LC-Sign") == f"{digest},{timestamp}"

    body = json.loads(body_text(request))
    assert body["authData"]["taptap"]["openid"] == "open-1"
    assert body["authData"]["taptap"]["access_token"] == "1/abc"


def test_exchange_session_token_uses_global_app() -> None:
    opener = StubOpener(lambda _: json_response({"sessionToken": "t"}))
    login_client = PhigrosLogin(TapTapRegion.GLOBAL, opener=opener)

    login_client.exchange_session_token({}, {})

    request = opener.last
    assert request.full_url == f"{LeanCloudApp.GLOBAL_BASE_URL}users"
    assert header_of(request, "X-LC-Id") == LeanCloudApp.GLOBAL_APP_ID


def test_exchange_session_token_raises_on_error_payload() -> None:
    opener = StubOpener(lambda _: json_response({"code": 210, "error": "invalid authData"}))
    login_client = PhigrosLogin(TapTapRegion.CHINA, opener=opener)

    with pytest.raises(PhigrosLoginError, match="invalid authData"):
        login_client.exchange_session_token({}, {})


def test_exchange_session_token_raises_when_token_missing() -> None:
    opener = StubOpener(lambda _: json_response({"objectId": "user-1"}))
    login_client = PhigrosLogin(TapTapRegion.CHINA, opener=opener)

    with pytest.raises(PhigrosLoginError, match="sessionToken"):
        login_client.exchange_session_token({}, {})


def test_exchange_session_token_reports_network_failure() -> None:
    def raise_url_error(request: urllib.request.Request, timeout: float) -> StubResponse:
        raise urllib.error.URLError("no route to host")

    login_client = PhigrosLogin(TapTapRegion.CHINA, opener=raise_url_error)

    with pytest.raises(PhigrosLoginError, match="网络"):
        login_client.exchange_session_token({}, {})


def test_login_result_creates_client_with_region() -> None:
    opener = StubOpener(lambda _: json_response({"sessionToken": "t"}))
    login_client = PhigrosLogin(TapTapRegion.GLOBAL, opener=opener)

    result = login_client.exchange_session_token({"name": "鸽子"}, {})
    client = result.create_client()

    assert client.session_token == "t"
    assert client.api.app == LeanCloudApp.global_()


# ---------- 整体流程 ----------


def test_login_runs_the_whole_flow() -> None:
    def responder(request: urllib.request.Request) -> StubResponse:
        path = request.full_url
        if path.endswith("/oauth2/v1/device/code"):
            return json_response(QR_RESPONSE)
        if path.endswith("/oauth2/v1/token"):
            return json_response(TOKEN_RESPONSE)
        if "/account/profile/v1" in path:
            return json_response(PROFILE_RESPONSE)
        if path.endswith("/1.1/users"):
            return json_response({"objectId": "user-1", "sessionToken": "session-token-1"})
        return json_response({}, status=404)

    opener = StubOpener(responder)
    login_client = PhigrosLogin(TapTapRegion.CHINA, clock=Clock(FIXED_NOW), opener=opener)

    shown: list[QrCodeData] = []
    result = login_client.login(shown.append)

    assert result.session_token == "session-token-1"
    assert result.nickname == "鸽子"
    assert shown[0].url == "https://accounts.tapapis.cn/qr/device-123"
    assert len(opener.requests) == 4
