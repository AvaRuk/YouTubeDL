using System.Runtime.CompilerServices;
using YoutubeCulvert.Http;
using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.Internal;
using YoutubeCulvert.Protobuf;

namespace YoutubeCulvert.Services;

/// <summary>Search, search continuation and suggestions.</summary>
public sealed class SearchService : ISearchService
{
    private const string SuggestionsEndpoint =
        "https://suggestqueries-clients6.youtube.com/complete/search";

    private readonly SearchEndpoint _endpoint;
    private readonly SearchParser _parser;
    private readonly YoutubeClientOptions _options;
    private readonly IHttpTransport _http;

    internal SearchService(
        SearchEndpoint endpoint,
        SearchParser parser,
        YoutubeClientOptions options,
        IHttpTransport http)
    {
        _endpoint = endpoint;
        _parser = parser;
        _options = options;
        _http = http;
    }

    public async Task<SearchPage> SearchAsync(
        string query,
        SearchFilter? filter = null,
        SortOrder sortOrder = SortOrder.Relevance,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        filter ??= new SearchFilter();
        var parameters = SearchParamsBuilder.Build(filter, sortOrder);

        return await FetchAsync(query, parameters, null, filter.Type, maxResults, ct).ConfigureAwait(false);
    }

    public async Task<SearchPage> ContinueAsync(
        string continuation,
        SearchFilter? filter = null,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(continuation);
        filter ??= new SearchFilter();

        return await FetchAsync(null, null, continuation, filter.Type, maxResults, ct).ConfigureAwait(false);
    }

    public Task<Result<SearchPage>> TrySearchAsync(
        string query,
        SearchFilter? filter = null,
        SortOrder sortOrder = SortOrder.Relevance,
        int maxResults = 20,
        CancellationToken ct = default) =>
        ServiceErrors.RunAsync(() => SearchAsync(query, filter, sortOrder, maxResults, ct));

    public async IAsyncEnumerable<SearchResult> EnumerateAsync(
        string target,
        PaginationOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var maxItems = Paging.MaxItems(options);
        var maxPages = Paging.MaxPages(options);
        var yielded = 0;

        var page = await SearchAsync(target, null, SortOrder.Relevance, 20, ct).ConfigureAwait(false);

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

            page = await ContinueAsync(page.Continuation, null, 20, ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<string>> GetSuggestionsAsync(
        string query,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var url = $"{SuggestionsEndpoint}?client=youtube" +
                  $"&hl={Uri.EscapeDataString(_options.Language)}" +
                  $"&gl={Uri.EscapeDataString(_options.Region)}" +
                  $"&gs_ri=youtube&ds=yt&q={Uri.EscapeDataString(query)}";

        var body = await _http.GetTextAsync(url, "application/json", ct).ConfigureAwait(false);
        return SearchParser.ParseSuggestions(body);
    }

    private async Task<SearchPage> FetchAsync(
        string? query,
        string? parameters,
        string? continuation,
        SearchType type,
        int maxResults,
        CancellationToken ct)
    {
        if (maxResults <= 0)
            maxResults = 20;

        var result = await _endpoint
            .ExecuteAsync(new SearchRequest(query, parameters, continuation), ct)
            .ConfigureAwait(false);

        var parsed = _parser.Parse(new SearchParseInput(result.Raw, type));

        if (!parsed.IsRecognized)
        {
            throw new YoutubeParseException(
                "The search response structure was not recognized: " +
                parsed.Diagnostics.Reason);
        }

        var items = parsed.Items.Count > maxResults
            ? parsed.Items.Take(maxResults).ToArray()
            : parsed.Items;

        return new SearchPage
        {
            Items = items,
            Continuation = parsed.Continuation,
            EstimatedResults = SearchParser.ParseEstimatedResults(result.Raw)
        };
    }
}
