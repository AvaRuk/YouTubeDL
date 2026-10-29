namespace YouTubeDL.Models;

/// <summary>Persisted queue entry (data/queue.json).</summary>
public sealed class QueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The original user input (URL or video id).</summary>
    public string Input { get; set; } = string.Empty;

    public string VideoId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ChannelTitle { get; set; } = string.Empty;

    public DownloadKind Kind { get; set; } = DownloadKind.Video;

    public string? QualityId { get; set; } = "best";

    public string? FormatId { get; set; } = "auto";

    public string? AudioTrackId { get; set; }

    public string? AudioTrackName { get; set; }

    public string? FileNameTemplate { get; set; }

    public string? CustomFilePath { get; set; }

    public string? OutputDirectory { get; set; }

    public DownloadPriority Priority { get; set; } = DownloadPriority.Interactive;

    public QueueStatus Status { get; set; } = QueueStatus.Queued;

    public string? ErrorText { get; set; }

    public ErrorCategory ErrorCategory { get; set; }

    public string OutputPath { get; set; } = string.Empty;

    public double Progress { get; set; }

    public long? BytesTotal { get; set; }

    public long BytesDone { get; set; }

    public double SpeedBytesPerSecond { get; set; }

    public TimeSpan? Duration { get; set; }

    public TimeSpan? Eta { get; set; }

    public string? ThumbnailUrl { get; set; }

    public string? PlaylistName { get; set; }

    public int? PlaylistIndex { get; set; }

    public int? SpeedLimitKbps { get; set; }

    public bool DownloadSubtitles { get; set; }

    public bool SaveThumbnail { get; set; }

    public bool EmbedMetadata { get; set; } = true;

    public bool SaveDescription { get; set; }

    public bool SaveJsonMetadata { get; set; }

    public bool SaveChapters { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? CompletedAt { get; set; }

    public DownloadRequest ToRequest() => new()
    {
        Input = string.IsNullOrWhiteSpace(VideoId) ? Input : VideoId,
        Kind = Kind,
        QualityId = QualityId,
        FormatId = FormatId,
        AudioTrackId = AudioTrackId,
        AudioTrackName = AudioTrackName,
        FileNameTemplate = FileNameTemplate,
        CustomFilePath = CustomFilePath,
        OutputDirectory = OutputDirectory,
        DownloadSubtitles = DownloadSubtitles,
        SaveThumbnail = SaveThumbnail,
        EmbedMetadata = EmbedMetadata,
        SaveDescription = SaveDescription,
        SaveJsonMetadata = SaveJsonMetadata,
        SaveChapters = SaveChapters,
        SpeedLimitKbps = SpeedLimitKbps,
        PlaylistName = PlaylistName,
        PlaylistIndex = PlaylistIndex,
        Priority = Priority
    };

    public QueueItem CloneForRestart() => new()
    {
        Id = Id,
        Input = Input,
        VideoId = VideoId,
        Title = Title,
        ChannelTitle = ChannelTitle,
        Kind = Kind,
        QualityId = QualityId,
        FormatId = FormatId,
        AudioTrackId = AudioTrackId,
        AudioTrackName = AudioTrackName,
        FileNameTemplate = FileNameTemplate,
        CustomFilePath = CustomFilePath,
        OutputDirectory = OutputDirectory,
        Priority = Priority,
        Status = QueueStatus.Queued,
        OutputPath = OutputPath,
        BytesTotal = BytesTotal,
        Duration = Duration,
        ThumbnailUrl = ThumbnailUrl,
        PlaylistName = PlaylistName,
        PlaylistIndex = PlaylistIndex,
        SpeedLimitKbps = SpeedLimitKbps,
        DownloadSubtitles = DownloadSubtitles,
        SaveThumbnail = SaveThumbnail,
        EmbedMetadata = EmbedMetadata,
        SaveDescription = SaveDescription,
        SaveJsonMetadata = SaveJsonMetadata,
        SaveChapters = SaveChapters,
        CreatedAt = CreatedAt
    };
}
