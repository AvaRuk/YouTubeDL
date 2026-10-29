using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using YoutubeCulvert.Datatypes;
using YoutubeCulvert.Url;
using YouTubeDL.Models;
using YouTubeDL.Services;

namespace YouTubeDL.ViewModels;

/// <summary>
/// Landing page and modal dialogs for a single video or a playlist.
/// The download mode is detected from the link; no mode picker is shown.
/// </summary>
public sealed class DownloadViewModel : ViewModelBase
{
    private readonly MediaResolver _resolver;
    private readonly BatchService _batch;
    private readonly QueueViewModel _queue;
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;
    private readonly DiagnosticsLog _log;
    private readonly NotificationService _notifications;
    private readonly Action? _onQueued;
    private readonly YoutubeUrlParser _urls = new();

    private string _input = string.Empty;
    private string _statusKey = "DownloadStatusReady";
    private object?[]? _statusArguments;
    private bool _isBusy;

    private bool _isVideoDialogOpen;
    private bool _isPlaylistDialogOpen;
    private bool _isEditingPlaylistItem;

    private string _loadedVideoId = string.Empty;
    private string _previewTitle = string.Empty;
    private string _previewChannel = string.Empty;
    private string _previewDuration = string.Empty;
    private string _previewViews = string.Empty;
    private Bitmap? _previewThumbnail;
    private int _previewVersion;

    private DownloadOption? _selectedOption;
    private string _outputFolder = string.Empty;
    private bool _folderCustomized;

    private string _fileNameTemplate = string.Empty;
    private bool _downloadSubtitles;
    private bool _saveThumbnail;
    private bool _embedMetadata = true;
    private bool _saveDescription;
    private bool _saveJsonMetadata;
    private bool _saveChapters = true;

    private string _playlistTitle = string.Empty;
    private DownloadOption? _playlistOption;
    private string _playlistOutputFolder = string.Empty;
    private string _playlistStatus = string.Empty;
    private PlaylistEntryViewModel? _editingEntry;
    private IReadOnlyList<int> _availableHeights = [];

    public DownloadViewModel(
        MediaResolver resolver,
        BatchService batch,
        QueueViewModel queue,
        SettingsService settings,
        LocalizationService localization,
        DiagnosticsLog log,
        HealthMonitor health,
        NotificationService notifications,
        Action? onQueued = null)
    {
        _resolver = resolver;
        _batch = batch;
        _queue = queue;
        _settings = settings;
        _localization = localization;
        _log = log;
        _notifications = notifications;
        _onQueued = onQueued;

        SearchCommand = new AsyncRelayCommand(SearchAsync);
        CloseDialogCommand = new RelayCommand(CloseDialog);
        DownloadCommand = new AsyncRelayCommand(DownloadAsync);
        DownloadPlaylistCommand = new AsyncRelayCommand(DownloadPlaylistAsync);
        BackCommand = new RelayCommand(BackToPlaylist);
        SelectAllCommand = new RelayCommand(() => SetPlaylistSelection(true));
        SelectNoneCommand = new RelayCommand(() => SetPlaylistSelection(false));

        Options = new ObservableCollection<DownloadOption>();
        PlaylistItems = new ObservableCollection<PlaylistEntryViewModel>();

        var current = _settings.Current;
        _downloadSubtitles = current.DownloadSubtitles;
        _saveThumbnail = current.SaveThumbnail;
        _embedMetadata = current.EmbedMetadata;
        _saveDescription = current.SaveDescription;
        _saveJsonMetadata = current.SaveJsonMetadata;
        _saveChapters = current.SaveChapters;
        _outputFolder = current.VideoFolder;

        _localization.LanguageChanged += OnLanguageChanged;
    }

    public ICommand SearchCommand { get; }

    public ICommand CloseDialogCommand { get; }

    public ICommand DownloadCommand { get; }

    public ICommand DownloadPlaylistCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand SelectNoneCommand { get; }

    public ObservableCollection<DownloadOption> Options { get; }

    public ObservableCollection<PlaylistEntryViewModel> PlaylistItems { get; }

    public string Input
    {
        get => _input;
        set
        {
            if (SetProperty(ref _input, value) && HasStatus)
                SetStatus("DownloadStatusReady");
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool HasStatus => _statusKey != "DownloadStatusReady";

    public string StatusText => _statusArguments is { Length: > 0 }
        ? _localization.Format(_statusKey, _statusArguments)
        : _localization.Get(_statusKey);

    public bool IsDialogOpen => IsVideoDialogOpen || IsPlaylistDialogOpen;

    public bool IsVideoDialogOpen
    {
        get => _isVideoDialogOpen;
        private set
        {
            if (!SetProperty(ref _isVideoDialogOpen, value))
                return;

            OnPropertyChanged(nameof(IsDialogOpen), nameof(DialogIsStandalone));
        }
    }

    public bool IsPlaylistDialogOpen
    {
        get => _isPlaylistDialogOpen;
        private set
        {
            if (!SetProperty(ref _isPlaylistDialogOpen, value))
                return;

            OnPropertyChanged(nameof(IsDialogOpen));
        }
    }

    public bool IsEditingPlaylistItem
    {
        get => _isEditingPlaylistItem;
        private set
        {
            if (!SetProperty(ref _isEditingPlaylistItem, value))
                return;

            OnPropertyChanged(nameof(DialogIsStandalone));
        }
    }

    /// <summary>False when the video was opened from a playlist (only "Back" is shown).</summary>
    public bool DialogIsStandalone => !IsEditingPlaylistItem;

    public string PreviewTitle
    {
        get => _previewTitle;
        private set => SetProperty(ref _previewTitle, value);
    }

    public string PreviewChannel
    {
        get => _previewChannel;
        private set => SetProperty(ref _previewChannel, value);
    }

    public string PreviewDuration
    {
        get => _previewDuration;
        private set => SetProperty(ref _previewDuration, value);
    }

    public string PreviewViews
    {
        get => _previewViews;
        private set => SetProperty(ref _previewViews, value);
    }

    public Bitmap? PreviewThumbnail
    {
        get => _previewThumbnail;
        private set => SetProperty(ref _previewThumbnail, value);
    }

    public DownloadOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (!SetProperty(ref _selectedOption, value))
                return;

            if (!IsEditingPlaylistItem && !_folderCustomized)
            {
                _outputFolder = DefaultFolderFor(value?.Kind ?? DownloadKind.Video);
                OnPropertyChanged(nameof(OutputFolder));
            }
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (!SetProperty(ref _outputFolder, value))
                return;

            _folderCustomized = true;
        }
    }

    public string FileNameTemplate
    {
        get => _fileNameTemplate;
        set => SetProperty(ref _fileNameTemplate, value);
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

    public string PlaylistTitle
    {
        get => _playlistTitle;
        private set => SetProperty(ref _playlistTitle, value);
    }

    public DownloadOption? PlaylistOption
    {
        get => _playlistOption;
        set
        {
            if (!SetProperty(ref _playlistOption, value))
                return;

            if (value is null)
                return;

            foreach (var item in PlaylistItems)
                item.Option = value;
        }
    }

    public string PlaylistOutputFolder
    {
        get => _playlistOutputFolder;
        set => SetProperty(ref _playlistOutputFolder, value);
    }

    public string PlaylistStatus
    {
        get => _playlistStatus;
        private set
        {
            if (SetProperty(ref _playlistStatus, value))
                OnPropertyChanged(nameof(PlaylistHasStatus));
        }
    }

    public bool PlaylistHasStatus => !string.IsNullOrWhiteSpace(PlaylistStatus);

    public int PlaylistSelectedCount => PlaylistItems.Count(static item => item.IsSelected);

    public string PlaylistSelectedText => _localization.Format(
        "PlaylistSelectedCount",
        PlaylistSelectedCount,
        PlaylistItems.Count);

    public bool PlaylistHasItems => PlaylistItems.Count > 0;

    private async Task SearchAsync()
    {
        if (IsBusy)
            return;

        var input = Input.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            SetStatus("DownloadStatusInvalidUrl");
            return;
        }

        IsBusy = true;

        try
        {
            if (LooksLikePlaylist(input))
            {
                await ShowPlaylistAsync(input).ConfigureAwait(true);
                return;
            }

            if (_urls.TryParseVideoId(input, out _))
            {
                await ShowVideoAsync(input, null).ConfigureAwait(true);
                return;
            }

            SetStatus("DownloadStatusInvalidUrl");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _log.WriteError("Loading the download input failed.", exception);
            SetStatus("DownloadStatusError", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool LooksLikePlaylist(string input)
    {
        if (input.Contains("list=", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!_urls.TryParsePlaylistId(input, out var id) || string.IsNullOrWhiteSpace(id))
            return false;

        return id.StartsWith("PL", StringComparison.Ordinal) ||
               id.StartsWith("UU", StringComparison.Ordinal) ||
               id.StartsWith("LL", StringComparison.Ordinal) ||
               id.StartsWith("OL", StringComparison.Ordinal) ||
               id.StartsWith("RD", StringComparison.Ordinal) ||
               id.StartsWith("FL", StringComparison.Ordinal);
    }

    private async Task<bool> ShowVideoAsync(string input, PlaylistEntryViewModel? editingEntry)
    {
        SetStatus("DownloadStatusLoading");

        var result = await _resolver.LoadVideoAsync(input).ConfigureAwait(true);

        if (!result.IsSuccess)
        {
            SetStatus("DownloadStatusError", result.Error!.Message);
            return false;
        }

        var video = result.Value!;
        var version = ++_previewVersion;

        _loadedVideoId = video.Id;
        _editingEntry = editingEntry;
        IsEditingPlaylistItem = editingEntry is not null;

        PreviewTitle = video.Title;
        PreviewChannel = video.ChannelTitle ?? string.Empty;
        PreviewDuration = FileNameFormatter.FormatDuration(video.Duration);
        PreviewViews = video.ViewCount is { } views ? views.ToString("N0") : string.Empty;
        PreviewThumbnail = null;

        _availableHeights = await _resolver.GetAvailableHeightsAsync(video.Id).ConfigureAwait(true);
        BuildOptions(_availableHeights);

        var preferred = editingEntry?.Option;
        SelectedOption = preferred is null
            ? Options.FirstOrDefault()
            : Options.FirstOrDefault(option =>
                  string.Equals(option.FormatId, preferred.FormatId, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(option.QualityId, preferred.QualityId, StringComparison.Ordinal)) ??
              Options.FirstOrDefault();

        if (editingEntry is null)
        {
            _folderCustomized = false;
            _outputFolder = DefaultFolderFor(SelectedOption?.Kind ?? DownloadKind.Video);
            OnPropertyChanged(nameof(OutputFolder));
        }
        else
        {
            _folderCustomized = true;
        }

        IsPlaylistDialogOpen = false;
        IsVideoDialogOpen = true;
        SetStatus("DownloadStatusReady");
        _ = LoadThumbnailAsync(video, version);

        return true;
    }

    private async Task ShowPlaylistAsync(string input)
    {
        SetStatus("DownloadStatusLoading");

        var result = await _batch
            .LoadPlaylistAsync(
                input,
                new BatchFilter
                {
                    MaxItems = 500,
                    OnlyNew = _settings.Current.SkipExisting
                })
            .ConfigureAwait(true);

        if (!result.IsSuccess)
        {
            SetStatus("DownloadStatusError", result.Error!.Message);
            return;
        }

        var selection = result.Value!;

        PlaylistTitle = selection.Title;
        PlaylistItems.Clear();

        BuildOptions([]);
        var defaultOption = Options.FirstOrDefault() ?? CreateDefaultOption();

        foreach (var video in selection.Items)
        {
            var entry = new PlaylistEntryViewModel(video, defaultOption, ConfigurePlaylistEntry);
            entry.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PlaylistEntryViewModel.IsSelected))
                    OnPropertyChanged(nameof(PlaylistSelectedCount), nameof(PlaylistSelectedText));
            };

            PlaylistItems.Add(entry);
        }

        PlaylistOption = defaultOption;
        PlaylistOutputFolder = _settings.Current.VideoFolder;
        PlaylistStatus = _localization.Format("PlaylistCount", PlaylistItems.Count);
        OnPropertyChanged(nameof(PlaylistHasItems), nameof(PlaylistSelectedCount), nameof(PlaylistSelectedText));

        IsVideoDialogOpen = false;
        IsEditingPlaylistItem = false;
        IsPlaylistDialogOpen = true;
        SetStatus("DownloadStatusReady");
    }

    private void ConfigurePlaylistEntry(PlaylistEntryViewModel entry) =>
        _ = OpenPlaylistItemAsync(entry);

    private async Task OpenPlaylistItemAsync(PlaylistEntryViewModel entry)
    {
        if (IsBusy)
            return;

        IsBusy = true;

        try
        {
            PlaylistStatus = string.Empty;
            var opened = await ShowVideoAsync(entry.VideoId, entry).ConfigureAwait(true);

            if (!opened)
            {
                PlaylistStatus = StatusText;
                _log.Write($"Could not open playlist item '{entry.VideoId}': {StatusText}");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BackToPlaylist()
    {
        if (_editingEntry is { } entry && SelectedOption is { } option)
            entry.Option = option;

        _editingEntry = null;
        IsEditingPlaylistItem = false;
        IsVideoDialogOpen = false;
        IsPlaylistDialogOpen = PlaylistItems.Count > 0;
    }

    private void CloseDialog()
    {
        _editingEntry = null;
        IsVideoDialogOpen = false;
        IsPlaylistDialogOpen = false;
        IsEditingPlaylistItem = false;
        Options.Clear();
        PlaylistItems.Clear();
        PreviewThumbnail = null;
        SelectedOption = null;
        PlaylistStatus = string.Empty;
        SetStatus("DownloadStatusReady");
    }

    private async Task DownloadAsync()
    {
        if (IsEditingPlaylistItem || SelectedOption is not { } option)
            return;

        var request = CreateRequest(
            _loadedVideoId,
            option,
            OutputFolder,
            playlistName: null,
            playlistIndex: null,
            DownloadPriority.Interactive);

        await _queue.EnqueueAsync(request).ConfigureAwait(true);
        CloseDialog();
        _onQueued?.Invoke();
    }

    private async Task DownloadPlaylistAsync()
    {
        var selected = PlaylistItems.Where(static item => item.IsSelected).ToArray();

        if (selected.Length == 0)
        {
            PlaylistStatus = _localization.Get("BatchNoSelection");
            return;
        }

        var requests = new List<DownloadRequest>(selected.Length);

        for (var index = 0; index < selected.Length; index++)
        {
            var entry = selected[index];
            requests.Add(CreateRequest(
                entry.VideoId,
                entry.Option,
                string.IsNullOrWhiteSpace(PlaylistOutputFolder)
                    ? DefaultFolderFor(entry.Option.Kind)
                    : PlaylistOutputFolder,
                PlaylistTitle,
                index + 1,
                DownloadPriority.Batch));
        }

        await _queue.EnqueueManyAsync(requests).ConfigureAwait(true);

        _notifications.Notify(
            _localization.Get("NotificationBatchTitle"),
            _localization.Format("NotificationBatchQueued", requests.Count, PlaylistTitle));

        CloseDialog();
        _onQueued?.Invoke();
    }

    private DownloadRequest CreateRequest(
        string videoId,
        DownloadOption option,
        string outputDirectory,
        string? playlistName,
        int? playlistIndex,
        DownloadPriority priority) => new()
        {
            Input = videoId,
            Kind = option.Kind,
            FormatId = option.FormatId,
            QualityId = option.QualityId,
            FileNameTemplate = string.IsNullOrWhiteSpace(FileNameTemplate) ? null : FileNameTemplate,
            OutputDirectory = string.IsNullOrWhiteSpace(outputDirectory) ? null : outputDirectory,
            DownloadSubtitles = DownloadSubtitles,
            SaveThumbnail = SaveThumbnail,
            EmbedMetadata = EmbedMetadata,
            SaveDescription = SaveDescription,
            SaveJsonMetadata = SaveJsonMetadata,
            SaveChapters = SaveChapters,
            PlaylistName = playlistName,
            PlaylistIndex = playlistIndex,
            Priority = priority
        };

    private void BuildOptions(IReadOnlyList<int> heights)
    {
        var best = _localization.Get("OptionBest");

        Options.Clear();
        Options.Add(new DownloadOption(DownloadKind.Video, "mp4", null, $"MP4 · {best}"));

        foreach (var height in heights
                     .Where(static height => height is > 0 and <= 2160)
                     .Distinct()
                     .OrderByDescending(static height => height))
        {
            Options.Add(new DownloadOption(
                DownloadKind.Video,
                "mp4",
                height.ToString(),
                $"MP4 · {height}p"));
        }

        Options.Add(new DownloadOption(DownloadKind.Video, "webm", null, $"WebM · {best}"));
        Options.Add(new DownloadOption(DownloadKind.Video, "mkv", null, $"MKV · {best}"));
        Options.Add(new DownloadOption(DownloadKind.Music, "m4a", null, $"M4A · {best}"));
        Options.Add(new DownloadOption(DownloadKind.Music, "mp3", null, "MP3 · 192 kbps"));
        Options.Add(new DownloadOption(DownloadKind.Music, "opus", null, "Opus · 160 kbps"));
        Options.Add(new DownloadOption(DownloadKind.Music, "flac", null, "FLAC"));

        SelectedOption = Options.FirstOrDefault();
    }

    private DownloadOption CreateDefaultOption() =>
        new(DownloadKind.Video, "mp4", null, $"MP4 · {_localization.Get("OptionBest")}");

    private string DefaultFolderFor(DownloadKind kind) =>
        kind == DownloadKind.Music ? _settings.Current.MusicFolder : _settings.Current.VideoFolder;

    private async Task LoadThumbnailAsync(Video video, int version)
    {
        var url = video.Thumbnails
            .OrderByDescending(static thumbnail => thumbnail.Area)
            .Select(static thumbnail => thumbnail.Url)
            .FirstOrDefault();

        var bitmap = await ThumbnailLoader.LoadAsync(url).ConfigureAwait(true);

        if (version == _previewVersion && IsDialogOpen)
            PreviewThumbnail = bitmap;
    }

    private void SetPlaylistSelection(bool selected)
    {
        foreach (var item in PlaylistItems)
            item.IsSelected = selected;

        OnPropertyChanged(nameof(PlaylistSelectedCount), nameof(PlaylistSelectedText));
    }

    private void SetStatus(string key, params object?[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments.Length > 0 ? arguments : null;
        OnPropertyChanged(nameof(StatusText), nameof(HasStatus));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StatusText), nameof(PlaylistSelectedText));

        if (IsVideoDialogOpen && _availableHeights.Count > 0)
        {
            var previous = SelectedOption;
            BuildOptions(_availableHeights);

            if (previous is not null)
            {
                SelectedOption = Options.FirstOrDefault(option =>
                                     option.FormatId == previous.FormatId &&
                                     option.QualityId == previous.QualityId) ??
                                 Options.FirstOrDefault();
            }
        }
    }
}
