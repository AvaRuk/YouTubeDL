using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using YoutubeCulvert.Authentication;
using YoutubeCulvert.Configuration;
using YoutubeCulvert.Http;

namespace YoutubeCulvert.InnerTube;

/// <summary>
/// Default <see cref="IInnerTubeTransport"/>: builds <c>context.client</c>, runs
/// the middleware chain, signs the request and parses the JSON response.
/// </summary>
public sealed class InnerTubeTransport : IInnerTubeTransport
{
    private readonly HttpTransport _http;
    private readonly AuthStateManager _auth;
    private readonly YoutubeClientOptions _options;
    private readonly IClientProfileProvider _profiles;
    private readonly IRequestSigner _signer;
    private readonly InnerTubeHandler _pipeline;

    internal InnerTubeTransport(
        HttpTransport http,
        AuthStateManager auth,
        YoutubeClientOptions options,
        IClientProfileProvider profiles,
        IRequestSigner signer,
        IReadOnlyList<IInnerTubeMiddleware> middlewares)
    {
        _http = http;
        _auth = auth;
        _options = options;
        _profiles = profiles;
        _signer = signer;

        var handler = (InnerTubeHandler)SendCoreAsync;

        for (var index = middlewares.Count - 1; index >= 0; index--)
        {
            var middleware = middlewares[index];
            var next = handler;
            handler = (request, ct) => middleware.InvokeAsync(request, next, ct);
        }

        _pipeline = handler;
    }

    public Task<JsonElement> SendAsync(InnerTubeRequest request, CancellationToken ct = default) =>
        _pipeline(request, ct);

    private async Task<JsonElement> SendCoreAsync(InnerTubeRequest request, CancellationToken ct)
    {
        await _http.InitializeAsync(ct).ConfigureAwait(false);

        var auth = _auth.GetAuthContext();
        var profile = _profiles.GetFor(request, auth);
        var signedRequest = request with { Profile = profile };

        var body = new JsonObject
        {
            ["context"] = new JsonObject
            {
                ["client"] = profile.CreateClientContext(
                    _options.Language,
                    _options.Region,
                    _options.SendVisitorData ? _http.VisitorData : null)
            }
        };

        foreach (var pair in request.Payload)
            body[pair.Key] = pair.Value?.DeepClone();

        var url = _http.CreateInnerTubeUrl(request.Endpoint, profile);
        var payload = Encoding.UTF8.GetBytes(body.ToJsonString());

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(payload)
        };

        message.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        _http.ApplyInnerTubeHeaders(message, profile);
        await _signer.SignAsync(signedRequest, message, ct).ConfigureAwait(false);

        var started = Stopwatch.GetTimestamp();

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);

        _http.CaptureVisitorData(response);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            throw new YoutubeApiException(
                response.StatusCode,
                request.Endpoint,
                HttpTransport.Truncate(errorBody),
                $"InnerTube/{request.Endpoint} returned {(int)response.StatusCode}.",
                ReadRetryAfter(response));
        }

        try
        {
            // The body is streamed into JsonDocument instead of being buffered twice.
            await using var stream = await response.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);

            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new YoutubeParseException(
                $"Failed to parse the InnerTube/{request.Endpoint} JSON response " +
                $"(elapsed {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms).",
                exception);
        }
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : null;
        }

        return null;
    }
}
