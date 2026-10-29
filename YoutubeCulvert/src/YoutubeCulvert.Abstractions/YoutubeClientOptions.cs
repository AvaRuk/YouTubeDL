using System.Text.RegularExpressions;
using YoutubeCulvert.Authentication;
using YoutubeCulvert.Caching;
using YoutubeCulvert.Configuration;
using YoutubeCulvert.Exceptions;
using YoutubeCulvert.InnerTube;
using YoutubeCulvert.Logging;

namespace YoutubeCulvert;

/// <summary>Settings of <see cref="IYoutubeClient"/>. Immutable after construction.</summary>
public sealed record YoutubeClientOptions
{
    /// <summary>YouTube base address. Override only for tests or a proxy.</summary>
    public string BaseUrl { get; set; } = "https://www.youtube.com";

    /// <summary>InnerTube and UI language (<c>hl</c>).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Region (<c>gl</c>), ISO 3166-1 alpha-2.</summary>
    public string Region { get; set; } = "US";

    /// <summary>Overrides the User-Agent of the selected client profile.</summary>
    public string? UserAgent { get; set; }

    /// <summary>User-Agent used for HTML pages (watch, channel) and timedtext.</summary>
    public string PageUserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>Timeout of one HTTP request.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>Number of retries of idempotent requests (<c>IsMutation = false</c>).</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay of the exponential backoff.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound of one retry delay.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Sends <c>visitorData</c> in the context and the <c>X-Goog-Visitor-Id</c> header.</summary>
    public bool SendVisitorData { get; set; } = true;

    /// <summary>Loads consent/visitor cookies before the first request.</summary>
    public bool InitializeSessionOnFirstRequest { get; set; } = true;

    /// <summary>Authentication storage; null means <c>FileAuthStore</c> in <c>%LOCALAPPDATA%</c>.</summary>
    public IAuthStore? AuthStore { get; set; }

    public GoogleOAuthOptions OAuth { get; set; } = new();

    /// <summary>Custom handler for tests or a proxy; the library does not dispose it.</summary>
    public HttpMessageHandler? HttpMessageHandler { get; set; }

    /// <summary>Client-profile selection; null means <c>AutoClientProfileProvider</c>.</summary>
    public IClientProfileProvider? ClientProfileProvider { get; set; }

    /// <summary>Prefers the original audio track when no explicit track is requested.</summary>
    public bool PreferOriginalAudioTrack { get; set; } = true;

    /// <summary>Explicit default audio track, for example <c>"ru-RU.3"</c>.</summary>
    public string? PreferredAudioTrackId { get; set; }

    public IYoutubeLogger? Logger { get; set; }

    public IYoutubeCache? Cache { get; set; }

    public CacheTtls CacheTtls { get; set; } = new();

    /// <summary>Parallel audio-track downloads of <c>DownloadAllAudioTracksAsync</c>.</summary>
    public int MaxConcurrentAudioTracks { get; set; } = 3;

    /// <summary>Base URL without a trailing slash.</summary>
    public string NormalizedBaseUrl => BaseUrl.TrimEnd('/');

    private static readonly Regex LanguageTag = new(
        @"^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8})*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RegionCode = new(
        @"^[A-Za-z]{2}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex AudioTrackId = new(
        @"^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8})*\.\d+$",
        RegexOptions.CultureInvariant);

    /// <summary>Validates the options; called by <see cref="IYoutubeClient.InitializeAsync"/>.</summary>
    /// <exception cref="YoutubeConfigurationException">A value is invalid.</exception>
    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new YoutubeConfigurationException(
                $"BaseUrl must be an absolute HTTP(S) URL, got '{BaseUrl}'.");
        }

        if (!LanguageTag.IsMatch(Language))
        {
            throw new YoutubeConfigurationException(
                $"Language must be a BCP-47 tag such as 'en' or 'pt-BR', got '{Language}'.");
        }

        if (!RegionCode.IsMatch(Region))
        {
            throw new YoutubeConfigurationException(
                $"Region must be an ISO 3166-1 alpha-2 code such as 'US', got '{Region}'.");
        }

        if (Timeout <= TimeSpan.Zero)
            throw new YoutubeConfigurationException("Timeout must be greater than zero.");

        if (MaxRetries < 0)
            throw new YoutubeConfigurationException("MaxRetries must be zero or greater.");

        if (RetryDelay < TimeSpan.Zero)
            throw new YoutubeConfigurationException("RetryDelay must be zero or greater.");

        if (MaxDelay < TimeSpan.Zero)
            throw new YoutubeConfigurationException("MaxDelay must be zero or greater.");

        if (MaxConcurrentAudioTracks <= 0)
            throw new YoutubeConfigurationException("MaxConcurrentAudioTracks must be greater than zero.");

        if (PreferredAudioTrackId is { } trackId && !AudioTrackId.IsMatch(trackId))
        {
            throw new YoutubeConfigurationException(
                $"PreferredAudioTrackId must look like 'xx-XX.N', got '{trackId}'.");
        }
    }
}
