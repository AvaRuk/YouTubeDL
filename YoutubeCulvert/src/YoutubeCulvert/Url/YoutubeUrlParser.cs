using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeCulvert.Url;

/// <summary>Default <see cref="IYoutubeUrlParser"/>: tolerant parsing of YouTube links.</summary>
public sealed partial class YoutubeUrlParser : IYoutubeUrlParser
{
    private const string WatchBase = "https://www.youtube.com/watch?v=";
    private const string PlaylistBase = "https://www.youtube.com/playlist?list=";
    private const string ChannelBase = "https://www.youtube.com/channel/";

    private static readonly string[] SpecialPlaylistIds = ["WL", "LL", "FL", "LM"];

    public bool TryParseVideoId(string url, out string? videoId)
    {
        if (!string.IsNullOrWhiteSpace(url) && IsVideoId(url.Trim()))
        {
            videoId = url.Trim();
            return true;
        }

        if (!TryCreateUri(url, out var uri) || !IsYoutubeHost(uri.Host))
        {
            videoId = null;
            return false;
        }

        if (IsShortHost(uri.Host))
        {
            var segment = GetPathSegments(uri).FirstOrDefault();
            if (IsVideoId(segment))
            {
                videoId = segment;
                return true;
            }
        }

        var queryId = GetQueryValue(uri, "v");
        if (IsVideoId(queryId))
        {
            videoId = queryId;
            return true;
        }

        var segments = GetPathSegments(uri);
        if (segments.Count >= 2 && IsVideoId(segments[1]))
        {
            var kind = segments[0].ToLowerInvariant();
            if (kind is "shorts" or "embed" or "live" or "v")
            {
                videoId = segments[1];
                return true;
            }
        }

        videoId = null;
        return false;
    }

    public bool TryParsePlaylistId(string url, out string? playlistId)
    {
        if (!string.IsNullOrWhiteSpace(url) && IsPlaylistId(url.Trim()))
        {
            playlistId = url.Trim();
            return true;
        }

        if (!TryCreateUri(url, out var uri) || !IsYoutubeHost(uri.Host))
        {
            playlistId = null;
            return false;
        }

        var queryId = GetQueryValue(uri, "list");
        if (IsPlaylistId(queryId))
        {
            playlistId = queryId;
            return true;
        }

        playlistId = null;
        return false;
    }

    public bool TryParseChannel(string url, out ChannelRef? channel)
    {
        channel = null;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        var input = url.Trim();

        if (IsChannelId(input))
        {
            channel = new ChannelRef.ById(input);
            return true;
        }

        if (input.StartsWith('@'))
        {
            channel = new ChannelRef.ByHandle(input[1..]);
            return true;
        }

        if (!TryCreateUri(input, out var uri) || !IsYoutubeHost(uri.Host))
            return false;

        var segments = GetPathSegments(uri);
        if (segments.Count >= 2)
        {
            var kind = segments[0].ToLowerInvariant();

            if (kind == "channel" && IsChannelId(segments[1]))
            {
                channel = new ChannelRef.ById(segments[1]);
                return true;
            }

            if (kind == "user")
            {
                channel = new ChannelRef.ByUsername(segments[1]);
                return true;
            }

            if (kind == "c")
            {
                channel = new ChannelRef.ByHandle(segments[1]);
                return true;
            }

            if (kind == "channel" || kind == "user")
            {
                channel = new ChannelRef.ByUrl(uri.ToString());
                return true;
            }
        }

        if (segments.Count == 1)
        {
            if (segments[0].StartsWith('@'))
            {
                channel = new ChannelRef.ByHandle(segments[0][1..]);
                return true;
            }

            if (IsChannelId(segments[0]))
            {
                channel = new ChannelRef.ById(segments[0]);
                return true;
            }
        }

        // Any other youtube.com link is not a channel reference.
        return false;
    }

    public bool TryParseVideoRef(string url, out VideoRef? video)
    {
        video = null;

        if (!TryParseVideoId(url, out var videoId) || videoId is null)
            return false;

        string? playlistId = null;
        int? index = null;
        TimeSpan? startAt = null;

        if (TryCreateUri(url, out var uri))
        {
            playlistId = GetQueryValue(uri, "list");
            startAt = ParseStartTime(GetQueryValue(uri, "t"));

            if (int.TryParse(
                    GetQueryValue(uri, "index"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedIndex))
            {
                index = parsedIndex;
            }
        }

        video = new VideoRef(videoId, playlistId, index, startAt);
        return true;
    }

    public string BuildVideoUrl(string videoId, TimeSpan? t = null)
    {
        if (!IsVideoId(videoId))
            throw new ArgumentException($"'{videoId}' is not a YouTube video identifier.", nameof(videoId));

        if (t is { } start && start > TimeSpan.Zero)
            return $"{WatchBase}{videoId}&t={(long)start.TotalSeconds}s";

        return WatchBase + videoId;
    }

    public string BuildChannelUrl(ChannelRef channel) => channel switch
    {
        ChannelRef.ById byId => ChannelBase + byId.ChannelId,
        ChannelRef.ByHandle byHandle => $"https://www.youtube.com/@{byHandle.Handle.TrimStart('@')}",
        ChannelRef.ByUsername byUsername => $"https://www.youtube.com/user/{byUsername.Username}",
        ChannelRef.ByUrl byUrl => byUrl.Url,
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    public string ParseVideoIdOrThrow(string url) =>
        TryParseVideoId(url, out var videoId) && videoId is not null
            ? videoId
            : throw new ArgumentException($"Could not determine a video identifier from '{url}'.", nameof(url));

    public string ParsePlaylistIdOrThrow(string url) =>
        TryParsePlaylistId(url, out var playlistId) && playlistId is not null
            ? playlistId
            : throw new ArgumentException($"Could not determine a playlist identifier from '{url}'.", nameof(url));

    public ChannelRef ParseChannelOrThrow(string url) =>
        TryParseChannel(url, out var channel) && channel is not null
            ? channel
            : throw new ArgumentException($"Could not determine a channel reference from '{url}'.", nameof(url));

    public static bool IsYoutubeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        host = host.ToLowerInvariant();

        return host is "youtube.com" or "youtu.be" or "youtube-nocookie.com" ||
               host.EndsWith(".youtube.com", StringComparison.Ordinal) ||
               host.EndsWith(".youtube-nocookie.com", StringComparison.Ordinal);
    }

    public static bool TryCreateUri(string? input, out Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(input))
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out var absolute) &&
                (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                uri = absolute;
                return true;
            }

            if (Uri.TryCreate("https://" + input, UriKind.Absolute, out var withScheme))
            {
                uri = withScheme;
                return true;
            }
        }

        uri = null!;
        return false;
    }

    public static string? GetQueryValue(Uri uri, string key)
    {
        var query = uri.Query;

        if (string.IsNullOrEmpty(query))
            return null;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];

            if (!Uri.UnescapeDataString(name).Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return separator < 0
                ? string.Empty
                : Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
        }

        return null;
    }

    public static IReadOnlyList<string> GetPathSegments(Uri uri) =>
        uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

    private static bool IsShortHost(string host) =>
        host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);

    private static bool IsVideoId(string? value) =>
        value is { Length: 11 } && value.All(IsIdentifierChar);

    private static bool IsChannelId(string? value) =>
        value is { Length: 24 } &&
        (value.StartsWith("UC", StringComparison.Ordinal) ||
         value.StartsWith("HC", StringComparison.Ordinal)) &&
        value.All(IsIdentifierChar);

    private static bool IsPlaylistId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (SpecialPlaylistIds.Contains(value, StringComparer.Ordinal))
            return true;

        return value.Length >= 10 && value.All(IsIdentifierChar);
    }

    private static bool IsIdentifierChar(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

    private static TimeSpan? ParseStartTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return TimeSpan.FromSeconds(seconds);

        if (value.EndsWith('s') &&
            long.TryParse(value[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var suffixSeconds))
        {
            return TimeSpan.FromSeconds(suffixSeconds);
        }

        var match = StartTimePattern().Match(value);
        if (!match.Success)
            return null;

        var hours = match.Groups["hours"].Success ? int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = match.Groups["minutes"].Success ? int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture) : 0;
        var secs = match.Groups["seconds"].Success ? int.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture) : 0;

        return new TimeSpan(hours, minutes, secs);
    }

    [GeneratedRegex(@"^(?:(?<hours>\d+)h)?(?:(?<minutes>\d+)m)?(?:(?<seconds>\d+)s)?$", RegexOptions.CultureInvariant)]
    private static partial Regex StartTimePattern();
}
