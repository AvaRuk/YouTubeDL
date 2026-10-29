using YoutubeCulvert.Authentication;
using YoutubeCulvert.Downloads;
using YoutubeCulvert.Internal;
using YoutubeCulvert.Services;
using YoutubeCulvert.Url;

namespace YoutubeCulvert;

/// <summary>Everything a <see cref="YoutubeClient"/> needs, resolved once.</summary>
internal sealed class YoutubeClientContext
{
    public required YoutubeClientOptions Options { get; init; }

    public required ISearchService Search { get; init; }

    public required IVideosService Videos { get; init; }

    public required ITranscriptsService Transcripts { get; init; }

    public required ICommentsService Comments { get; init; }

    public required IChannelsService Channels { get; init; }

    public required IPlaylistsService Playlists { get; init; }

    public required IHomeService Home { get; init; }

    public required IAccountFeedService AccountFeed { get; init; }

    public required IInteractionsService Interactions { get; init; }

    public required IPageService Pages { get; init; }

    public required IYoutubeDownloader Downloader { get; init; }

    public required IAuthenticationService Auth { get; init; }

    public required IYoutubeUrlParser Urls { get; init; }

    public required IInnerTubeTransport Transport { get; init; }

    public required Func<CancellationToken, Task> InitializeSessionAsync { get; init; }

    public required Func<CancellationToken, Task> SaveStateAsync { get; init; }

    public IReadOnlyList<IDisposable> OwnedResources { get; init; } = [];
}

/// <summary>Entry point of the YoutubeCulvert library.</summary>
public sealed class YoutubeClient : IYoutubeClient
{
    private readonly YoutubeClientContext _context;
    private int _disposed;

    /// <summary>Builds a self-contained client with the default infrastructure.</summary>
    public YoutubeClient(YoutubeClientOptions? options = null)
        : this(YoutubeClientFactory.Create(options ?? new YoutubeClientOptions()))
    {
    }

    internal YoutubeClient(YoutubeClientContext context) => _context = context;

    public ISearchService Search => _context.Search;

    public IVideosService Videos => _context.Videos;

    public ITranscriptsService Transcripts => _context.Transcripts;

    public ICommentsService Comments => _context.Comments;

    public IChannelsService Channels => _context.Channels;

    public IPlaylistsService Playlists => _context.Playlists;

    public IHomeService Home => _context.Home;

    public IAccountFeedService AccountFeed => _context.AccountFeed;

    public IInteractionsService Interactions => _context.Interactions;

    public IPageService Pages => _context.Pages;

    public IYoutubeDownloader Downloader => _context.Downloader;

    public IAuthenticationService Auth => _context.Auth;

    public IYoutubeUrlParser YoutubeUrl => _context.Urls;

    public YoutubeClientOptions Options => _context.Options;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        _context.Options.Validate();
        await _context.InitializeSessionAsync(ct).ConfigureAwait(false);
    }

    public Task<DownloadResult> DownloadAsync(
        string videoId,
        string path,
        int? maxHeight = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _context.Downloader.DownloadAsync(videoId, path, maxHeight, progress, ct);
    }

    public Task<DownloadResult> DownloadAudioAsync(
        string videoId,
        string path,
        string? audioTrackId = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _context.Downloader.DownloadAudioAsync(videoId, path, audioTrackId, progress, ct);
    }

    public Task<JsonElement> RawAsync(
        string endpoint,
        JsonObject payload,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(payload);

        return _context.Transport.SendAsync(new InnerTubeRequest(endpoint, payload), ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            await _context.SaveStateAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            foreach (var resource in _context.OwnedResources)
                resource.Dispose();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(
        Volatile.Read(ref _disposed) == 1,
        this);
}
