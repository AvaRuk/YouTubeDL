namespace YoutubeCulvert.Datatypes;

/// <summary>A link published on a channel page.</summary>
public sealed record ChannelLink(string Title, string Url);

/// <summary>Detailed channel information from a <c>browse</c> channel page.</summary>
public sealed record Channel : YoutubeEntity
{
    public required string Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Handle { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<Thumbnail> Avatars { get; init; } = [];

    public IReadOnlyList<Thumbnail> Banners { get; init; } = [];

    public long? SubscriberCount { get; init; }

    public long? VideoCount { get; init; }

    public IReadOnlyList<ChannelLink> Links { get; init; } = [];

    public bool IsVerified { get; init; }

    /// <summary>Available tabs, for example <c>"videos"</c>, <c>"shorts"</c>, <c>"playlists"</c>.</summary>
    public IReadOnlyList<string> Tabs { get; init; } = [];

    public string Url => $"https://www.youtube.com/channel/{Id}";
}

/// <summary>Detailed playlist information.</summary>
public sealed record Playlist : YoutubeEntity
{
    public required string Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? OwnerChannelId { get; init; }

    public string? OwnerChannelTitle { get; init; }

    public int? ItemCount { get; init; }

    public IReadOnlyList<Thumbnail> Thumbnails { get; init; } = [];

    public string Url => $"https://www.youtube.com/playlist?list={Id}";
}

/// <summary>An entry of a playlist; <see cref="SetVideoId"/> enables edits.</summary>
public sealed record PlaylistVideo : YoutubeEntity
{
    public required string VideoId { get; init; }

    public string Title { get; init; } = string.Empty;

    public int? Index { get; init; }

    public string? SetVideoId { get; init; }

    public TimeSpan? Duration { get; init; }

    public IReadOnlyList<Thumbnail> Thumbnails { get; init; } = [];

    public string Url => $"https://www.youtube.com/watch?v={VideoId}";
}

/// <summary>A comment under a video.</summary>
public sealed record Comment : YoutubeEntity
{
    public required string Id { get; init; }

    public string? Author { get; init; }

    public string? AuthorChannelId { get; init; }

    public Thumbnail? AuthorThumbnail { get; init; }

    public string? Text { get; init; }

    public long? LikeCount { get; init; }

    public int? ReplyCount { get; init; }

    /// <summary>Relative publish time as shown by YouTube.</summary>
    public string? PublishedTime { get; init; }

    public bool IsPinned { get; init; }

    /// <summary>True when the comment author is the owner of the video.</summary>
    public bool IsHearted { get; init; }

    /// <summary>Token that loads the replies of this comment; null when there are none.</summary>
    public string? RepliesContinuation { get; init; }
}

/// <summary>A reply to a top-level comment.</summary>
public sealed record CommentReply : YoutubeEntity
{
    public required string Id { get; init; }

    public string? Author { get; init; }

    public string? AuthorChannelId { get; init; }

    public string? Text { get; init; }

    public long? LikeCount { get; init; }

    public string? PublishedTime { get; init; }
}

/// <summary>A comment together with its first batch of replies.</summary>
public sealed record CommentThread : YoutubeEntity
{
    public required Comment Comment { get; init; }

    public IReadOnlyList<CommentReply> Replies { get; init; } = [];

    public string? Continuation { get; init; }
}

/// <summary>Kind of an entry returned by search.</summary>
public enum SearchResultKind
{
    Video,
    Channel,
    Playlist,
    /// <summary>A search refinement ("filter") chip advertised by YouTube.</summary>
    Filter
}

/// <summary>A single search result.</summary>
public sealed record SearchResult : YoutubeEntity
{
    public required SearchResultKind Kind { get; init; }

    public VideoSummary? Video { get; init; }

    public ChannelSummary? Channel { get; init; }

    public PlaylistSummary? Playlist { get; init; }

    /// <summary>Label of a filter/refinement entry.</summary>
    public string? FilterName { get; init; }

    /// <summary>Identifier of the underlying entity, when there is one.</summary>
    public string? Id => Kind switch
    {
        SearchResultKind.Video => Video?.Id,
        SearchResultKind.Channel => Channel?.Id,
        SearchResultKind.Playlist => Playlist?.Id,
        _ => null
    };

    public string? Title => Kind switch
    {
        SearchResultKind.Video => Video?.Title,
        SearchResultKind.Channel => Channel?.Title,
        SearchResultKind.Playlist => Playlist?.Title,
        _ => FilterName
    };
}

/// <summary>The signed-in YouTube account.</summary>
public sealed record Account : YoutubeEntity
{
    public string? Name { get; init; }

    public string? ChannelId { get; init; }

    public string? Handle { get; init; }

    public string? Email { get; init; }

    public string? AvatarUrl { get; init; }
}

/// <summary>An entry of the notifications inbox.</summary>
public sealed record NotificationItem : YoutubeEntity
{
    public required string Id { get; init; }

    public string? Text { get; init; }

    public string? PublishedTime { get; init; }

    public string? ThumbnailUrl { get; init; }

    public bool IsRead { get; init; }

    public string? VideoId { get; init; }

    public string? ChannelId { get; init; }

    public string? PlaylistId { get; init; }
}

/// <summary>One item of the home/recommendation feed.</summary>
public sealed record FeedItem : YoutubeEntity
{
    public required VideoSummary Video { get; init; }
}

/// <summary>One entry of the watch history.</summary>
public sealed record HistoryItem : YoutubeEntity
{
    public required VideoSummary Video { get; init; }

    public string? WatchedAt { get; init; }
}

/// <summary>One entry of the trending page.</summary>
public sealed record TrendingItem : YoutubeEntity
{
    public required VideoSummary Video { get; init; }

    public int? Rank { get; init; }

    public string? Category { get; init; }
}
