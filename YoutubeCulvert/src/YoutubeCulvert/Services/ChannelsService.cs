using System.Runtime.CompilerServices;
using YoutubeCulvert.Handle;
using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.Url;

namespace YoutubeCulvert.Services;

/// <summary>Channels: information and tabs.</summary>
public sealed class ChannelsService : IChannelsService
{
    private const string VideosTab = "EgZ2aWRlb3PyBgQKAjoA";
    private const string ShortsTab = "EgZzaG9ydHPyBgUKA5oBAA==";
    private const string LiveTab = "EgdzdHJlYW1z8gYECgJ6AA==";
    private const string PlaylistsTab = "EglwbGF5bGlzdHPyBgQKAkIA";

    private readonly BrowseEndpoint _browse;
    private readonly ChannelHandleResolver _resolver;
    private readonly ChannelParser _parser;
    private readonly FeedParser _feedParser;
    private readonly FeedPlaylistParser _feedPlaylistParser;
    private readonly IYoutubeUrlParser _urls;

    internal ChannelsService(
        BrowseEndpoint browse,
        ChannelHandleResolver resolver,
        ChannelParser parser,
        FeedParser feedParser,
        FeedPlaylistParser feedPlaylistParser,
        IYoutubeUrlParser urls)
    {
        _browse = browse;
        _resolver = resolver;
        _parser = parser;
        _feedParser = feedParser;
        _feedPlaylistParser = feedPlaylistParser;
        _urls = urls;
    }

    public Task<Channel> GetAsync(ChannelRef channel, CancellationToken ct = default) =>
        GetCoreAsync(channel, ct);

    public Task<Channel> GetAsync(string channelIdHandleOrUrl, CancellationToken ct = default) =>
        GetCoreAsync(_urls.ParseChannelOrThrow(channelIdHandleOrUrl), ct);

    public async Task<Result<Channel>> TryGetAsync(
        string channelIdHandleOrUrl,
        CancellationToken ct = default) =>
        await ServiceErrors.RunAsync(() => GetAsync(channelIdHandleOrUrl, ct)).ConfigureAwait(false);

    public async Task<VideoPage> GetVideosAsync(
        string channelIdHandleOrUrl,
        ChannelTab tab = ChannelTab.Videos,
        string? continuation = null,
        CancellationToken ct = default)
    {
        string? channelId = null;

        if (string.IsNullOrWhiteSpace(continuation))
            channelId = await ResolveIdAsync(channelIdHandleOrUrl, ct).ConfigureAwait(false);

        var result = await _browse
            .ExecuteAsync(
                new BrowseRequest(channelId, GetTabParameters(tab), continuation),
                ct)
            .ConfigureAwait(false);

        var parsed = _feedParser.Parse(result.Raw);

        if (!parsed.IsRecognized)
        {
            throw new YoutubeParseException(
                "The channel videos response structure was not recognized: " +
                parsed.Diagnostics.Reason);
        }

        return new VideoPage
        {
            Items = parsed.Items,
            Continuation = parsed.Continuation
        };
    }

    public async Task<PlaylistPage> GetPlaylistsAsync(
        string channelIdHandleOrUrl,
        string? continuation = null,
        CancellationToken ct = default)
    {
        string? channelId = null;

        if (string.IsNullOrWhiteSpace(continuation))
            channelId = await ResolveIdAsync(channelIdHandleOrUrl, ct).ConfigureAwait(false);

        var result = await _browse
            .ExecuteAsync(new BrowseRequest(channelId, PlaylistsTab, continuation), ct)
            .ConfigureAwait(false);

        var parsed = _feedPlaylistParser.Parse(result.Raw);

        if (!parsed.IsRecognized)
        {
            throw new YoutubeParseException(
                "The channel playlists response structure was not recognized: " +
                parsed.Diagnostics.Reason);
        }

        return new PlaylistPage
        {
            Items = parsed.Items,
            Continuation = parsed.Continuation
        };
    }

    public Task<Result<VideoPage>> TryGetVideosAsync(
        string channelIdHandleOrUrl,
        ChannelTab tab = ChannelTab.Videos,
        string? continuation = null,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetVideosAsync(channelIdHandleOrUrl, tab, continuation, ct));

    public async IAsyncEnumerable<VideoSummary> EnumerateAsync(
        string target,
        PaginationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var maxItems = Paging.MaxItems(options);
        var maxPages = Paging.MaxPages(options);
        var yielded = 0;

        var page = await GetVideosAsync(target, ChannelTab.Videos, null, ct).ConfigureAwait(false);

        for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            foreach (var item in page.Items)
            {
                if (yielded >= maxItems)
                    yield break;

                yielded++;
                yield return item;
            }

            if (page.Continuation is null || yielded >= maxItems)
                yield break;

            page = await GetVideosAsync(target, ChannelTab.Videos, page.Continuation, ct)
                .ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<PlaylistSummary> EnumeratePlaylistsAsync(
        string channelIdHandleOrUrl,
        PaginationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var maxItems = Paging.MaxItems(options);
        var maxPages = Paging.MaxPages(options);
        var yielded = 0;

        var page = await GetPlaylistsAsync(channelIdHandleOrUrl, null, ct).ConfigureAwait(false);

        for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            foreach (var item in page.Items)
            {
                if (yielded >= maxItems)
                    yield break;

                yielded++;
                yield return item;
            }

            if (page.Continuation is null || yielded >= maxItems)
                yield break;

            page = await GetPlaylistsAsync(channelIdHandleOrUrl, page.Continuation, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task<Channel> GetCoreAsync(ChannelRef channel, CancellationToken ct)
    {
        var channelId = await _resolver.ResolveAsync(channel, ct).ConfigureAwait(false);

        var result = await _browse
            .ExecuteAsync(new BrowseRequest(channelId), ct)
            .ConfigureAwait(false);

        var parsed = _parser.Parse(new ChannelParseInput(result.Raw, channelId));

        if (!parsed.IsRecognized || parsed.Items.Count == 0)
        {
            throw new YoutubeParseException(
                $"The channel page of '{channelId}' was not recognized: {parsed.Diagnostics.Reason}");
        }

        return parsed.Items[0];
    }

    private Task<string> ResolveIdAsync(string channelIdHandleOrUrl, CancellationToken ct)
    {
        var channel = _urls.ParseChannelOrThrow(channelIdHandleOrUrl);
        return _resolver.ResolveAsync(channel, ct);
    }

    private static string? GetTabParameters(ChannelTab tab) => tab switch
    {
        ChannelTab.Videos => VideosTab,
        ChannelTab.Shorts => ShortsTab,
        ChannelTab.Live => LiveTab,
        ChannelTab.Playlists => PlaylistsTab,

        // Community/About have no stable params; the default channel browse is used.
        _ => null
    };
}
