using System.Collections.Concurrent;
using YoutubeCulvert.Caching;

namespace YoutubeCulvert.Caching;

/// <summary>Cache implementation that stores nothing; the default.</summary>
public sealed class NullYoutubeCache : IYoutubeCache
{
    public static NullYoutubeCache Instance { get; } = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
        Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Thread-safe in-memory cache with per-entry TTL. The default TTL policy comes
/// from <see cref="YoutubeCulvert.Configuration.CacheTtls"/> in
/// <c>CacheMiddleware</c>, not from this class.
/// </summary>
public sealed class MemoryYoutubeCache : IYoutubeCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public MemoryYoutubeCache() : this(TimeProvider.System)
    {
    }

    public MemoryYoutubeCache(TimeProvider time) => _time = time;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        if (!_entries.TryGetValue(key, out var entry))
            return Task.FromResult<T?>(default);

        if (entry.ExpiresAt <= _time.GetUtcNow())
        {
            _entries.TryRemove(key, out _);
            return Task.FromResult<T?>(default);
        }

        return Task.FromResult(entry.Value is T typed ? typed : default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        if (value is null || ttl <= TimeSpan.Zero)
        {
            _entries.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        _entries[key] = new Entry(value, _time.GetUtcNow() + ttl);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ct.ThrowIfCancellationRequested();

        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private sealed record Entry(object Value, DateTimeOffset ExpiresAt);
}
