using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.Internal;
using YoutubeCulvert.Url;

namespace YoutubeCulvert.Services;

/// <summary>Video details, streams, audio tracks, chapters, related videos and captions.</summary>
public sealed class VideosService : IVideosService
{
    private readonly PlayerEndpoint _player;
    private readonly NextEndpoint _next;
    private readonly VideoParser _videoParser;
    private readonly StreamManifestParser _manifestParser;
    private readonly CaptionTrackParser _captionParser;
    private readonly ChapterParser _chapterParser;
    private readonly IYoutubeUrlParser _urls;
    private readonly YoutubeClientOptions _options;

    internal VideosService(
        PlayerEndpoint player,
        NextEndpoint next,
        VideoParser videoParser,
        StreamManifestParser manifestParser,
        CaptionTrackParser captionParser,
        ChapterParser chapterParser,
        IYoutubeUrlParser urls,
        YoutubeClientOptions options)
    {
        _player = player;
        _next = next;
        _videoParser = videoParser;
        _manifestParser = manifestParser;
        _captionParser = captionParser;
        _chapterParser = chapterParser;
        _urls = urls;
        _options = options;
    }

    public async Task<Video> GetAsync(string videoId, CancellationToken ct = default)
    {
        var id = _urls.ParseVideoIdOrThrow(videoId);

        var result = await _player
            .ExecuteAsync(new PlayerRequest(id, CacheKeys.Video(id, _options.Language, _options.Region)), ct)
            .ConfigureAwait(false);

        var parsed = _videoParser.Parse(new PlayerParseInput(result.Raw, id));

        if (!parsed.IsRecognized || parsed.Items.Count == 0)
        {
            throw new YoutubeParseException(
                $"The player response of '{id}' was not recognized: {parsed.Diagnostics.Reason}");
        }

        var video = parsed.Items[0];

        if (!video.PlayabilityStatus.IsOk)
            throw new VideoUnavailableException(video.Id, video.PlayabilityStatus);

        return video;
    }

    public Task<Result<Video>> TryGetAsync(string videoId, CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetAsync(videoId, ct));

    public async Task<StreamManifest> GetStreamsAsync(string videoId, CancellationToken ct = default)
    {
        var id = _urls.ParseVideoIdOrThrow(videoId);

        var result = await _player
            .ExecuteAsync(new PlayerRequest(id, CacheKeys.Streams(id, _options.Language, _options.Region)), ct)
            .ConfigureAwait(false);

        var video = _videoParser.Parse(new PlayerParseInput(result.Raw, id));

        if (!video.IsRecognized || video.Items.Count == 0)
        {
            throw new YoutubeParseException(
                $"The player response of '{id}' was not recognized: {video.Diagnostics.Reason}");
        }

        if (!video.Items[0].PlayabilityStatus.IsOk)
            throw new VideoUnavailableException(id, video.Items[0].PlayabilityStatus);

        var parsed = _manifestParser.Parse(result.Raw);

        if (!parsed.IsRecognized || parsed.Items.Count == 0)
        {
            throw new YoutubeParseException(
                $"The stream manifest of '{id}' was not recognized: {parsed.Diagnostics.Reason}");
        }

        var manifest = parsed.Items[0];

        if (manifest.Streams.Count == 0)
            throw new YoutubeException($"YouTube returned no streams for video '{id}'.");

        return manifest;
    }

    public Task<Result<StreamManifest>> TryGetStreamsAsync(
        string videoId,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetStreamsAsync(videoId, ct));

    public async Task<IReadOnlyList<AudioTrack>> GetAudioTracksAsync(
        string videoId,
        CancellationToken ct = default)
    {
        var manifest = await GetStreamsAsync(videoId, ct).ConfigureAwait(false);
        return manifest.GetAudioTracks();
    }

    public Task<Result<IReadOnlyList<AudioTrack>>> TryGetAudioTracksAsync(
        string videoId,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetAudioTracksAsync(videoId, ct));

    public async Task<IReadOnlyList<Chapter>> GetChaptersAsync(
        string videoId,
        CancellationToken ct = default)
    {
        var id = _urls.ParseVideoIdOrThrow(videoId);

        var result = await _next
            .ExecuteAsync(new NextRequest(VideoId: id), ct)
            .ConfigureAwait(false);

        var parsed = _chapterParser.Parse(result.Raw);

        if (parsed.Items.Count > 0)
            return parsed.Items;

        // Fall back to timestamped lines of the description.
        var video = await GetAsync(id, ct).ConfigureAwait(false);
        return ChapterParser.ParseDescription(video.Description);
    }

    public Task<Result<IReadOnlyList<Chapter>>> TryGetChaptersAsync(
        string videoId,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetChaptersAsync(videoId, ct));

    public async Task<RelatedPage> GetRelatedAsync(
        string videoId,
        string? continuation = null,
        CancellationToken ct = default)
    {
        var id = _urls.ParseVideoIdOrThrow(videoId);

        var result = await _next
            .ExecuteAsync(new NextRequest(VideoId: id, Continuation: continuation), ct)
            .ConfigureAwait(false);

        var root = result.Raw;
        var secondary = root.Path(
                             "contents", "twoColumnWatchNextResults", "secondaryResults", "secondaryResults")
                         ?? root.Path("contents", "twoColumnWatchNextResults", "secondaryResults");

        var source = secondary ?? root;

        var items = VideoParser
            .ParseAllSummaries(source, 21)
            .Where(summary => !string.Equals(summary.Id, id, StringComparison.Ordinal))
            .Take(20)
            .ToArray();

        return new RelatedPage
        {
            Items = items,
            Continuation = secondary is { } secondaryElement
                ? secondaryElement.FindContinuationToken()
                : null
        };
    }

    public Task<Result<RelatedPage>> TryGetRelatedAsync(
        string videoId,
        string? continuation = null,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetRelatedAsync(videoId, continuation, ct));

    public async Task<IReadOnlyList<CaptionTrack>> GetCaptionTracksAsync(
        string videoId,
        CancellationToken ct = default)
    {
        var id = _urls.ParseVideoIdOrThrow(videoId);

        var result = await _player
            .ExecuteAsync(new PlayerRequest(id, CacheKeys.Video(id, _options.Language, _options.Region)), ct)
            .ConfigureAwait(false);

        var parsed = _captionParser.Parse(result.Raw);
        return parsed.IsRecognized ? parsed.Items : [];
    }

    public Task<Result<IReadOnlyList<CaptionTrack>>> TryGetCaptionTracksAsync(
        string videoId,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetCaptionTracksAsync(videoId, ct));
}
