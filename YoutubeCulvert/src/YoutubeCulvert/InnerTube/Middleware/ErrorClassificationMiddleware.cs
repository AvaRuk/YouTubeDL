using YoutubeCulvert.Internal;

namespace YoutubeCulvert.InnerTube.Middleware;

/// <summary>
/// Converts HTTP status failures into the specific library exceptions:
/// 401 → <see cref="YoutubeAuthenticationException"/>, 403 →
/// <see cref="YoutubeAuthenticationRequiredException"/>. 429 and 5xx are left
/// untouched so <see cref="RetryMiddleware"/> can retry them.
/// </summary>
public sealed class ErrorClassificationMiddleware : IInnerTubeMiddleware
{
    public async Task<JsonElement> InvokeAsync(
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct)
    {
        try
        {
            return await next(request, ct).ConfigureAwait(false);
        }
        catch (YoutubeApiException exception)
        {
            var classified = ErrorClassifier.Classify(exception);

            if (ReferenceEquals(classified, exception))
                throw;

            throw classified;
        }
    }
}
