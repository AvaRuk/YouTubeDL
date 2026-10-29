using System.Text.Json.Serialization;

namespace YouTubeDL.Models;

/// <summary>One completed download (data/history.json).</summary>
public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string VideoId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ChannelTitle { get; set; } = string.Empty;

    public DownloadKind Kind { get; set; } = DownloadKind.Video;

    public string? AudioTrackId { get; set; }

    public string? AudioTrackName { get; set; }

    public string? Quality { get; set; }

    public string Container { get; set; } = "mp4";

    public string FilePath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public TimeSpan? Duration { get; set; }

    public DateTimeOffset? UploadDate { get; set; }

    public string? PlaylistName { get; set; }

    public DateTimeOffset DownloadedAt { get; set; } = DateTimeOffset.Now;

    public bool Succeeded { get; set; } = true;

    public string? ErrorText { get; set; }

    [JsonIgnore]
    public bool FileExists => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);

    [JsonIgnore]
    public string SizeText => FileSize > 0 ? FormatSize(FileSize) : string.Empty;

    [JsonIgnore]
    public string DownloadedAtText => DownloadedAt.LocalDateTime.ToString("g");

    [JsonIgnore]
    public string KindText => Kind == DownloadKind.Music ? "Audio" : "Video";

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }
}
