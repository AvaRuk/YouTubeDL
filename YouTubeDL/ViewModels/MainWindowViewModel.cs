using Avalonia.Controls;
using Avalonia.Threading;
using YoutubeCulvert.Url;
using YouTubeDL.Models;
using YouTubeDL.Services;

namespace YouTubeDL.ViewModels;

/// <summary>Root view model: the six tabs, the status bar and the background services.</summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private readonly LocalizationService _localization;
    private readonly DispatcherTimer _spaceTimer;
    private Window? _window;
    private WatchFolderService? _watchFolder;
    private SchedulerService? _scheduler;
    private ClipboardWatcher? _clipboard;
    private int _selectedTabIndex;
    private string _freeSpaceText = string.Empty;
    private string _lastProxy = string.Empty;
    private string _lastProfile = string.Empty;
    private string _lastLanguage = string.Empty;
    private bool _disposed;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        _localization = LocalizationService.Instance;

        Queue = new QueueViewModel(
            services.Download,
            services.Settings,
            _localization,
            services.History,
            services.QueueStore,
            services.Notifications,
            services.Log,
            services.Health);

        Download = new DownloadViewModel(
            services.Resolver,
            services.Batch,
            Queue,
            services.Settings,
            _localization,
            services.Log,
            services.Health,
            services.Notifications,
            () => { SelectedTabIndex = 1; });

        Account = new AccountViewModel(
            services.ClientManager,
            _localization,
            services.Notifications,
            services.Log);

        Settings = new SettingsViewModel(
            services.Settings,
            _localization,
            services.Ffmpeg,
            services.Log,
            services.Notifications);

        _services.Health.Changed += OnHealthChanged;
        _services.Settings.Changed += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;

        _lastProxy = services.Settings.Current.Proxy;
        _lastProfile = services.Settings.Current.Profile;
        _lastLanguage = services.Settings.Current.Language;

        _spaceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _spaceTimer.Tick += (_, _) => UpdateFreeSpace();
        _spaceTimer.Start();

        UpdateFreeSpace();
    }

    public DownloadViewModel Download { get; }

    public QueueViewModel Queue { get; }

    public AccountViewModel Account { get; }

    public SettingsViewModel Settings { get; }

    public NotificationService Notifications => _services.Notifications;

    public string AppVersion => typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0";

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value))
                return;

            OnPropertyChanged(nameof(IsDownloadPage), nameof(IsQueuePage),
                nameof(IsAccountPage), nameof(IsSettingsPage));
        }
    }

    public bool IsDownloadPage => SelectedTabIndex == 0;

    public bool IsQueuePage => SelectedTabIndex == 1;

    public bool IsAccountPage => SelectedTabIndex == 2;

    public bool IsSettingsPage => SelectedTabIndex == 3;

    public string HealthKey => _services.Health.State switch
    {
        HealthState.Online => "HealthOnline",
        HealthState.Degraded => "HealthDegraded",
        HealthState.Offline => "HealthOffline",
        _ => "HealthUnknown"
    };

    public string HealthText => _localization.Get(HealthKey);

    public string LastSuccessText => _services.Health.LastSuccess is { } last
        ? _localization.Format("HealthLastSuccess", last.LocalDateTime.ToString("HH:mm:ss"))
        : _localization.Get("HealthNoSuccess");

    public string FreeSpaceText => _freeSpaceText;

    public string AccountSummary => _services.ClientManager.Client.Auth.IsAuthenticated
        ? _services.ClientManager.Client.Auth.CurrentAccount?.Name ?? _localization.Get("AccountSignedIn")
        : _localization.Get("AccountSignedOut");

    public void AttachWindow(Window window)
    {
        _window = window;
    }

    public async Task InitializeAsync()
    {
        Queue.Restore();

        Account.RefreshFromAuth();
        UpdateFreeSpace();

        _watchFolder = new WatchFolderService(
            _services.Settings,
            EnqueueFromWatchFolderAsync,
            _services.Log);

        _scheduler = new SchedulerService(_services.Settings, _services.Log);
        _scheduler.PauseRequested += async pause =>
        {
            if (pause)
                Queue.PauseAll();
            else
                Queue.ResumeAll();

            await Task.CompletedTask;
        };

        _clipboard = new ClipboardWatcher(() => _window, new YoutubeUrlParser());
        _clipboard.LinkFound += OnClipboardLink;

        ApplyBackgroundServices();

        _services.Log.Write("Main window initialized.");

        // Load cookies/consent in the background; the UI must never wait for YouTube.
        _ = _services.ClientManager.InitializeSessionAsync();
    }

    private async Task EnqueueFromWatchFolderAsync(IReadOnlyList<string> urls)
    {
        var requests = urls
            .Select(url => CreateBatchRequest(url))
            .ToArray();

        await Queue.EnqueueManyAsync(requests).ConfigureAwait(true);
        SelectedTabIndex = 1;
    }

    private DownloadRequest CreateBatchRequest(string url) => new()
    {
        Input = url,
        Kind = DownloadKind.Video,
        Priority = DownloadPriority.Batch,
        DownloadSubtitles = _services.Settings.Current.DownloadSubtitles,
        SaveThumbnail = _services.Settings.Current.SaveThumbnail,
        EmbedMetadata = _services.Settings.Current.EmbedMetadata,
        SaveDescription = _services.Settings.Current.SaveDescription,
        SaveJsonMetadata = _services.Settings.Current.SaveJsonMetadata,
        SaveChapters = _services.Settings.Current.SaveChapters
    };

    private void OnClipboardLink(string url)
    {
        if (!_services.Settings.Current.ClipboardWatchEnabled)
            return;

        _services.Notifications.Notify(
            _localization.Get("ClipboardTitle"),
            url,
            _localization.Get("ClipboardDownload"),
            () =>
            {
                Download.Input = url;
                SelectedTabIndex = 0;
            });
    }

    private void ApplyBackgroundServices()
    {
        if (_services.Settings.Current.WatchFolderEnabled)
            _watchFolder?.Start();
        else
            _watchFolder?.Stop();

        if (string.IsNullOrWhiteSpace(_services.Settings.Current.ScheduleStart) &&
            string.IsNullOrWhiteSpace(_services.Settings.Current.ScheduleStop) &&
            !_services.Settings.Current.SkipInWorkingHours)
        {
            _scheduler?.Stop();
        }
        else
        {
            _scheduler?.Start();
        }

        if (_services.Settings.Current.ClipboardWatchEnabled)
            _clipboard?.Start();
        else
            _clipboard?.Stop();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => _ = ApplySettingsAsync();

    private async Task ApplySettingsAsync()
    {
        var settings = _services.Settings.Current;

        ThemeService.Apply(settings.Theme);

        var languageChanged = !string.Equals(_lastLanguage, settings.Language, StringComparison.OrdinalIgnoreCase);
        var clientChanged = languageChanged ||
                            !string.Equals(_lastProxy, settings.Proxy, StringComparison.Ordinal) ||
                            !string.Equals(_lastProfile, settings.Profile, StringComparison.OrdinalIgnoreCase);

        _lastLanguage = settings.Language;
        _lastProxy = settings.Proxy;
        _lastProfile = settings.Profile;

        if (languageChanged)
            _localization.Apply(settings.Language);

        if (clientChanged)
        {
            try
            {
                await _services.ClientManager.InitializeAsync().ConfigureAwait(true);
                Account.RefreshFromAuth();
            }
            catch (Exception exception)
            {
                _services.Log.WriteError("Applying network settings failed.", exception);
            }
        }

        ApplyBackgroundServices();
        UpdateFreeSpace();
    }

    private void UpdateFreeSpace()
    {
        var folder = _services.Settings.Current.VideoFolder;
        var free = FileSystemService.GetFreeSpace(
            Directory.Exists(folder) ? folder : AppPaths.DataDirectory);

        _freeSpaceText = free is { } bytes
            ? _localization.Format("StatusFreeSpace", HistoryEntry.FormatSize(bytes))
            : string.Empty;

        OnPropertyChanged(nameof(FreeSpaceText));
    }

    private void OnHealthChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            OnPropertyChanged(nameof(HealthKey), nameof(HealthText), nameof(LastSuccessText));
            return;
        }

        Dispatcher.UIThread.Post(() =>
            OnPropertyChanged(nameof(HealthKey), nameof(HealthText), nameof(LastSuccessText)));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HealthText), nameof(LastSuccessText), nameof(FreeSpaceText),
            nameof(AccountSummary));
        UpdateFreeSpace();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _spaceTimer.Stop();

        _services.Health.Changed -= OnHealthChanged;
        _services.Settings.Changed -= OnSettingsChanged;
        _localization.LanguageChanged -= OnLanguageChanged;

        _watchFolder?.Dispose();
        _scheduler?.Dispose();
        _clipboard?.Dispose();

        Account.Dispose();
        Queue.Dispose();
    }
}
