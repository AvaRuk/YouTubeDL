using System.Runtime.CompilerServices;
using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.Internal;
using YoutubeCulvert.Protobuf;
using YoutubeCulvert.Url;

namespace YoutubeCulvert.Services;

/// <summary>Comments and replies of a video.</summary>
public sealed class CommentsService : ICommentsService
{
    private readonly NextEndpoint _next;
    private readonly CommentParser _parser;
    private readonly IYoutubeUrlParser _urls;

    internal CommentsService(NextEndpoint next, CommentParser parser, IYoutubeUrlParser urls)
    {
        _next = next;
        _parser = parser;
        _urls = urls;
    }

    public async Task<CommentPage> GetAsync(
        string videoId,
        CommentSortOrder sortOrder = CommentSortOrder.TopComments,
        CancellationToken ct = default)
    {
        var id = _urls.ParseVideoIdOrThrow(videoId);

        // The comment section has its own token; requesting it directly avoids
        // fetching the whole watch-next feed first.
        var token = CommentParams.CreateCommentsSectionToken(id, sortOrder);

        try
        {
            return await ContinueAsync(token, ct).ConfigureAwait(false);
        }
        catch (YoutubeApiException)
        {
            var fallback = await FindWatchNextTokenAsync(id, ct).ConfigureAwait(false);
            return fallback is null
                ? new CommentPage()
                : await ContinueAsync(fallback, ct).ConfigureAwait(false);
        }
    }

    public async Task<CommentPage> ContinueAsync(
        string continuation,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(continuation);

        var result = await _next
            .ExecuteAsync(new NextRequest(Continuation: continuation), ct)
            .ConfigureAwait(false);

        var parsed = _parser.Parse(result.Raw);

        if (!parsed.IsRecognized)
        {
            throw new YoutubeParseException(
                "The comments response structure was not recognized: " + parsed.Diagnostics.Reason);
        }

        return new CommentPage
        {
            Items = parsed.Items,
            Continuation = parsed.Continuation
        };
    }

    public Task<CommentPage> GetRepliesAsync(Comment comment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(comment);

        if (string.IsNullOrWhiteSpace(comment.RepliesContinuation))
        {
            throw new YoutubeException(
                $"Comment '{comment.Id}' has no reply continuation token; it likely has no replies.");
        }

        return GetRepliesAsync(comment.RepliesContinuation, ct);
    }

    public Task<CommentPage> GetRepliesAsync(string continuation, CancellationToken ct = default) =>
        ContinueAsync(continuation, ct);

    public Task<Result<CommentPage>> TryGetAsync(
        string videoId,
        CommentSortOrder sortOrder = CommentSortOrder.TopComments,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetAsync(videoId, sortOrder, ct));

    public Task<Result<CommentPage>> TryGetRepliesAsync(Comment comment, CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetRepliesAsync(comment, ct));

    public async IAsyncEnumerable<Comment> EnumerateAsync(
        string target,
        PaginationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var maxItems = Paging.MaxItems(options);
        var maxPages = Paging.MaxPages(options);
        var yielded = 0;

        var page = await GetAsync(target, CommentSortOrder.TopComments, ct).ConfigureAwait(false);

        for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            foreach (var comment in page.Items)
            {
                if (yielded >= maxItems)
                    yield break;

                yielded++;
                yield return comment;
            }

            if (page.Continuation is null || yielded >= maxItems)
                yield break;

            page = await ContinueAsync(page.Continuation, ct).ConfigureAwait(false);
        }
    }

    private async Task<string?> FindWatchNextTokenAsync(string videoId, CancellationToken ct)
    {
        var watchNext = await _next
            .ExecuteAsync(new NextRequest(VideoId: videoId), ct)
            .ConfigureAwait(false);

        return CommentParser.FindCommentsContinuation(watchNext.Raw);
    }
}
