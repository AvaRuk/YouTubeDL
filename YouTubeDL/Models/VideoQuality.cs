using YouTubeDL.Services;

namespace YouTubeDL.Models;

/// <summary>A target video height; <see cref="MaxHeight"/> is null for automatic and best.</summary>
public sealed class VideoQuality
{
    public static VideoQuality Auto { get; } = new("auto", null, "QualityAuto");

    public static VideoQuality Best { get; } = new("best", null, "QualityBest");

    public static IReadOnlyList<VideoQuality> Heights { get; } =
    [
        new("2160", 2160, null),
        new("1440", 1440, null),
        new("1080", 1080, null),
        new("720", 720, null),
        new("480", 480, null),
        new("360", 360, null),
        new("240", 240, null),
        new("144", 144, null)
    ];

    public static IReadOnlyList<VideoQuality> All { get; } = [Auto, Best, .. Heights];

    private VideoQuality(string id, int? maxHeight, string? displayKey)
    {
        Id = id;
        MaxHeight = maxHeight;
        DisplayKey = displayKey;
    }

    public string Id { get; }

    public int? MaxHeight { get; }

    public string? DisplayKey { get; }

    public bool IsAutomatic => ReferenceEquals(this, Auto);

    public string Display => DisplayKey is not null
        ? LocalizationService.Instance.Get(DisplayKey)
        : $"{MaxHeight}p";

    public static VideoQuality FromId(string? id) =>
        All.FirstOrDefault(quality => string.Equals(quality.Id, id, StringComparison.OrdinalIgnoreCase)) ??
        Best;
}
