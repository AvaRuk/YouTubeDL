using YoutubeCulvert.Logging;

namespace YoutubeCulvert.InnerTube.Middleware;

/// <summary>
/// Retries idempotent requests with exponential backoff and jitter. Mutations
/// (<see cref="InnerTubeRequest.IsMutation"/>) are never retried.
/// </summary>
public sealed class RetryMiddleware : IInnerTubeMiddleware
{
    private readonly YoutubeClientOptions _options;
    private readonly IYoutubeLogger _logger;

    public RetryMiddleware(YoutubeClientOptions options, IYoutubeLogger? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<JsonElement> InvokeAsync(
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await next(request, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                !request.IsMutation &&
                attempt < _options.MaxRetries &&
                IsRetryable(exception, ct))
            {
                var delay = ComputeDelay(attempt, exception);

                _logger.Log(
                    YoutubeLogLevel.Warning,
                    "retry",
                    $"Retrying InnerTube {request.Endpoint} after {delay.TotalMilliseconds:F0} ms " +
                    $"(attempt {attempt + 1}/{_options.MaxRetries}).",
                    fields: new Dictionary<string, object?>
                    {
                        ["endpoint"] = request.Endpoint,
                        ["attempt"] = attempt + 1,
                        ["delayMs"] = (long)delay.TotalMilliseconds,
                        ["status"] = (exception as YoutubeApiException)?.StatusCodeValue
                    });

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetryable(Exception exception, CancellationToken ct) => exception switch
    {
        YoutubeApiException api =>
            (int)api.StatusCode == 408 ||
            (int)api.StatusCode == 429 ||
            (int)api.StatusCode >= 500,

        HttpRequestException => true,
        IOException => true,

        // HttpClient.Timeout surfaces as a cancellation of its own token.
        OperationCanceledException => !ct.IsCancellationRequested,

        _ => false
    };

    private TimeSpan ComputeDelay(int attempt, Exception exception)
    {
        var exponent = Math.Min(attempt, 16);
        var ticks = _options.RetryDelay.Ticks * Math.Pow(2, exponent);
        var capped = Math.Min(ticks, _options.MaxDelay.Ticks);

        var jitter = 0.5 + Random.Shared.NextDouble() * 0.5;
        var delay = TimeSpan.FromTicks((long)(capped * jitter));

        if (exception is YoutubeApiException { RetryAfter: { } retryAfter } && retryAfter > delay)
            delay = retryAfter;

        return delay;
    }
}
