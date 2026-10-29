using YouTubeDL.Services;

namespace YouTubeDL.Models;

/// <summary>Output container and the matching YouTube stream preference.</summary>
public sealed class MediaFormat
{
    public static IReadOnlyList<MediaFormat> VideoFormats { get; } =
    [
        new("auto", "FormatAuto", DownloadKind.Video, ".mp4", preferWebM: false, conversion: null),
        new("mp4", "FormatMp4", DownloadKind.Video, ".mp4", preferWebM: false, conversion: null),
        new("webm", "FormatWebmVideo", DownloadKind.Video, ".webm", preferWebM: true, conversion: null),
        new("mkv", "FormatMkv", DownloadKind.Video, ".mkv", preferWebM: false, conversion: null)
    ];

    public static IReadOnlyList<MediaFormat> AudioFormats { get; } =
    [
        new("auto", "FormatAuto", DownloadKind.Music, ".m4a", preferWebM: false, conversion: null),
        new("m4a", "FormatM4a", DownloadKind.Music, ".m4a", preferWebM: false, conversion: null),
        new("webm", "FormatWebmAudio", DownloadKind.Music, ".webm", preferWebM: true, conversion: null),
        new("mp3", "FormatMp3", DownloadKind.Music, ".mp3", preferWebM: false, conversion: "mp3"),
        new("opus", "FormatOpus", DownloadKind.Music, ".opus", preferWebM: true, conversion: "opus"),
        new("flac", "FormatFlac", DownloadKind.Music, ".flac", preferWebM: false, conversion: "flac")
    ];

    private MediaFormat(
        string id,
        string displayKey,
        DownloadKind kind,
        string extension,
        bool preferWebM,
        string? conversion)
    {
        Id = id;
        DisplayKey = displayKey;
        Kind = kind;
        Extension = extension;
        PreferWebM = preferWebM;
        Conversion = conversion;
    }

    public string Id { get; }

    public string DisplayKey { get; }

    public DownloadKind Kind { get; }

    public string Extension { get; }

    /// <summary>True when the container is WebM (VP9/Opus).</summary>
    public bool PreferWebM { get; }

    /// <summary>FFmpeg conversion target when the container is not a YouTube container.</summary>
    public string? Conversion { get; }

    public string Display => LocalizationService.Instance.Get(DisplayKey);

    public bool IsAudioConversion => Conversion is not null;

    public static IReadOnlyList<MediaFormat> ForKind(DownloadKind kind) =>
        kind == DownloadKind.Music ? AudioFormats : VideoFormats;

    public static MediaFormat Parse(string? id, DownloadKind kind)
    {
        var formats = ForKind(kind);
        return formats.FirstOrDefault(format =>
                   string.Equals(format.Id, id, StringComparison.OrdinalIgnoreCase)) ??
               formats[0];
    }
}
