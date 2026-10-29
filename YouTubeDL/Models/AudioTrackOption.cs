namespace YouTubeDL.Models;

/// <summary>One entry of the audio track picker; <see cref="Id"/> is null for "original/default".</summary>
public sealed record AudioTrackOption(string? Id, string DisplayName, string? LanguageCode, bool IsDefault, bool IsOriginal)
{
    public static AudioTrackOption Default { get; } = new(null, "Default", null, true, true);

    public bool IsAutomatic => Id is null;

    public string Label => string.IsNullOrWhiteSpace(LanguageCode)
        ? DisplayName
        : $"{DisplayName} ({LanguageCode})";
}
