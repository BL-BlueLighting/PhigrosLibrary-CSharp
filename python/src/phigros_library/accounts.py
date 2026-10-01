"""区服与应用配置。

国服与国际服使用两套独立的 TapTap 与 LeanCloud 端点，账号数据不互通。
这些配置同时被查分接口（:mod:`.api`）与登录流程（:mod:`.login`）使用，
放在这里以免两者互相导入。
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class TapTapRegion(Enum):
    """账号所在的服务区。"""

    CHINA = "china"
    """国服（accounts.tapapis.cn / rak3ffdi.cloud.tds1.tapapis.cn）。"""

    GLOBAL = "global"
    """国际服（accounts.tapapis.com / kviehlel.cloud.ap-sg.tapapis.com）。"""

    @classmethod
    def parse(cls, value: str) -> TapTapRegion:
        """解析区服名，兼容若干常见写法。"""
        normalized = value.strip().lower()
        if normalized in {"china", "cn", "tapcn", "国服"}:
            return cls.CHINA
        if normalized in {"global", "gb", "tapio", "国际服"}:
            return cls.GLOBAL

        raise ValueError(f"无法识别的区服 '{value}'，可选 china 或 global。")


@dataclass(frozen=True, slots=True)
class LeanCloudApp:
    """一个区服对应的 LeanCloud 应用配置。"""

    app_id: str
    """LeanCloud AppId，作为 ``X-LC-Id`` 发送。"""

    app_key: str
    """LeanCloud AppKey，用于计算 ``X-LC-Sign``。"""

    base_url: str
    """接口根地址，末尾带斜杠。"""

    region: TapTapRegion
    """该应用所属的区服。"""

    #: 国服 Phigros 的 AppId。
    CHINA_APP_ID = "rAK3FfdieFob2Nn8Am"
    #: 国服 Phigros 的 AppKey。
    CHINA_APP_KEY = "Qr9AEqtuoSVS3zeD6iVbM4ZC0AtkJcQ89tywVyi0"
    #: 国服接口根地址。
    CHINA_BASE_URL = "https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/"
    #: 国际服 Phigros 的 AppId。
    GLOBAL_APP_ID = "kviehleldgxsagpozb"
    #: 国际服 Phigros 的 AppKey。
    GLOBAL_APP_KEY = "tG9CTm0LDD736k9HMM9lBZrbeBGRmUkjSfNLDNib"
    #: 国际服接口根地址。
    GLOBAL_BASE_URL = "https://kviehlel.cloud.ap-sg.tapapis.com/1.1/"

    @classmethod
    def china(cls) -> LeanCloudApp:
        """国服。"""
        return cls(cls.CHINA_APP_ID, cls.CHINA_APP_KEY, cls.CHINA_BASE_URL, TapTapRegion.CHINA)

    @classmethod
    def global_(cls) -> LeanCloudApp:
        """国际服（``global`` 是 Python 关键字，因此带下划线）。"""
        return cls(cls.GLOBAL_APP_ID, cls.GLOBAL_APP_KEY, cls.GLOBAL_BASE_URL, TapTapRegion.GLOBAL)

    @classmethod
    def for_region(cls, region: TapTapRegion) -> LeanCloudApp:
        """取指定区服的应用配置。"""
        return cls.global_() if region is TapTapRegion.GLOBAL else cls.china()
