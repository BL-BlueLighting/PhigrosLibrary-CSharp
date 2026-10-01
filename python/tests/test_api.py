"""接口层的测试，全部使用桩实现，不产生任何网络请求。"""

from __future__ import annotations

import json
import urllib.error
import urllib.request

from stubs import StubOpener, StubResponse, header_of, json_response

import pytest

from PhigrosScoreLibrary import APP_ID, APP_KEY, LeanCloudClient, Summary, write_summary
from PhigrosScoreLibrary.exceptions import PhigrosApiError

SESSION_TOKEN = "fake-session-token"


def save_record_payload(summary_b64: str) -> dict:
    return {
        "results": [
            {
                "objectId": "save-1",
                "summary": summary_b64,
                "updatedAt": "2024-05-06T07:08:09.000Z",
                "gameFile": {"objectId": "file-1", "url": "https://example.invalid/save.zip"},
                "user": {"__type": "Pointer", "objectId": "user-1"},
            }
        ]
    }


def test_parses_save_record() -> None:
    opener = StubOpener(lambda _: json_response(save_record_payload("c3VtbWFyeQ==")))
    client = LeanCloudClient(opener=opener)

    record = client.get_save_record(SESSION_TOKEN)

    assert record.object_id == "save-1"
    assert record.summary == "c3VtbWFyeQ=="
    assert record.file_id == "file-1"
    assert record.url == "https://example.invalid/save.zip"
    assert record.user_id == "user-1"
    assert record.updated_at is not None
    assert record.updated_at.isoformat() == "2024-05-06T07:08:09+00:00"


def test_sends_expected_headers() -> None:
    opener = StubOpener(lambda _: json_response(save_record_payload("c3VtbWFyeQ==")))
    LeanCloudClient(opener=opener).get_save_record(SESSION_TOKEN)

    request = opener.requests[0]
    assert request.full_url == "https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/classes/_GameSave?limit=1"
    assert header_of(request, "X-LC-Session") == SESSION_TOKEN
    assert header_of(request, "X-LC-Id") == APP_ID
    assert header_of(request, "X-LC-Key") == APP_KEY


def test_parses_nickname() -> None:
    opener = StubOpener(lambda _: json_response({"nickname": "鸽子"}))
    client = LeanCloudClient(opener=opener)

    assert client.get_nickname(SESSION_TOKEN) == "鸽子"
    assert opener.requests[0].full_url.endswith("/users/me")


def test_attaches_cloud_metadata_to_summary() -> None:
    summary = Summary(
        save_version=1,
        challenge_mode_rank=7,
        ranking_score=16.5,
        game_version=87,
        avatar="Introduction.0",
        progress=list(range(12)),
    )
    opener = StubOpener(lambda _: json_response(save_record_payload(write_summary(summary))))
    client = LeanCloudClient(opener=opener)

    parsed = client.get_summary(SESSION_TOKEN)

    assert parsed.ranking_score == pytest.approx(16.5)
    assert parsed.object_id == "save-1"
    assert parsed.user_id == "user-1"
    assert parsed.file_id == "file-1"
    assert parsed.url == "https://example.invalid/save.zip"


def test_throws_with_server_error_message() -> None:
    opener = StubOpener(lambda _: json_response({"code": 210, "error": "sessionToken 无效"}))
    client = LeanCloudClient(opener=opener)

    with pytest.raises(PhigrosApiError) as info:
        client.get_nickname(SESSION_TOKEN)

    assert "sessionToken 无效" in str(info.value)
    assert "210" in str(info.value)


def test_throws_when_no_save_exists() -> None:
    opener = StubOpener(lambda _: json_response({"results": []}))
    client = LeanCloudClient(opener=opener)

    with pytest.raises(PhigrosApiError):
        client.get_save_record(SESSION_TOKEN)


def test_throws_for_non_json_body() -> None:
    opener = StubOpener(lambda _: StubResponse(b"<html>502</html>", status=502))
    client = LeanCloudClient(opener=opener)

    with pytest.raises(PhigrosApiError):
        client.get_save_record(SESSION_TOKEN)


def test_throws_on_http_error() -> None:
    def raise_http_error(request: urllib.request.Request, timeout: float) -> StubResponse:
        raise urllib.error.HTTPError(request.full_url, 404, "Not Found", {}, None)

    client = LeanCloudClient(opener=raise_http_error)

    with pytest.raises(PhigrosApiError, match="404"):
        client.get_save_record(SESSION_TOKEN)


def test_throws_on_network_failure() -> None:
    def raise_url_error(request: urllib.request.Request, timeout: float) -> StubResponse:
        raise urllib.error.URLError("no route to host")

    client = LeanCloudClient(opener=raise_url_error)

    with pytest.raises(PhigrosApiError):
        client.get_nickname(SESSION_TOKEN)


def test_downloads_save_bytes() -> None:
    payload = b"\x01\x02\x03\x04"
    opener = StubOpener(lambda _: StubResponse(payload))
    client = LeanCloudClient(opener=opener)

    assert client.download_save("https://example.invalid/save.zip") == payload
    assert opener.requests[0].full_url == "https://example.invalid/save.zip"


def test_rejects_empty_session_token() -> None:
    client = LeanCloudClient(opener=StubOpener(lambda _: json_response({})))

    with pytest.raises(ValueError):
        client.get_nickname("")
