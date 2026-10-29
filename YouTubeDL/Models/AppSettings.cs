using System.Globalization;

namespace YouTubeDL.Models;

/// <summary>All persistent application settings (data/settings.json).</summary>
public sealed class AppSettings
{
    public string FileNameTemplate { get; set; } = "%title [%id]";

    public string VideoFolder { get; set; } = DefaultFolder(Environment.SpecialFolder.MyVideos, "Videos");

    public string MusicFolder { get; set; } = DefaultFolder(Environment.SpecialFolder.MyMusic, "Music");

    public string Language { get; set; } = DetectLanguage();

    public string Theme { get; set; } = "system";

    public int MaxConcurrentDownloads { get; set; } = 3;

    /// <summary>Global bandwidth limit in KB/s; 0 means unlimited.</summary>
    public int SpeedLimitKbps { get; set; }

    public bool PreferM4aAudio { get; set; } = true;

    public string AudioConvertFormat { get; set; } = "auto";

    public bool OverwriteExisting { get; set; }

    public bool SkipExisting { get; set; } = true;

    public bool OpenFolderAfterDownload { get; set; }

    public bool NotifyOnComplete { get; set; } = true;

    public string FfmpegPath { get; set; } = string.Empty;

    public bool PreferOriginalAudioTrack { get; set; } = true;

    public string PreferredAudioTrackId { get; set; } = string.Empty;

    public bool DownloadSubtitles { get; set; }

    public bool SaveThumbnail { get; set; }

    public bool EmbedMetadata { get; set; } = true;

    public bool SaveDescription { get; set; }

    public bool SaveJsonMetadata { get; set; }

    public bool SaveChapters { get; set; } = true;

    public string Proxy { get; set; } = string.Empty;

    public bool WatchFolderEnabled { get; set; }

    public bool ClipboardWatchEnabled { get; set; }

    public string ScheduleStart { get; set; } = string.Empty;

    public string ScheduleStop { get; set; } = string.Empty;

    public bool SkipInWorkingHours { get; set; }

    public string WorkHoursStart { get; set; } = "09:00";

    public string WorkHoursEnd { get; set; } = "18:00";

    /// <summary>Account profile whose data/profile/&lt;name&gt; folder is used.</summary>
    public string Profile { get; set; } = "default";

    public int MaxHistoryEntries { get; set; } = 2000;

    public bool ConfirmDeleteToRecycleBin { get; set; } = true;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(FileNameTemplate))
            FileNameTemplate = "%title [%id]";

        if (string.IsNullOrWhiteSpace(VideoFolder))
            VideoFolder = DefaultFolder(Environment.SpecialFolder.MyVideos, "Videos");

        if (string.IsNullOrWhiteSpace(MusicFolder))
            MusicFolder = DefaultFolder(Environment.SpecialFolder.MyMusic, "Music");

        if (string.IsNullOrWhiteSpace(Language))
            Language = "en";

        if (Theme is not ("system" or "dark" or "light"))
            Theme = "system";

        if (AudioConvertFormat is not ("auto" or "mp3" or "opus" or "flac" or "m4a"))
            AudioConvertFormat = "auto";

        if (string.IsNullOrWhiteSpace(Profile))
            Profile = "default";

        FfmpegPath = FfmpegPath?.Trim() ?? string.Empty;
        Proxy = Proxy?.Trim() ?? string.Empty;
        PreferredAudioTrackId = PreferredAudioTrackId?.Trim() ?? string.Empty;
        ScheduleStart = NormalizeTime(ScheduleStart);
        ScheduleStop = NormalizeTime(ScheduleStop);
        WorkHoursStart = NormalizeTime(WorkHoursStart, "09:00");
        WorkHoursEnd = NormalizeTime(WorkHoursEnd, "18:00");

        MaxConcurrentDownloads = Math.Clamp(MaxConcurrentDownloads, 1, 8);
        SpeedLimitKbps = Math.Clamp(SpeedLimitKbps, 0, 1_000_000);
        MaxHistoryEntries = Math.Clamp(MaxHistoryEntries, 50, 100_000);
    }

    public static string DefaultFolder(Environment.SpecialFolder folder, string fallbackName)
    {
        var root = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root))
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = string.IsNullOrWhiteSpace(profile)
                ? Path.Combine(AppContext.BaseDirectory, fallbackName)
                : Path.Combine(profile, fallbackName);
        }

        return Path.Combine(root, "YouTubeDL");
    }

    private static string NormalizeTime(string? value, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return TimeOnly.TryParseExact(
            value.Trim(),
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var time)
            ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
            : fallback;
    }

    private static string DetectLanguage()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
    }
}
