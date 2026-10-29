using YoutubeCulvert.Internal;

namespace YoutubeCulvert.Parsing;

/// <summary>Parses video entries of a feed (home, history, watch later, channel tabs).</summary>
public sealed class FeedParser : IResponseParser<JsonElement, VideoSummary>
{
    public ParseResult<VideoSummary> Parse(JsonElement root)
    {
        var items = VideoParser.ParseAllSummaries(root, int.MaxValue);

        var recognized = items.Count > 0 ||
                         root.TryGetProperty("contents", out _) ||
                         root.TryGetProperty("alerts", out _) ||
                         root.TryGetProperty("continuationContents", out _) ||
                         root.TryGetProperty("onResponseReceivedActions", out _);

        if (!recognized)
        {
            return ParseResult<VideoSummary>.Unrecognized(
                "feed response has no contents/alerts",
                "contents");
        }

        return ParseResult<VideoSummary>.Recognized(items, root.FindContinuationToken());
    }
}

/// <summary>Parses playlist entries of a feed.</summary>
public sealed class FeedPlaylistParser : IResponseParser<JsonElement, PlaylistSummary>
{
    public ParseResult<PlaylistSummary> Parse(JsonElement root)
    {
        var items = PlaylistParser.ParseAllSummaries(root, int.MaxValue);

        var recognized = items.Count > 0 ||
                         root.TryGetProperty("contents", out _) ||
                         root.TryGetProperty("alerts", out _) ||
                         root.TryGetProperty("continuationContents", out _);

        if (!recognized)
        {
            return ParseResult<PlaylistSummary>.Unrecognized(
                "feed response has no contents/alerts",
                "contents");
        }

        return ParseResult<PlaylistSummary>.Recognized(items, root.FindContinuationToken());
    }
}

/// <summary>Parses channel entries of a feed (subscriptions).</summary>
public sealed class FeedChannelParser : IResponseParser<JsonElement, ChannelSummary>
{
    public ParseResult<ChannelSummary> Parse(JsonElement root)
    {
        var items = new List<ChannelSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in root.Descendants())
        {
            if (element.TryGetProperty("channelRenderer", out var renderer) &&
                renderer.ValueKind == JsonValueKind.Object)
            {
                var summary = ChannelParser.ParseSummary(renderer);
                if (summary is not null && seen.Add(summary.Id))
                    items.Add(summary);

                continue;
            }

            if (element.TryGetProperty("tileRenderer", out var tile) &&
                tile.ValueKind == JsonValueKind.Object)
            {
                var summary = ParseTile(tile);
                if (summary is not null && seen.Add(summary.Id))
                    items.Add(summary);
            }
        }

        var recognized = items.Count > 0 ||
                         root.TryGetProperty("contents", out _) ||
                         root.TryGetProperty("alerts", out _) ||
                         root.TryGetProperty("onResponseReceivedActions", out _);

        if (!recognized)
        {
            return ParseResult<ChannelSummary>.Unrecognized(
                "feed response has no channel entries",
                "contents");
        }

        return ParseResult<ChannelSummary>.Recognized(items, root.FindContinuationToken());
    }

    /// <summary>Parses a TV-style <c>tileRenderer</c> channel.</summary>
    public static ChannelSummary? ParseTile(JsonElement tile)
    {
        if (!string.Equals(
                tile.GetString("contentType"),
                "TILE_CONTENT_TYPE_CHANNEL",
                StringComparison.Ordinal))
        {
            return null;
        }

        var channelId = tile.GetString("contentId");
        if (string.IsNullOrWhiteSpace(channelId))
            return null;

        var metadata = tile.Path("metadata", "tileMetadataRenderer");
        var title = metadata is { } m && m.TryGetProperty("title", out var titleElement)
            ? CommonParser.FindFirstText(titleElement)
            : null;

        string? subscriberText = null;
        string? handle = null;

        if (metadata is { } metadataElement &&
            metadataElement.TryGetProperty("lines", out var lines) &&
            lines.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in lines.EnumerateArray())
            {
                var text = CommonParser.FindFirstText(line);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (text.StartsWith('@'))
                    handle ??= text;
                else if (text.Contains("subscriber", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("\u043F\u043E\u0434\u043F\u0438\u0441\u0447\u0438\u043A", StringComparison.OrdinalIgnoreCase))
                    subscriberText ??= text;
            }
        }

        var avatar = tile.Path("header", "tileHeaderRenderer", "thumbnail");
        var thumbnails = avatar is { } avatarValue ? avatarValue.ParseThumbnails() : [];

        return new ChannelSummary
        {
            Id = channelId,
            Title = string.IsNullOrWhiteSpace(title) ? handle ?? channelId : title,
            Handle = handle,
            Thumbnails = thumbnails,
            SubscriberCountText = subscriberText,
            SubscriberCount = JsonHelpers.ParseCount(subscriberText),
            Raw = tile.Clone()
        };
    }
}

/// <summary>Parses TV-style playlist tiles (account library).</summary>
public sealed class PlaylistTileParser : IResponseParser<JsonElement, PlaylistSummary>
{
    public ParseResult<PlaylistSummary> Parse(JsonElement root)
    {
        var items = PlaylistParser.ParseAllSummaries(root, int.MaxValue).ToList();
        var seen = new HashSet<string>(items.Select(static item => item.Id), StringComparer.Ordinal);

        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("tileRenderer", out var tile) ||
                tile.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!string.Equals(
                    tile.GetString("contentType"),
                    "TILE_CONTENT_TYPE_PLAYLIST",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var playlistId = tile.GetString("contentId");
            if (string.IsNullOrWhiteSpace(playlistId) || !seen.Add(playlistId))
                continue;

            var metadata = tile.Path("metadata", "tileMetadataRenderer");
            var title = metadata is { } m && m.TryGetProperty("title", out var titleElement)
                ? CommonParser.FindFirstText(titleElement)
                : null;

            var thumbnail = tile.Path("header", "tileHeaderRenderer", "thumbnail");

            items.Add(new PlaylistSummary
            {
                Id = playlistId,
                Title = string.IsNullOrWhiteSpace(title) ? playlistId : title,
                Thumbnails = thumbnail is { } thumbnailValue
                    ? thumbnailValue.ParseThumbnails()
                    : [],
                Raw = tile.Clone()
            });
        }

        var recognized = items.Count > 0 ||
                         root.TryGetProperty("contents", out _) ||
                         root.TryGetProperty("alerts", out _);

        if (!recognized)
        {
            return ParseResult<PlaylistSummary>.Unrecognized(
                "feed response has no playlist entries",
                "contents");
        }

        return ParseResult<PlaylistSummary>.Recognized(items, root.FindContinuationToken());
    }
}

/// <summary>Parses <c>notificationRenderer</c> items of the inbox.</summary>
public sealed class NotificationParser : IResponseParser<JsonElement, NotificationItem>
{
    public ParseResult<NotificationItem> Parse(JsonElement root)
    {
        var result = new List<NotificationItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("notificationRenderer", out var renderer) ||
                renderer.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = renderer.GetString("notificationId");
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                continue;

            var endpoint = renderer.FindFirstObjectWithProperty("navigationEndpoint");
            var videoId = endpoint?.Path("watchEndpoint", "videoId")?.GetString();
            var browseId = endpoint?.Path("browseEndpoint", "browseId")?.GetString();

            string? channelId = null;
            string? playlistId = null;

            if (!string.IsNullOrWhiteSpace(browseId))
            {
                if (browseId.StartsWith("UC", StringComparison.Ordinal) && browseId.Length == 24)
                    channelId = browseId;
                else
                    playlistId = browseId.StartsWith("VL", StringComparison.Ordinal) ? browseId[2..] : browseId;
            }

            result.Add(new NotificationItem
            {
                Id = id,
                Text = renderer.GetTextProperty("text"),
                PublishedTime = renderer.GetTextProperty("sentTimeText"),
                ThumbnailUrl = renderer.TryGetProperty("thumbnail", out var thumbnail)
                    ? thumbnail.BestThumbnailUrl()
                    : null,
                IsRead = renderer.GetBool("read") == true,
                VideoId = videoId,
                ChannelId = channelId,
                PlaylistId = playlistId,
                Raw = renderer.Clone()
            });
        }

        var recognized = result.Count > 0 ||
                         root.TryGetProperty("contents", out _) ||
                         root.TryGetProperty("actions", out _) ||
                         root.FindFirstObjectWithProperty("notificationRenderer") is not null;

        if (!recognized)
        {
            return ParseResult<NotificationItem>.Unrecognized(
                "notifications response has no notificationRenderer items",
                "contents");
        }

        return ParseResult<NotificationItem>.Recognized(result, root.FindContinuationToken());
    }
}

/// <summary>Parses <c>account/account_menu</c>.</summary>
public sealed class AccountParser : IResponseParser<JsonElement, Account>
{
    public ParseResult<Account> Parse(JsonElement root)
    {
        var header = root.FindFirstObjectWithProperty("activeAccountHeaderRenderer");

        if (header is not { } activeAccount)
        {
            return ParseResult<Account>.Unrecognized(
                "account menu has no activeAccountHeaderRenderer",
                "actions/0/openPopupAction/popup/...");
        }

        var account = new Account
        {
            Name = activeAccount.GetTextProperty("accountName")
                   ?? root.FindFirstString("accountName"),
            Handle = CommonParser.NormalizeHandle(activeAccount.GetTextProperty("channelHandle")),
            Email = activeAccount.GetTextProperty("email"),
            ChannelId = activeAccount.FindChannelId(),
            AvatarUrl = activeAccount.TryGetProperty("accountPhoto", out var photo)
                ? photo.BestThumbnailUrl()
                : null,
            Raw = root.Clone()
        };

        return ParseResult<Account>.Recognized([account]);
    }
}
