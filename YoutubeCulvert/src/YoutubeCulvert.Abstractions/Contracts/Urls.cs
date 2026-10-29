namespace YoutubeCulvert.Url;

/// <summary>A reference to a channel: by id, handle, URL or legacy username.</summary>
public abstract record ChannelRef
{
    public sealed record ById(string ChannelId) : ChannelRef;

    /// <summary>Handle without the leading <c>@</c>.</summary>
    public sealed record ByHandle(string Handle) : ChannelRef;

    public sealed record ByUrl(string Url) : ChannelRef;

    /// <summary>Legacy <c>/user/…</c> name.</summary>
    public sealed record ByUsername(string Username) : ChannelRef;
}

/// <summary>A watch reference that keeps <c>list</c> and <c>index</c> parameters.</summary>
public sealed record VideoRef(
    string VideoId,
    string? PlaylistId = null,
    int? Index = null,
    TimeSpan? StartAt = null);

/// <summary>Parsing and construction of YouTube URLs and identifiers.</summary>
public interface IYoutubeUrlParser
{
    bool TryParseVideoId(string url, out string? videoId);

    bool TryParsePlaylistId(string url, out string? playlistId);

    bool TryParseChannel(string url, out ChannelRef? channel);

    /// <summary>Parses a watch URL, preserving <c>list</c>, <c>index</c> and <c>t</c>.</summary>
    bool TryParseVideoRef(string url, out VideoRef? video);

    string BuildVideoUrl(string videoId, TimeSpan? t = null);

    string BuildChannelUrl(ChannelRef channel);

    string ParseVideoIdOrThrow(string url);

    string ParsePlaylistIdOrThrow(string url);

    ChannelRef ParseChannelOrThrow(string url);
}
