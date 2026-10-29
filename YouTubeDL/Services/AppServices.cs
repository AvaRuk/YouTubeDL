namespace YouTubeDL.Services;

/// <summary>Manual composition root (no DI container) for the whole application.</summary>
public sealed class AppServices : IAsyncDisposable
{
    private AppServices(
        SettingsService settings,
        DiagnosticsLog log,
        HealthMonitor health,
        NotificationService notifications,
        YoutubeClientManager clientManager,
        StreamDownloader downloader,
        FfmpegService ffmpeg,
        HistoryStore history,
        QueueStore queueStore,
        MediaResolver resolver,
        DownloadService download,
        BatchService batch)
    {
        Settings = settings;
        Log = log;
        Health = health;
        Notifications = notifications;
        ClientManager = clientManager;
        Downloader = downloader;
        Ffmpeg = ffmpeg;
        History = history;
        QueueStore = queueStore;
        Resolver = resolver;
        Download = download;
        Batch = batch;
    }

    public SettingsService Settings { get; }

    public DiagnosticsLog Log { get; }

    public HealthMonitor Health { get; }

    public NotificationService Notifications { get; }

    public YoutubeClientManager ClientManager { get; }

    public StreamDownloader Downloader { get; }

    public FfmpegService Ffmpeg { get; }

    public HistoryStore History { get; }

    public QueueStore QueueStore { get; }

    public MediaResolver Resolver { get; }

    public DownloadService Download { get; }

    public BatchService Batch { get; }

    public static AppServices Create()
    {
        AppPaths.EnsureCreated();

        var settings = new SettingsService();
        settings.Load();

        var localization = LocalizationService.Instance;

        if (localization.AvailableLanguages.Count == 0)
            localization.Initialize(settings.Current.Language);
        else
            localization.Apply(settings.Current.Language);

        ThemeService.Apply(settings.Current.Theme);

        var log = new DiagnosticsLog();
        var health = new HealthMonitor();
        var notifications = new NotificationService();

        log.Write($"Application starting. Data directory: {AppPaths.DataDirectory}");

        var clientManager = new YoutubeClientManager(settings, log, health);

        var downloader = new StreamDownloader(() => clientManager.HttpHandler, log);
        var ffmpeg = new FfmpegService(settings, log);
        var history = new HistoryStore(settings);
        var queueStore = new QueueStore();
        var resolver = new MediaResolver(clientManager, settings, health, log);

        var download = new DownloadService(
            resolver,
            downloader,
            ffmpeg,
            history,
            settings,
            clientManager,
            log);

        var batch = new BatchService(clientManager, settings, history, health, log);

        log.Write("Composition root created; the UI can start.");

        return new AppServices(
            settings,
            log,
            health,
            notifications,
            clientManager,
            downloader,
            ffmpeg,
            history,
            queueStore,
            resolver,
            download,
            batch);
    }

    public static Task<AppServices> CreateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Create());

    public async ValueTask DisposeAsync()
    {
        try
        {
            await ClientManager.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.WriteError("Disposing the client failed.", exception);
        }

        Downloader.Dispose();
        Log.Dispose();
    }
}
