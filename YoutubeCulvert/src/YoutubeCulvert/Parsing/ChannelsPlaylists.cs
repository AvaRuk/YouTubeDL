using YoutubeCulvert.Internal;

namespace YoutubeCulvert.Parsing;

/// <summary>Parses a channel page and <c>channelRenderer</c> objects.</summary>
public sealed class ChannelParser : IResponseParser<ChannelParseInput, Channel>
{
    public ParseResult<Channel> Parse(ChannelParseInput input)
    {
        var root = input.Root;
        var metadata = root.FindFirstObjectWithProperty("channelMetadataRenderer");
        var pageHeader = root.FindFirstObjectWithProperty("pageHeaderRenderer");
        var c4Header = root.FindFirstObjectWithProperty("c4TabbedHeaderRenderer");
        var genericHeader = root.FindFirstObjectWithProperty("header");

        if (metadata is null && pageHeader is null && c4Header is null && genericHeader is null)
        {
            return ParseResult<Channel>.Unrecognized(
                "channel page has no header/metadata renderer",
                "header");
        }

        var id = metadata?.GetString("externalId") ?? input.FallbackChannelId;
        var title = metadata?.GetString("title") ?? string.Empty;
        var description = metadata?.GetString("description");
        var handle = CommonParser.NormalizeHandle(metadata?.GetString("vanityChannelUrl"));
        var avatars = new List<Thumbnail>();
        var banners = new List<Thumbnail>();
        var subscriberText = (string?)null;
        var videoCountText = (string?)null;

        if (metadata is { } metadataElement &&
            metadataElement.TryGetProperty("avatar", out var metadataAvatar))
        {
            avatars.AddRange(metadataAvatar.ParseThumbnails());
        }

        if (pageHeader is { } header)
        {
            var headerTitle = header.Path("content", "pageHeaderViewModel", "title") is { } titleElement
                ? CommonParser.FindFirstText(titleElement)
                : null;

            if (!string.IsNullOrWhiteSpace(headerTitle))
                title = headerTitle;

            foreach (var text in CommonParser.FindMetadataTexts(header))
            {
                if (IsSubscriberText(text))
                {
                    subscriberText ??= text;
                }
                else if (IsVideoText(text))
                {
                    videoCountText ??= text;
                }
            }

            if (avatars.Count == 0)
                avatars.AddRange(CommonParser.FindThumbnailsDeep(header));

            if (header.FindFirstObjectWithProperty("banner") is { } banner)
                banners.AddRange(CommonParser.FindThumbnailsDeep(banner));

            if (header.FindFirstObjectWithProperty("channelHandle")?.GetText() is { } headerHandle &&
                !string.IsNullOrWhiteSpace(headerHandle))
            {
                handle = CommonParser.NormalizeHandle(headerHandle);
            }
        }

        if (c4Header is { } c4)
        {
            if (string.IsNullOrEmpty(title))
                title = c4.GetTextProperty("title") ?? string.Empty;

            if (c4.GetTextProperty("subscriberCountText") is { } subscriberCount)
                subscriberText ??= subscriberCount;

            if (c4.GetTextProperty("videosCountText") is { } videosCount)
                videoCountText ??= videosCount;

            if (avatars.Count == 0 && c4.TryGetProperty("avatar", out var c4Avatar))
                avatars.AddRange(c4Avatar.ParseThumbnails());

            if (banners.Count == 0 && c4.TryGetProperty("banner", out var c4Banner))
                banners.AddRange(c4Banner.ParseThumbnails());
        }

        if (avatars.Count == 0)
        {
            if (root.FindFirstObjectWithProperty("avatar") is { } avatar)
                avatars.AddRange(CommonParser.FindThumbnailsDeep(avatar));
        }

        if (banners.Count == 0)
        {
            if (root.FindFirstObjectWithProperty("banner") is { } banner)
                banners.AddRange(CommonParser.FindThumbnailsDeep(banner));
        }

        if (handle is null)
            handle = CommonParser.NormalizeHandle(root.FindFirstObjectWithProperty("channelHandle")?.GetText());

        var channel = new Channel
        {
            Id = id,
            Title = title,
            Handle = handle,
            Description = description,
            Avatars = avatars,
            Banners = banners,
            SubscriberCount = JsonHelpers.ParseCount(subscriberText),
            VideoCount = JsonHelpers.ParseItemCount(videoCountText),
            Links = ParseLinks(root),
            IsVerified = IsVerified(root),
            Tabs = ParseTabs(root),
            Raw = root.Clone()
        };

        return ParseResult<Channel>.Recognized([channel]);
    }

    /// <summary>Builds a list entry from a <c>channelRenderer</c>-like object.</summary>
    public static ChannelSummary? ParseSummary(JsonElement channelRenderer)
    {
        var channelId = channelRenderer.GetString("channelId");
        if (string.IsNullOrWhiteSpace(channelId))
            return null;

        var title = channelRenderer.GetTextProperty("title")
                    ?? channelRenderer.GetString("title")
                    ?? string.Empty;

        var handle = ExtractHandle(channelRenderer);
        var subscriberText = (string?)null;
        var videoCountText = (string?)null;

        foreach (var candidate in new[]
                 {
                     channelRenderer.GetTextProperty("subscriberCountText"),
                     channelRenderer.GetTextProperty("videoCountText")
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (candidate.StartsWith('@'))
            {
                handle ??= candidate;
            }
            else if (IsSubscriberText(candidate))
            {
                subscriberText ??= candidate;
            }
            else if (IsVideoText(candidate))
            {
                videoCountText ??= candidate;
            }
        }

        var thumbnails = channelRenderer.TryGetProperty("thumbnail", out var thumbnail)
            ? thumbnail.ParseThumbnails()
            : CommonParser.FindThumbnailsDeep(channelRenderer);

        return new ChannelSummary
        {
            Id = channelId,
            Title = title,
            Handle = handle,
            Thumbnails = thumbnails,
            SubscriberCountText = subscriberText,
            SubscriberCount = JsonHelpers.ParseCount(subscriberText),
            VideoCountText = videoCountText,
            Description = channelRenderer.GetTextProperty("descriptionSnippet"),
            Raw = channelRenderer.Clone()
        };
    }

    private static string? ExtractHandle(JsonElement renderer)
    {
        var direct = renderer.GetTextProperty("channelHandle");
        if (!string.IsNullOrWhiteSpace(direct))
            return CommonParser.NormalizeHandle(direct);

        foreach (var element in renderer.Descendants())
        {
            if (element.TryGetProperty("canonicalBaseUrl", out var canonical) &&
                canonical.ValueKind == JsonValueKind.String)
            {
                var value = canonical.GetString();
                if (!string.IsNullOrWhiteSpace(value) && value.Contains('@'))
                    return CommonParser.NormalizeHandle(value);
            }
        }

        return null;
    }

    private static IReadOnlyList<ChannelLink> ParseLinks(JsonElement root)
    {
        var links = new List<ChannelLink>();

        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("channelExternalLinkViewModel", out var link) ||
                link.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = link.Path("title") is { } titleElement
                ? CommonParser.FindFirstText(titleElement)
                : null;

            var url = link.FindFirstString("url");

            if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(title))
                links.Add(new ChannelLink(title!, url!));
        }

        return links;
    }

    private static IReadOnlyList<string> ParseTabs(JsonElement root)
    {
        var tabs = new List<string>();

        foreach (var element in root.Descendants())
        {
            foreach (var propertyName in new[] { "tabRenderer", "expandableTabRenderer" })
            {
                if (!element.TryGetProperty(propertyName, out var tab) ||
                    tab.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var title = tab.GetTextProperty("title")?.Trim();
                if (!string.IsNullOrWhiteSpace(title) &&
                    !tabs.Contains(title, StringComparer.OrdinalIgnoreCase))
                {
                    tabs.Add(title);
                }

                break;
            }
        }

        return tabs;
    }

    // Recognize both English and Russian labels returned for localized YouTube UI.
    private static bool IsSubscriberText(string text) =>
        text.Contains("subscriber", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("\u043F\u043E\u0434\u043F\u0438\u0441\u0447\u0438\u043A", StringComparison.OrdinalIgnoreCase);

    private static bool IsVideoText(string text) =>
        text.Contains("video", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("\u0432\u0438\u0434\u0435\u043E", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("\u0440\u043E\u043B\u0438\u043A", StringComparison.OrdinalIgnoreCase);

    private static bool IsViewText(string text) =>
        text.Contains("view", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("\u043F\u0440\u043E\u0441\u043C\u043E\u0442\u0440", StringComparison.OrdinalIgnoreCase);

    private static bool IsVerified(JsonElement root)
    {
        foreach (var element in root.Descendants())
        {
            if (element.TryGetProperty("isVerified", out var isVerified) &&
                isVerified.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                isVerified.GetBoolean())
            {
                return true;
            }

            if (element.TryGetProperty("style", out var style) &&
                style.ValueKind == JsonValueKind.String &&
                style.GetString()?.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            if (element.TryGetProperty("label", out var label) &&
                label.ValueKind == JsonValueKind.String &&
                label.GetString() is { } labelText &&
                (labelText.Equals("Verified", StringComparison.OrdinalIgnoreCase) ||
                 labelText.Equals("\u041F\u043E\u0434\u0442\u0432\u0435\u0440\u0436\u0434\u0435\u043D\u043E", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Parses a playlist page and <c>playlistRenderer</c> objects.</summary>
public sealed class PlaylistParser : IResponseParser<PlaylistParseInput, Playlist>
{
    public ParseResult<Playlist> Parse(PlaylistParseInput input)
    {
        var root = input.Root;
        var metadata = root.Path("metadata", "playlistMetadataRenderer");
        var header = root.FindFirstObjectWithProperty("playlistHeaderRenderer");
        var pageHeader = root.FindFirstObjectWithProperty("pageHeaderRenderer");

        if (metadata is null && header is null && pageHeader is null)
        {
            return ParseResult<Playlist>.Unrecognized(
                "playlist page has no metadata/header renderer",
                "metadata/playlistMetadataRenderer");
        }

        var id = input.FallbackPlaylistId;
        var title = string.Empty;
        var description = (string?)null;
        var ownerChannelId = (string?)null;
        var ownerChannelTitle = (string?)null;
        var itemCount = (int?)null;
        var thumbnails = new List<Thumbnail>();

        if (metadata is { } metadataElement)
        {
            title = metadataElement.GetString("title") ?? string.Empty;
            description = metadataElement.GetString("description");
        }

        if (header is { } playlistHeader)
        {
            var (channelId, channelTitle) = CommonParser.ParseOwner(playlistHeader);
            id = playlistHeader.GetString("playlistId") ?? id;
            title = playlistHeader.GetTextProperty("title") ?? title;
            description = playlistHeader.GetTextProperty("descriptionText")
                          ?? playlistHeader.GetString("description")
                          ?? description;
            ownerChannelId = channelId;
            ownerChannelTitle = channelTitle;
            itemCount = JsonHelpers.ParseItemCount(playlistHeader.GetTextProperty("numVideosText"));

            if (playlistHeader.TryGetProperty("playlistHeaderBanner", out var banner))
                thumbnails.AddRange(CommonParser.FindThumbnailsDeep(banner));

            if (thumbnails.Count == 0)
                thumbnails.AddRange(CommonParser.FindThumbnailsDeep(playlistHeader));
        }

        if (pageHeader is { } headerElement)
        {
            if (string.IsNullOrEmpty(title))
            {
                var titleElement = headerElement.Path("content", "pageHeaderViewModel", "title");
                var headerTitle = titleElement is { } element ? CommonParser.FindFirstText(element) : null;

                if (!string.IsNullOrWhiteSpace(headerTitle))
                    title = headerTitle;
            }

            foreach (var text in CommonParser.FindMetadataTexts(headerElement))
            {
                if (ownerChannelTitle is null &&
                    (text.StartsWith("by ", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("\u0430\u0432\u0442\u043E\u0440", StringComparison.OrdinalIgnoreCase)))
                {
                    ownerChannelTitle = text.StartsWith("by ", StringComparison.OrdinalIgnoreCase)
                        ? text[3..].Trim()
                        : text;
                }
                else if (itemCount is null && IsVideoCountText(text))
                {
                    itemCount = JsonHelpers.ParseItemCount(text);
                }
            }

            if (thumbnails.Count == 0)
                thumbnails.AddRange(CommonParser.FindThumbnailsDeep(headerElement));
        }

        ownerChannelId ??= root.FindChannelId();

        var playlist = new Playlist
        {
            Id = id,
            Title = title,
            Description = description,
            OwnerChannelId = ownerChannelId,
            OwnerChannelTitle = ownerChannelTitle,
            ItemCount = itemCount,
            Thumbnails = thumbnails,
            Raw = root.Clone()
        };

        return ParseResult<Playlist>.Recognized([playlist]);
    }

    public static PlaylistSummary? ParseSummary(JsonElement playlistRenderer)
    {
        var playlistId = playlistRenderer.GetString("playlistId")
                         ?? playlistRenderer.GetString("contentId");

        if (string.IsNullOrWhiteSpace(playlistId))
            return null;

        var (channelId, channelTitle) = CommonParser.ParseOwner(playlistRenderer);
        var itemCountText = playlistRenderer.GetTextProperty("videoCountText")
                            ?? playlistRenderer.GetTextProperty("videoCount");

        var thumbnails = playlistRenderer.TryGetProperty("thumbnail", out var thumbnail)
            ? thumbnail.ParseThumbnails()
            : CommonParser.FindThumbnailsDeep(playlistRenderer);

        return new PlaylistSummary
        {
            Id = playlistId,
            Title = playlistRenderer.GetTextProperty("title") ?? string.Empty,
            ChannelId = channelId,
            ChannelTitle = channelTitle,
            Thumbnails = thumbnails,
            ItemCount = playlistRenderer.GetInt32("videoCount") ?? JsonHelpers.ParseItemCount(itemCountText),
            ItemCountText = itemCountText,
            Raw = playlistRenderer.Clone()
        };
    }

    /// <summary>Collects playlists from playlistRenderer / gridPlaylistRenderer / lockupViewModel.</summary>
    public static IReadOnlyList<PlaylistSummary> ParseAllSummaries(JsonElement root, int maxResults)
    {
        var playlists = new List<PlaylistSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in root.Descendants())
        {
            if (playlists.Count >= maxResults)
                break;

            PlaylistSummary? summary = null;

            foreach (var propertyName in new[] { "playlistRenderer", "gridPlaylistRenderer" })
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
                lockup.ValueKind == JsonValueKind.Object)
            {
                summary = LockupParser.Parse(lockup)?.Playlist;
            }

            if (summary is not null && seen.Add(summary.Id))
                playlists.Add(summary);
        }

        return playlists;
    }

    private static bool IsVideoCountText(string text) =>
        text.Contains("video", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("\u0432\u0438\u0434\u0435\u043E", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Parses playlist entries, preserving <c>setVideoId</c> and <c>index</c>.</summary>
public sealed class PlaylistVideoParser : IResponseParser<JsonElement, PlaylistVideo>
{
    public ParseResult<PlaylistVideo> Parse(JsonElement root)
    {
        var items = new List<PlaylistVideo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in root.Descendants())
        {
            if (element.TryGetProperty("lockupViewModel", out var lockup) &&
                lockup.ValueKind == JsonValueKind.Object &&
                !LockupParser.IsAdvertisement(lockup) &&
                LockupParser.Parse(lockup) is { Video: { } lockupVideo } &&
                seen.Add(lockupVideo.Id))
            {
                items.Add(new PlaylistVideo
                {
                    VideoId = lockupVideo.Id,
                    Title = lockupVideo.Title,
                    Duration = lockupVideo.Duration,
                    Thumbnails = lockupVideo.Thumbnails,
                    Raw = lockup.Clone()
                });

                continue;
            }

            foreach (var propertyName in new[]
                     {
                         "playlistVideoRenderer", "videoRenderer", "gridVideoRenderer"
                     })
            {
                if (!element.TryGetProperty(propertyName, out var renderer) ||
                    renderer.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var summary = VideoParser.ParseSummary(renderer);
                if (summary is null || !seen.Add(summary.Id))
                    break;

                items.Add(new PlaylistVideo
                {
                    VideoId = summary.Id,
                    Title = summary.Title,
                    Index = JsonHelpers.ParseItemCount(renderer.GetTextProperty("index")),
                    SetVideoId = summary.SetVideoId ?? renderer.GetString("setVideoId"),
                    Duration = summary.Duration,
                    Thumbnails = summary.Thumbnails,
                    Raw = renderer.Clone()
                });

                break;
            }
        }

        var recognized = items.Count > 0 ||
                         root.TryGetProperty("contents", out _) ||
                         root.Path("metadata", "playlistMetadataRenderer") is not null;

        if (!recognized)
        {
            return ParseResult<PlaylistVideo>.Unrecognized(
                "playlist response has no video entries",
                "contents");
        }

        return ParseResult<PlaylistVideo>.Recognized(
            items,
            root.FindContinuationToken());
    }
}
