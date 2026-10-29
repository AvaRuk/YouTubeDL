using YoutubeCulvert.Url;

namespace YoutubeCulvert;

/// <summary>
/// Static facade over <see cref="IYoutubeUrlParser"/> for callers that do not
/// use dependency injection.
/// </summary>
public static class YoutubeUrl
{
    private static readonly YoutubeUrlParser Parser = new();

    /// <summary>Returns the video identifier or throws <see cref="ArgumentException"/>.</summary>
    public static string GetVideoId(string? value) => Parser.ParseVideoIdOrThrow(value ?? string.Empty);

    /// <summary>Tries to extract an 11-character video identifier from a string or URL.</summary>
    public static bool TryGetVideoId(string? value, out string videoId)
    {
        var success = Parser.TryParseVideoId(value ?? string.Empty, out var parsed);
        videoId = parsed ?? string.Empty;
        return success && parsed is not null;
    }

    /// <summary>Returns the playlist identifier or throws <see cref="ArgumentException"/>.</summary>
    public static string GetPlaylistId(string? value) => Parser.ParsePlaylistIdOrThrow(value ?? string.Empty);

    /// <summary>Tries to extract a playlist identifier.</summary>
    public static bool TryGetPlaylistId(string? value, out string playlistId)
    {
        var success = Parser.TryParsePlaylistId(value ?? string.Empty, out var parsed);
        playlistId = parsed ?? string.Empty;
        return success && parsed is not null;
    }

    /// <summary>Returns the channel id (UC...) or throws <see cref="ArgumentException"/>.</summary>
    public static string GetChannelId(string? value)
    {
        var channel = Parser.ParseChannelOrThrow(value ?? string.Empty);

        return channel switch
        {
            ChannelRef.ById byId => byId.ChannelId,
            ChannelRef.ByHandle handle => throw new ArgumentException(
                $"'{value}' is a handle, not a UC channel identifier.", nameof(value)),
            _ => throw new ArgumentException(
                $"'{value}' does not contain a UC channel identifier.", nameof(value))
        };
    }

    /// <summary>Tries to extract a channel identifier from a /channel/UC... URL.</summary>
    public static bool TryGetChannelId(string? value, out string channelId)
    {
        channelId = string.Empty;

        if (!Parser.TryParseChannel(value ?? string.Empty, out var channel) ||
            channel is not ChannelRef.ById byId)
        {
            return false;
        }

        channelId = byId.ChannelId;
        return true;
    }

    public static bool IsYoutubeHost(string? host) => YoutubeUrlParser.IsYoutubeHost(host);

    public static string WatchUrl(string videoId) => Parser.BuildVideoUrl(videoId);

    public static string PlaylistUrl(string playlistId) =>
        "https://www.youtube.com/playlist?list=" + Parser.ParsePlaylistIdOrThrow(playlistId);

    public static string ChannelUrl(string channelId) => Parser.BuildChannelUrl(new ChannelRef.ById(channelId));

    public static bool TryCreateUri(string input, out Uri uri) => YoutubeUrlParser.TryCreateUri(input, out uri);

    public static string? GetQueryValue(Uri uri, string key) => YoutubeUrlParser.GetQueryValue(uri, key);

    public static IReadOnlyList<string> GetPathSegments(Uri uri) => YoutubeUrlParser.GetPathSegments(uri);
}
