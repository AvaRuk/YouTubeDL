using YoutubeCulvert.Authentication;
using YoutubeCulvert.Caching;
using YoutubeCulvert.Configuration;
using YoutubeCulvert.Downloads;
using YoutubeCulvert.Handle;
using YoutubeCulvert.Http;
using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.InnerTube.Middleware;
using YoutubeCulvert.Logging;
using YoutubeCulvert.Parsing;
using YoutubeCulvert.Services;
using YoutubeCulvert.Url;

namespace YoutubeCulvert.Internal;

/// <summary>Builds the default object graph of <see cref="YoutubeClient"/>.</summary>
internal static class YoutubeClientFactory
{
    public static YoutubeClientContext Create(YoutubeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var store = options.AuthStore ?? FileAuthStore.CreateDefault();
        var authState = new AuthStateManager(store, new Uri(options.NormalizedBaseUrl));
        var http = new HttpTransport(options, authState.Cookies);
        var cache = options.Cache ?? NullYoutubeCache.Instance;
        var ttls = options.CacheTtls;
        var logger = options.Logger ?? NullLogger.Instance;
        var profiles = options.ClientProfileProvider ?? new AutoClientProfileProvider();

        var middlewares = new List<IInnerTubeMiddleware>
        {
            new LoggingMiddleware(logger),
            new CacheMiddleware(cache, ttls, logger),
            new AuthSigningMiddleware(authState),
            new RetryMiddleware(options, logger),
            new RateLimitMiddleware(),
            new ErrorClassificationMiddleware()
        };

        var transport = new InnerTubeTransport(
            http,
            authState,
            options,
            profiles,
            new RequestSigner(authState),
            middlewares);

        var player = new PlayerEndpoint(transport);
        var browse = new BrowseEndpoint(transport);
        var search = new SearchEndpoint(transport);
        var next = new NextEndpoint(transport);
        var accounts = new AccountEndpoint(transport);
        var resolve = new ResolveUrlEndpoint(transport);

        var videoParser = new VideoParser();
        var manifestParser = new StreamManifestParser();
        var captionParser = new CaptionTrackParser();
        var chapterParser = new ChapterParser();
        var commentParser = new CommentParser();
        var channelParser = new ChannelParser();
        var playlistParser = new PlaylistParser();
        var playlistVideoParser = new PlaylistVideoParser();
        var searchParser = new SearchParser();
        var feedParser = new FeedParser();
        var feedChannelParser = new FeedChannelParser();
        var feedPlaylistParser = new FeedPlaylistParser();
        var playlistTileParser = new PlaylistTileParser();
        var notificationParser = new NotificationParser();
        var transcriptPanelParser = new TranscriptPanelParser();

        var urls = new YoutubeUrlParser();
        var channelResolver = new ChannelHandleResolver(resolve, http, cache, ttls, options);

        var videos = new VideosService(
            player,
            next,
            videoParser,
            manifestParser,
            captionParser,
            chapterParser,
            urls,
            options);

        var searchService = new SearchService(search, searchParser, options, http);
        var comments = new CommentsService(next, commentParser, urls);
        var channels = new ChannelsService(
            browse,
            channelResolver,
            channelParser,
            feedParser,
            feedPlaylistParser,
            urls);
        var playlists = new PlaylistsService(browse, playlistParser, playlistVideoParser, urls);
        var home = new HomeService(browse, feedParser);

        var oauth = new OAuthProvider(options.OAuth, http, accounts, authState);
        var cookies = new CookieAuthProvider(authState, accounts);
        var auth = new AuthService(authState, cookies, oauth, accounts, http);

        var transcripts = new TranscriptsService(
            player,
            next,
            captionParser,
            transcriptPanelParser,
            http,
            cache,
            ttls,
            urls,
            options);

        var accountFeed = new AccountFeedService(
            browse,
            accounts,
            feedParser,
            feedChannelParser,
            playlistTileParser,
            notificationParser,
            auth);

        var interactions = new InteractionsService(accounts, channelResolver, auth, urls);
        var pages = new PageService(http, urls);

        var streamResolver = new StreamResolver(videos);
        var strategy = new RangeDownloadStrategy(options.HttpMessageHandler, options.UserAgent);
        var coordinator = new DownloadCoordinator(
            streamResolver,
            strategy,
            new CompanionFileManager(),
            videos);
        var downloader = new YoutubeDownloader(coordinator, strategy, videos, options, strategy);

        return new YoutubeClientContext
        {
            Options = options,
            Search = searchService,
            Videos = videos,
            Transcripts = transcripts,
            Comments = comments,
            Channels = channels,
            Playlists = playlists,
            Home = home,
            AccountFeed = accountFeed,
            Interactions = interactions,
            Pages = pages,
            Downloader = downloader,
            Auth = auth,
            Urls = urls,
            Transport = transport,
            InitializeSessionAsync = async ct =>
            {
                await http.InitializeAsync(ct).ConfigureAwait(false);
                await auth.InitializeAsync(ct).ConfigureAwait(false);
            },
            SaveStateAsync = ct => authState.SaveAsync(ct),
            OwnedResources = [http, downloader]
        };
    }
}
