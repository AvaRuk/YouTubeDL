using System.Text;

namespace YoutubeCulvert.Datatypes;

/// <summary>
/// A cookie of an authorized YouTube session. Used to import browser cookies
/// and to persist the session.
/// </summary>
public sealed record AuthCookie
{
    public string Name { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Domain { get; init; } = ".youtube.com";

    public string Path { get; init; } = "/";

    public DateTimeOffset? Expires { get; init; }

    public bool Secure { get; init; }

    public bool HttpOnly { get; init; }

    /// <summary>Parses a <c>"SAPISID=...; SID=...; HSID=..."</c> cookie header.</summary>
    public static IReadOnlyList<AuthCookie> ParseHeader(string? cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
            return [];

        var result = new List<AuthCookie>();

        foreach (var part in cookieHeader.Split(
                     ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
                continue;

            var name = part[..separator].Trim();
            if (name.Length == 0 || name.StartsWith('$'))
                continue;

            result.Add(new AuthCookie
            {
                Name = name,
                Value = part[(separator + 1)..].Trim(),
                Secure = name.StartsWith("__Secure-", StringComparison.OrdinalIgnoreCase) ||
                         name.StartsWith("__Host-", StringComparison.OrdinalIgnoreCase)
            });
        }

        return result;
    }

    /// <summary>Whether the cookie belongs to an authorized YouTube session.</summary>
    public static bool IsAuthenticationCookie(string name) => name is
        "SID" or "HSID" or "SSID" or "APISID" or "SAPISID" or
        "LOGIN_INFO" or "SIDCC" or "ACCOUNT_CHANGE" or
        "__Secure-1PSID" or "__Secure-3PSID" or
        "__Secure-1PAPISID" or "__Secure-3PAPISID" or
        "__Secure-1PSIDTS" or "__Secure-3PSIDTS" or
        "__Secure-1PSIDCC" or "__Secure-3PSIDCC" or
        "__Host-1PLSID" or "__Host-3PLSID" or
        "__Host-1PSID" or "__Host-3PSID";

    /// <summary>Whether the cookie carries a SAPISID value (usable for SAPISIDHASH).</summary>
    public static bool IsSapisid(string name) =>
        name is "SAPISID" or "__Secure-1PAPISID" or "__Secure-3PAPISID";
}

/// <summary>Persisted authentication state: cookies, OAuth tokens and account data.</summary>
public sealed record AuthState
{
    public string? AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public DateTimeOffset? AccessTokenExpiresAt { get; init; }

    public string? Sapisid { get; init; }

    public IReadOnlyList<AuthCookie> Cookies { get; init; } = [];

    /// <summary>Visitor identifier bound to the session, when it is known.</summary>
    public string? VisitorData { get; init; }

    public Account? Account { get; init; }
}

/// <summary>Data to show the user during OAuth2 device-code sign-in.</summary>
public sealed record DeviceCodeChallenge
{
    /// <summary>Secret device code. Do not show it or write it to logs.</summary>
    public string DeviceCode { get; init; } = string.Empty;

    /// <summary>Short code the user enters on the verification page.</summary>
    public string UserCode { get; init; } = string.Empty;

    public string VerificationUrl { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; init; }

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Raised when the authentication state changes.</summary>
public sealed class AuthenticationStateChangedEventArgs(bool isAuthenticated, Account? account) : EventArgs
{
    public bool IsAuthenticated { get; } = isAuthenticated;

    public Account? Account { get; } = account;
}

/// <summary>Subtitles of a video, split into timed segments.</summary>
public sealed record Transcript : YoutubeEntity
{
    public required string VideoId { get; init; }

    public string LanguageCode { get; init; } = string.Empty;

    public bool IsAutomatic { get; init; }

    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];

    /// <summary>Languages this transcript can be machine-translated to.</summary>
    public IReadOnlyList<string> TranslationLanguages { get; init; } = [];

    public TimeSpan Duration => Segments.Count == 0 ? TimeSpan.Zero : Segments[^1].End;

    /// <summary>All segment texts joined into one line.</summary>
    public string Text => string.Join(' ', Segments.Select(static segment => segment.Text));

    /// <summary>Returns the transcript as timed <c>hh:mm:ss — text</c> lines.</summary>
    public string ToTimedText()
    {
        var builder = new StringBuilder();

        foreach (var segment in Segments)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append(segment.Start.ToString(@"hh\:mm\:ss"))
                .Append(" — ")
                .Append(segment.Text);
        }

        return builder.ToString();
    }
}

/// <summary>A timed subtitle segment.</summary>
public sealed record TranscriptSegment : YoutubeEntity
{
    public TimeSpan Start { get; init; }

    public TimeSpan Duration { get; init; }

    public string Text { get; init; } = string.Empty;

    public TimeSpan End => Start + Duration;
}

/// <summary>A fully downloaded transcript track.</summary>
public sealed record TranscriptTrack : YoutubeEntity
{
    public required string LanguageCode { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsAutoGenerated { get; init; }

    public bool IsTranslatable { get; init; }

    public string? BaseUrl { get; init; }

    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];
}

/// <summary>A subtitle track advertised in <c>captions.playerCaptionsTracklistRenderer</c>.</summary>
public sealed record CaptionTrack : YoutubeEntity
{
    public required string LanguageCode { get; init; }

    /// <summary><c>"asr"</c> for auto-generated tracks, <c>"standard"</c> otherwise.</summary>
    public string? Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsTranslatable { get; init; }

    public string? BaseUrl { get; init; }

    public bool IsAutoGenerated =>
        string.Equals(Kind, "asr", StringComparison.OrdinalIgnoreCase);
}
