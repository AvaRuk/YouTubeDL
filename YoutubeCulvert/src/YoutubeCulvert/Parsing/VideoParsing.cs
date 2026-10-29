using System.Globalization;
using System.Text.RegularExpressions;
using YoutubeCulvert.Internal;

namespace YoutubeCulvert.Parsing;

/// <summary>Parses the <c>player</c> response into a <see cref="Video"/>.</summary>
public sealed class VideoParser : IResponseParser<PlayerParseInput, Video>
{
    public ParseResult<Video> Parse(PlayerParseInput input)
    {
        var root = input.Root;
        var hasDetails = root.TryGetProperty("videoDetails", out var details) &&
                         details.ValueKind == JsonValueKind.Object;
        var hasPlayability = root.TryGetProperty("playabilityStatus", out _);

        if (!hasDetails && !hasPlayability)
        {
            return ParseResult<Video>.Unrecognized(
                "player response has neither videoDetails nor playabilityStatus",
                "videoDetails");
        }

        var playability = ParsePlayability(root);
        var thumbnails = Array.Empty<Thumbnail>();
        var videoId = input.VideoId;
        var title = string.Empty;
        var description = (string?)null;
        var channelId = (string?)null;
        var channelTitle = (string?)null;
        var duration = TimeSpan.Zero;
        long? viewCount = null;
        IReadOnlyList<string> keywords = [];
        var isLive = false;

        if (hasDetails)
        {
            videoId = details.GetString("videoId") ?? input.VideoId;
            title = details.GetString("title") ?? string.Empty;
            description = details.GetString("shortDescription");
            channelId = details.GetString("channelId");
            channelTitle = details.GetString("author");
            duration = JsonHelpers.TimeSpanFromSeconds(details.GetString("lengthSeconds"));
            viewCount = JsonHelpers.ParseCount(details.GetString("viewCount"));
            keywords = ParseKeywords(details);
            isLive = details.GetBool("isLiveContent") == true || details.GetBool("isLive") == true;

            if (details.TryGetProperty("thumbnail", out var thumbnail))
                thumbnails = thumbnail.ParseThumbnails().ToArray();
        }

        var publishDate = (DateTimeOffset?)null;
        var uploadDate = (DateTimeOffset?)null;
        var isAgeRestricted = false;
        var isUpcoming = false;
        var channelHandle = (string?)null;

        if (root.Path("microformat", "playerMicroformatRenderer") is { ValueKind: JsonValueKind.Object } microformat)
        {
            publishDate = JsonHelpers.ParseDate(microformat.GetString("publishDate"));
            uploadDate = JsonHelpers.ParseDate(microformat.GetString("uploadDate"));
            isAgeRestricted = microformat.GetBool("isFamilySafe") == false;

            if (microformat.GetString("ownerProfileUrl") is { } profileUrl)
                channelHandle = CommonParser.NormalizeHandle(profileUrl);

            if (microformat.TryGetProperty("thumbnail", out var microThumbnail) && thumbnails.Length == 0)
                thumbnails = microThumbnail.ParseThumbnails().ToArray();

            if (microformat.TryGetProperty("liveBroadcastDetails", out var liveDetails) &&
                liveDetails.ValueKind == JsonValueKind.Object)
            {
                var isLiveNow = liveDetails.GetBool("isLiveNow");

                if (isLiveNow == true)
                    isLive = true;
                else if (isLiveNow == false && !isLive)
                    isUpcoming = true;
            }
        }

        if (thumbnails.Length == 0)
        {
            thumbnails =
            [
                new Thumbnail
                {
                    Url = $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg",
                    Width = 480,
                    Height = 360
                }
            ];
        }

        if (!isAgeRestricted && playability.Reason is { } reason &&
            reason.Contains("age", StringComparison.OrdinalIgnoreCase))
        {
            isAgeRestricted = true;
        }

        var video = new Video
        {
            Id = videoId,
            Title = title,
            Description = description,
            ChannelId = channelId,
            ChannelTitle = channelTitle,
            ChannelHandle = channelHandle,
            Duration = duration,
            ViewCount = viewCount,
            PublishDate = publishDate,
            UploadDate = uploadDate,
            Thumbnails = thumbnails,
            Keywords = keywords,
            IsLive = isLive,
            IsUpcoming = isUpcoming,
            IsAgeRestricted = isAgeRestricted,
            PlayabilityStatus = playability,
            Raw = root.Clone()
        };

        return ParseResult<Video>.Recognized([video]);
    }

    /// <summary>Reads <c>playabilityStatus</c>; returns <see cref="PlayabilityStatus.Ok"/> when absent.</summary>
    public static PlayabilityStatus ParsePlayability(JsonElement root)
    {
        if (!root.TryGetProperty("playabilityStatus", out var status) ||
            status.ValueKind != JsonValueKind.Object)
        {
            return PlayabilityStatus.Ok;
        }

        return new PlayabilityStatus
        {
            Status = status.GetString("status") ?? "OK",
            Reason = status.GetString("reason"),
            SubReason = status.Path("errorScreen", "errorMessage") is { } errorScreen
                ? errorScreen.GetText()
                : null,
            Raw = status.Clone()
        };
    }

    public static IReadOnlyList<CaptionTrack> ParseCaptionTracks(JsonElement root) =>
        new CaptionTrackParser().Parse(root).Items;

    /// <summary>Builds a list entry from <c>videoRenderer</c>-like objects.</summary>
    public static VideoSummary? ParseSummary(JsonElement renderer)
    {
        var videoId = renderer.GetString("videoId");
        if (string.IsNullOrWhiteSpace(videoId))
            return null;

        var (channelId, channelTitle) = CommonParser.ParseOwner(renderer);
        var thumbnails = new List<Thumbnail>();

        if (renderer.TryGetProperty("thumbnail", out var thumbnail))
            thumbnails.AddRange(thumbnail.ParseThumbnails());

        if (thumbnails.Count == 0)
        {
            thumbnails.Add(new Thumbnail
            {
                Url = $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg",
                Width = 480,
                Height = 360
            });
        }

        var viewCountText = renderer.GetTextProperty("viewCountText")
                            ?? renderer.GetTextProperty("shortViewCountText");

        return new VideoSummary
        {
            Id = videoId,
            Title = renderer.GetTextProperty("title")
                    ?? renderer.GetTextProperty("headline")
                    ?? string.Empty,
            ChannelId = channelId,
            ChannelTitle = channelTitle,
            Thumbnails = thumbnails,
            Duration = CommonParser.ParseDuration(renderer),
            ViewCount = JsonHelpers.ParseCount(viewCountText),
            ViewCountText = viewCountText,
            PublishedTime = renderer.GetTextProperty("publishedTimeText"),
            IsLive = CommonParser.IsLive(renderer),
            SetVideoId = renderer.GetString("setVideoId"),
            Raw = renderer.Clone()
        };
    }

    /// <summary>Collects videos from all known renderer shapes in appearance order.</summary>
    public static IReadOnlyList<VideoSummary> ParseAllSummaries(JsonElement root, int maxResults)
    {
        var videos = new List<VideoSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in root.Descendants())
        {
            if (videos.Count >= maxResults)
                break;

            VideoSummary? summary = null;

            foreach (var propertyName in new[]
                     {
                         "videoRenderer", "gridVideoRenderer", "compactVideoRenderer",
                         "reelItemRenderer", "playlistVideoRenderer"
                     })
            {
                if (element.TryGetProperty(propertyName, out var renderer) &&
                    renderer.ValueKind == JsonValueKind.Object)
                {
                    summary = ParseSummary(renderer);
                    break;
                }
            }

            if (summary is null &&
                element.TryGetProperty("lockupViewModel", out var lockup) &&
                lockup.ValueKind == JsonValueKind.Object &&
                !LockupParser.IsAdvertisement(lockup))
            {
                summary = LockupParser.Parse(lockup)?.Video;
            }

            if (summary is null &&
                element.TryGetProperty("shortsLockupViewModel", out var shortsLockup) &&
                shortsLockup.ValueKind == JsonValueKind.Object)
            {
                summary = ParseShortsSummary(shortsLockup);
            }

            if (summary is not null && seen.Add(summary.Id))
                videos.Add(summary);
        }

        return videos;
    }

    /// <summary>Parses a Shorts shelf item; the id lives in the reel endpoint.</summary>
    public static VideoSummary? ParseShortsSummary(JsonElement lockup)
    {
        var videoId = lockup.Path("onTap", "innertubeCommand", "reelWatchEndpoint", "videoId")?.GetString()
                      ?? lockup.FindFirstObjectWithProperty("reelWatchEndpoint")?.GetString("videoId");

        if (string.IsNullOrWhiteSpace(videoId))
            return null;

        var overlay = lockup.Path("overlayMetadata");
        var title = overlay?.GetTextProperty("primaryText");
        var viewCountText = overlay?.GetTextProperty("secondaryText");
        var thumbnails = CommonParser.FindThumbnailsDeep(lockup);

        if (thumbnails.Count == 0)
        {
            thumbnails =
            [
                new Thumbnail
                {
                    Url = $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg",
                    Width = 480,
                    Height = 360
                }
            ];
        }

        return new VideoSummary
        {
            Id = videoId,
            Title = title ?? string.Empty,
            Thumbnails = thumbnails,
            ViewCountText = viewCountText,
            ViewCount = JsonHelpers.ParseCount(viewCountText),
            Raw = lockup.Clone()
        };
    }

    private static IReadOnlyList<string> ParseKeywords(JsonElement details)
    {
        if (!details.TryGetProperty("keywords", out var keywords) ||
            keywords.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return keywords
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()!)
            .ToArray();
    }
}

/// <summary>Parses <c>streamingData</c>, including multi-language audio tracks.</summary>
public sealed class StreamManifestParser : IResponseParser<JsonElement, StreamManifest>
{
    public ParseResult<StreamManifest> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("streamingData", out var streaming) ||
            streaming.ValueKind != JsonValueKind.Object)
        {
            return ParseResult<StreamManifest>.Unrecognized(
                "player response without streamingData",
                "streamingData");
        }

        var streams = new List<MediaStream>();
        ParseFormats(streaming, "formats", isAdaptive: false, streams);
        ParseFormats(streaming, "adaptiveFormats", isAdaptive: true, streams);

        var expiresInSeconds = streaming.GetInt64("expiresInSeconds");

        var manifest = new StreamManifest
        {
            Streams = streams,
            ExpiresAt = expiresInSeconds is null
                ? null
                : DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds.Value),
            Raw = streaming.Clone()
        };

        return ParseResult<StreamManifest>.Recognized([manifest]);
    }

    private static void ParseFormats(
        JsonElement streaming,
        string propertyName,
        bool isAdaptive,
        List<MediaStream> output)
    {
        if (!streaming.TryGetProperty(propertyName, out var formats) ||
            formats.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var format in formats.EnumerateArray())
        {
            if (format.ValueKind != JsonValueKind.Object)
                continue;

            var (url, cipher) = ReadUrl(format);
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var mimeType = format.GetString("mimeType") ?? string.Empty;
            var audioTrack = format.Path("audioTrack");
            var trackId = audioTrack?.GetString("audioTrackId") ?? audioTrack?.GetString("id");

            output.Add(new MediaStream
            {
                Url = url,
                MimeType = mimeType,
                Codecs = format.GetString("codecs") ?? ExtractCodecs(mimeType),
                QualityLabel = format.GetString("qualityLabel"),
                AudioQuality = format.GetString("audioQuality"),
                Itag = format.GetInt32("itag"),
                Width = format.GetInt32("width"),
                Height = format.GetInt32("height"),
                Fps = format.GetInt32("fps"),
                Bitrate = format.GetInt64("bitrate"),
                ContentLength = JsonHelpers.ParseInt64(format.GetString("contentLength")),
                IsAudioOnly = mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase),
                IsVideoOnly = isAdaptive &&
                              mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase),
                IsAdaptive = isAdaptive,
                AudioTrackId = trackId,
                AudioTrackName = audioTrack?.GetTextProperty("displayName"),
                AudioIsDefault = audioTrack?.GetBool("audioIsDefault") == true,
                LanguageCode = StreamManifest.LanguageFromTrackId(trackId),
                ApproxDuration = JsonHelpers.TimeSpanFromMilliseconds(format.GetString("approxDurationMs")),
                InitRange = format.Path("initRange").ParseByteRange(),
                IndexRange = format.Path("indexRange").ParseByteRange(),
                Cipher = cipher,
                Raw = format.Clone()
            });
        }
    }

    private static (string? Url, Cipher? Cipher) ReadUrl(JsonElement format)
    {
        var directUrl = format.GetString("url");
        if (!string.IsNullOrWhiteSpace(directUrl))
            return (directUrl, null);

        foreach (var propertyName in new[] { "signatureCipher", "cipher" })
        {
            if (format.GetString(propertyName) is not { } rawCipher)
                continue;

            var query = ParseQueryString(rawCipher);
            var cipher = new Cipher(
                query.GetValueOrDefault("url"),
                query.GetValueOrDefault("s"),
                query.GetValueOrDefault("sp"),
                rawCipher);

            if (!string.IsNullOrWhiteSpace(cipher.Url))
                return (cipher.Url, cipher);
        }

        return (null, null);
    }

    private static Dictionary<string, string> ParseQueryString(string? value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (var pair in value.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
                continue;

            var name = Uri.UnescapeDataString(pair[..separator]);
            var parameterValue = Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            result[name] = parameterValue;
        }

        return result;
    }

    private static string? ExtractCodecs(string mimeType)
    {
        const string marker = "codecs=\"";
        var index = mimeType.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
            return null;

        var start = index + marker.Length;
        var end = mimeType.IndexOf('"', start);
        return end < 0 ? null : mimeType[start..end];
    }
}

/// <summary>Parses <c>playabilityStatus</c>.</summary>
public sealed class PlayabilityParser : IResponseParser<JsonElement, PlayabilityStatus>
{
    public ParseResult<PlayabilityStatus> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("playabilityStatus", out var status) ||
            status.ValueKind != JsonValueKind.Object)
        {
            return ParseResult<PlayabilityStatus>.Unrecognized(
                "response has no playabilityStatus",
                "playabilityStatus");
        }

        return ParseResult<PlayabilityStatus>.Recognized([VideoParser.ParsePlayability(root)]);
    }
}

/// <summary>Parses <c>captions.playerCaptionsTracklistRenderer.captionTracks[]</c>.</summary>
public sealed class CaptionTrackParser : IResponseParser<JsonElement, CaptionTrack>
{
    public ParseResult<CaptionTrack> Parse(JsonElement root)
    {
        var list = root.Path("captions", "playerCaptionsTracklistRenderer", "captionTracks");

        if (list is not { ValueKind: JsonValueKind.Array } tracks)
        {
            return ParseResult<CaptionTrack>.Unrecognized(
                "player response has no captionTracks",
                "captions/playerCaptionsTracklistRenderer/captionTracks");
        }

        var result = new List<CaptionTrack>();

        foreach (var track in tracks.EnumerateArray())
        {
            if (track.ValueKind != JsonValueKind.Object)
                continue;

            var languageCode = track.GetString("languageCode");
            if (string.IsNullOrWhiteSpace(languageCode))
                continue;

            result.Add(new CaptionTrack
            {
                LanguageCode = languageCode,
                Kind = track.GetString("kind"),
                Name = track.GetTextProperty("name") ?? languageCode,
                IsTranslatable = track.GetBool("isTranslatable") == true,
                BaseUrl = track.GetString("baseUrl"),
                Raw = track.Clone()
            });
        }

        return ParseResult<CaptionTrack>.Recognized(result);
    }
}

/// <summary>Parses chapter macro markers, with a description fallback.</summary>
public sealed partial class ChapterParser : IResponseParser<JsonElement, Chapter>
{
    public ParseResult<Chapter> Parse(JsonElement root)
    {
        var chapters = new List<Chapter>();
        var sawMarkers = false;

        foreach (var element in root.Descendants())
        {
            if (element.TryGetProperty("chapteredMacroMarkersListRenderer", out _))
                sawMarkers = true;

            if (!element.TryGetProperty("macroMarkersListItemRenderer", out var renderer) ||
                renderer.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            sawMarkers = true;

            var title = renderer.GetTextProperty("title");
            var startMs = renderer.GetDouble("timeRangeStartMillis");

            if (string.IsNullOrWhiteSpace(title) || startMs is null)
                continue;

            chapters.Add(new Chapter
            {
                Title = title.Trim(),
                Start = TimeSpan.FromMilliseconds(startMs.Value),
                ThumbnailUrl = renderer.TryGetProperty("thumbnail", out var thumbnail)
                    ? thumbnail.BestThumbnailUrl()
                    : null,
                Raw = renderer.Clone()
            });
        }

        if (!sawMarkers)
        {
            return ParseResult<Chapter>.Unrecognized(
                "response has no chapter macro markers",
                "chapteredMacroMarkersListRenderer");
        }

        return ParseResult<Chapter>.Recognized(Finalize(chapters));
    }

    /// <summary>
    /// Extracts chapters from a description that lists timestamps
    /// ("00:00 Intro"), which many creators publish manually.
    /// </summary>
    public static IReadOnlyList<Chapter> ParseDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return [];

        var chapters = new List<Chapter>();

        foreach (var rawLine in description.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var match = ChapterLine().Match(line);
            if (!match.Success)
                continue;

            var timestamp = JsonHelpers.ParseDuration(match.Groups["time"].Value);
            var title = match.Groups["title"].Value.Trim(' ', '-', '–', '—', ':', '|', '.');
            if (timestamp is null || title.Length == 0)
                continue;

            chapters.Add(new Chapter
            {
                Title = title,
                Start = timestamp.Value
            });
        }

        // A couple of timestamps are usually just links, not a real chapter list.
        return chapters.Count < 3 ? [] : Finalize(chapters);
    }

    private static IReadOnlyList<Chapter> Finalize(List<Chapter> chapters)
    {
        if (chapters.Count == 0)
            return [];

        var ordered = chapters.OrderBy(static chapter => chapter.Start).ToList();
        var result = new List<Chapter>(ordered.Count);

        for (var index = 0; index < ordered.Count; index++)
        {
            var current = ordered[index];
            var end = index + 1 < ordered.Count ? ordered[index + 1].Start : (TimeSpan?)null;
            result.Add(current with { End = end });
        }

        return result;
    }

    // 00:00, 0:00, 1:02:03 followed by a title.
    [GeneratedRegex(@"^(?<time>\d{1,2}:\d{2}(?::\d{2})?)\s*(?<title>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChapterLine();
}
