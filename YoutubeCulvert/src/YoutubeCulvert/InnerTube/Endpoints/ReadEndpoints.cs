using System.Diagnostics;

namespace YoutubeCulvert.InnerTube.Endpoints;

/// <summary>Request of <c>player</c>.</summary>
public sealed record PlayerRequest(string VideoId, string? CacheKey = null);

/// <summary>Request of <c>browse</c> (channel, playlist, feed, home).</summary>
public sealed record BrowseRequest(
    string? BrowseId = null,
    string? Params = null,
    string? Continuation = null,
    string? CacheKey = null);

/// <summary>Request of <c>search</c>.</summary>
public sealed record SearchRequest(
    string? Query = null,
    string? Parameters = null,
    string? Continuation = null,
    string? CacheKey = null);

/// <summary>Request of <c>next</c> (watch next, comments, transcript panel).</summary>
public sealed record NextRequest(
    string? VideoId = null,
    string? Continuation = null,
    string? CacheKey = null);

/// <summary>Request of <c>guide</c>.</summary>
public sealed record GuideRequest(string? BrowseId = null, string? Params = null);

/// <summary>Base class of the built-in endpoints.</summary>
public abstract class InnerTubeEndpointBase
{
    private readonly IInnerTubeTransport _transport;

    protected InnerTubeEndpointBase(IInnerTubeTransport transport) => _transport = transport;

    protected async Task<EndpointResult<JsonElement>> SendAsync(
        string endpoint,
        JsonObject payload,
        string? cacheKey,
        bool isMutation,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        var raw = await _transport
            .SendAsync(
                new InnerTubeRequest(
                    endpoint,
                    payload,
                    CacheKey: cacheKey,
                    IsMutation: isMutation),
                ct)
            .ConfigureAwait(false);

        return new EndpointResult<JsonElement>(
            raw,
            FromCache: false,
            Stopwatch.GetElapsedTime(started));
    }
}

/// <summary><c>youtubei/v1/player</c>: video details, streams, captions, playability.</summary>
public sealed class PlayerEndpoint : InnerTubeEndpointBase, IInnerTubeEndpoint<PlayerRequest, JsonElement>
{
    public PlayerEndpoint(IInnerTubeTransport transport) : base(transport)
    {
    }

    public string Path => "player";

    public Task<EndpointResult<JsonElement>> ExecuteAsync(PlayerRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VideoId);

        var payload = new JsonObject
        {
            ["videoId"] = request.VideoId,
            ["contentCheckOk"] = true,
            ["racyCheckOk"] = true
        };

        return SendAsync(Path, payload, request.CacheKey, isMutation: false, ct);
    }
}

/// <summary><c>youtubei/v1/browse</c>: channels, playlists, feeds, home.</summary>
public sealed class BrowseEndpoint : InnerTubeEndpointBase, IInnerTubeEndpoint<BrowseRequest, JsonElement>
{
    public BrowseEndpoint(IInnerTubeTransport transport) : base(transport)
    {
    }

    public string Path => "browse";

    public Task<EndpointResult<JsonElement>> ExecuteAsync(BrowseRequest request, CancellationToken ct = default)
    {
        var payload = new JsonObject();

        if (!string.IsNullOrWhiteSpace(request.Continuation))
        {
            payload["continuation"] = request.Continuation;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.BrowseId))
                payload["browseId"] = request.BrowseId;

            if (!string.IsNullOrWhiteSpace(request.Params))
                payload["params"] = request.Params;
        }

        return SendAsync(Path, payload, request.CacheKey, isMutation: false, ct);
    }
}

/// <summary><c>youtubei/v1/search</c>: search and continuation.</summary>
public sealed class SearchEndpoint : InnerTubeEndpointBase, IInnerTubeEndpoint<SearchRequest, JsonElement>
{
    public SearchEndpoint(IInnerTubeTransport transport) : base(transport)
    {
    }

    public string Path => "search";

    public Task<EndpointResult<JsonElement>> ExecuteAsync(SearchRequest request, CancellationToken ct = default)
    {
        var payload = new JsonObject();

        if (!string.IsNullOrWhiteSpace(request.Continuation))
        {
            payload["continuation"] = request.Continuation;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.Query))
                payload["query"] = request.Query;

            if (!string.IsNullOrWhiteSpace(request.Parameters))
                payload["params"] = request.Parameters;
        }

        return SendAsync(Path, payload, request.CacheKey, isMutation: false, ct);
    }
}

/// <summary><c>youtubei/v1/next</c>: related videos, comments, transcript panel.</summary>
public sealed class NextEndpoint : InnerTubeEndpointBase, IInnerTubeEndpoint<NextRequest, JsonElement>
{
    public NextEndpoint(IInnerTubeTransport transport) : base(transport)
    {
    }

    public string Path => "next";

    public Task<EndpointResult<JsonElement>> ExecuteAsync(NextRequest request, CancellationToken ct = default)
    {
        var payload = new JsonObject();

        if (!string.IsNullOrWhiteSpace(request.Continuation))
            payload["continuation"] = request.Continuation;
        else if (!string.IsNullOrWhiteSpace(request.VideoId))
            payload["videoId"] = request.VideoId;

        return SendAsync(Path, payload, request.CacheKey, isMutation: false, ct);
    }
}

/// <summary>Request of <c>navigation/resolve_url</c>.</summary>
public sealed record ResolveUrlRequest(string Url);

/// <summary><c>youtubei/v1/guide</c>: guide feed.</summary>
public sealed class GuideEndpoint : InnerTubeEndpointBase, IInnerTubeEndpoint<GuideRequest, JsonElement>
{
    public GuideEndpoint(IInnerTubeTransport transport) : base(transport)
    {
    }

    public string Path => "guide";

    public Task<EndpointResult<JsonElement>> ExecuteAsync(GuideRequest request, CancellationToken ct = default)
    {
        var payload = new JsonObject();

        if (!string.IsNullOrWhiteSpace(request.BrowseId))
            payload["browseId"] = request.BrowseId;

        if (!string.IsNullOrWhiteSpace(request.Params))
            payload["params"] = request.Params;

        return SendAsync(Path, payload, cacheKey: null, isMutation: false, ct);
    }
}

/// <summary><c>youtubei/v1/navigation/resolve_url</c>: resolves handles and short links.</summary>
public sealed class ResolveUrlEndpoint : InnerTubeEndpointBase, IInnerTubeEndpoint<ResolveUrlRequest, JsonElement>
{
    public ResolveUrlEndpoint(IInnerTubeTransport transport) : base(transport)
    {
    }

    public string Path => "navigation/resolve_url";

    public Task<EndpointResult<JsonElement>> ExecuteAsync(
        ResolveUrlRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Url);

        var payload = new JsonObject { ["url"] = request.Url };
        return SendAsync(Path, payload, cacheKey: null, isMutation: false, ct);
    }
}
