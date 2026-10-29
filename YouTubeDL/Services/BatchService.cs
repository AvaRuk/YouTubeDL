using YoutubeCulvert;
using YoutubeCulvert.Datatypes;
using YoutubeCulvert.Exceptions;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>Loads playlist/channel batches and applies the user's filters.</summary>
public sealed class BatchService
{
    private readonly YoutubeClientManager _clientManager;
    private readonly SettingsService _settings;
    private readonly HistoryStore _history;
    private readonly HealthMonitor _health;
    private readonly DiagnosticsLog _log;

    public BatchService(
        YoutubeClientManager clientManager,
        SettingsService settings,
        HistoryStore history,
        HealthMonitor health,
        DiagnosticsLog log)
    {
        _clientManager = clientManager;
        _settings = settings;
        _history = history;
        _health = health;
        _log = log;
    }

    public sealed record BatchVideo(
        string VideoId,
        string Title,
        string? ChannelTitle,
        TimeSpan? Duration,
        string? ThumbnailUrl,
        DateTimeOffset? Published,
        long? ViewCount,
        string Url);

    public sealed record BatchSelection(
        string Title,
        string? PlaylistName,
        IReadOnlyList<BatchVideo> Items,
        bool Truncated);

    public async Task<AppResult<BatchSelection>> LoadPlaylistAsync(
        string input,
        BatchFilter filter,
        CancellationToken cancellationToken = default)
    {
        var client = _clientManager.Client;

        if (!client.YoutubeUrl.TryParsePlaylistId(input, out var playlistId) ||
            string.IsNullOrWhiteSpace(playlistId))
        {
            return AppResult<BatchSelection>.Fail(new ItemError(
                ErrorCategory.Terminal,
                LocalizationService.Instance.Get("ErrorInvalidPlaylist")));
        }

        try
        {
            var playlistResult = await client.Playlists
                .TryGetAsync(playlistId, cancellationToken)
                .ConfigureAwait(false);

            if (playlistResult.Error is { } error)
                return AppResult<BatchSelection>.Fail(Map(error));

            var playlist = playlistResult.Value!;
            var items = new List<BatchVideo>();
            var max = Math.Max(1, filter.MaxItems);
            var truncated = false;
            string? continuation = null;

            // Page manually: a broken continuation page must not discard the
            // hundreds of entries that were already loaded from page one.
            for (var pageNumber = 0; pageNumber < 20; pageNumber++)
            {
                var pageResult = await client.Playlists
                    .TryGetVideosAsync(playlistId, continuation, cancellationToken)
                    .ConfigureAwait(false);

                if (pageResult.Error is { } pageError)
                {
                    if (pageNumber == 0)
                        return AppResult<BatchSelection>.Fail(Map(pageError));

                    _log.WriteError($"Playlist continuation of '{playlistId}' failed.", null);
                    break;
                }

                var page = pageResult.Value!;

                foreach (var video in page.Items)
                {
                    if (items.Count >= max)
                    {
                        truncated = true;
                        break;
                    }

                    if (filter.OnlyNew && _history.ContainsVideo(video.VideoId))
                        continue;

                    var summary = new VideoSummary
                    {
                        Id = video.VideoId,
                        Title = video.Title,
                        Duration = video.Duration,
                        Thumbnails = video.Thumbnails
                    };

                    if (!filter.Matches(summary))
                        continue;

                    items.Add(new BatchVideo(
                        video.VideoId,
                        video.Title,
                        playlist.OwnerChannelTitle,
                        video.Duration,
                        video.Thumbnails.OrderByDescending(static thumbnail => thumbnail.Area)
                            .Select(static thumbnail => thumbnail.Url)
                            .FirstOrDefault(),
                        null,
                        null,
                        video.Url));
                }

                if (items.Count >= max || page.Continuation is null)
                {
                    truncated |= items.Count >= max;
                    break;
                }

                continuation = page.Continuation;
            }

            _health.ReportSuccess();

            return AppResult<BatchSelection>.Ok(new BatchSelection(
                playlist.Title,
                playlist.Title,
                items,
                truncated));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is YoutubeException or HttpRequestException)
        {
            _log.WriteError($"Loading playlist '{playlistId}' failed.", exception);
            _health.ReportFailure(exception.Message, isNetwork: true);
            return AppResult<BatchSelection>.Fail(new ItemError(
                ErrorCategory.Retryable,
                exception.Message,
                ErrorAction.Retry));
        }
    }

    public async Task<AppResult<BatchSelection>> LoadChannelAsync(
        string input,
        BatchFilter filter,
        bool allTabs,
        CancellationToken cancellationToken = default)
    {
        var client = _clientManager.Client;

        if (!client.YoutubeUrl.TryParseChannel(input, out var channelRef) || channelRef is null)
        {
            return AppResult<BatchSelection>.Fail(new ItemError(
                ErrorCategory.Terminal,
                LocalizationService.Instance.Get("ErrorInvalidChannel")));
        }

        try
        {
            var channelResult = await client.Channels
                .TryGetAsync(input, cancellationToken)
                .ConfigureAwait(false);

            if (channelResult.Error is { } error)
                return AppResult<BatchSelection>.Fail(Map(error));

            var channel = channelResult.Value!;
            ChannelTab[] tabs = allTabs
                ? [ChannelTab.Videos, ChannelTab.Shorts, ChannelTab.Live]
                : [filter.Tab];

            var items = new List<BatchVideo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var max = Math.Max(1, filter.MaxItems);
            var truncated = false;

            foreach (var tab in tabs)
            {
                string? continuation = null;
                var pages = 0;

                while (items.Count < max && pages++ < 25)
                {
                    var page = await client.Channels
                        .GetVideosAsync(input, tab, continuation, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var video in page.Items)
                    {
                        if (items.Count >= max)
                        {
                            truncated = true;
                            break;
                        }

                        if (!seen.Add(video.Id))
                            continue;

                        if (filter.OnlyNew && _history.ContainsVideo(video.Id))
                            continue;

                        if (!filter.Matches(video))
                            continue;

                        items.Add(new BatchVideo(
                            video.Id,
                            video.Title,
                            video.ChannelTitle ?? channel.Title,
                            video.Duration,
                            video.ThumbnailUrl,
                            BatchFilter.ParsePublished(video.PublishedTime),
                            video.ViewCount,
                            video.Url));
                    }

                    continuation = page.Continuation;

                    if (continuation is null)
                        break;
                }

                if (items.Count >= max)
                {
                    truncated = true;
                    break;
                }
            }

            _health.ReportSuccess();

            return AppResult<BatchSelection>.Ok(new BatchSelection(
                channel.Title,
                null,
                items,
                truncated));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is YoutubeException or HttpRequestException)
        {
            _log.WriteError($"Loading channel '{input}' failed.", exception);
            _health.ReportFailure(exception.Message, isNetwork: true);
            return AppResult<BatchSelection>.Fail(new ItemError(
                ErrorCategory.Retryable,
                exception.Message,
                ErrorAction.Retry));
        }
    }

    private static ItemError Map(YoutubeError error) => error.Kind switch
    {
        YoutubeErrorKind.Unavailable => new ItemError(
            ErrorCategory.Terminal,
            LocalizationService.Instance.Get("ErrorVideoUnavailable"),
            ErrorAction.OpenOnYouTube),
        YoutubeErrorKind.AuthenticationRequired => new ItemError(
            ErrorCategory.Terminal,
            LocalizationService.Instance.Get("ErrorAuthRequired"),
            ErrorAction.SignIn),
        YoutubeErrorKind.RateLimited => new ItemError(
            ErrorCategory.Retryable,
            LocalizationService.Instance.Get("ErrorRateLimited"),
            ErrorAction.Retry),
        _ => new ItemError(
            ErrorCategory.Retryable,
            LocalizationService.Instance.Get("ErrorNetwork"),
            ErrorAction.Retry)
    };
}
