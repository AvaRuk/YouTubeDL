using YoutubeCulvert.Internal;

namespace YoutubeCulvert.Parsing;

/// <summary>Input of the player-response parser.</summary>
public readonly record struct PlayerParseInput(JsonElement Root, string VideoId);

/// <summary>Input of the channel-page parser.</summary>
public readonly record struct ChannelParseInput(JsonElement Root, string FallbackChannelId);

/// <summary>Input of the playlist-page parser.</summary>
public readonly record struct PlaylistParseInput(JsonElement Root, string FallbackPlaylistId);

/// <summary>Input of the search parser.</summary>
public readonly record struct SearchParseInput(JsonElement Root, SearchType Type);

/// <summary>Shared helpers for YouTube renderer objects.</summary>
public static class CommonParser
{
    public static (string? ChannelId, string? ChannelTitle) ParseOwner(JsonElement renderer)
    {
        foreach (var propertyName in new[]
                 {
                     "ownerText", "longBylineText", "shortBylineText",
                     "videoOwnerRenderer", "subtitle"
                 })
        {
            if (!renderer.TryGetProperty(propertyName, out var owner) ||
                owner.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = owner.GetText()
                        ?? owner.GetTextProperty("title")
                        ?? owner.GetTextProperty("headline");

            var channelId = owner.FindFirstObjectWithProperty("browseEndpoint") is { } browse
                ? browse.GetString("browseId")
                : owner.FindChannelId();

            return (channelId, title);
        }

        return (renderer.GetString("channelId"), renderer.GetString("author"));
    }

    public static TimeSpan? ParseDuration(JsonElement renderer)
    {
        if (renderer.TryGetProperty("lengthText", out var lengthText))
        {
            var duration = lengthText.TextToDuration();
            if (duration is not null)
                return duration;
        }

        foreach (var element in renderer.Descendants())
        {
            if (element.TryGetProperty("thumbnailOverlayTimeStatusRenderer", out var overlay) &&
                overlay.ValueKind == JsonValueKind.Object &&
                overlay.TryGetProperty("text", out var text))
            {
                var duration = text.TextToDuration();
                if (duration is not null)
                    return duration;
            }
        }

        foreach (var element in renderer.Descendants())
        {
            if (element.ValueKind == JsonValueKind.String &&
                JsonHelpers.ParseDurationLoose(element.GetString()) is { } duration)
            {
                return duration;
            }
        }

        return null;
    }

    public static bool IsLive(JsonElement renderer)
    {
        if (renderer.GetBool("isLiveNow") == true)
            return true;

        foreach (var element in renderer.Descendants())
        {
            if (element.TryGetProperty("style", out var style) &&
                style.ValueKind == JsonValueKind.String &&
                style.GetString()?.Contains("LIVE", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            if (element.TryGetProperty("label", out var label) &&
                label.ValueKind == JsonValueKind.String &&
                label.GetString()?.Contains("LIVE", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Best thumbnail URL anywhere inside an object.</summary>
    public static string? FindBestThumbnailDeep(JsonElement element) =>
        FindThumbnailsDeep(element).FirstOrDefault()?.Url;

    /// <summary>All distinct thumbnails found inside an object, largest first.</summary>
    public static IReadOnlyList<Thumbnail> FindThumbnailsDeep(JsonElement element)
    {
        var byUrl = new Dictionary<string, Thumbnail>(StringComparer.Ordinal);

        foreach (var candidate in element.Descendants())
        {
            foreach (var thumbnail in candidate.ParseThumbnails())
            {
                if (thumbnail.Url.Length > 0)
                    byUrl.TryAdd(thumbnail.Url, thumbnail);
            }
        }

        return byUrl.Values
            .OrderByDescending(static thumbnail => thumbnail.Area)
            .ToArray();
    }

    public static IReadOnlyList<string> FindMetadataTexts(
        JsonElement element,
        string propertyName = "content")
    {
        var result = new List<string>();

        foreach (var candidate in element.Descendants())
        {
            if (candidate.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                var text = value.GetString()!;
                if (!result.Contains(text, StringComparer.Ordinal))
                    result.Add(text);
            }
        }

        return result;
    }

    public static string? FindTextContaining(JsonElement element, string substring)
    {
        foreach (var text in FindMetadataTexts(element))
        {
            if (text.Contains(substring, StringComparison.OrdinalIgnoreCase))
                return text;
        }

        return null;
    }

    /// <summary>First text inside an object, including dynamicTextViewModel.</summary>
    public static string? FindFirstText(JsonElement element)
    {
        if (element.GetText() is { Length: > 0 } direct)
            return direct;

        foreach (var descendant in element.Descendants())
        {
            if (descendant.GetText() is { Length: > 0 } text)
                return text;
        }

        return null;
    }

    public static string? NormalizeHandle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            value.Contains('/', StringComparison.Ordinal))
        {
            var slashIndex = value.TrimEnd('/').LastIndexOf('/');
            value = slashIndex >= 0 ? value[(slashIndex + 1)..] : value;
        }

        if (value.Length == 0 || value.StartsWith('@'))
            return value;

        return "@" + value;
    }
}

internal enum LockupKind
{
    Video,
    Channel,
    Playlist
}

internal sealed record LockupResult(
    LockupKind Kind,
    VideoSummary? Video,
    ChannelSummary? Channel,
    PlaylistSummary? Playlist);

/// <summary>
/// Parses the new YouTube UI (<c>lockupViewModel</c>), which is gradually
/// replacing videoRenderer/channelRenderer/playlistRenderer.
/// </summary>
internal static class LockupParser
{
    /// <summary>True for promoted lockups (feed ads).</summary>
    public static bool IsAdvertisement(JsonElement lockup) =>
        lockup.Path("metadata", "feedAdMetadataViewModel") is not null ||
        lockup.Path("metadata", "adSlotMetadataViewModel") is not null ||
        lockup.FindFirstObjectWithProperty("adSlotRenderer") is not null;

    public static LockupResult? Parse(JsonElement lockup)
    {
        var contentType = lockup.GetString("contentType") ?? string.Empty;
        var contentId = lockup.GetString("contentId");

        if (string.IsNullOrWhiteSpace(contentId))
            contentId = lockup.FindFirstString("contentId");

        if (string.IsNullOrWhiteSpace(contentId))
            return null;

        var kind = ResolveKind(contentType, contentId);
        var metadataViewModel = lockup.FindFirstObjectWithProperty("lockupMetadataViewModel");

        var title = metadataViewModel is { } metadataTitle
            ? metadataTitle.GetTextProperty("title") ?? string.Empty
            : lockup.GetTextProperty("title") ?? string.Empty;

        var thumbnails = CommonParser.FindThumbnailsDeep(lockup);
        var metadataTexts = CollectMetadataTexts(lockup, metadataViewModel);
        var allTexts = CollectAllTexts(lockup);
        var badgeCountText = allTexts.FirstOrDefault(IsVideoCountText);

        return kind switch
        {
            LockupKind.Channel => new LockupResult(
                kind,
                null,
                new ChannelSummary
                {
                    Id = contentId,
                    Title = title,
                    Handle = metadataTexts.FirstOrDefault(static text =>
                        text.StartsWith('@')),
                    Thumbnails = thumbnails,
                    SubscriberCountText = metadataTexts.FirstOrDefault(IsSubscriberText),
                    VideoCountText = metadataTexts.FirstOrDefault(IsVideoCountText),
                    SubscriberCount = JsonHelpers.ParseCount(
                        metadataTexts.FirstOrDefault(IsSubscriberText))
                },
                null),

            LockupKind.Playlist => new LockupResult(
                kind,
                null,
                null,
                new PlaylistSummary
                {
                    Id = contentId,
                    Title = title,
                    Thumbnails = thumbnails,
                    ChannelId = lockup.FindChannelId(),
                    ChannelTitle = metadataTexts.FirstOrDefault(IsChannelNameText),
                    ItemCountText = badgeCountText,
                    ItemCount = JsonHelpers.ParseItemCount(badgeCountText)
                }),

            _ => new LockupResult(
                LockupKind.Video,
                new VideoSummary
                {
                    Id = contentId,
                    Title = title,
                    Thumbnails = thumbnails,
                    ChannelId = lockup.FindChannelId(),
                    ChannelTitle = metadataTexts.FirstOrDefault(IsChannelNameText),
                    Duration = allTexts
                        .Select(JsonHelpers.ParseDurationLoose)
                        .FirstOrDefault(static duration => duration is not null)
                        ?? metadataTexts
                            .Select(JsonHelpers.ParseDurationLoose)
                            .FirstOrDefault(static duration => duration is not null),
                    ViewCountText = metadataTexts.FirstOrDefault(IsViewCountText),
                    ViewCount = JsonHelpers.ParseCount(metadataTexts.FirstOrDefault(IsViewCountText)),
                    PublishedTime = metadataTexts.FirstOrDefault(IsPublishedText),
                    IsLive = metadataTexts.Any(static text =>
                        text.Contains("LIVE", StringComparison.OrdinalIgnoreCase))
                },
                null,
                null)
        };
    }

    private static LockupKind ResolveKind(string contentType, string contentId)
    {
        if (contentType.Contains("CHANNEL", StringComparison.OrdinalIgnoreCase))
            return LockupKind.Channel;

        if (contentType.Contains("PLAYLIST", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("ALBUM", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("PODCAST", StringComparison.OrdinalIgnoreCase))
        {
            return LockupKind.Playlist;
        }

        if (contentType.Contains("VIDEO", StringComparison.OrdinalIgnoreCase))
            return LockupKind.Video;

        if (contentId.StartsWith("UC", StringComparison.Ordinal))
            return LockupKind.Channel;

        if (contentId.StartsWith("PL", StringComparison.Ordinal) ||
            contentId.StartsWith("UU", StringComparison.Ordinal) ||
            contentId.StartsWith("LL", StringComparison.Ordinal) ||
            contentId.StartsWith("OL", StringComparison.Ordinal) ||
            contentId.StartsWith("FL", StringComparison.Ordinal) ||
            contentId.StartsWith("RD", StringComparison.Ordinal))
        {
            return LockupKind.Playlist;
        }

        return LockupKind.Video;
    }

    private static IReadOnlyList<string> CollectMetadataTexts(
        JsonElement lockup,
        JsonElement? metadataViewModel)
    {
        var metadata = metadataViewModel is { } viewModel
            ? viewModel.Path("metadata")
            : lockup.Path("metadata");

        return CommonParser.FindMetadataTexts(metadata ?? lockup);
    }

    private static IReadOnlyList<string> CollectAllTexts(JsonElement lockup)
    {
        var result = new List<string>();

        foreach (var element in lockup.Descendants())
        {
            foreach (var propertyName in new[] { "content", "text", "simpleText" })
            {
                if (!element.TryGetProperty(propertyName, out var value) ||
                    value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) && !result.Contains(text, StringComparer.Ordinal))
                    result.Add(text);
            }
        }

        return result;
    }

    private static bool IsSubscriberText(string text) =>
        text.Contains("subscriber", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("\u043F\u043E\u0434\u043F\u0438\u0441\u0447\u0438\u043A", StringComparison.OrdinalIgnoreCase);

    private static bool IsViewCountText(string text) =>
        text.Contains("view", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("\u043F\u0440\u043E\u0441\u043C\u043E\u0442\u0440", StringComparison.OrdinalIgnoreCase);

    private static bool IsVideoCountText(string text) =>
        text.Contains("video", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("\u0432\u0438\u0434\u0435\u043E", StringComparison.OrdinalIgnoreCase);

    private static bool IsPublishedText(string text) =>
        text.Contains("ago", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("\u043D\u0430\u0437\u0430\u0434", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("streamed", StringComparison.OrdinalIgnoreCase);

    private static bool IsChannelNameText(string text) =>
        !IsViewCountText(text) &&
        !IsSubscriberText(text) &&
        !IsPublishedText(text) &&
        !text.StartsWith('@') &&
        !text.Equals("Playlist", StringComparison.OrdinalIgnoreCase) &&
        !text.Equals("\u0412\u0438\u0434\u0435\u043E", StringComparison.OrdinalIgnoreCase) &&
        !text.Equals("\u041F\u043B\u0435\u0439\u043B\u0438\u0441\u0442", StringComparison.OrdinalIgnoreCase) &&
        !text.Equals("View full playlist", StringComparison.OrdinalIgnoreCase) &&
        JsonHelpers.ParseDurationLoose(text) is null;
}
