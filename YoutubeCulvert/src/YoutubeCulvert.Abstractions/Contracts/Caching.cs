namespace YoutubeCulvert.Caching;

/// <summary>Cache of InnerTube responses. The default implementation caches nothing.</summary>
public interface IYoutubeCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);
}
