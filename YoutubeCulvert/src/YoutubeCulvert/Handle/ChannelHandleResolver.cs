using YoutubeCulvert.Caching;
using YoutubeCulvert.Configuration;
using YoutubeCulvert.Http;
using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.Internal;
using YoutubeCulvert.Url;

namespace YoutubeCulvert.Handle;

/// <summary>
/// Resolves <c>@handle</c> / URL / <c>UC…</c> to a canonical channel id, with
/// results cached for <see cref="CacheTtls.ChannelResolve"/>.
/// </summary>
internal sealed class ChannelHandleResolver
{
    private readonly ResolveUrlEndpoint _resolve;
    private readonly HttpTransport _http;
    private readonly IYoutubeCache _cache;
    private readonly CacheTtls _ttls;
    private readonly YoutubeClientOptions _options;

    public ChannelHandleResolver(
        ResolveUrlEndpoint resolve,
        HttpTransport http,
        IYoutubeCache cache,
        CacheTtls ttls,
        YoutubeClientOptions options)
    {
        _resolve = resolve;
        _http = http;
        _cache = cache;
        _ttls = ttls;
        _options = options;
    }

    public async Task<string> ResolveAsync(ChannelRef channel, CancellationToken ct = default)
    {
        if (channel is ChannelRef.ById byId)
            return byId.ChannelId;

        var cacheKey = "channel-resolve:" + GetCacheToken(channel);
        var cached = await _cache.GetAsync<string>(cacheKey, ct).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(cached))
            return cached;

        var url = BuildChannelUrl(channel);
        string? channelId = null;

        try
        {
            var resolved = await _resolve
                .ExecuteAsync(new ResolveUrlRequest(url), ct)
                .ConfigureAwait(false);

            channelId = resolved.Raw.FindChannelId();
        }
        catch (YoutubeException)
        {
            // resolve_url is sometimes unavailable; fall back to the HTML page.
        }

        if (channelId is null)
        {
            var html = await _http.GetPageHtmlAsync(url, ct).ConfigureAwait(false);
            channelId = HtmlJsonExtractor.ExtractChannelId(html);
        }

        if (channelId is null)
        {
            throw new ArgumentException(
                $"Could not determine a channel identifier from '{url}'.",
                nameof(channel));
        }

        await _cache.SetAsync(cacheKey, channelId, _ttls.ChannelResolve, ct).ConfigureAwait(false);
        return channelId;
    }

    public string BuildChannelUrl(ChannelRef channel) => channel switch
    {
        ChannelRef.ById byId => _options.NormalizedBaseUrl + "/channel/" + byId.ChannelId,
        ChannelRef.ByHandle byHandle => _options.NormalizedBaseUrl + "/@" + byHandle.Handle.TrimStart('@'),
        ChannelRef.ByUsername byUsername => _options.NormalizedBaseUrl + "/user/" + byUsername.Username,
        ChannelRef.ByUrl byUrl => byUrl.Url,
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    private static string GetCacheToken(ChannelRef channel) => channel switch
    {
        ChannelRef.ById byId => byId.ChannelId,
        ChannelRef.ByHandle byHandle => "@" + byHandle.Handle.TrimStart('@'),
        ChannelRef.ByUsername byUsername => "user:" + byUsername.Username,
        ChannelRef.ByUrl byUrl => byUrl.Url,
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };
}
