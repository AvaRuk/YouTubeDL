namespace YouTubeDL.Models;

/// <summary>Exception whose message is a localization resource key.</summary>
public sealed class LocalizedException : Exception
{
    public LocalizedException(string resourceKey)
        : base(resourceKey)
    {
        ResourceKey = resourceKey;
    }

    public string ResourceKey { get; }
}

