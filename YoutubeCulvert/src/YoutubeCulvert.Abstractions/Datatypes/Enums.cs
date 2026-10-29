namespace YoutubeCulvert.Datatypes;

/// <summary>Kind of results a search asks for.</summary>
public enum SearchType
{
    All = 0,
    Video = 1,
    Channel = 2,
    Playlist = 3,

    /// <summary>Live broadcasts (video type plus the live feature flag).</summary>
    Live = 4,
    Movie = 5,

    /// <summary>YouTube Shorts.</summary>
    Shorts = 6
}

/// <summary>Result ordering of a search.</summary>
public enum SortOrder
{
    Relevance = 0,
    Rating = 1,
    UploadDate = 2,
    ViewCount = 3
}

/// <summary>Upload-date restriction of a search.</summary>
public enum SearchUploadDate
{
    Any = 0,
    Today = 2,
    ThisWeek = 3,
    ThisMonth = 4,
    ThisYear = 5
}

/// <summary>Video-length restriction of a search.</summary>
public enum SearchDuration
{
    Any = 0,

    /// <summary>Longer than 20 minutes.</summary>
    Long = 2,

    /// <summary>Shorter than 3 minutes.</summary>
    Short = 4,

    /// <summary>Between 3 and 20 minutes.</summary>
    Medium = 5
}

/// <summary>Extra feature filters of a search.</summary>
[Flags]
public enum SearchFeatures
{
    None = 0,
    Hd = 1 << 0,
    Subtitles = 1 << 1,
    CreativeCommons = 1 << 2,
    ThreeD = 1 << 3,
    Live = 1 << 4,
    Purchased = 1 << 5,
    FourK = 1 << 6,
    ThreeSixty = 1 << 7,
    Location = 1 << 8,
    Hdr = 1 << 9,
    Vr180 = 1 << 10
}

/// <summary>Filters applied to a search.</summary>
public sealed record SearchFilter
{
    public SearchType Type { get; init; } = SearchType.All;

    public SearchUploadDate UploadDate { get; init; } = SearchUploadDate.Any;

    public SearchDuration Duration { get; init; } = SearchDuration.Any;

    public SearchFeatures Features { get; init; } = SearchFeatures.None;
}

/// <summary>Ordering of a comment section.</summary>
public enum CommentSortOrder
{
    /// <summary>Most relevant comments first (YouTube default).</summary>
    TopComments = 0,

    /// <summary>Newest comments first.</summary>
    NewestFirst = 1
}

/// <summary>The viewer's rating of a video.</summary>
public enum LikeState
{
    None = 0,
    Like = 1,
    Dislike = 2
}

/// <summary>Visibility of a playlist that is being created.</summary>
public enum PlaylistPrivacy
{
    Public = 0,
    Private = 1,
    Unlisted = 2
}

/// <summary>Notification level of a subscription.</summary>
public enum SubscriptionNotifications
{
    None = 0,
    Personalized = 1,
    All = 2
}

/// <summary>A tab of a channel page.</summary>
public enum ChannelTab
{
    Videos,
    Shorts,
    Live,
    Playlists,
    Community,
    About
}

/// <summary>A category of the trending page.</summary>
public enum TrendingTab
{
    Now,
    Music,
    Gaming,
    Movies
}

/// <summary>Options controlling enumeration of a paginated source.</summary>
/// <param name="MaxPages">Maximum number of requests; null means unlimited.</param>
/// <param name="MaxItems">Maximum number of items; null means unlimited.</param>
public sealed record PaginationOptions(int? MaxPages = null, int? MaxItems = null);
