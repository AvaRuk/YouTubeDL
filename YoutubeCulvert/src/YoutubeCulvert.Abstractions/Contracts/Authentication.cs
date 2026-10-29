using YoutubeCulvert.Datatypes;

namespace YoutubeCulvert.Authentication;

/// <summary>Authentication state storage persisted across application runs.</summary>
public interface IAuthStore
{
    Task<AuthState?> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(AuthState state, CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>In-memory view of the persisted authentication state.</summary>
public interface IAuthStateManager
{
    AuthState? Current { get; }

    Task LoadAsync(CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>Imports a browser cookie session and validates it.</summary>
public interface ICookieAuthProvider
{
    Task<Account> SignInAsync(string cookieHeader, CancellationToken ct = default);
}

/// <summary>OAuth2 device-code flow and token refresh.</summary>
public interface IOAuthProvider
{
    Task<DeviceCodeChallenge> StartAsync(CancellationToken ct = default);

    Task<Account> CompleteAsync(DeviceCodeChallenge challenge, CancellationToken ct = default);

    Task<AuthState> RefreshAsync(AuthState state, CancellationToken ct = default);
}

/// <summary>Facade over the authentication providers.</summary>
public interface IAuthenticationService
{
    bool IsAuthenticated { get; }

    bool IsCookieSession { get; }

    Account? CurrentAccount { get; }

    event EventHandler<AuthenticationStateChangedEventArgs>? StateChanged;

    Task InitializeAsync(CancellationToken ct = default);

    Task<Account> SignInWithCookieHeaderAsync(string cookieHeader, CancellationToken ct = default);

    Task<DeviceCodeChallenge> StartDeviceCodeFlowAsync(CancellationToken ct = default);

    Task<Account> CompleteDeviceCodeFlowAsync(DeviceCodeChallenge challenge, CancellationToken ct = default);

    Task<Account> RefreshAccountAsync(CancellationToken ct = default);

    Task SignOutAsync(CancellationToken ct = default);
}
