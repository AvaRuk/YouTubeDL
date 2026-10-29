using System.Windows.Input;
using YouTubeDL.Models;
using YouTubeDL.Services;

namespace YouTubeDL.ViewModels;

/// <summary>Presentation of one <see cref="QueueItem"/>.</summary>
public sealed class QueueItemViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly Action<QueueItemViewModel> _pauseRequested;
    private readonly Action<QueueItemViewModel> _resumeRequested;
    private readonly Action<QueueItemViewModel> _cancelRequested;
    private readonly Action<QueueItemViewModel> _removeRequested;
    private readonly Action<QueueItemViewModel> _openRequested;

    private CancellationTokenSource? _cancellation;
    private QueueStatus _status;
    private double _progress;
    private string? _errorText;
    private string? _outputPath;
    private double _speed;
    private TimeSpan? _eta;
    private long? _totalBytes;
    private long _bytes;
    private bool _isSelected;
    private bool _isRunning;

    public QueueItemViewModel(
        QueueItem model,
        LocalizationService localization,
        Action<QueueItemViewModel> pauseRequested,
        Action<QueueItemViewModel> resumeRequested,
        Action<QueueItemViewModel> cancelRequested,
        Action<QueueItemViewModel> removeRequested,
        Action<QueueItemViewModel> openRequested)
    {
        Model = model;
        _localization = localization;
        _pauseRequested = pauseRequested;
        _resumeRequested = resumeRequested;
        _cancelRequested = cancelRequested;
        _removeRequested = removeRequested;
        _openRequested = openRequested;

        _status = model.Status;
        _progress = model.Progress;
        _errorText = model.ErrorText;
        _outputPath = string.IsNullOrWhiteSpace(model.OutputPath) ? null : model.OutputPath;
        _totalBytes = model.BytesTotal;

        PauseCommand = new RelayCommand(() => _pauseRequested(this), () => CanPause);
        ResumeCommand = new RelayCommand(() => _resumeRequested(this), () => CanResume);
        CancelCommand = new RelayCommand(() => _cancelRequested(this), () => CanCancel);
        RemoveCommand = new RelayCommand(() => _removeRequested(this));
        OpenCommand = new RelayCommand(() => _openRequested(this), () => CanOpen);
    }

    public QueueItem Model { get; }

    public Guid Id => Model.Id;

    public string Title => string.IsNullOrWhiteSpace(Model.Title) ? Model.Input : Model.Title;

    public string ChannelTitle => Model.ChannelTitle;

    public string KindText => _localization.Get(Model.Kind == DownloadKind.Music ? "KindMusic" : "KindVideo");

    public string Input => Model.Input;

    public string DisplayText => string.IsNullOrWhiteSpace(ChannelTitle)
        ? Title
        : $"{Title} — {ChannelTitle}";

    public string? PlaylistName => Model.PlaylistName;

    public bool HasPlaylist => !string.IsNullOrWhiteSpace(Model.PlaylistName);

    public ICommand PauseCommand { get; }

    public ICommand ResumeCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand OpenCommand { get; }

    public QueueStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value))
                return;

            Model.Status = value;
            OnPropertyChanged(nameof(StatusText), nameof(IsFinished), nameof(IsPaused),
                nameof(CanPause), nameof(CanResume), nameof(CanCancel), nameof(StatusKey));
            RaiseCommands();
        }
    }

    public string StatusKey => _status switch
    {
        QueueStatus.Queued => "QueueStatusQueued",
        QueueStatus.Paused => "QueueStatusPaused",
        QueueStatus.Resolving => "QueueStatusResolving",
        QueueStatus.Downloading => "QueueStatusDownloading",
        QueueStatus.Converting => "QueueStatusConverting",
        QueueStatus.Muxing => "QueueStatusMuxing",
        QueueStatus.Completed => "QueueStatusCompleted",
        QueueStatus.Failed => "QueueStatusFailed",
        QueueStatus.Canceled => "QueueStatusCanceled",
        QueueStatus.Skipped => "QueueStatusSkipped",
        _ => "QueueStatusQueued"
    };

    public string StatusText => string.IsNullOrWhiteSpace(ErrorText)
        ? _localization.Get(StatusKey)
        : $"{_localization.Get(StatusKey)}: {ErrorText}";

    public string? ErrorText
    {
        get => _errorText;
        set
        {
            if (SetProperty(ref _errorText, value))
                OnPropertyChanged(nameof(StatusText), nameof(DetailText));
        }
    }

    public string? OutputPath
    {
        get => _outputPath;
        set
        {
            if (SetProperty(ref _outputPath, value))
            {
                Model.OutputPath = value ?? string.Empty;
                OnPropertyChanged(nameof(DetailText), nameof(CanOpen));
            }
        }
    }

    public string DetailText => !string.IsNullOrWhiteSpace(ErrorText)
        ? ErrorText!
        : OutputPath ?? string.Empty;

    public double Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, value))
            {
                Model.Progress = value;
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public string ProgressText => $"{Progress:P0}";

    public string SpeedText => _speed > 0 ? ProgressSnapshot.FormatSpeed(_speed) + "/s" : string.Empty;

    public string EtaText => _eta is { } eta
        ? $"{(int)eta.TotalHours:00}:{eta.Minutes:00}:{eta.Seconds:00}"
        : string.Empty;

    public string SizeText
    {
        get
        {
            if (_totalBytes is { } total && total > 0)
                return $"{ProgressSnapshot.FormatSpeed(_bytes)} / {ProgressSnapshot.FormatSpeed(total)}";

            return _bytes > 0 ? ProgressSnapshot.FormatSpeed(_bytes) : string.Empty;
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
                RaiseCommands();
        }
    }

    public bool IsFinished =>
        _status is QueueStatus.Completed or QueueStatus.Failed or QueueStatus.Canceled or QueueStatus.Skipped;

    public bool IsPaused => _status == QueueStatus.Paused;

    public bool CanPause =>
        !IsFinished && !IsPaused &&
        _status is QueueStatus.Queued or QueueStatus.Resolving or QueueStatus.Downloading
            or QueueStatus.Converting or QueueStatus.Muxing;

    public bool CanResume => IsPaused || (_status == QueueStatus.Canceled && Model.ErrorCategory != ErrorCategory.Terminal);

    public bool CanCancel => !IsFinished && !IsPaused;

    public bool CanOpen => !string.IsNullOrWhiteSpace(OutputPath);

    public void AttachCancellation(CancellationTokenSource cancellation) => _cancellation = cancellation;

    public void CancelWork()
    {
        try
        {
            _cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void ApplyStage(DownloadStage stage) => Status = stage switch
    {
        DownloadStage.Resolving => QueueStatus.Resolving,
        DownloadStage.DownloadingVideo or DownloadStage.DownloadingAudio => QueueStatus.Downloading,
        DownloadStage.Muxing => QueueStatus.Muxing,
        DownloadStage.Converting => QueueStatus.Converting,
        _ => Status
    };

    public void ApplyProgress(ProgressSnapshot snapshot)
    {
        Progress = snapshot.Percent;
        _bytes = snapshot.Bytes;
        _totalBytes = snapshot.Total ?? _totalBytes;
        _speed = snapshot.SpeedBytesPerSecond;
        _eta = snapshot.Eta;

        Model.BytesDone = snapshot.Bytes;
        Model.BytesTotal = snapshot.Total;
        Model.SpeedBytesPerSecond = snapshot.SpeedBytesPerSecond;
        Model.Eta = snapshot.Eta;

        OnPropertyChanged(nameof(SpeedText), nameof(EtaText), nameof(SizeText));
    }

    public void ApplyPlan(DownloadPlan plan)
    {
        Model.VideoId = plan.VideoId;
        Model.Title = plan.Title;
        Model.ChannelTitle = plan.ChannelTitle;
        Model.Duration = plan.Duration;
        Model.ThumbnailUrl = plan.ThumbnailUrl;
        Model.AudioTrackId = plan.AudioTrackId;
        Model.AudioTrackName = plan.AudioTrackName;
        Model.BytesTotal = plan.TotalBytes;
        OutputPath = plan.OutputPath;

        OnPropertyChanged(nameof(Title), nameof(ChannelTitle), nameof(DisplayText),
            nameof(HasPlaylist), nameof(PlaylistName), nameof(KindText));
    }

    public void Complete(DownloadService.DownloadOutcome outcome)
    {
        Progress = 1;
        OutputPath = outcome.Files.FirstOrDefault() ?? outcome.Plan.OutputPath;
        Status = outcome.Skipped ? QueueStatus.Skipped : QueueStatus.Completed;
        ErrorText = null;
        Model.ErrorCategory = ErrorCategory.None;
        Model.CompletedAt = DateTimeOffset.Now;
    }

    public void Fail(ItemError error)
    {
        ErrorText = error.Message;
        Model.ErrorCategory = error.Category;
        Status = error.Category == ErrorCategory.Canceled ? QueueStatus.Canceled : QueueStatus.Failed;
    }

    public void ResetForRetry()
    {
        ErrorText = null;
        Model.ErrorCategory = ErrorCategory.None;
        Progress = 0;
        Status = QueueStatus.Queued;
    }

    public void RefreshLocalization() =>
        OnPropertyChanged(nameof(KindText), nameof(StatusText), nameof(StatusKey));

    private void RaiseCommands()
    {
        (PauseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ResumeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (OpenCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
