using YoutubeCulvert.Datatypes;

namespace YouTubeDL.Models;

/// <summary>A fully resolved download: streams, container, target path and requested artifacts.</summary>
public sealed record DownloadPlan
{
    public required string VideoId { get; init; }

    public required string Title { get; init; }

    public string? ChannelId { get; init; }

    public string ChannelTitle { get; init; } = string.Empty;

    public TimeSpan Duration { get; init; }

    public DateTimeOffset? UploadDate { get; init; }

    public long? ViewCount { get; init; }

    public string? Description { get; init; }

    public string? ThumbnailUrl { get; init; }

    public DownloadKind Kind { get; init; } = DownloadKind.Video;

    public required MediaFormat Container { get; init; }

    public required string OutputPath { get; init; }

    public MediaStream? VideoStream { get; init; }

    public MediaStream? AudioStream { get; init; }

    public string? AudioTrackId { get; init; }

    public string? AudioTrackName { get; init; }

    public bool IsAudioOnly { get; init; }

    /// <summary>True when video and audio are separate streams and FFmpeg is required.</summary>
    public bool NeedsMux { get; init; }

    /// <summary>True when the downloaded file must be remuxed into another container.</summary>
    public bool NeedsRemux { get; init; }

    /// <summary>True when the downloaded audio must be converted (mp3/opus/flac).</summary>
    public bool NeedsAudioConversion { get; init; }

    public int? MaxHeight { get; init; }

    public string? FileNameTemplate { get; init; }

    public string? PlaylistName { get; init; }

    public int? PlaylistIndex { get; init; }

    public string? QualityLabel { get; init; }

    public bool DownloadSubtitles { get; init; }

    public bool SaveThumbnail { get; init; }

    public bool EmbedMetadata { get; init; }

    public bool SaveDescription { get; init; }

    public bool SaveJsonMetadata { get; init; }

    public bool SaveChapters { get; init; }

    /// <summary>Per-item speed limit in KB/s; falls back to the global setting.</summary>
    public int? SpeedLimitKbps { get; init; }

    public IReadOnlyList<Chapter> Chapters { get; init; } = [];

    public long? VideoBytes { get; init; }

    public long? AudioBytes { get; init; }

    public long? TotalBytes
    {
        get
        {
            var total = (VideoBytes ?? 0) + (AudioBytes ?? 0);
            return total > 0 ? total : null;
        }
    }
}

/// <summary>What the user asked for before the streams were resolved.</summary>
public sealed record DownloadRequest
{
    public required string Input { get; init; }

    public DownloadKind Kind { get; init; } = DownloadKind.Video;

    public string? QualityId { get; init; }

    public string? FormatId { get; init; }

    public string? AudioTrackId { get; init; }

    public string? AudioTrackName { get; init; }

    public string? FileNameTemplate { get; init; }

    public string? CustomFilePath { get; init; }

    /// <summary>Folder chosen in the download dialog; null means the default folder.</summary>
    public string? OutputDirectory { get; init; }

    public bool DownloadSubtitles { get; init; }

    public bool SaveThumbnail { get; init; }

    public bool EmbedMetadata { get; init; }

    public bool SaveDescription { get; init; }

    public bool SaveJsonMetadata { get; init; }

    public bool SaveChapters { get; init; }

    public int? SpeedLimitKbps { get; init; }

    public string? PlaylistName { get; init; }

    public int? PlaylistIndex { get; init; }

    public DownloadPriority Priority { get; init; } = DownloadPriority.Interactive;
}
