using System.Net;
using YoutubeCulvert;
using YoutubeCulvert.Authentication;
using YoutubeCulvert.Caching;
using YoutubeCulvert.Exceptions;

namespace YouTubeDL.Services;

/// <summary>
/// Owns the current <see cref="IYoutubeClient"/> for the selected account
/// profile. Profiles are isolated folders (data/profiles/&lt;name&gt;/auth.json).
/// The client is created synchronously (no network); the session is loaded in
/// the background so the window never waits for YouTube.
/// </summary>
public sealed class YoutubeClientManager : IAsyncDisposable
{
    private readonly SettingsService _settings;
    private readonly DiagnosticsLog _log;
    private readonly HealthMonitor _health;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IYoutubeClient? _client;
    private HttpMessageHandler? _handler;
    private string _profile = "default";
    private Task? _sessionTask;

    public YoutubeClientManager(SettingsService settings, DiagnosticsLog log, HealthMonitor health)
    {
        _settings = settings;
        _log = log;
        _health = health;

        CreateClient();
    }

    public event EventHandler? ClientChanged;

    public IYoutubeClient Client =>
        _client ?? throw new InvalidOperationException("The YouTube client is not initialized.");

    public bool IsReady => _client is not null;

    public string Profile => _profile;

    public HttpMessageHandler? HttpHandler => _handler;

    /// <summary>Starts loading cookies/consent without blocking the caller. Safe to call repeatedly.</summary>
    public Task InitializeSessionAsync()
    {
        var existing = _sessionTask;

        if (existing is { IsCompleted: false })
            return existing;

        _sessionTask = Task.Run(InitializeSessionCoreAsync);
        return _sessionTask;
    }

    /// <summary>Recreates the client (proxy/profile/language changed) and reloads the session.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeClientAsync().ConfigureAwait(false);
            CreateClient();
        }
        finally
        {
            _gate.Release();
        }

        await InitializeSessionAsync().ConfigureAwait(false);
        ClientChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CreateClient()
    {
        var settings = _settings.Current;
        _profile = string.IsNullOrWhiteSpace(settings.Profile) ? "default" : settings.Profile;
        AppPaths.EnsureProfile(_profile);

        _handler = CreateHandler(settings.Proxy);

        var preferredTrack = settings.PreferredAudioTrackId;

        if (string.IsNullOrWhiteSpace(preferredTrack) ||
            string.Equals(preferredTrack, "default", StringComparison.OrdinalIgnoreCase) ||
            !preferredTrack.Contains('.'))
        {
            preferredTrack = null;
        }

        var options = new YoutubeClientOptions
        {
            Language = settings.Language,
            Region = string.Equals(settings.Language, "ru", StringComparison.OrdinalIgnoreCase)
                ? "RU"
                : "US",
            AuthStore = new FileAuthStore(AppPaths.GetProfileAuthFile(_profile)),
            HttpMessageHandler = _handler,
            Logger = _log,
            Cache = new MemoryYoutubeCache(),
            PreferOriginalAudioTrack = settings.PreferOriginalAudioTrack,
            PreferredAudioTrackId = preferredTrack,
            Timeout = TimeSpan.FromSeconds(40)
        };

        var profileOverride = Environment.GetEnvironmentVariable("YTDL_PROFILE");

        if (!string.IsNullOrWhiteSpace(profileOverride))
        {
            options.ClientProfileProvider = new FixedProfileProvider(profileOverride.Trim().ToLowerInvariant() switch
            {
                "web" => YoutubeCulvert.Configuration.InnerTubeClientProfile.Web,
                "tv" => YoutubeCulvert.Configuration.InnerTubeClientProfile.TvHtml5,
                "android" => YoutubeCulvert.Configuration.InnerTubeClientProfile.Android,
                "ios" => YoutubeCulvert.Configuration.InnerTubeClientProfile.Ios,
                _ => YoutubeCulvert.Configuration.InnerTubeClientProfile.VisionOs
            });
        }

        _client = new YoutubeClient(options);
        _sessionTask = null;
    }

    private async Task InitializeSessionCoreAsync()
    {
        try
        {
            var client = _client;

            if (client is null)
                return;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            await client.InitializeAsync(timeout.Token).ConfigureAwait(false);
            _health.ReportSuccess();
            _log.Write("YouTube session initialized.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log.WriteError("Could not initialize the YouTube session.", exception);
            _health.ReportFailure(exception.Message, isNetwork: true);
        }
        finally
        {
            ClientChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task SwitchProfileAsync(string profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return;

        _settings.Current.Profile = profile.Trim();
        _settings.Save();
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return;

        try
        {
            await _client.Auth.SignOutAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or YoutubeException)
        {
            _log.WriteError("Sign-out failed.", exception);
        }

        ClientChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<string> GetProfiles()
    {
        AppPaths.EnsureCreated();

        var profiles = Directory
            .EnumerateDirectories(AppPaths.ProfilesDirectory)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!profiles.Contains("default", StringComparer.OrdinalIgnoreCase))
            profiles.Insert(0, "default");

        return profiles;
    }

    public static HttpMessageHandler? CreateHandler(string? proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy))
            return null;

        var value = proxy.Trim();

        if (!value.Contains("://", StringComparison.Ordinal))
            value = "http://" + value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return null;

        return new SocketsHttpHandler
        {
            Proxy = new WebProxy(uri),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
    }

    private async Task DisposeClientAsync()
    {
        if (_client is { } client)
        {
            _client = null;

            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _log.WriteError("Disposing the YouTube client failed.", exception);
            }
        }

        _handler?.Dispose();
        _handler = null;
        _sessionTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeClientAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
