using System.Windows.Input;
using Avalonia.Threading;
using YoutubeCulvert.Datatypes;
using YouTubeDL.Services;

namespace YouTubeDL.ViewModels;

/// <summary>Single-account view: signed-in state, Google device code and cookie sign-in.</summary>
public sealed class AccountViewModel : ViewModelBase, IDisposable
{
    private readonly YoutubeClientManager _manager;
    private readonly LocalizationService _localization;
    private readonly NotificationService _notifications;
    private readonly DiagnosticsLog _log;

    private CancellationTokenSource? _deviceCodeCancellation;
    private DeviceCodeChallenge? _challenge;
    private string _cookieHeader = string.Empty;
    private bool _isBusy;
    private bool _isSignedIn;
    private string _accountName = string.Empty;
    private string _accountHandle = string.Empty;
    private string _accountEmail = string.Empty;
    private string _deviceCode = string.Empty;
    private string _verificationUrl = string.Empty;
    private string _statusKey = "AccountSignedOut";
    private object?[]? _statusArguments;

    public AccountViewModel(
        YoutubeClientManager manager,
        LocalizationService localization,
        NotificationService notifications,
        DiagnosticsLog log)
    {
        _manager = manager;
        _localization = localization;
        _notifications = notifications;
        _log = log;

        SignInCookieCommand = new AsyncRelayCommand(SignInWithCookieAsync);
        ImportCookiesCommand = new AsyncRelayCommand(ImportCookiesAsync);
        StartDeviceCodeCommand = new AsyncRelayCommand(StartDeviceCodeAsync, () => !IsSignedIn);
        CancelDeviceCodeCommand = new RelayCommand(CancelDeviceCode);
        SignOutCommand = new AsyncRelayCommand(SignOutAsync, () => IsSignedIn);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenVerificationUrlCommand = new RelayCommand(OpenVerificationUrl);

        _manager.ClientChanged += OnClientChanged;
        _localization.LanguageChanged += OnLanguageChanged;

        RefreshFromAuth();
    }

    public ICommand SignInCookieCommand { get; }

    public ICommand ImportCookiesCommand { get; }

    public ICommand StartDeviceCodeCommand { get; }

    public ICommand CancelDeviceCodeCommand { get; }

    public ICommand SignOutCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand OpenVerificationUrlCommand { get; }

    public string CookieHeader
    {
        get => _cookieHeader;
        set => SetProperty(ref _cookieHeader, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                RaiseCommands();
        }
    }

    public bool IsSignedIn
    {
        get => _isSignedIn;
        private set
        {
            if (!SetProperty(ref _isSignedIn, value))
                return;

            OnPropertyChanged(nameof(IsSignedOut));
            RaiseCommands();
        }
    }

    public bool IsSignedOut => !IsSignedIn;

    public string AccountName
    {
        get => _accountName;
        private set => SetProperty(ref _accountName, value);
    }

    public string AccountHandle
    {
        get => _accountHandle;
        private set
        {
            if (SetProperty(ref _accountHandle, value))
                OnPropertyChanged(nameof(HasAccountHandle));
        }
    }

    public string AccountEmail
    {
        get => _accountEmail;
        private set
        {
            if (SetProperty(ref _accountEmail, value))
                OnPropertyChanged(nameof(HasAccountEmail));
        }
    }

    public bool HasAccountHandle => !string.IsNullOrWhiteSpace(AccountHandle);

    public bool HasAccountEmail => !string.IsNullOrWhiteSpace(AccountEmail);

    public string DeviceCode
    {
        get => _deviceCode;
        private set
        {
            if (SetProperty(ref _deviceCode, value))
                OnPropertyChanged(nameof(HasDeviceCode));
        }
    }

    public string VerificationUrl
    {
        get => _verificationUrl;
        private set => SetProperty(ref _verificationUrl, value);
    }

    public bool HasDeviceCode => !string.IsNullOrWhiteSpace(DeviceCode);

    public string StatusText => _statusArguments is { Length: > 0 }
        ? _localization.Format(_statusKey, _statusArguments)
        : _localization.Get(_statusKey);

    public void RefreshFromAuth()
    {
        var account = _manager.Client.Auth.CurrentAccount;
        var signedIn = _manager.Client.Auth.IsAuthenticated;

        IsSignedIn = signedIn;
        AccountName = account?.Name ?? account?.Handle ?? string.Empty;
        AccountHandle = account?.Handle is { Length: > 0 } handle
            ? (handle.StartsWith('@') ? handle : "@" + handle)
            : string.Empty;
        AccountEmail = account?.Email ?? string.Empty;

        SetStatus(signedIn ? "AccountSignedIn" : "AccountSignedOut");
    }

    private async Task SignInWithCookieAsync()
    {
        var header = CookieHeader.Trim();

        if (string.IsNullOrWhiteSpace(header))
        {
            SetStatus("SignInCookieEmpty");
            return;
        }

        IsBusy = true;

        try
        {
            await _manager.Client.Auth
                .SignInWithCookieHeaderAsync(header)
                .ConfigureAwait(true);

            SetStatus("SignInCookieSuccess");
            RefreshFromAuth();
            NotifySignedIn();
        }
        catch (Exception exception)
        {
            _log.WriteError("Cookie sign-in failed.", exception);
            SetStatus("SignInCookieFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportCookiesAsync()
    {
        await ImportCookieFileAsync(AppPaths.CookieFile).ConfigureAwait(true);
    }

    public async Task ImportCookieFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            SetStatus("CookieFileMissing");
            return;
        }

        try
        {
            var headers = CookieImporter.ParseFile(path);
            var header = headers.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(header))
            {
                SetStatus("CookieFileEmpty");
                return;
            }

            CookieHeader = header;
            await SignInWithCookieAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.WriteError("Reading the cookie file failed.", exception);
            SetStatus("SignInCookieFailed");
        }
    }

    private async Task StartDeviceCodeAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        _deviceCodeCancellation?.Dispose();
        _deviceCodeCancellation = new CancellationTokenSource();

        try
        {
            SetStatus("SignInStarting");

            _challenge = await _manager.Client.Auth
                .StartDeviceCodeFlowAsync(_deviceCodeCancellation.Token)
                .ConfigureAwait(true);

            DeviceCode = _challenge.UserCode;
            VerificationUrl = _challenge.VerificationUrl;
            SetStatus("SignInWaiting");

            OpenVerificationUrl();

            await _manager.Client.Auth
                .CompleteDeviceCodeFlowAsync(_challenge, _deviceCodeCancellation.Token)
                .ConfigureAwait(true);

            SetStatus("SignInSuccess");
            RefreshFromAuth();
            NotifySignedIn();
        }
        catch (OperationCanceledException)
        {
            SetStatus("SignInCanceled");
        }
        catch (Exception exception)
        {
            _log.WriteError("Device-code sign-in failed.", exception);
            SetStatus("SignInFailed");
        }
        finally
        {
            DeviceCode = string.Empty;
            VerificationUrl = string.Empty;
            _challenge = null;
            IsBusy = false;
        }
    }

    private void CancelDeviceCode()
    {
        _deviceCodeCancellation?.Cancel();
        SetStatus("SignInCanceled");
    }

    private void OpenVerificationUrl()
    {
        if (!string.IsNullOrWhiteSpace(VerificationUrl))
            FileSystemService.OpenFile(VerificationUrl);
    }

    private async Task SignOutAsync()
    {
        IsBusy = true;

        try
        {
            await _manager.SignOutAsync().ConfigureAwait(true);
            SetStatus("AccountSignedOut");
            RefreshFromAuth();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            await _manager.Client.Auth.RefreshAccountAsync().ConfigureAwait(true);
            RefreshFromAuth();
        }
        catch (Exception exception)
        {
            _log.WriteError("Refreshing the account failed.", exception);
            SetStatus("SignInFailed");
        }
    }

    private void OnClientChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshFromAuth();
            return;
        }

        Dispatcher.UIThread.Post(RefreshFromAuth);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(StatusText));

    private void NotifySignedIn()
    {
        _notifications.Notify(
            _localization.Get("NotificationAccountTitle"),
            _localization.Get("SignInSuccess"));
    }

    private void SetStatus(string key, params object?[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments.Length > 0 ? arguments : null;
        OnPropertyChanged(nameof(StatusText));
    }

    private void RaiseCommands()
    {
        (StartDeviceCodeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SignOutCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        _manager.ClientChanged -= OnClientChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        _deviceCodeCancellation?.Cancel();
        _deviceCodeCancellation?.Dispose();
    }
}
