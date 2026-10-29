using System.Windows.Input;
using YouTubeDL.Models;
using YouTubeDL.Services;

namespace YouTubeDL.ViewModels;

/// <summary>One row of the playlist dialog.</summary>
public sealed class PlaylistEntryViewModel : ViewModelBase
{
    private bool _isSelected = true;
    private DownloadOption _option;

    public PlaylistEntryViewModel(
        BatchService.BatchVideo video,
        DownloadOption option,
        Action<PlaylistEntryViewModel> configure)
    {
        VideoId = video.VideoId;
        Title = video.Title;
        ChannelTitle = video.ChannelTitle ?? string.Empty;
        Duration = video.Duration;
        _option = option;
        ConfigureCommand = new RelayCommand(() => configure(this));
    }

    public string VideoId { get; }

    public string Title { get; }

    public string ChannelTitle { get; }

    public TimeSpan? Duration { get; }

    public string DurationText => Duration is { } duration
        ? FileNameFormatter.FormatDuration(duration)
        : string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public DownloadOption Option
    {
        get => _option;
        set
        {
            if (!SetProperty(ref _option, value))
                return;

            OnPropertyChanged(nameof(OptionDisplay));
        }
    }

    public string OptionDisplay => Option.Display;

    public ICommand ConfigureCommand { get; }
}
