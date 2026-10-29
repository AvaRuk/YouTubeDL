using YoutubeCulvert.Authentication;
using YoutubeCulvert.Datatypes;
using YoutubeCulvert.Downloads;
using YoutubeCulvert.Services;
using YoutubeCulvert.Url;

namespace YoutubeCulvert;

/// <summary>
/// A paginated source. <c>target</c> identifies what to crawl: a query, video
/// id, channel reference, playlist id or account-feed name.
/// </summary>
public interface IPagedService<T>
{
    IAsyncEnumerable<T> EnumerateAsync(
        string target,
        PaginationOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>Entry point of the library.</summary>
public interface IYoutubeClient : IAsyncDisposable
{
    ISearchService Search { get; }

    IVideosService Videos { get; }

    ITranscriptsService Transcripts { get; }

    ICommentsService Comments { get; }

    IChannelsService Channels { get; }

    IPlaylistsService Playlists { get; }

    IHomeService Home { get; }

    IAccountFeedService AccountFeed { get; }

    IInteractionsService Interactions { get; }

    IPageService Pages { get; }

    IYoutubeDownloader Downloader { get; }

    IAuthenticationService Auth { get; }

    IYoutubeUrlParser YoutubeUrl { get; }

    YoutubeClientOptions Options { get; }

    /// <summary>Validates options, restores the session and stores consent/visitor cookies.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    Task<DownloadResult> DownloadAsync(
        string videoId,
        string path,
        int? maxHeight = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    Task<DownloadResult> DownloadAudioAsync(
        string videoId,
        string path,
        string? audioTrackId = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Raw InnerTube call for new or non-standard endpoints.</summary>
    Task<JsonElement> RawAsync(string endpoint, JsonObject payload, CancellationToken ct = default);
}
