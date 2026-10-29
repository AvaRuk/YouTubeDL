using YoutubeCulvert.Handle;
using YoutubeCulvert.Http;
using YoutubeCulvert.Url;

namespace YoutubeCulvert.Services;

/// <summary>HTML pages and the JSON embedded in them.</summary>
public sealed class PageService : IPageService
{
    private readonly IHttpTransport _http;
    private readonly IYoutubeUrlParser _urls;

    internal PageService(IHttpTransport http, IYoutubeUrlParser urls)
    {
        _http = http;
        _urls = urls;
    }

    public Task<string> GetHtmlAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !YoutubeUrlParser.IsYoutubeHost(uri.Host))
        {
            throw new ArgumentException("A youtube.com URL is expected.", nameof(url));
        }

        return _http.GetPageHtmlAsync(uri.ToString(), ct);
    }

    public async Task<JsonElement?> GetInitialPlayerResponseAsync(
        string videoId,
        CancellationToken ct = default)
    {
        var id = _urls.ParseVideoIdOrThrow(videoId);
        var html = await _http.GetPageHtmlAsync(YoutubeUrl.WatchUrl(id), ct).ConfigureAwait(false);

        return HtmlJsonExtractor.ExtractPlayerResponse(html);
    }

    public async Task<JsonElement?> GetInitialDataAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !YoutubeUrlParser.IsYoutubeHost(uri.Host))
        {
            throw new ArgumentException("A youtube.com URL is expected.", nameof(url));
        }

        var html = await _http.GetPageHtmlAsync(uri.ToString(), ct).ConfigureAwait(false);
        return HtmlJsonExtractor.ExtractInitialData(html);
    }
}
