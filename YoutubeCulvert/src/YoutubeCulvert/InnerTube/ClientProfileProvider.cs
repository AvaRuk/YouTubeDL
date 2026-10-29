using YoutubeCulvert.Configuration;

namespace YoutubeCulvert.InnerTube;

/// <summary>
/// Default profile selection: an explicit profile wins; playback uses VISIONOS
/// (direct URLs without a PO token); account mutations may use the OAuth-capable
/// TVHTML5 client; all other reads use WEB (with SAPISID cookies when present).
/// </summary>
public sealed class AutoClientProfileProvider : IClientProfileProvider
{
    public InnerTubeClientProfile GetFor(InnerTubeRequest request, AuthContext auth)
    {
        if (request.Profile is { } explicitProfile)
            return explicitProfile;

        // Playback goes through VISIONOS even without a session: it returns
        // direct URLs and does not require a PO token. WEB would answer
        // UNPLAYABLE for anonymous player calls.
        if (string.Equals(request.Endpoint, "player", StringComparison.OrdinalIgnoreCase))
            return InnerTubeClientProfile.VisionOs;

        // Account mutations are the only place that needs the OAuth-capable TV
        // client. TVHTML5 rejects this library's browse payloads with HTTP 400,
        // so reads must stay on WEB even when a bearer token is stored.
        if (request.Endpoint.StartsWith("account", StringComparison.OrdinalIgnoreCase) &&
            auth.HasBearer)
        {
            return InnerTubeClientProfile.TvHtml5;
        }

        return InnerTubeClientProfile.Web;
    }
}
