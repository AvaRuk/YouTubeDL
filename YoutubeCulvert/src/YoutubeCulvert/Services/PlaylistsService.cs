using System.Runtime.CompilerServices;
using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.Url;

namespace YoutubeCulvert.Services;

/// <summary>Playlists: information and entries.</summary>
public sealed class PlaylistsService : IPlaylistsService
{
    private readonly BrowseEndpoint _browse;
    private readonly PlaylistParser _parser;
    private readonly PlaylistVideoParser _videoParser;
    private readonly IYoutubeUrlParser _urls;

    internal PlaylistsService(
        BrowseEndpoint browse,
        PlaylistParser parser,
        PlaylistVideoParser videoParser,
        IYoutubeUrlParser urls)
    {
        _browse = browse;
        _parser = parser;
        _videoParser = videoParser;
        _urls = urls;
    }

    public async Task<Playlist> GetAsync(string playlistId, CancellationToken ct = default)
    {
        var id = _urls.ParsePlaylistIdOrThrow(playlistId);

        var result = await _browse
            .ExecuteAsync(new BrowseRequest(GetBrowseId(id)), ct)
            .ConfigureAwait(false);

        var parsed = _parser.Parse(new PlaylistParseInput(result.Raw, id));

        if (!parsed.IsRecognized || parsed.Items.Count == 0)
        {
            throw new YoutubeParseException(
                $"The playlist page of '{id}' was not recognized: {parsed.Diagnostics.Reason}");
        }

        return parsed.Items[0];
    }

    public Task<Result<Playlist>> TryGetAsync(string playlistId, CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetAsync(playlistId, ct));

    public async Task<PlaylistVideoPage> GetVideosAsync(
        string playlistId,
        string? continuation = null,
        CancellationToken ct = default)
    {
        string? browseId = null;

        if (string.IsNullOrWhiteSpace(continuation))
        {
            var id = _urls.ParsePlaylistIdOrThrow(playlistId);
            browseId = GetBrowseId(id);
        }

        var result = await _browse
            .ExecuteAsync(new BrowseRequest(browseId, Continuation: continuation), ct)
            .ConfigureAwait(false);

        var parsed = _videoParser.Parse(result.Raw);

        if (!parsed.IsRecognized)
        {
            throw new YoutubeParseException(
                "The playlist videos response structure was not recognized: " +
                parsed.Diagnostics.Reason);
        }

        return new PlaylistVideoPage
        {
            Items = parsed.Items,
            Continuation = parsed.Continuation
        };
    }

    public Task<Result<PlaylistVideoPage>> TryGetVideosAsync(
        string playlistId,
        string? continuation = null,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetVideosAsync(playlistId, continuation, ct));

    public async IAsyncEnumerable<PlaylistVideo> EnumerateAsync(
        string target,
        PaginationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var maxItems = Paging.MaxItems(options);
        var maxPages = Paging.MaxPages(options);
        var yielded = 0;

        var page = await GetVideosAsync(target, null, ct).ConfigureAwait(false);

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

            page = await GetVideosAsync(target, page.Continuation, ct).ConfigureAwait(false);
        }
    }

    private static string GetBrowseId(string playlistId) =>
        playlistId.StartsWith("VL", StringComparison.Ordinal) ? playlistId : "VL" + playlistId;
}
