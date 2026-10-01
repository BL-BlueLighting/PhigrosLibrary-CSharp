using System.Text.Json.Serialization;

namespace PhigrosLibrary.Models;

/// <summary>
/// <c>settings</c> 条目：游戏内设置。结构固定，不带版本号。
/// </summary>
public sealed class Settings
{
    [JsonPropertyName("chordSupport")]
    public bool ChordSupport { get; set; }

    /// <summary>是否显示 FC / AP 指示。</summary>
    [JsonPropertyName("fcAPIndicator")]
    public bool FcApIndicator { get; set; }

    [JsonPropertyName("enableHitSound")]
    public bool EnableHitSound { get; set; }

    [JsonPropertyName("lowResolutionMode")]
    public bool LowResolutionMode { get; set; }

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("bright")]
    public float Bright { get; set; }

    [JsonPropertyName("musicVolume")]
    public float MusicVolume { get; set; }

    [JsonPropertyName("effectVolume")]
    public float EffectVolume { get; set; }

    [JsonPropertyName("hitSoundVolume")]
    public float HitSoundVolume { get; set; }

    /// <summary>音画偏移。</summary>
    [JsonPropertyName("soundOffset")]
    public float SoundOffset { get; set; }

    [JsonPropertyName("noteScale")]
    public float NoteScale { get; set; }
}
