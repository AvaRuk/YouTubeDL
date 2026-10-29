using System.Globalization;
using System.Text.RegularExpressions;
using YoutubeCulvert.Datatypes;

namespace YouTubeDL.Models;

/// <summary>Filters applied to a playlist/channel batch before items are queued.</summary>
public sealed partial class BatchFilter
{
    /// <summary>0 means no limit.</summary>
    public int MaxAgeDays { get; set; }

    /// <summary>0 means no limit.</summary>
    public int MinDurationMinutes { get; set; }

    /// <summary>0 means no limit.</summary>
    public int MaxDurationMinutes { get; set; }

    /// <summary>0 means no limit.</summary>
    public long MinViews { get; set; }

    /// <summary>Skip videos already present in the local history.</summary>
    public bool OnlyNew { get; set; } = true;

    public int MaxItems { get; set; } = 200;

    public ChannelTab Tab { get; set; } = ChannelTab.Videos;

    public bool Matches(VideoSummary video)
    {
        if (MinDurationMinutes > 0 && video.Duration is { } duration &&
            duration < TimeSpan.FromMinutes(MinDurationMinutes))
        {
            return false;
        }

        if (MaxDurationMinutes > 0 && video.Duration is { } maxDuration &&
            maxDuration > TimeSpan.FromMinutes(MaxDurationMinutes))
        {
            return false;
        }

        if (MinViews > 0 && video.ViewCount is { } views && views < MinViews)
            return false;

        if (MaxAgeDays > 0 && ParsePublished(video.PublishedTime) is { } published &&
            published < DateTimeOffset.Now.AddDays(-MaxAgeDays))
        {
            return false;
        }

        return true;
    }

    /// <summary>Turns YouTube's relative publish text ("2 days ago", "2 дня назад") into a date.</summary>
    public static DateTimeOffset? ParsePublished(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = PublishedPattern().Match(text);
        if (!match.Success)
            return null;

        var amount = int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups["unit"].Value.ToLowerInvariant();

        var multiplier = unit switch
        {
            "second" or "секунд" or "сек" => TimeSpan.FromSeconds(1),
            "minute" or "минут" or "мин" => TimeSpan.FromMinutes(1),
            "hour" or "час" => TimeSpan.FromHours(1),
            "day" or "дн" or "день" or "дня" or "сут" => TimeSpan.FromDays(1),
            "week" or "недел" => TimeSpan.FromDays(7),
            "month" or "мес" => TimeSpan.FromDays(30),
            "year" or "год" or "лет" or "года" => TimeSpan.FromDays(365),
            _ => TimeSpan.Zero
        };

        return multiplier == TimeSpan.Zero ? null : DateTimeOffset.Now - multiplier * amount;
    }

    [GeneratedRegex(@"(?<amount>\d+)\s*(?<unit>second|minute|hour|day|week|month|year|сек|мин|час|дн|день|дня|сут|недел|мес|год|года|лет)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PublishedPattern();
}
