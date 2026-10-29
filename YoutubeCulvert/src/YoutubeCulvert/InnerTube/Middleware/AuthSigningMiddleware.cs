using YoutubeCulvert.Authentication;

namespace YoutubeCulvert.InnerTube.Middleware;

/// <summary>
/// Resolves <see cref="AuthMode.Auto"/> to the mechanism the current session
/// supports, so later steps (profile selection, signing) see a concrete mode.
/// </summary>
public sealed class AuthSigningMiddleware : IInnerTubeMiddleware
{
    private readonly AuthStateManager _auth;

    public AuthSigningMiddleware(AuthStateManager auth) => _auth = auth;

    public Task<JsonElement> InvokeAsync(
        InnerTubeRequest request,
        InnerTubeHandler next,
        CancellationToken ct)
    {
        if (request.AuthMode != AuthMode.Auto)
            return next(request, ct);

        var context = _auth.GetAuthContext();

        // A cookie session is unambiguous. A bearer token must be resolved by
        // the client profile: VISIONOS player calls do not accept OAuth, while
        // TVHTML5 does. Leaving Auto lets the profile decide.
        var mode = context.HasSapisid ? AuthMode.Cookie : AuthMode.Auto;

        return next(request with { AuthMode = mode }, ct);
    }
}
