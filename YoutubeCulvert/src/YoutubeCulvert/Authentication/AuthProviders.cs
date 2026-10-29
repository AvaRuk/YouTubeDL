using System.Net;
using System.Text.Json;
using YoutubeCulvert.Configuration;
using YoutubeCulvert.Http;
using YoutubeCulvert.InnerTube.Endpoints;
using YoutubeCulvert.Internal;
using YoutubeCulvert.Parsing;

namespace YoutubeCulvert.Authentication;

/// <summary>Reads the signed-in account from <c>account/account_menu</c>.</summary>
internal static class AccountReader
{
    public static async Task<Account> ReadAsync(AccountEndpoint accounts, CancellationToken ct)
    {
        var result = await accounts.GetMenuAsync(ct).ConfigureAwait(false);
        var parsed = new AccountParser().Parse(result.Raw);

        if (!parsed.IsRecognized || parsed.Items.Count == 0)
        {
            throw new YoutubeAuthenticationException(
                "YouTube did not return account information; the session is not authorized.");
        }

        return parsed.Items[0];
    }
}

/// <summary>Imports browser cookies and validates the session.</summary>
public sealed class CookieAuthProvider : ICookieAuthProvider
{
    private readonly AuthStateManager _auth;
    private readonly AccountEndpoint _accounts;

    public CookieAuthProvider(AuthStateManager auth, AccountEndpoint accounts)
    {
        _auth = auth;
        _accounts = accounts;
    }

    public async Task<Account> SignInAsync(string cookieHeader, CancellationToken ct = default)
    {
        var cookies = AuthCookie.ParseHeader(cookieHeader);
        if (cookies.Count == 0)
        {
            throw new YoutubeAuthenticationException(
                "The cookie string does not contain any name=value pairs.");
        }

        // An explicit cookie sign-in replaces the previous OAuth/cookie session.
        _auth.SetAccessToken(null);
        _auth.SetSapisid(null);
        _auth.ClearAuthenticationCookies();
        _auth.AddCookies(cookies);

        var sapisid = cookies.FirstOrDefault(static cookie => AuthCookie.IsSapisid(cookie.Name))?.Value;

        if (string.IsNullOrWhiteSpace(sapisid))
            sapisid = _auth.FindSapisid();
        else
            _auth.SetSapisid(sapisid);

        Account account;

        try
        {
            account = await AccountReader.ReadAsync(_accounts, ct).ConfigureAwait(false);
        }
        catch (YoutubeAuthenticationException)
        {
            throw;
        }
        catch (YoutubeException exception)
        {
            throw new YoutubeAuthenticationException(
                "Failed to validate the YouTube cookie session.", exception);
        }

        var state = (_auth.Current ?? new AuthState()) with
        {
            Sapisid = sapisid,
            Cookies = _auth.ExportAuthCookies(),
            Account = account
        };

        _auth.Update(state);
        await _auth.SaveAsync(ct).ConfigureAwait(false);

        return account;
    }
}

/// <summary>OAuth2 device-code flow with a serialised token refresh.</summary>
public sealed class OAuthProvider : IOAuthProvider
{
    private readonly GoogleOAuthOptions _options;
    private readonly HttpTransport _http;
    private readonly AccountEndpoint _accounts;
    private readonly AuthStateManager _auth;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    internal OAuthProvider(
        GoogleOAuthOptions options,
        HttpTransport http,
        AccountEndpoint accounts,
        AuthStateManager auth)
    {
        _options = options;
        _http = http;
        _accounts = accounts;
        _auth = auth;
    }

    public async Task<DeviceCodeChallenge> StartAsync(CancellationToken ct = default)
    {
        var form = new[]
        {
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("scope", _options.Scope)
        };

        var (_, body) = await PostFormJsonAsync(_options.DeviceCodeEndpoint, form, ct).ConfigureAwait(false);

        if (body.TryGetProperty("error", out _))
        {
            throw new YoutubeAuthenticationException(
                $"Google rejected the device code request: {ReadError(body)}");
        }

        var deviceCode = body.GetString("device_code");
        var userCode = body.GetString("user_code");
        var verificationUrl = body.GetString("verification_url")
                              ?? body.GetString("verification_uri")
                              ?? "https://www.google.com/device";

        if (string.IsNullOrWhiteSpace(deviceCode) || string.IsNullOrWhiteSpace(userCode))
        {
            throw new YoutubeAuthenticationException(
                "The Google response does not contain device_code/user_code.");
        }

        var expiresIn = body.GetInt64("expires_in") ?? 1800;
        var interval = body.GetInt64("interval") ?? 5;

        return new DeviceCodeChallenge
        {
            DeviceCode = deviceCode,
            UserCode = userCode,
            VerificationUrl = verificationUrl,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            Interval = TimeSpan.FromSeconds(Math.Max(1, interval))
        };
    }

    public async Task<Account> CompleteAsync(
        DeviceCodeChallenge challenge,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        var interval = challenge.Interval > TimeSpan.Zero ? challenge.Interval : TimeSpan.FromSeconds(5);

        while (DateTimeOffset.UtcNow < challenge.ExpiresAt)
        {
            var form = new[]
            {
                new KeyValuePair<string, string>("client_id", _options.ClientId),
                new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
                new KeyValuePair<string, string>("device_code", challenge.DeviceCode),
                new KeyValuePair<string, string>(
                    "grant_type", "urn:ietf:params:oauth:grant-type:device_code")
            };

            var (status, body) = await PostFormJsonAsync(_options.TokenEndpoint, form, ct)
                .ConfigureAwait(false);

            if (body.TryGetProperty("access_token", out _))
                return await ApplyTokenAsync(body, ct).ConfigureAwait(false);

            switch (body.GetString("error"))
            {
                case "authorization_pending":
                    break;

                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    break;

                case "access_denied":
                    throw new YoutubeAuthenticationException("The user denied access.");

                case "expired_token":
                    throw new YoutubeAuthenticationException("The device code has expired.");

                default:
                    throw new YoutubeAuthenticationException(
                        $"Failed to obtain an OAuth token (HTTP {(int)status}): {ReadError(body)}");
            }

            await Task.Delay(interval, ct).ConfigureAwait(false);
        }

        throw new YoutubeAuthenticationException("Timed out waiting for code confirmation.");
    }

    public async Task<AuthState> RefreshAsync(AuthState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (state.AccessTokenExpiresAt is not { } expiresAt ||
                expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return state;
            }

            if (string.IsNullOrWhiteSpace(state.RefreshToken))
                return state;

            var form = new[]
            {
                new KeyValuePair<string, string>("client_id", _options.ClientId),
                new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", state.RefreshToken)
            };

            try
            {
                var (_, body) = await PostFormJsonAsync(_options.TokenEndpoint, form, ct)
                    .ConfigureAwait(false);

                var accessToken = body.GetString("access_token");
                if (string.IsNullOrWhiteSpace(accessToken))
                    return state;

                var refreshed = state with
                {
                    AccessToken = accessToken,
                    RefreshToken = body.GetString("refresh_token") ?? state.RefreshToken,
                    AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                        body.GetInt64("expires_in") ?? 3600)
                };

                _auth.Update(refreshed);
                await _auth.SaveAsync(ct).ConfigureAwait(false);
                return refreshed;
            }
            catch (YoutubeException)
            {
                return state;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return state;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<Account> ApplyTokenAsync(JsonElement tokenBody, CancellationToken ct)
    {
        var accessToken = tokenBody.GetString("access_token");

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new YoutubeAuthenticationException("The Google response does not contain access_token.");

        _auth.SetAccessToken(accessToken);

        Account account;

        try
        {
            account = await AccountReader.ReadAsync(_accounts, ct).ConfigureAwait(false);
        }
        catch (YoutubeException)
        {
            // The token itself is valid even if the account endpoint is unavailable.
            account = new Account();
        }

        var state = (_auth.Current ?? new AuthState()) with
        {
            AccessToken = accessToken,
            RefreshToken = tokenBody.GetString("refresh_token") ?? _auth.Current?.RefreshToken,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                tokenBody.GetInt64("expires_in") ?? 3600),
            Sapisid = _auth.FindSapisid(),
            Cookies = _auth.ExportAuthCookies(),
            Account = account
        };

        _auth.Update(state);
        await _auth.SaveAsync(ct).ConfigureAwait(false);

        return account;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostFormJsonAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>> form,
        CancellationToken ct)
    {
        var (status, body) = await _http.PostFormAsync(url, form, ct).ConfigureAwait(false);

        try
        {
            using var document = JsonDocument.Parse(body);
            return (status, document.RootElement.Clone());
        }
        catch (JsonException exception)
        {
            throw new YoutubeAuthenticationException(
                $"Google returned invalid JSON (HTTP {(int)status}).", exception);
        }
    }

    private static string ReadError(JsonElement body)
    {
        var error = body.GetString("error");
        var description = body.GetString("error_description");

        return string.IsNullOrWhiteSpace(description)
            ? error ?? body.GetRawText()
            : $"{error}: {description}";
    }
}

/// <summary>Facade over <see cref="ICookieAuthProvider"/> and <see cref="IOAuthProvider"/>.</summary>
public sealed class AuthService : IAuthenticationService
{
    private readonly AuthStateManager _state;
    private readonly ICookieAuthProvider _cookies;
    private readonly IOAuthProvider _oauth;
    private readonly AccountEndpoint _accounts;
    private readonly HttpTransport _http;

    private int _initialized;

    internal AuthService(
        AuthStateManager state,
        ICookieAuthProvider cookies,
        IOAuthProvider oauth,
        AccountEndpoint accounts,
        HttpTransport http)
    {
        _state = state;
        _cookies = cookies;
        _oauth = oauth;
        _accounts = accounts;
        _http = http;
    }

    public bool IsAuthenticated => _state.IsAuthenticated;

    public bool IsCookieSession => _state.FindSapisid() is not null;

    public Account? CurrentAccount => _state.Current?.Account;

    public event EventHandler<AuthenticationStateChangedEventArgs>? StateChanged;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;

        await _state.LoadAsync(ct).ConfigureAwait(false);

        var current = _state.Current;

        if (current is not null)
        {
            if (current.AccessTokenExpiresAt is { } expiresAt &&
                expiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                await _oauth.RefreshAsync(current, ct).ConfigureAwait(false);
            }
        }

        if (string.IsNullOrEmpty(_state.Current?.VisitorData) &&
            _http.VisitorData is { } visitorData &&
            _state.SetVisitorData(visitorData))
        {
            await _state.SaveAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<Account> SignInWithCookieHeaderAsync(
        string cookieHeader,
        CancellationToken ct = default)
    {
        var account = await _cookies.SignInAsync(cookieHeader, ct).ConfigureAwait(false);
        RaiseStateChanged();
        return account;
    }

    public Task<DeviceCodeChallenge> StartDeviceCodeFlowAsync(CancellationToken ct = default) =>
        _oauth.StartAsync(ct);

    public async Task<Account> CompleteDeviceCodeFlowAsync(
        DeviceCodeChallenge challenge,
        CancellationToken ct = default)
    {
        var account = await _oauth.CompleteAsync(challenge, ct).ConfigureAwait(false);
        RaiseStateChanged();
        return account;
    }

    public async Task<Account> RefreshAccountAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
            throw new YoutubeAuthenticationRequiredException("Sign in to a YouTube account first.");

        var account = await AccountReader.ReadAsync(_accounts, ct).ConfigureAwait(false);

        var state = (_state.Current ?? new AuthState()) with
        {
            Account = account,
            Cookies = _state.ExportAuthCookies(),
            Sapisid = _state.FindSapisid(),
            VisitorData = _http.VisitorData ?? _state.Current?.VisitorData
        };

        _state.Update(state);
        await _state.SaveAsync(ct).ConfigureAwait(false);
        RaiseStateChanged();

        return account;
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await _state.ClearAsync(ct).ConfigureAwait(false);
        RaiseStateChanged();
    }

    private void RaiseStateChanged() =>
        StateChanged?.Invoke(this, new AuthenticationStateChangedEventArgs(IsAuthenticated, CurrentAccount));
}
