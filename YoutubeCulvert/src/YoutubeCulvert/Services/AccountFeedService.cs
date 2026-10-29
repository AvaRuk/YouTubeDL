using System.Runtime.CompilerServices;
using YoutubeCulvert.Authentication;
using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.Internal;

namespace YoutubeCulvert.Services;

/// <summary>Feeds of the signed-in account.</summary>
public sealed class AccountFeedService : IAccountFeedService
{
    private const string ChannelsBrowseId = "FEchannels";
    private const string SubscriptionsBrowseId = "FEsubscriptions";
    private const string PlaylistsBrowseId = "FEplaylist_aggregation";
    private const string HistoryBrowseId = "FEhistory";
    private const string WatchLaterBrowseId = "VLWL";
    private const string LikedBrowseId = "VLLL";

    private readonly BrowseEndpoint _browse;
    private readonly AccountEndpoint _accounts;
    private readonly FeedParser _feedParser;
    private readonly FeedChannelParser _channelParser;
    private readonly PlaylistTileParser _playlistParser;
    private readonly NotificationParser _notificationParser;
    private readonly IAuthenticationService _auth;

    internal AccountFeedService(
        BrowseEndpoint browse,
        AccountEndpoint accounts,
        FeedParser feedParser,
        FeedChannelParser channelParser,
        PlaylistTileParser playlistParser,
        NotificationParser notificationParser,
        IAuthenticationService auth)
    {
        _browse = browse;
        _accounts = accounts;
        _feedParser = feedParser;
        _channelParser = channelParser;
        _playlistParser = playlistParser;
        _notificationParser = notificationParser;
        _auth = auth;
    }

    public async Task<ChannelPage> GetSubscriptionsAsync(
        int maxResults = 30,
        string? continuation = null,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        if (maxResults <= 0)
            maxResults = 30;

        var result = await _browse
            .ExecuteAsync(new BrowseRequest(ChannelsBrowseId, Continuation: continuation), ct)
            .ConfigureAwait(false);

        var parsed = _channelParser.Parse(result.Raw);

        if (parsed.Items.Count == 0 && string.IsNullOrWhiteSpace(continuation))
        {
            result = await _browse
                .ExecuteAsync(new BrowseRequest(SubscriptionsBrowseId), ct)
                .ConfigureAwait(false);

            parsed = _channelParser.Parse(result.Raw);
        }

        return new ChannelPage
        {
            Items = parsed.Items.Take(maxResults).ToArray(),
            Continuation = parsed.Continuation
        };
    }

    public async Task<HistoryPage> GetHistoryAsync(
        int maxResults = 30,
        string? continuation = null,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var page = await GetVideoFeedAsync(HistoryBrowseId, maxResults, continuation, ct)
            .ConfigureAwait(false);

        return new HistoryPage
        {
            Items = page.Items.Select(static video => new HistoryItem { Video = video }).ToArray(),
            Continuation = page.Continuation
        };
    }

    public async Task<VideoPage> GetWatchLaterAsync(
        int maxResults = 30,
        string? continuation = null,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
        return await GetVideoFeedAsync(WatchLaterBrowseId, maxResults, continuation, ct)
            .ConfigureAwait(false);
    }

    public async Task<VideoPage> GetLikedAsync(
        int maxResults = 30,
        string? continuation = null,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
        return await GetVideoFeedAsync(LikedBrowseId, maxResults, continuation, ct)
            .ConfigureAwait(false);
    }

    public async Task<PlaylistPage> GetPlaylistsAsync(
        int maxResults = 30,
        string? continuation = null,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        if (maxResults <= 0)
            maxResults = 30;

        var result = await _browse
            .ExecuteAsync(new BrowseRequest(PlaylistsBrowseId, Continuation: continuation), ct)
            .ConfigureAwait(false);

        var parsed = _playlistParser.Parse(result.Raw);

        return new PlaylistPage
        {
            Items = parsed.Items.Take(maxResults).ToArray(),
            Continuation = parsed.Continuation
        };
    }

    public async Task<NotificationPage> GetNotificationsAsync(
        int maxResults = 20,
        string? continuation = null,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        if (maxResults <= 0)
            maxResults = 20;

        var result = await _accounts.GetNotificationsAsync(continuation, ct).ConfigureAwait(false);
        var parsed = _notificationParser.Parse(result.Raw);

        return new NotificationPage
        {
            Items = parsed.Items.Take(maxResults).ToArray(),
            Continuation = parsed.Continuation
        };
    }

    public async Task<int> GetUnseenNotificationCountAsync(CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

        var result = await _accounts.GetUnseenNotificationCountAsync(ct).ConfigureAwait(false);
        var count = result.Raw.GetInt64("unseenCount");

        if (count is null)
        {
            foreach (var element in result.Raw.Descendants())
            {
                if (element.TryGetProperty("updateNotificationsUnseenCountAction", out var action) &&
                    action.ValueKind == JsonValueKind.Object &&
                    action.GetInt64("unseenCount") is { } nested)
                {
                    count = nested;
                    break;
                }
            }
        }

        return (int)Math.Clamp(count ?? 0, 0, int.MaxValue);
    }

    public async IAsyncEnumerable<VideoSummary> EnumerateAsync(
        string target,
        PaginationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var maxItems = Paging.MaxItems(options);
        var maxPages = Paging.MaxPages(options);
        var yielded = 0;

        var browseId = target switch
        {
            "history" => HistoryBrowseId,
            "watch-later" => WatchLaterBrowseId,
            "liked" => LikedBrowseId,
            _ => throw new ArgumentException(
                $"Unknown account feed '{target}'. Use 'history', 'watch-later' or 'liked'.",
                nameof(target))
        };

        var page = await GetVideoFeedAsync(browseId, 30, null, ct).ConfigureAwait(false);

        for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            foreach (var video in page.Items)
            {
                if (yielded >= maxItems)
                    yield break;

                yielded++;
                yield return video;
            }

            if (page.Continuation is null || yielded >= maxItems)
                yield break;

            page = await GetVideoFeedAsync(browseId, 30, page.Continuation, ct).ConfigureAwait(false);
        }
    }

    private async Task<VideoPage> GetVideoFeedAsync(
        string browseId,
        int maxResults,
        string? continuation,
        CancellationToken ct)
    {
        if (maxResults <= 0)
            maxResults = 30;

        var result = await _browse
            .ExecuteAsync(new BrowseRequest(browseId, Continuation: continuation), ct)
            .ConfigureAwait(false);

        var parsed = _feedParser.Parse(result.Raw);

        if (!parsed.IsRecognized)
        {
            throw new YoutubeParseException(
                $"The account feed '{browseId}' was not recognized: {parsed.Diagnostics.Reason}");
        }

        return new VideoPage
        {
            Items = parsed.Items.Take(maxResults).ToArray(),
            Continuation = parsed.Continuation
        };
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        await _auth.InitializeAsync(ct).ConfigureAwait(false);

        if (!_auth.IsAuthenticated)
        {
            throw new YoutubeAuthenticationRequiredException(
                "This feed belongs to a YouTube account. " +
                "Call Auth.SignInWithCookieHeaderAsync or Auth.StartDeviceCodeFlowAsync first.");
        }
    }
}
