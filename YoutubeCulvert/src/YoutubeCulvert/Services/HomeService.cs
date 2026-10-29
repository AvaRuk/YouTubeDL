using YoutubeCulvert.InnerTube.Endpoints;

namespace YoutubeCulvert.Services;

/// <summary>Trending and the home feed.</summary>
public sealed class HomeService : IHomeService
{
    private const string TrendingBrowseId = "FEtrending";
    private const string HomeBrowseId = "FEwhat_to_watch";

    private readonly BrowseEndpoint _browse;
    private readonly FeedParser _parser;

    internal HomeService(BrowseEndpoint browse, FeedParser parser)
    {
        _browse = browse;
        _parser = parser;
    }

    public async Task<IReadOnlyList<TrendingItem>> GetTrendingAsync(
        TrendingTab tab = TrendingTab.Now,
        int maxResults = 30,
        CancellationToken ct = default)
    {
        if (maxResults <= 0)
            maxResults = 30;

        var result = await _browse
            .ExecuteAsync(new BrowseRequest(TrendingBrowseId, GetTrendingParameters(tab)), ct)
            .ConfigureAwait(false);

        var parsed = _parser.Parse(result.Raw);

        if (!parsed.IsRecognized)
        {
            throw new YoutubeParseException(
                "The trending response structure was not recognized: " + parsed.Diagnostics.Reason);
        }

        return parsed.Items
            .Take(maxResults)
            .Select((video, index) => new TrendingItem
            {
                Video = video,
                Rank = index + 1,
                Category = tab.ToString()
            })
            .ToArray();
    }

    public Task<Result<IReadOnlyList<TrendingItem>>> TryGetTrendingAsync(
        TrendingTab tab = TrendingTab.Now,
        int maxResults = 30,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetTrendingAsync(tab, maxResults, ct));

    public async Task<FeedPage> GetHomeAsync(
        int maxResults = 20,
        string? continuation = null,
        CancellationToken ct = default)
    {
        if (maxResults <= 0)
            maxResults = 20;

        var result = await _browse
            .ExecuteAsync(new BrowseRequest(HomeBrowseId, Continuation: continuation), ct)
            .ConfigureAwait(false);

        var parsed = _parser.Parse(result.Raw);

        if (!parsed.IsRecognized)
        {
            throw new YoutubeParseException(
                "The home-feed response structure was not recognized: " + parsed.Diagnostics.Reason);
        }

        return new FeedPage
        {
            Items = parsed.Items
                .Take(maxResults)
                .Select(static video => new FeedItem { Video = video })
                .ToArray(),
            Continuation = parsed.Continuation
        };
    }

    public Task<Result<FeedPage>> TryGetHomeAsync(
        int maxResults = 20,
        string? continuation = null,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => GetHomeAsync(maxResults, continuation, ct));

    private static string? GetTrendingParameters(TrendingTab tab) => tab switch
    {
        TrendingTab.Music => "4gINGgt5dG1hX2NoYXJ0cw==",
        TrendingTab.Gaming => "4gIcGhpnYW1pbmdfY29ycHVzX21vc3RfcG9wdWxhcg==",
        TrendingTab.Movies => "4gIKGgh0cmFpbGVycw==",
        _ => null
    };
}
