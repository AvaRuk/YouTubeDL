namespace YoutubeCulvert.Datatypes;

/// <summary>A page of results with a continuation token.</summary>
public interface IPage<out T>
{
    IReadOnlyList<T> Items { get; }

    string? Continuation { get; }

    bool HasMore => !string.IsNullOrEmpty(Continuation);
}

/// <summary>A page of search results.</summary>
public sealed record SearchPage : IPage<SearchResult>
{
    public IReadOnlyList<SearchResult> Items { get; init; } = [];

    public string? Continuation { get; init; }

    /// <summary>Value of <c>estimatedResults</c>, when YouTube reports it.</summary>
    public long? EstimatedResults { get; init; }
}

/// <summary>A page of comments or replies.</summary>
public sealed record CommentPage : IPage<Comment>
{
    public IReadOnlyList<Comment> Items { get; init; } = [];

    public string? Continuation { get; init; }
}

/// <summary>A page of related videos.</summary>
public sealed record RelatedPage : IPage<VideoSummary>
{
    public IReadOnlyList<VideoSummary> Items { get; init; } = [];

    public string? Continuation { get; init; }
}

/// <summary>A page of videos.</summary>
public sealed record VideoPage : IPage<VideoSummary>
{
    public IReadOnlyList<VideoSummary> Items { get; init; } = [];

    public string? Continuation { get; init; }
}

/// <summary>A page of playlists.</summary>
public sealed record PlaylistPage : IPage<PlaylistSummary>
{
    public IReadOnlyList<PlaylistSummary> Items { get; init; } = [];

    public string? Continuation { get; init; }
}

/// <summary>A page of channels.</summary>
public sealed record ChannelPage : IPage<ChannelSummary>
{
    public IReadOnlyList<ChannelSummary> Items { get; init; } = [];

    public string? Continuation { get; init; }
}

/// <summary>A page of notifications.</summary>
public sealed record NotificationPage : IPage<NotificationItem>
{
    public IReadOnlyList<NotificationItem> Items { get; init; } = [];

    public string? Continuation { get; init; }
}

/// <summary>A page of home-feed entries.</summary>
public sealed record FeedPage : IPage<FeedItem>
{
    public IReadOnlyList<FeedItem> Items { get; init; } = [];

    public string? Continuation { get; init; }
}

/// <summary>A page of watch-history entries.</summary>
public sealed record HistoryPage : IPage<HistoryItem>
{
    public IReadOnlyList<HistoryItem> Items { get; init; } = [];

    public string? Continuation { get; init; }
}

/// <summary>A page of playlist entries.</summary>
public sealed record PlaylistVideoPage : IPage<PlaylistVideo>
{
    public IReadOnlyList<PlaylistVideo> Items { get; init; } = [];

    public string? Continuation { get; init; }
}
