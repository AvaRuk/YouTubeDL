namespace YoutubeCulvert.Datatypes;

/// <summary>Detailed video information from the <c>player</c> endpoint.</summary>
public sealed record Video : YoutubeEntity
{
    public required string Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? ChannelId { get; init; }

    /// <summary>Channel display name (<c>videoDetails.author</c>).</summary>
    public string? ChannelTitle { get; init; }

    /// <summary>Canonical <c>@handle</c> of the channel, when YouTube reports it.</summary>
    public string? ChannelHandle { get; init; }

    public TimeSpan Duration { get; init; }

    public long? ViewCount { get; init; }

    public long? LikeCount { get; init; }

    public DateTimeOffset? PublishDate { get; init; }

    public DateTimeOffset? UploadDate { get; init; }

    public IReadOnlyList<Thumbnail> Thumbnails { get; init; } = [];

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public bool IsLive { get; init; }

    public bool IsUpcoming { get; init; }

    public bool IsAgeRestricted { get; init; }

    public PlayabilityStatus PlayabilityStatus { get; init; } = YoutubeCulvert.Datatypes.PlayabilityStatus.Ok;

    public IReadOnlyList<Chapter> Chapters { get; init; } = [];

    /// <summary>Related videos when the caller asked for them.</summary>
    public IReadOnlyList<VideoSummary>? RelatedVideos { get; init; }

    public string Url => $"https://www.youtube.com/watch?v={Id}";
}

/// <summary>Brief video information for lists (search, feeds, playlists).</summary>
public sealed record VideoSummary : YoutubeEntity
{
    public required string Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? ChannelId { get; init; }

    public string? ChannelTitle { get; init; }

    public string? ChannelHandle { get; init; }

    public IReadOnlyList<Thumbnail> Thumbnails { get; init; } = [];

    public TimeSpan? Duration { get; init; }

    public long? ViewCount { get; init; }

    public string? ViewCountText { get; init; }

    /// <summary>Relative publish time as shown by YouTube, for example "2 days ago".</summary>
    public string? PublishedTime { get; init; }

    public bool IsLive { get; init; }

    /// <summary>Playlist entry key, required to remove or reorder that entry.</summary>
    public string? SetVideoId { get; init; }

    public string? ThumbnailUrl => BestThumbnail(Thumbnails);

    public string Url => $"https://www.youtube.com/watch?v={Id}";

    internal static string? BestThumbnail(IReadOnlyList<Thumbnail> thumbnails) =>
        thumbnails
            .OrderByDescending(static thumbnail => (long)thumbnail.Width * thumbnail.Height)
            .Select(static thumbnail => thumbnail.Url)
            .FirstOrDefault();
}

/// <summary>Brief channel information (search result, feed entry).</summary>
public sealed record ChannelSummary : YoutubeEntity
{
    public required string Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Handle { get; init; }

    public IReadOnlyList<Thumbnail> Thumbnails { get; init; } = [];

    public long? SubscriberCount { get; init; }

    public string? SubscriberCountText { get; init; }

    public string? VideoCountText { get; init; }

    public string? Description { get; init; }

    public string? ThumbnailUrl => BestThumbnail(Thumbnails);

    public string Url => $"https://www.youtube.com/channel/{Id}";

    internal static string? BestThumbnail(IReadOnlyList<Thumbnail> thumbnails) =>
        thumbnails
            .OrderByDescending(static thumbnail => (long)thumbnail.Width * thumbnail.Height)
            .Select(static thumbnail => thumbnail.Url)
            .FirstOrDefault();
}

/// <summary>Brief playlist information.</summary>
public sealed record PlaylistSummary : YoutubeEntity
{
    public required string Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? ChannelId { get; init; }

    public string? ChannelTitle { get; init; }

    public IReadOnlyList<Thumbnail> Thumbnails { get; init; } = [];

    public int? ItemCount { get; init; }

    public string? ItemCountText { get; init; }

    public string? ThumbnailUrl => BestThumbnail(Thumbnails);

    public string Url => $"https://www.youtube.com/playlist?list={Id}";

    internal static string? BestThumbnail(IReadOnlyList<Thumbnail> thumbnails) =>
        thumbnails
            .OrderByDescending(static thumbnail => (long)thumbnail.Width * thumbnail.Height)
            .Select(static thumbnail => thumbnail.Url)
            .FirstOrDefault();
}
