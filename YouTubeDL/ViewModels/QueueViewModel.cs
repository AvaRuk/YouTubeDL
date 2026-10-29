using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using YouTubeDL.Models;
using YouTubeDL.Services;

namespace YouTubeDL.ViewModels;

/// <summary>
/// The download queue engine: priority scheduling, global concurrency limit,
/// pause/resume, persistence, export/import and per-item progress.
/// </summary>
public sealed class QueueViewModel : ViewModelBase, IDisposable
{
    private readonly DownloadService _downloadService;
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;
    private readonly HistoryStore _history;
    private readonly QueueStore _store;
    private readonly NotificationService _notifications;
    private readonly DiagnosticsLog _log;
    private readonly HealthMonitor _health;
    private readonly DispatcherTimer _statsTimer;

    private readonly HashSet<Guid> _pauseRequested = new();
    private readonly object _persistLock = new();

    private QueueItemViewModel? _selectedItem;
    private string _searchText = string.Empty;
    private int _statusFilterIndex;
    private bool _pausedAll;
    private bool _disposed;

    public QueueViewModel(
        DownloadService downloadService,
        SettingsService settings,
        LocalizationService localization,
        HistoryStore history,
        QueueStore store,
        NotificationService notifications,
        DiagnosticsLog log,
        HealthMonitor health)
    {
        _downloadService = downloadService;
        _settings = settings;
        _localization = localization;
        _history = history;
        _store = store;
        _notifications = notifications;
        _log = log;
        _health = health;

        PauseAllCommand = new RelayCommand(PauseAll, () => !_pausedAll);
        ResumeAllCommand = new RelayCommand(ResumeAll, () => _pausedAll);
        CancelSelectedCommand = new RelayCommand(CancelSelected);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected);
        ClearFinishedCommand = new RelayCommand(ClearFinished);
        RetryFailedCommand = new RelayCommand(RetryFailed);
        SelectAllCommand = new RelayCommand(() => SetSelection(true));
        SelectNoneCommand = new RelayCommand(() => SetSelection(false));

        Items.CollectionChanged += (_, _) => RebuildVisible();

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) => UpdateStats();
        _statsTimer.Start();

        _localization.LanguageChanged += OnLanguageChanged;
        _settings.Changed += OnSettingsChanged;
    }

    public event Action<QueueItemViewModel>? ItemFailed;

    public ObservableCollection<QueueItemViewModel> Items { get; } = new();

    /// <summary>Items after the search/status filter; the list is bound to this collection.</summary>
    public ObservableCollection<QueueItemViewModel> VisibleItems { get; } = new();

    public RelayCommand PauseAllCommand { get; }

    public RelayCommand ResumeAllCommand { get; }

    public RelayCommand CancelSelectedCommand { get; }

    public RelayCommand RemoveSelectedCommand { get; }

    public RelayCommand ClearFinishedCommand { get; }

    public RelayCommand RetryFailedCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    public QueueItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => SelectedItem is not null;

    public bool IsPausedAll => _pausedAll;

    public bool IsQueueEmpty => Items.Count == 0;

    public bool HasVisibleItems => VisibleItems.Count > 0;

    public int Limit => Math.Clamp(_settings.Current.MaxConcurrentDownloads, 1, 8);

    public int ActiveCount => Items.Count(static item => item.IsRunning);

    public int QueuedCount => Items.Count(static item => item.Status == QueueStatus.Queued);

    public int FailedCount => Items.Count(static item => item.Status == QueueStatus.Failed);

    public int CompletedCount => Items.Count(static item => item.Status is QueueStatus.Completed or QueueStatus.Skipped);

    public int TotalCount => Items.Count;

    public string SummaryText => _localization.Format("QueueSummary", TotalCount, ActiveCount, Limit);

    public string CountersText => _localization.Format(
        "QueueCounters",
        QueuedCount,
        ActiveCount,
        FailedCount,
        CompletedCount);

    public string SpeedText
    {
        get
        {
            var speed = Items.Where(static item => item.IsRunning).Sum(static item => item.Model.SpeedBytesPerSecond);
            return speed > 0 ? ProgressSnapshot.FormatSpeed(speed) + "/s" : string.Empty;
        }
    }

    public string EtaText
    {
        get
        {
            var remaining = Items
                .Where(static item => item.IsRunning && item.Model.Eta is not null)
                .Select(static item => item.Model.Eta!.Value)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Max();

            return remaining > TimeSpan.Zero
                ? $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
                : string.Empty;
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RebuildVisible();
        }
    }

    public int StatusFilterIndex
    {
        get => _statusFilterIndex;
        set
        {
            if (SetProperty(ref _statusFilterIndex, value))
                RebuildVisible();
        }
    }

    public void Restore()
    {
        var saved = _store.Load();

        foreach (var model in saved)
        {
            if (model.Status is not (QueueStatus.Completed or QueueStatus.Failed or QueueStatus.Canceled))
                model.Status = QueueStatus.Queued;

            Items.Add(CreateItem(model));
        }

        RebuildVisible();
        UpdateStats();
        Pump();
    }

    public Task EnqueueAsync(DownloadRequest request) => EnqueueManyAsync([request]);

    public Task EnqueueManyAsync(IEnumerable<DownloadRequest> requests)
    {
        var added = 0;

        foreach (var request in requests)
        {
            var model = CreateModel(request);
            Items.Add(CreateItem(model));
            added++;
        }

        if (added == 0)
            return Task.CompletedTask;

        Persist();
        RebuildVisible();
        UpdateStats();
        Pump();

        return Task.CompletedTask;
    }

    public void PauseAll()
    {
        _pausedAll = true;

        foreach (var item in Items.Where(static item => item.Status == QueueStatus.Queued).ToArray())
            item.Status = QueueStatus.Paused;

        foreach (var item in Items.Where(static item => item.IsRunning).ToArray())
        {
            _pauseRequested.Add(item.Id);
            item.CancelWork();
        }

        PauseAllCommand.RaiseCanExecuteChanged();
        ResumeAllCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsPausedAll));
        Persist();
        RebuildVisible();
    }

    public void ResumeAll()
    {
        _pausedAll = false;

        foreach (var item in Items.Where(static item => item.Status == QueueStatus.Paused).ToArray())
            item.Status = QueueStatus.Queued;

        PauseAllCommand.RaiseCanExecuteChanged();
        ResumeAllCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsPausedAll));
        Persist();
        RebuildVisible();
        Pump();
    }

    public void PauseItem(QueueItemViewModel? item)
    {
        if (item is null || !item.CanPause)
            return;

        if (item.IsRunning)
        {
            _pauseRequested.Add(item.Id);
            item.CancelWork();
        }
        else
        {
            item.Status = QueueStatus.Paused;
        }

        Persist();
        RebuildVisible();
    }

    public void ResumeItem(QueueItemViewModel? item)
    {
        if (item is null)
            return;

        _pauseRequested.Remove(item.Id);
        item.ResetForRetry();
        Persist();
        RebuildVisible();
        Pump();
    }

    public void CancelItem(QueueItemViewModel? item)
    {
        if (item is null || item.IsFinished)
            return;

        if (item.IsRunning)
        {
            _pauseRequested.Remove(item.Id);
            item.CancelWork();
        }
        else
        {
            item.Status = QueueStatus.Canceled;
        }

        Persist();
        RebuildVisible();
    }

    public void RemoveItem(QueueItemViewModel? item)
    {
        if (item is null)
            return;

        _pauseRequested.Remove(item.Id);

        if (item.IsRunning)
            item.CancelWork();

        Items.Remove(item);

        if (ReferenceEquals(SelectedItem, item))
            SelectedItem = null;

        Persist();
        RebuildVisible();
        UpdateStats();
        Pump();
    }

    public void RetryItem(QueueItemViewModel? item)
    {
        if (item is null)
            return;

        if (!Items.Contains(item))
            Items.Add(item);

        item.ResetForRetry();
        Persist();
        RebuildVisible();
        Pump();
    }

    public void CancelSelected() => RunOnSelected(CancelItem);

    public void RemoveSelected() => RunOnSelected(RemoveItem);

    public void ClearFinished()
    {
        foreach (var item in Items.Where(static item => item.IsFinished).ToArray())
            Items.Remove(item);

        SelectedItem = null;
        Persist();
        RebuildVisible();
        UpdateStats();
    }

    public void RetryFailed()
    {
        foreach (var item in Items
                     .Where(static item => item.Status == QueueStatus.Failed &&
                                           item.Model.ErrorCategory == ErrorCategory.Retryable)
                     .ToArray())
        {
            item.ResetForRetry();
        }

        Persist();
        RebuildVisible();
        Pump();
    }

    public void SetSelection(bool selected)
    {
        foreach (var item in VisibleItems)
            item.IsSelected = selected;
    }

    public void Export(string path)
    {
        try
        {
            _store.Export(path, Items.Select(static item => item.Model));
            _notifications.Notify(
                _localization.Get("NotificationQueueExportedTitle"),
                _localization.Get("NotificationQueueExported"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.WriteError("Queue export failed.", exception);
            _notifications.NotifyError(
                _localization.Get("NotificationErrorTitle"),
                _localization.Get("ErrorQueueExport"));
        }
    }

    public void Import(string path)
    {
        try
        {
            var items = _store.Import(path);
            var known = Items.Select(static item => item.Id).ToHashSet();

            foreach (var model in items)
            {
                if (known.Contains(model.Id))
                    continue;

                model.Status = QueueStatus.Queued;
                model.ErrorText = null;
                model.ErrorCategory = ErrorCategory.None;
                Items.Add(CreateItem(model));
            }

            Persist();
            RebuildVisible();
            Pump();

            _notifications.Notify(
                _localization.Get("NotificationQueueImportedTitle"),
                _localization.Get("NotificationQueueImported"));
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            _log.WriteError("Queue import failed.", exception);
            _notifications.NotifyError(
                _localization.Get("NotificationErrorTitle"),
                _localization.Get("ErrorQueueImport"));
        }
    }

    private void Pump()
    {
        if (_disposed || _pausedAll)
            return;

        while (ActiveCount < Limit)
        {
            var next = Items
                .Where(static item => item.Status == QueueStatus.Queued)
                .OrderByDescending(static item => item.Model.Priority)
                .ThenBy(static item => item.Model.CreatedAt)
                .FirstOrDefault();

            if (next is null)
                return;

            _ = ProcessAsync(next);
        }
    }

    private async Task ProcessAsync(QueueItemViewModel item)
    {
        if (item.IsRunning || item.Status != QueueStatus.Queued)
            return;

        item.IsRunning = true;
        item.Status = QueueStatus.Resolving;

        using var cancellation = new CancellationTokenSource();
        item.AttachCancellation(cancellation);

        var progress = new Progress<ProgressSnapshot>(item.ApplyProgress);
        var stage = new Progress<DownloadStage>(item.ApplyStage);

        try
        {
            var request = item.Model.ToRequest();
            var result = await _downloadService
                .RunAsync(
                    request,
                    progress,
                    stage,
                    cancellation.Token,
                    plan => Dispatcher.UIThread.Post(() => item.ApplyPlan(plan)))
                .ConfigureAwait(true);

            if (result.IsSuccess)
            {
                var outcome = result.Value!;
                item.ApplyPlan(outcome.Plan);
                item.Complete(outcome);

                _health.ReportSuccess();

                if (outcome.Skipped)
                {
                    _notifications.Notify(
                        _localization.Get("NotificationSkippedTitle"),
                        item.Title);
                }
                else
                {
                    NotifyCompleted(item, outcome);
                }
            }
            else if (result.Error is { } error)
            {
                if (error.Category == ErrorCategory.Canceled && _pauseRequested.Remove(item.Id))
                {
                    item.Status = QueueStatus.Paused;
                }
                else
                {
                    item.Fail(error);

                    if (error.Category != ErrorCategory.Canceled)
                    {
                        _health.ReportFailure(error.Message, error.IsRetryable);
                        ItemFailed?.Invoke(item);
                        NotifyFailed(item, error);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (_pauseRequested.Remove(item.Id))
                item.Status = QueueStatus.Paused;
            else
                item.Fail(ItemError.Canceled(_localization.Get("ErrorCanceled")));
        }
        catch (Exception exception)
        {
            _log.WriteError($"Unhandled queue failure for '{item.Title}'.", exception);
            var error = new ItemError(ErrorCategory.Retryable, exception.Message, ErrorAction.Retry);
            item.Fail(error);
            ItemFailed?.Invoke(item);
            NotifyFailed(item, error);
        }
        finally
        {
            item.IsRunning = false;
            _pauseRequested.Remove(item.Id);
            Persist();
            UpdateStats();
            Pump();
        }
    }

    private void NotifyCompleted(QueueItemViewModel item, DownloadService.DownloadOutcome outcome)
    {
        if (!_settings.Current.NotifyOnComplete)
            return;

        var path = outcome.Files.FirstOrDefault() ?? outcome.Plan.OutputPath;

        _notifications.Notify(
            _localization.Get("NotificationCompletedTitle"),
            item.Title,
            _localization.Get("OpenFolder"),
            () => FileSystemService.RevealFile(path));

        if (_settings.Current.OpenFolderAfterDownload)
            FileSystemService.RevealFile(path);
    }

    private void NotifyFailed(QueueItemViewModel item, ItemError error)
    {
        if (!_settings.Current.NotifyOnComplete || !error.IsRetryable)
            return;

        Action? retryAction = error.IsRetryable ? () => RetryItem(item) : null;

        _notifications.NotifyError(
            _localization.Get("NotificationFailedTitle"),
            $"{item.Title}: {error.Message}",
            _localization.Get("Retry"),
            retryAction);
    }

    private QueueItemViewModel CreateItem(QueueItem model)
    {
        var item = new QueueItemViewModel(
            model,
            _localization,
            PauseItem,
            ResumeItem,
            CancelItem,
            RemoveItem,
            OpenItem);

        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(QueueItemViewModel.Status) or nameof(QueueItemViewModel.IsRunning))
            {
                RebuildVisible();
                UpdateStats();
            }
        };

        return item;
    }

    private QueueItem CreateModel(DownloadRequest request)
    {
        return new QueueItem
        {
            Input = request.Input,
            VideoId = request.Input,
            Kind = request.Kind,
            QualityId = request.QualityId ?? "best",
            FormatId = request.FormatId ?? "auto",
            AudioTrackId = request.AudioTrackId,
            AudioTrackName = request.AudioTrackName,
            FileNameTemplate = request.FileNameTemplate,
            CustomFilePath = request.CustomFilePath,
            OutputDirectory = request.OutputDirectory,
            Priority = request.Priority,
            SpeedLimitKbps = request.SpeedLimitKbps,
            DownloadSubtitles = request.DownloadSubtitles,
            SaveThumbnail = request.SaveThumbnail,
            EmbedMetadata = request.EmbedMetadata,
            SaveDescription = request.SaveDescription,
            SaveJsonMetadata = request.SaveJsonMetadata,
            SaveChapters = request.SaveChapters,
            PlaylistName = request.PlaylistName,
            PlaylistIndex = request.PlaylistIndex,
            Status = QueueStatus.Queued,
            CreatedAt = DateTimeOffset.Now
        };
    }

    private void OpenItem(QueueItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.OutputPath))
            FileSystemService.RevealFile(item.OutputPath);
    }

    private void RebuildVisible()
    {
        var query = Items.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var text = _searchText.Trim();
            query = query.Where(item =>
                item.Title.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                item.ChannelTitle.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                item.Input.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        query = _statusFilterIndex switch
        {
            1 => query.Where(static item => item.IsRunning),
            2 => query.Where(static item => item.Status == QueueStatus.Queued || item.Status == QueueStatus.Paused),
            3 => query.Where(static item => item.Status is QueueStatus.Completed or QueueStatus.Skipped),
            4 => query.Where(static item => item.Status == QueueStatus.Failed),
            _ => query
        };

        VisibleItems.Clear();

        foreach (var item in query)
            VisibleItems.Add(item);

        OnPropertyChanged(nameof(IsQueueEmpty), nameof(HasVisibleItems));
    }

    private void UpdateStats()
    {
        OnPropertyChanged(
            nameof(ActiveCount),
            nameof(QueuedCount),
            nameof(FailedCount),
            nameof(CompletedCount),
            nameof(TotalCount),
            nameof(SummaryText),
            nameof(CountersText),
            nameof(SpeedText),
            nameof(EtaText));
    }

    private void RunOnSelected(Action<QueueItemViewModel?> action)
    {
        var selected = VisibleItems.Where(static item => item.IsSelected).ToArray();

        if (selected.Length == 0 && SelectedItem is { } single)
        {
            action(single);
            return;
        }

        foreach (var item in selected)
            action(item);
    }

    private void Persist()
    {
        lock (_persistLock)
        {
            try
            {
                _store.Save(Items.Select(static item => item.Model));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _log.WriteError("Queue persistence failed.", exception);
            }
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in Items)
            item.RefreshLocalization();

        UpdateStats();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Limit), nameof(SummaryText));
        Pump();
    }

    public void Dispose()
    {
        _disposed = true;
        _statsTimer.Stop();
        _localization.LanguageChanged -= OnLanguageChanged;
        _settings.Changed -= OnSettingsChanged;

        foreach (var item in Items)
            item.CancelWork();
    }
}
