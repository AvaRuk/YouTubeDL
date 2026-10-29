namespace YouTubeDL.Models;

/// <summary>A coalesced snapshot of a running download, published to the UI.</summary>
public sealed record ProgressSnapshot(
    long Bytes,
    long? Total,
    double Percent,
    double SpeedBytesPerSecond,
    TimeSpan? Eta,
    string? AudioTrackId,
    string FilePath)
{
    public string SpeedText => SpeedBytesPerSecond > 0
        ? $"{FormatSpeed(SpeedBytesPerSecond)}/s"
        : string.Empty;

    public string EtaText => Eta is { } eta
        ? $"{(int)eta.TotalHours:00}:{eta.Minutes:00}:{eta.Seconds:00}"
        : string.Empty;

    public string SizeText => Total is { } total
        ? $"{FormatSpeed(Bytes)} / {FormatSpeed(total)}"
        : FormatSpeed(Bytes);

    public static string FormatSpeed(double bytesPerSecond)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = bytesPerSecond;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
