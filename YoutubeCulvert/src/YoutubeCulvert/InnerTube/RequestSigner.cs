using YoutubeCulvert.Authentication;

namespace YoutubeCulvert.InnerTube;

/// <summary>Applies cookie (SAPISIDHASH) or bearer authorization to a request.</summary>
public sealed class RequestSigner : IRequestSigner
{
    private const string DefaultOrigin = "https://www.youtube.com";

    private readonly AuthStateManager _auth;
    private readonly string _origin;

    public RequestSigner(AuthStateManager auth, string? origin = null)
    {
        _auth = auth;
        _origin = string.IsNullOrWhiteSpace(origin) ? DefaultOrigin : origin.TrimEnd('/');
    }

    public Task SignAsync(
        InnerTubeRequest request,
        HttpRequestMessage http,
        CancellationToken ct = default)
    {
        var sapisid = _auth.FindSapisid();
        var bearer = _auth.Current?.AccessToken;

        var mode = request.AuthMode;
        if (mode == AuthMode.Auto)
        {
            var profileSupportsOAuth = request.Profile?.SupportsOAuth ?? true;

            if (request.Profile?.SupportsOAuth == true && !string.IsNullOrEmpty(bearer) && sapisid is null)
                mode = AuthMode.Bearer;
            else if (sapisid is not null)
                mode = AuthMode.Cookie;
            else if (!string.IsNullOrEmpty(bearer) && profileSupportsOAuth)
                mode = AuthMode.Bearer;
            else
                mode = AuthMode.Anonymous;
        }

        switch (mode)
        {
            case AuthMode.Cookie when sapisid is not null:
                http.Headers.TryAddWithoutValidation(
                    "Authorization",
                    SapisidHash.CreateAuthorizationHeader(sapisid, _origin));
                http.Headers.TryAddWithoutValidation("X-Goog-AuthUser", "0");
                http.Headers.TryAddWithoutValidation("X-Origin", _origin);
                break;

            case AuthMode.Bearer when !string.IsNullOrEmpty(bearer):
                http.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
                http.Headers.TryAddWithoutValidation("X-Goog-AuthUser", "0");
                break;
        }

        return Task.CompletedTask;
    }
}
