namespace YoutubeCulvert.Configuration;

/// <summary>
/// InnerTube client description: headers and context fields YouTube expects
/// from a particular application. Profiles are data, not code paths.
/// </summary>
public sealed record InnerTubeClientProfile
{
    public const string DefaultEndpoint = "youtubei/v1";

    /// <summary><c>context.client.clientName</c>, for example <c>"WEB"</c>.</summary>
    public required string ClientName { get; init; }

    /// <summary><c>context.client.clientVersion</c>.</summary>
    public required string ClientVersion { get; init; }

    /// <summary>User-Agent sent with InnerTube requests.</summary>
    public required string UserAgent { get; init; }

    /// <summary>InnerTube API key of the client (public, not a secret).</summary>
    public required string ApiKey { get; init; }

    /// <summary>Whether the client requires a PO token for playback.</summary>
    public bool NeedsPoToken { get; init; }

    /// <summary>Whether the client returns muxed (progressive) formats.</summary>
    public bool SupportsMuxed { get; init; } = true;

    /// <summary>API root path, without a leading slash.</summary>
    public string Endpoint { get; init; } = DefaultEndpoint;

    /// <summary>Extra <c>context.client</c> fields (deviceMake, osName, ...).</summary>
    public JsonObject? ExtraContext { get; init; }

    /// <summary>Value of <c>X-Youtube-Client-Name</c>; null to omit the header.</summary>
    public int? NumericId { get; init; }

    /// <summary>Whether the client accepts an OAuth bearer token.</summary>
    public bool SupportsOAuth { get; init; }

    /// <summary>Builds a fresh <c>context.client</c> object for one request.</summary>
    public JsonObject CreateClientContext(string language, string region, string? visitorData)
    {
        var client = new JsonObject
        {
            ["clientName"] = ClientName,
            ["clientVersion"] = ClientVersion,
            ["hl"] = language,
            ["gl"] = region,
            ["utcOffsetMinutes"] = 0
        };

        if (!string.IsNullOrWhiteSpace(visitorData))
            client["visitorData"] = visitorData;

        if (ExtraContext is { } extra)
        {
            foreach (var property in extra)
                client[property.Key] = property.Value?.DeepClone();
        }

        return client;
    }

    /// <summary>Web client; the default for browsing and cookie (SAPISIDHASH) sessions.</summary>
    public static InnerTubeClientProfile Web { get; } = new()
    {
        ClientName = "WEB",
        ClientVersion = "2.20250221.01.00",
        UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        ApiKey = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8",
        NumericId = 1,
        SupportsOAuth = false,
        SupportsMuxed = true,
        NeedsPoToken = true
    };

    /// <summary>TV client; the only client that accepts Google OAuth2 tokens.</summary>
    public static InnerTubeClientProfile TvHtml5 { get; } = new()
    {
        ClientName = "TVHTML5",
        ClientVersion = "7.20260707.07.00",
        UserAgent =
            "Mozilla/5.0 (ChromiumStylePlatform) Cobalt/25.lts.30.1034943-gold " +
            "(unlike Gecko), Unknown_TV_Unknown_0/Unknown (Unknown, Unknown)",
        ApiKey = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8",
        NumericId = 7,
        SupportsOAuth = true,
        SupportsMuxed = true
    };

    /// <summary>
    /// VisionOS client: returns direct cipher-free stream URLs without a PO
    /// token, usually only adaptive (not muxed) formats.
    /// </summary>
    public static InnerTubeClientProfile VisionOs { get; } = new()
    {
        ClientName = "VISIONOS",
        ClientVersion = "1.02",
        UserAgent =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_7_3) AppleWebKit/605.1.15 " +
            "(KHTML, like Gecko) Version/18.0 Safari/605.1.15",
        ApiKey = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8",
        NumericId = null,
        SupportsOAuth = false,
        SupportsMuxed = false,
        ExtraContext = new JsonObject
        {
            ["deviceMake"] = "Apple",
            ["deviceModel"] = "RealityDevice17,1",
            ["osName"] = "visionOS",
            ["osVersion"] = "26.5.23O471"
        }
    };

    /// <summary>Android client; useful for alternative formats.</summary>
    public static InnerTubeClientProfile Android { get; } = new()
    {
        ClientName = "ANDROID",
        ClientVersion = "20.10.38",
        UserAgent = "com.google.android.youtube/20.10.38 (Linux; U; Android 11) gzip",
        ApiKey = "AIzaSyA8eiZmM1FaDVjRy-df2KTyQ_vz_yYM39w",
        NumericId = 3,
        SupportsOAuth = false,
        SupportsMuxed = true,
        NeedsPoToken = true,
        ExtraContext = new JsonObject
        {
            ["androidSdkVersion"] = 30,
            ["osName"] = "Android",
            ["osVersion"] = "11"
        }
    };

    /// <summary>iOS client; sometimes returns different formats than Android.</summary>
    public static InnerTubeClientProfile Ios { get; } = new()
    {
        ClientName = "IOS",
        ClientVersion = "20.10.4",
        UserAgent = "com.google.ios.youtube/20.10.4 (iPhone16,2; U; CPU iOS 18_1_0 like Mac OS X;)",
        ApiKey = "AIzaSyB-63vPrdThhKuerbB2N_l7Kwwcxj6yUAc",
        NumericId = 5,
        SupportsOAuth = false,
        SupportsMuxed = true
    };
}

/// <summary>Google OAuth2 device-code settings. Defaults match the public TV client.</summary>
public sealed record GoogleOAuthOptions
{
    public string ClientId { get; init; } =
        "861556708454-d6dlm3lh05idd8npek18k6be8ba3oc68.apps.googleusercontent.com";

    public string ClientSecret { get; init; } = "SboVhoG9s0rNafixCSGGKXAT";

    public string Scope { get; init; } = "https://www.googleapis.com/auth/youtube";

    public string DeviceCodeEndpoint { get; init; } = "https://oauth2.googleapis.com/device/code";

    public string TokenEndpoint { get; init; } = "https://oauth2.googleapis.com/token";
}

/// <summary>Lifetime of cache entries, per kind of data.</summary>
public sealed class CacheTtls
{
    public TimeSpan Video { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan Streams { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan ChannelResolve { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Lifetime of cached negative answers (unavailable/not found).</summary>
    public TimeSpan Negative { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan Transcripts { get; set; } = TimeSpan.FromHours(1);
}
