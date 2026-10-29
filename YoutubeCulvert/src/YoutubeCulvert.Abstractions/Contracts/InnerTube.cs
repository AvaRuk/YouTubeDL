using System.Net.Http;
using YoutubeCulvert.Configuration;

namespace YoutubeCulvert.InnerTube;

/// <summary>How a request should be authorized.</summary>
public enum AuthMode
{
    /// <summary>Pick the mechanism from the current session.</summary>
    Auto,
    Anonymous,
    Cookie,
    Bearer
}

/// <summary>Authentication facts used to select a client profile.</summary>
public sealed record AuthContext(bool HasSapisid, bool HasBearer, string? AccountId);

/// <summary>
/// One InnerTube call: an endpoint, its payload (without <c>context</c>) and
/// the pipeline metadata. Payloads are built manually, so the library stays
/// reflection-free and trim/AOT friendly.
/// </summary>
public sealed record InnerTubeRequest(
    string Endpoint,
    JsonObject Payload,
    InnerTubeClientProfile? Profile = null,
    AuthMode AuthMode = AuthMode.Auto,
    bool IsMutation = false,
    string? CacheKey = null);

/// <summary>Transport for typed endpoints; owns the middleware pipeline.</summary>
public interface IInnerTubeTransport
{
    Task<JsonElement> SendAsync(InnerTubeRequest request, CancellationToken ct = default);
}

/// <summary>The next step of the middleware pipeline.</summary>
public delegate Task<JsonElement> InnerTubeHandler(InnerTubeRequest request, CancellationToken ct);

/// <summary>A step of the InnerTube pipeline.</summary>
public interface IInnerTubeMiddleware
{
    Task<JsonElement> InvokeAsync(
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct);
}

/// <summary>Where to insert a middleware into the default pipeline.</summary>
public enum MiddlewarePosition
{
    Start,
    BeforeCache,
    AfterCache,
    BeforeAuth,
    AfterAuth,
    End
}

/// <summary>A typed InnerTube endpoint.</summary>
public interface IInnerTubeEndpoint<TRequest, TResponse>
{
    /// <summary>InnerTube path, for example <c>"player"</c>.</summary>
    string Path { get; }

    Task<EndpointResult<TResponse>> ExecuteAsync(TRequest request, CancellationToken ct = default);
}

/// <summary>Raw result of an endpoint call.</summary>
public readonly record struct EndpointResult<T>(JsonElement Raw, bool FromCache, TimeSpan Elapsed);

/// <summary>Chooses the client profile (WEB / TVHTML5 / VISIONOS / ...) of a request.</summary>
public interface IClientProfileProvider
{
    InnerTubeClientProfile GetFor(InnerTubeRequest request, AuthContext auth);
}

/// <summary>Applies SAPISIDHASH or a bearer token to an outgoing HTTP request.</summary>
public interface IRequestSigner
{
    Task SignAsync(InnerTubeRequest request, HttpRequestMessage http, CancellationToken ct = default);
}
