using YoutubeCulvert.Internal;

namespace YoutubeCulvert.Parsing;

/// <summary>Parses a search or search-continuation response.</summary>
public sealed class SearchParser : IResponseParser<SearchParseInput, SearchResult>
{
    public ParseResult<SearchResult> Parse(SearchParseInput input)
    {
        var items = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sawKnownRenderer = false;

        foreach (var element in input.Root.Descendants())
        {
            var result = TryParseItem(element);
            if (result is null)
                continue;

            sawKnownRenderer = true;

            if (!MatchesType(result, input.Type))
                continue;

            var key = CreateKey(result);
            if (key is null || !seen.Add(key))
                continue;

            items.Add(result);
        }

        var recognized = sawKnownRenderer ||
                         input.Root.TryGetProperty("estimatedResults", out _) ||
                         input.Root.TryGetProperty("contents", out _) ||
                         input.Root.TryGetProperty("onResponseReceivedCommands", out _);

        if (!recognized)
        {
            return ParseResult<SearchResult>.Unrecognized(
                "search response has no known result renderers",
                "contents");
        }

        return ParseResult<SearchResult>.Recognized(
            items,
            input.Root.FindContinuationToken());
    }

    public static long? ParseEstimatedResults(JsonElement root)
    {
        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("estimatedResults", out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count))
                return count;

            if (value.ValueKind == JsonValueKind.String &&
                long.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses the JSONP document of the suggestions endpoint:
    /// <c>window.google.ac.h(["query",[["suggestion",0,[...]], ...]])</c>.
    /// </summary>
    public static IReadOnlyList<string> ParseSuggestions(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return [];

        var start = body.IndexOf('(');
        var end = body.LastIndexOf(')');

        if (start < 0 || end <= start)
            return [];

        using var document = JsonDocument.Parse(body[(start + 1)..end]);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
            return [];

        var result = new List<string>();

        foreach (var item in root[1].EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() == 0)
                continue;

            var suggestion = item[0].GetString();
            if (!string.IsNullOrWhiteSpace(suggestion))
                result.Add(suggestion);
        }

        return result;
    }

    private static SearchResult? TryParseItem(JsonElement element)
    {
        foreach (var propertyName in new[]
                 {
                     "videoRenderer", "gridVideoRenderer", "compactVideoRenderer", "reelItemRenderer"
                 })
        {
            if (element.TryGetProperty(propertyName, out var videoRenderer) &&
                videoRenderer.ValueKind == JsonValueKind.Object)
            {
                var summary = VideoParser.ParseSummary(videoRenderer);
                return summary is null
                    ? null
                    : new SearchResult { Kind = SearchResultKind.Video, Video = summary };
            }
        }

        foreach (var propertyName in new[] { "channelRenderer", "gridChannelRenderer" })
        {
            if (element.TryGetProperty(propertyName, out var channelRenderer) &&
                channelRenderer.ValueKind == JsonValueKind.Object)
            {
                var summary = ChannelParser.ParseSummary(channelRenderer);
                return summary is null
                    ? null
                    : new SearchResult { Kind = SearchResultKind.Channel, Channel = summary };
            }
        }

        foreach (var propertyName in new[] { "playlistRenderer", "gridPlaylistRenderer" })
        {
            if (element.TryGetProperty(propertyName, out var playlistRenderer) &&
                playlistRenderer.ValueKind == JsonValueKind.Object)
            {
                var summary = PlaylistParser.ParseSummary(playlistRenderer);
                return summary is null
                    ? null
                    : new SearchResult { Kind = SearchResultKind.Playlist, Playlist = summary };
            }
        }

        if (element.TryGetProperty("lockupViewModel", out var lockup) &&
            lockup.ValueKind == JsonValueKind.Object &&
            !LockupParser.IsAdvertisement(lockup))
        {
            var result = LockupParser.Parse(lockup);
            if (result is null)
                return null;

            return result.Kind switch
            {
                LockupKind.Channel => new SearchResult
                {
                    Kind = SearchResultKind.Channel,
                    Channel = result.Channel
                },
                LockupKind.Playlist => new SearchResult
                {
                    Kind = SearchResultKind.Playlist,
                    Playlist = result.Playlist
                },
                _ => new SearchResult
                {
                    Kind = SearchResultKind.Video,
                    Video = result.Video
                }
            };
        }

        if (element.TryGetProperty("shortsLockupViewModel", out var shortsLockup) &&
            shortsLockup.ValueKind == JsonValueKind.Object)
        {
            var summary = VideoParser.ParseShortsSummary(shortsLockup);
            return summary is null
                ? null
                : new SearchResult { Kind = SearchResultKind.Video, Video = summary };
        }

        return null;
    }

    private static bool MatchesType(SearchResult item, SearchType searchType) => searchType switch
    {
        SearchType.All => true,
        SearchType.Video => item.Kind == SearchResultKind.Video,
        SearchType.Channel => item.Kind == SearchResultKind.Channel,
        SearchType.Playlist => item.Kind == SearchResultKind.Playlist,
        SearchType.Live => item.Kind == SearchResultKind.Video && item.Video?.IsLive == true,
        SearchType.Movie => item.Kind == SearchResultKind.Video,
        SearchType.Shorts => item.Kind == SearchResultKind.Video,
        _ => true
    };

    private static string? CreateKey(SearchResult item) => item.Kind switch
    {
        SearchResultKind.Video => item.Video is null ? null : "v:" + item.Video.Id,
        SearchResultKind.Channel => item.Channel is null ? null : "c:" + item.Channel.Id,
        SearchResultKind.Playlist => item.Playlist is null ? null : "p:" + item.Playlist.Id,
        _ => null
    };
}
