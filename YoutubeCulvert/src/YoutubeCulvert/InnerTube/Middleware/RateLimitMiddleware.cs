using System.Collections.Concurrent;
using System.Diagnostics;

namespace YoutubeCulvert.InnerTube.Middleware;

/// <summary>
/// Token bucket per (endpoint, auth mode). Transcript endpoints get a much
/// smaller bucket because Google limits <c>tlang</c> requests aggressively.
/// </summary>
public sealed class RateLimitMiddleware : IInnerTubeMiddleware
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new(StringComparer.Ordinal);

    public Task<JsonElement> InvokeAsync(
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct)
    {
        var key = request.Endpoint + "|" + request.AuthMode;
        var bucket = _buckets.GetOrAdd(key, _ => CreateBucket(request.Endpoint));

        return WaitAndSendAsync(bucket, request, next, ct);
    }

    private static async Task<JsonElement> WaitAndSendAsync(
        TokenBucket bucket,
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct)
    {
        await bucket.WaitAsync(ct).ConfigureAwait(false);
        return await next(request, ct).ConfigureAwait(false);
    }

    private static TokenBucket CreateBucket(string endpoint) =>
        endpoint.Contains("transcript", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Contains("timedtext", StringComparison.OrdinalIgnoreCase)
            ? new TokenBucket(capacity: 1, refillPerSecond: 0.2)
            : new TokenBucket(capacity: 10, refillPerSecond: 5);

    private sealed class TokenBucket
    {
        private readonly SemaphoreSlim _mutex = new(1, 1);
        private readonly double _capacity;
        private readonly double _refillPerSecond;
        private readonly long _started = Stopwatch.GetTimestamp();

        private double _tokens;
        private TimeSpan _lastRefill;

        public TokenBucket(double capacity, double refillPerSecond)
        {
            _capacity = capacity;
            _refillPerSecond = refillPerSecond;
            _tokens = capacity;
        }

        public async Task WaitAsync(CancellationToken ct)
        {
            while (true)
            {
                TimeSpan wait;

                await _mutex.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    Refill();

                    if (_tokens >= 1)
                    {
                        _tokens -= 1;
                        return;
                    }

                    wait = TimeSpan.FromSeconds((1 - _tokens) / _refillPerSecond);
                }
                finally
                {
                    _mutex.Release();
                }

                await Task.Delay(wait, ct).ConfigureAwait(false);
            }
        }

        private void Refill()
        {
            var now = Stopwatch.GetElapsedTime(_started);
            var elapsed = now - _lastRefill;

            if (elapsed <= TimeSpan.Zero)
                return;

            _tokens = Math.Min(_capacity, _tokens + elapsed.TotalSeconds * _refillPerSecond);
            _lastRefill = now;
        }
    }
}
