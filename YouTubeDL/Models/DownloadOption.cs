namespace YouTubeDL.Models;

/// <summary>One entry of the combined "format + quality" picker.</summary>
public sealed record DownloadOption(
    DownloadKind Kind,
    string FormatId,
    string? QualityId,
    string Display)
{
    public bool IsAudio => Kind == DownloadKind.Music;

    public override string ToString() => Display;
}
