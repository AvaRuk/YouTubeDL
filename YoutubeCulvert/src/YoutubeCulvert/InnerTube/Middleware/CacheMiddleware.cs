using YoutubeCulvert.Caching;
using YoutubeCulvert.Configuration;
using YoutubeCulvert.Internal;
using YoutubeCulvert.Logging;

namespace YoutubeCulvert.InnerTube.Middleware;

/// <summary>
/// Caches complete InnerTube responses by <see cref="InnerTubeRequest.CacheKey"/>.
/// Unavailable videos are cached for <see cref="CacheTtls.Negative"/> so they
/// are not requested again on every call.
/// </summary>
public sealed class CacheMiddleware : IInnerTubeMiddleware
{
    private const string NegativeFlag = "__youtube_unavailable";

    private readonly IYoutubeCache _cache;
    private readonly CacheTtls _ttls;
    private readonly IYoutubeLogger _logger;

    public CacheMiddleware(IYoutubeCache? cache = null, CacheTtls? ttls = null, IYoutubeLogger? logger = null)
    {
        _cache = cache ?? NullYoutubeCache.Instance;
        _ttls = ttls ?? new CacheTtls();
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<JsonElement> InvokeAsync(
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct)
    {
        if (request.CacheKey is not { Length: > 0 } key)
            return await next(request, ct).ConfigureAwait(false);

        var cached = await _cache.GetAsync<JsonElement>(key, ct).ConfigureAwait(false);

        if (cached.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            if (TryReadNegative(cached, out var videoId, out var status, out var reason))
                throw new VideoUnavailableException(
                    videoId,
                    new PlayabilityStatus { Status = status, Reason = reason });

            _logger.Log(
                YoutubeLogLevel.Debug,
                "cache.hit",
                $"Cache hit for {key}",
                fields: new Dictionary<string, object?> { ["key"] = key });

            return cached;
        }

        _logger.Log(
            YoutubeLogLevel.Debug,
            "cache.miss",
            $"Cache miss for {key}",
            fields: new Dictionary<string, object?> { ["key"] = key });

        try
        {
            var result = await next(request, ct).ConfigureAwait(false);
            await _cache.SetAsync(key, result, ResolveTtl(key), ct).ConfigureAwait(false);
            return result;
        }
        catch (VideoUnavailableException exception)
        {
            await _cache
                .SetAsync(key, BuildNegative(exception), _ttls.Negative, ct)
                .ConfigureAwait(false);

            throw;
        }
    }

    private TimeSpan ResolveTtl(string key)
    {
        var separator = key.IndexOf(':');
        var kind = separator > 0 ? key[..separator] : key;

        return kind switch
        {
            "streams" => _ttls.Streams,
            "channel-resolve" => _ttls.ChannelResolve,
            "transcript" => _ttls.Transcripts,
            "negative" => _ttls.Negative,
            _ => _ttls.Video
        };
    }

    private static JsonElement BuildNegative(VideoUnavailableException exception)
    {
        var payload = new JsonObject
        {
            [NegativeFlag] = true,
            ["videoId"] = exception.VideoId,
            ["status"] = exception.Playability.Status,
            ["reason"] = exception.Playability.Reason
        };

        using var document = JsonDocument.Parse(payload.ToJsonString());
        return document.RootElement.Clone();
    }

    private static bool TryReadNegative(
        JsonElement element,
        out string videoId,
        out string status,
        out string? reason)
    {
        videoId = string.Empty;
        status = "UNPLAYABLE";
        reason = null;

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(NegativeFlag, out var flag) ||
            flag.ValueKind != JsonValueKind.True)
        {
            return false;
        }

        videoId = element.GetString("videoId") ?? string.Empty;
        status = element.GetString("status") ?? "UNPLAYABLE";
        reason = element.GetString("reason");
        return true;
    }
}
