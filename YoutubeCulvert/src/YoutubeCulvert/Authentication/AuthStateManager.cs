using System.Net;
using YoutubeCulvert.Logging;

namespace YoutubeCulvert.Authentication;

/// <summary>
/// Owns the cookie container and the persisted <see cref="AuthState"/>.
/// Reads are lock-free, writes are serialised.
/// </summary>
public sealed class AuthStateManager : IAuthStateManager
{
    private readonly IAuthStore _store;
    private readonly Uri _baseUri;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly object _gate = new();

    private volatile AuthState? _current;

    public AuthStateManager(IAuthStore store, Uri baseUri)
    {
        _store = store;
        _baseUri = baseUri;
    }

    /// <inheritdoc />
    public AuthState? Current => _current;

    /// <summary>Cookies used by the HTTP layer; shared with <c>HttpTransport</c>.</summary>
    public CookieContainer Cookies { get; } = new();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct).ConfigureAwait(false);
        if (state is null)
            return;

        AddCookies(state.Cookies);
        _current = state;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        var state = _current;
        if (state is null)
            return;

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _store.SaveAsync(state, ct).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        _current = null;
        ClearAuthenticationCookies();

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _store.ClearAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>Replaces the state and imports its cookies.</summary>
    internal void Update(AuthState? state)
    {
        lock (_gate)
        {
            _current = state;
        }

        if (state is not null)
            AddCookies(state.Cookies);
    }

    /// <summary>Updates only the visitor identifier of the current state.</summary>
    internal bool SetVisitorData(string? visitorData)
    {
        if (string.IsNullOrWhiteSpace(visitorData))
            return false;

        lock (_gate)
        {
            if (string.Equals(_current?.VisitorData, visitorData, StringComparison.Ordinal))
                return false;

            _current = (_current ?? new AuthState()) with { VisitorData = visitorData };
            return true;
        }
    }

    internal void SetAccessToken(string? accessToken)
    {
        lock (_gate)
        {
            _current = (_current ?? new AuthState()) with { AccessToken = accessToken };
        }
    }

    internal void SetSapisid(string? sapisid)
    {
        lock (_gate)
        {
            _current = (_current ?? new AuthState()) with { Sapisid = sapisid };
        }
    }

    internal AuthContext GetAuthContext() => new(
        HasSapisid: FindSapisid() is not null,
        HasBearer: !string.IsNullOrEmpty(Current?.AccessToken),
        AccountId: Current?.Account?.ChannelId);

    internal bool IsAuthenticated => FindSapisid() is not null ||
                                     !string.IsNullOrEmpty(Current?.AccessToken) ||
                                     Current?.Cookies.Count > 0;

    internal void AddCookies(IEnumerable<AuthCookie> cookies)
    {
        foreach (var cookie in cookies)
        {
            if (string.IsNullOrWhiteSpace(cookie.Name))
                continue;

            if (cookie.Expires is { } expires && expires <= DateTimeOffset.UtcNow)
                continue;

            var systemCookie = new Cookie(cookie.Name.Trim(), cookie.Value ?? string.Empty)
            {
                Domain = string.IsNullOrWhiteSpace(cookie.Domain) ? ".youtube.com" : cookie.Domain,
                Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                Secure = cookie.Secure,
                HttpOnly = cookie.HttpOnly
            };

            if (cookie.Expires is { } cookieExpires)
                systemCookie.Expires = cookieExpires.UtcDateTime;

            try
            {
                Cookies.Add(_baseUri, systemCookie);
            }
            catch (CookieException)
            {
                // One invalid cookie must not break importing the rest.
            }
        }
    }

    internal IReadOnlyList<AuthCookie> ExportAuthCookies()
    {
        var result = new List<AuthCookie>();

        foreach (Cookie cookie in Cookies.GetCookies(_baseUri))
        {
            if (!AuthCookie.IsAuthenticationCookie(cookie.Name))
                continue;

            result.Add(new AuthCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = string.IsNullOrWhiteSpace(cookie.Domain) ? ".youtube.com" : cookie.Domain,
                Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                Expires = cookie.Expires == DateTime.MinValue
                    ? null
                    : new DateTimeOffset(DateTime.SpecifyKind(cookie.Expires, DateTimeKind.Utc)),
                Secure = cookie.Secure,
                HttpOnly = cookie.HttpOnly
            });
        }

        return result;
    }

    internal string? FindSapisid() =>
        Current?.Sapisid
        ?? GetCookieValue("SAPISID")
        ?? GetCookieValue("__Secure-3PAPISID")
        ?? GetCookieValue("__Secure-1PAPISID");

    internal string? GetCookieValue(string name)
    {
        foreach (Cookie cookie in Cookies.GetCookies(_baseUri))
        {
            if (cookie.Name.Equals(name, StringComparison.Ordinal))
                return cookie.Value;
        }

        return null;
    }

    internal void ClearAuthenticationCookies()
    {
        string[] names =
        [
            "SID", "HSID", "SSID", "APISID", "SAPISID", "LOGIN_INFO", "SIDCC", "ACCOUNT_CHANGE",
            "__Secure-1PSID", "__Secure-3PSID",
            "__Secure-1PAPISID", "__Secure-3PAPISID",
            "__Secure-1PSIDTS", "__Secure-3PSIDTS",
            "__Secure-1PSIDCC", "__Secure-3PSIDCC",
            "__Host-1PLSID", "__Host-3PLSID",
            "__Host-1PSID", "__Host-3PSID"
        ];

        foreach (var name in names)
        {
            try
            {
                Cookies.Add(_baseUri, new Cookie(name, string.Empty)
                {
                    Domain = ".youtube.com",
                    Path = "/",
                    Expires = DateTime.UtcNow.AddDays(-2),
                    Secure = name.StartsWith("__", StringComparison.Ordinal)
                });
            }
            catch (CookieException)
            {
                // The cookie may already be absent.
            }
        }
    }
}
