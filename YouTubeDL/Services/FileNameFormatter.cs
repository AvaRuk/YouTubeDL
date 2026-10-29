using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YoutubeCulvert.Datatypes;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>
/// Builds safe file names from a template:
/// %title, %id, %channel, %track, %quality, %ext, %playlist, %index, %upload_date.
/// </summary>
public static partial class FileNameFormatter
{
    public static string Format(
        string template,
        Video video,
        string? quality,
        string? extension,
        string? track,
        string? playlist = null,
        int? index = null)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = video.Title,
            ["id"] = video.Id,
            ["channel"] = video.ChannelTitle ?? string.Empty,
            ["author"] = video.ChannelTitle ?? string.Empty,
            ["track"] = track ?? string.Empty,
            ["quality"] = quality ?? string.Empty,
            ["ext"] = (extension ?? string.Empty).TrimStart('.'),
            ["playlist"] = playlist ?? string.Empty,
            ["index"] = index?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["upload_date"] = video.UploadDate?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                              ?? string.Empty,
            ["date"] = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["duration"] = FormatDuration(video.Duration),
            ["views"] = video.ViewCount?.ToString("N0", CultureInfo.InvariantCulture) ?? string.Empty,
            ["type"] = "video"
        };

        var result = VariablePattern().Replace(
            template,
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

        result = Sanitize(result);

        if (string.IsNullOrWhiteSpace(result))
            result = Sanitize($"{video.Title} [{video.Id}]");

        if (string.IsNullOrWhiteSpace(result))
            result = video.Id;

        return SanitizeLength(result, 180);
    }

    public static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) || char.IsControl(character)
                ? '_'
                : character);
        }

        var result = builder.ToString().Trim(' ', '.');

        // Windows reserved names.
        if (ReservedNames.Contains(result))
            result = "_" + result;

        return result;
    }

    /// <summary>Trims to a safe length while keeping the extension of the path.</summary>
    public static string SanitizeLength(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        var extension = Path.GetExtension(value);
        var stem = value[..^extension.Length];

        if (stem.Length > maxLength - extension.Length)
            stem = stem[..(maxLength - extension.Length)].TrimEnd();

        return stem + extension;
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return string.Empty;

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    public static string ExtensionFor(MediaStream? stream, DownloadKind kind, MediaFormat format)
    {
        if (format.Id is not "auto")
            return format.Extension;

        var mime = stream?.MimeType ?? string.Empty;

        if (kind == DownloadKind.Music)
        {
            return mime.Contains("webm", StringComparison.OrdinalIgnoreCase)
                ? ".webm"
                : ".m4a";
        }

        return mime.Contains("webm", StringComparison.OrdinalIgnoreCase)
            ? ".webm"
            : ".mp4";
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    [GeneratedRegex("%([A-Za-z_]+)")]
    private static partial Regex VariablePattern();
}
