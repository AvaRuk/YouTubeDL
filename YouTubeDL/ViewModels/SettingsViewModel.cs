using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using YouTubeDL.Models;
using YouTubeDL.Services;

namespace YouTubeDL.ViewModels;

/// <summary>The Settings tab: all persistent options, FFmpeg installation and data tools.</summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localization;
    private readonly FfmpegService _ffmpeg;
    private readonly DiagnosticsLog _log;
    private readonly NotificationService _notifications;

    private string _fileNameTemplate;
    private string _videoFolder;
    private string _musicFolder;
    private string _languageCode;
    private LocalizedOption _theme;
    private int _maxConcurrentDownloads;
    private int _speedLimitKbps;
    private bool _skipExisting;
    private bool _overwriteExisting;
    private bool _openFolderAfterDownload;
    private bool _notifyOnComplete;
    private string _ffmpegPath;
    private bool _preferOriginalAudioTrack;
    private string _preferredAudioTrackId;
    private bool _downloadSubtitles;
    private bool _saveThumbnail;
    private bool _embedMetadata;
    private bool _saveDescription;
    private bool _saveJsonMetadata;
    private bool _saveChapters;
    private string _proxy;
    private bool _watchFolderEnabled;
    private bool _clipboardWatchEnabled;
    private string _scheduleStart;
    private string _scheduleStop;
    private bool _skipInWorkingHours;
    private string _workHoursStart;
    private string _workHoursEnd;
    private LocalizedOption _audioConvertFormat;
    private int _maxHistoryEntries;
    private bool _confirmDeleteToRecycleBin;
    private bool _isBusy;
    private double _ffmpegProgress;
    private string _statusKey = "SettingsAutoSave";
    private object?[]? _statusArguments;
    private readonly DispatcherTimer _saveTimer;
    private bool _initializing = true;

    public SettingsViewModel(
        SettingsService settingsService,
        LocalizationService localization,
        FfmpegService ffmpeg,
        DiagnosticsLog log,
        NotificationService notifications)
    {
        _settingsService = settingsService;
        _localization = localization;
        _ffmpeg = ffmpeg;
        _log = log;
        _notifications = notifications;

        var settings = settingsService.Current;
        _fileNameTemplate = settings.FileNameTemplate;
        _videoFolder = settings.VideoFolder;
        _musicFolder = settings.MusicFolder;
        _languageCode = settings.Language;
        _maxConcurrentDownloads = settings.MaxConcurrentDownloads;
        _speedLimitKbps = settings.SpeedLimitKbps;
        _skipExisting = settings.SkipExisting;
        _overwriteExisting = settings.OverwriteExisting;
        _openFolderAfterDownload = settings.OpenFolderAfterDownload;
        _notifyOnComplete = settings.NotifyOnComplete;
        _ffmpegPath = settings.FfmpegPath;
        _preferOriginalAudioTrack = settings.PreferOriginalAudioTrack;
        _preferredAudioTrackId = settings.PreferredAudioTrackId;
        _downloadSubtitles = settings.DownloadSubtitles;
        _saveThumbnail = settings.SaveThumbnail;
        _embedMetadata = settings.EmbedMetadata;
        _saveDescription = settings.SaveDescription;
        _saveJsonMetadata = settings.SaveJsonMetadata;
        _saveChapters = settings.SaveChapters;
        _proxy = settings.Proxy;
        _watchFolderEnabled = settings.WatchFolderEnabled;
        _clipboardWatchEnabled = settings.ClipboardWatchEnabled;
        _scheduleStart = settings.ScheduleStart;
        _scheduleStop = settings.ScheduleStop;
        _skipInWorkingHours = settings.SkipInWorkingHours;
        _workHoursStart = settings.WorkHoursStart;
        _workHoursEnd = settings.WorkHoursEnd;
        _maxHistoryEntries = settings.MaxHistoryEntries;
        _confirmDeleteToRecycleBin = settings.ConfirmDeleteToRecycleBin;

        Languages = new ObservableCollection<LanguageInfo>(localization.AvailableLanguages);
        Themes =
        [
            new LocalizedOption("system", "ThemeSystem"),
            new LocalizedOption("dark", "ThemeDark"),
            new LocalizedOption("light", "ThemeLight")
        ];
        AudioConvertFormats =
        [
            new LocalizedOption("auto", "FormatAuto"),
            new LocalizedOption("mp3", "FormatMp3"),
            new LocalizedOption("opus", "FormatOpus"),
            new LocalizedOption("flac", "FormatFlac"),
            new LocalizedOption("m4a", "FormatM4a")
        ];

        _theme = Themes.FirstOrDefault(option => option.Value == settings.Theme) ?? Themes[0];
        _audioConvertFormat = AudioConvertFormats.FirstOrDefault(option => option.Value == settings.AudioConvertFormat) ??
                              AudioConvertFormats[0];

        SaveCommand = new RelayCommand(Save);
        InstallFfmpegCommand = new AsyncRelayCommand(InstallFfmpegAsync);
        ReloadLanguagesCommand = new RelayCommand(ReloadLanguages);
        OpenDataFolderCommand = new RelayCommand(() => FileSystemService.OpenFolder(AppPaths.DataDirectory));
        RefreshFfmpegCommand = new RelayCommand(RefreshFfmpeg);

        _localization.LanguagesChanged += OnLanguagesChanged;
        _localization.LanguageChanged += OnLanguageChanged;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };

        PropertyChanged += OnSettingsPropertyChanged;
        _initializing = false;
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_initializing || e.PropertyName is null)
            return;

        if (e.PropertyName is nameof(StatusText) or nameof(FfmpegProgress) or nameof(FfmpegProgressText)
            or nameof(FfmpegStatusText) or nameof(IsFfmpegAvailable) or nameof(IsBusy)
            or nameof(DataFolderText))
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public ICommand SaveCommand { get; }

    public ICommand InstallFfmpegCommand { get; }

    public ICommand ReloadLanguagesCommand { get; }

    public ICommand OpenDataFolderCommand { get; }

    public ICommand RefreshFfmpegCommand { get; }

    public ObservableCollection<LanguageInfo> Languages { get; }

    public IReadOnlyList<LocalizedOption> Themes { get; }

    public IReadOnlyList<LocalizedOption> AudioConvertFormats { get; }

    public string FileNameTemplate
    {
        get => _fileNameTemplate;
        set => SetProperty(ref _fileNameTemplate, value);
    }

    public string VideoFolder
    {
        get => _videoFolder;
        set => SetProperty(ref _videoFolder, value);
    }

    public string MusicFolder
    {
        get => _musicFolder;
        set => SetProperty(ref _musicFolder, value);
    }

    public string LanguageCode
    {
        get => _languageCode;
        set
        {
            if (!SetProperty(ref _languageCode, value))
                return;

            OnPropertyChanged(nameof(SelectedLanguage));
        }
    }

    public LanguageInfo? SelectedLanguage
    {
        get => Languages.FirstOrDefault(language =>
            string.Equals(language.Code, _languageCode, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null)
                LanguageCode = value.Code;
        }
    }

    public LocalizedOption Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    public int MaxConcurrentDownloads
    {
        get => _maxConcurrentDownloads;
        set => SetProperty(ref _maxConcurrentDownloads, Math.Clamp(value, 1, 8));
    }

    public int SpeedLimitKbps
    {
        get => _speedLimitKbps;
        set => SetProperty(ref _speedLimitKbps, Math.Max(0, value));
    }

    public bool SkipExisting
    {
        get => _skipExisting;
        set => SetProperty(ref _skipExisting, value);
    }

    public bool OverwriteExisting
    {
        get => _overwriteExisting;
        set => SetProperty(ref _overwriteExisting, value);
    }

    public bool OpenFolderAfterDownload
    {
        get => _openFolderAfterDownload;
        set => SetProperty(ref _openFolderAfterDownload, value);
    }

    public bool NotifyOnComplete
    {
        get => _notifyOnComplete;
        set => SetProperty(ref _notifyOnComplete, value);
    }

    public string FfmpegPath
    {
        get => _ffmpegPath;
        set => SetProperty(ref _ffmpegPath, value);
    }

    public bool PreferOriginalAudioTrack
    {
        get => _preferOriginalAudioTrack;
        set => SetProperty(ref _preferOriginalAudioTrack, value);
    }

    public string PreferredAudioTrackId
    {
        get => _preferredAudioTrackId;
        set => SetProperty(ref _preferredAudioTrackId, value);
    }

    public bool DownloadSubtitles
    {
        get => _downloadSubtitles;
        set => SetProperty(ref _downloadSubtitles, value);
    }

    public bool SaveThumbnail
    {
        get => _saveThumbnail;
        set => SetProperty(ref _saveThumbnail, value);
    }

    public bool EmbedMetadata
    {
        get => _embedMetadata;
        set => SetProperty(ref _embedMetadata, value);
    }

    public bool SaveDescription
    {
        get => _saveDescription;
        set => SetProperty(ref _saveDescription, value);
    }

    public bool SaveJsonMetadata
    {
        get => _saveJsonMetadata;
        set => SetProperty(ref _saveJsonMetadata, value);
    }

    public bool SaveChapters
    {
        get => _saveChapters;
        set => SetProperty(ref _saveChapters, value);
    }

    public string Proxy
    {
        get => _proxy;
        set => SetProperty(ref _proxy, value);
    }

    public bool WatchFolderEnabled
    {
        get => _watchFolderEnabled;
        set => SetProperty(ref _watchFolderEnabled, value);
    }

    public bool ClipboardWatchEnabled
    {
        get => _clipboardWatchEnabled;
        set => SetProperty(ref _clipboardWatchEnabled, value);
    }

    public string ScheduleStart
    {
        get => _scheduleStart;
        set => SetProperty(ref _scheduleStart, value);
    }

    public string ScheduleStop
    {
        get => _scheduleStop;
        set => SetProperty(ref _scheduleStop, value);
    }

    public bool SkipInWorkingHours
    {
        get => _skipInWorkingHours;
        set => SetProperty(ref _skipInWorkingHours, value);
    }

    public string WorkHoursStart
    {
        get => _workHoursStart;
        set => SetProperty(ref _workHoursStart, value);
    }

    public string WorkHoursEnd
    {
        get => _workHoursEnd;
        set => SetProperty(ref _workHoursEnd, value);
    }

    public LocalizedOption AudioConvertFormat
    {
        get => _audioConvertFormat;
        set => SetProperty(ref _audioConvertFormat, value);
    }

    public int MaxHistoryEntries
    {
        get => _maxHistoryEntries;
        set => SetProperty(ref _maxHistoryEntries, Math.Clamp(value, 50, 100_000));
    }

    public bool ConfirmDeleteToRecycleBin
    {
        get => _confirmDeleteToRecycleBin;
        set => SetProperty(ref _confirmDeleteToRecycleBin, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public double FfmpegProgress
    {
        get => _ffmpegProgress;
        private set
        {
            if (SetProperty(ref _ffmpegProgress, value))
                OnPropertyChanged(nameof(FfmpegProgressText));
        }
    }

    public string FfmpegProgressText => $"{FfmpegProgress:P0}";

    public bool IsFfmpegAvailable => _ffmpeg.IsAvailable;

    public string FfmpegStatusText => IsFfmpegAvailable
        ? _localization.Format("SettingsFfmpegFound", _ffmpeg.ExecutablePath)
        : _localization.Get("SettingsFfmpegNotFound");

    public string DataFolderText => AppPaths.DataDirectory;

    public string StatusText => _statusArguments is { Length: > 0 }
        ? _localization.Format(_statusKey, _statusArguments)
        : _localization.Get(_statusKey);

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(FileNameTemplate))
        {
            SetStatus("SettingsEmptyTemplate");
            return;
        }

        if (string.IsNullOrWhiteSpace(VideoFolder) || string.IsNullOrWhiteSpace(MusicFolder))
        {
            SetStatus("SettingsFoldersEmpty");
            return;
        }

        var settings = _settingsService.Current;
        settings.FileNameTemplate = FileNameTemplate;
        settings.VideoFolder = VideoFolder;
        settings.MusicFolder = MusicFolder;
        settings.Language = string.IsNullOrWhiteSpace(LanguageCode) ? "en" : LanguageCode;
        settings.Theme = Theme?.Value ?? "system";
        settings.MaxConcurrentDownloads = MaxConcurrentDownloads;
        settings.SpeedLimitKbps = SpeedLimitKbps;
        settings.SkipExisting = SkipExisting;
        settings.OverwriteExisting = OverwriteExisting;
        settings.OpenFolderAfterDownload = OpenFolderAfterDownload;
        settings.NotifyOnComplete = NotifyOnComplete;
        settings.FfmpegPath = FfmpegPath;
        settings.PreferOriginalAudioTrack = PreferOriginalAudioTrack;
        settings.DownloadSubtitles = DownloadSubtitles;
        settings.SaveThumbnail = SaveThumbnail;
        settings.EmbedMetadata = EmbedMetadata;
        settings.SaveDescription = SaveDescription;
        settings.SaveJsonMetadata = SaveJsonMetadata;
        settings.SaveChapters = SaveChapters;
        settings.Proxy = Proxy;
        settings.WatchFolderEnabled = WatchFolderEnabled;
        settings.ClipboardWatchEnabled = ClipboardWatchEnabled;
        settings.ScheduleStart = ScheduleStart;
        settings.ScheduleStop = ScheduleStop;
        settings.SkipInWorkingHours = SkipInWorkingHours;
        settings.WorkHoursStart = WorkHoursStart;
        settings.WorkHoursEnd = WorkHoursEnd;
        settings.AudioConvertFormat = AudioConvertFormat?.Value ?? "auto";
        settings.MaxHistoryEntries = MaxHistoryEntries;
        settings.ConfirmDeleteToRecycleBin = ConfirmDeleteToRecycleBin;

        try
        {
            _settingsService.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.WriteError("Saving settings failed.", exception);
            SetStatus("SettingsError");
            return;
        }

        _ffmpeg.Refresh();
        OnPropertyChanged(nameof(FfmpegStatusText), nameof(IsFfmpegAvailable));

        SetStatus("SettingsSaved");
    }

    private async Task InstallFfmpegAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        FfmpegProgress = 0;

        try
        {
            SetStatus("SettingsFfmpegDownloading");

            var progress = new Progress<double>(value => FfmpegProgress = value);
            var path = await _ffmpeg.InstallAsync(progress).ConfigureAwait(true);

            FfmpegPath = path;
            Save();
            OnPropertyChanged(nameof(FfmpegStatusText), nameof(IsFfmpegAvailable));
            SetStatus("SettingsFfmpegDownloaded");
        }
        catch (Exception exception)
        {
            _log.WriteError("FFmpeg installation failed.", exception);
            SetStatus("SettingsFfmpegDownloadFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshFfmpeg()
    {
        _ffmpeg.Refresh();
        OnPropertyChanged(nameof(FfmpegStatusText), nameof(IsFfmpegAvailable));
    }

    private void ReloadLanguages()
    {
        if (_localization.ReloadLanguages())
            SetStatus("SettingsLanguagesReloaded");
        else
            SetStatus("SettingsLanguagesReloadFailed");
    }

    private void OnLanguagesChanged(object? sender, EventArgs e)
    {
        Languages.Clear();

        foreach (var language in _localization.AvailableLanguages)
            Languages.Add(language);

        OnPropertyChanged(nameof(SelectedLanguage));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var option in Themes)
            option.Refresh();

        foreach (var option in AudioConvertFormats)
            option.Refresh();

        OnPropertyChanged(nameof(StatusText), nameof(FfmpegStatusText));
    }

    private void SetStatus(string key, params object?[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments.Length > 0 ? arguments : null;
        OnPropertyChanged(nameof(StatusText));
    }
}
