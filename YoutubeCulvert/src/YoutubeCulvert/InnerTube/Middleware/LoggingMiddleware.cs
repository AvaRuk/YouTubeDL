using System.Diagnostics;
using YoutubeCulvert.Logging;

namespace YoutubeCulvert.InnerTube.Middleware;

/// <summary>Logs every InnerTube call without exposing cookies or tokens.</summary>
public sealed class LoggingMiddleware : IInnerTubeMiddleware
{
    private readonly IYoutubeLogger _logger;

    public LoggingMiddleware(IYoutubeLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    public async Task<JsonElement> InvokeAsync(
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct)
    {
        if (_logger.IsEnabled(YoutubeLogLevel.Debug))
        {
            _logger.Log(
                YoutubeLogLevel.Debug,
                "innertube.request",
                $"InnerTube {request.Endpoint}",
                fields: new Dictionary<string, object?>
                {
                    ["endpoint"] = request.Endpoint,
                    ["profile"] = request.Profile?.ClientName,
                    ["authMode"] = request.AuthMode.ToString(),
                    ["isMutation"] = request.IsMutation,
                    ["cacheKey"] = request.CacheKey
                });
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await next(request, ct).ConfigureAwait(false);

            if (_logger.IsEnabled(YoutubeLogLevel.Debug))
            {
                _logger.Log(
                    YoutubeLogLevel.Debug,
                    "innertube.response",
                    $"InnerTube {request.Endpoint} completed",
                    fields: new Dictionary<string, object?>
                    {
                        ["endpoint"] = request.Endpoint,
                        ["elapsedMs"] = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds
                    });
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log(
                YoutubeLogLevel.Error,
                "api.error",
                $"InnerTube {request.Endpoint} failed: {exception.Message}",
                exception,
                new Dictionary<string, object?>
                {
                    ["endpoint"] = request.Endpoint,
                    ["status"] = (exception as YoutubeApiException)?.StatusCodeValue,
                    ["responseBody"] = Truncate((exception as YoutubeApiException)?.ResponseBody, 512)
                });

            throw;
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
