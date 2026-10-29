using System.Text.Json.Serialization;

namespace YouTubeDL.Models;

[JsonConverter(typeof(JsonStringEnumConverter<QueueStatus>))]
public enum QueueStatus
{
    Queued = 0,
    Paused = 1,
    Resolving = 2,
    Downloading = 3,
    Converting = 4,
    Muxing = 5,
    Completed = 6,
    Failed = 7,
    Canceled = 8,
    Skipped = 9
}

[JsonConverter(typeof(JsonStringEnumConverter<ErrorCategory>))]
public enum ErrorCategory
{
    None = 0,

    /// <summary>Network, rate-limit or a temporary YouTube problem; retry makes sense.</summary>
    Retryable = 1,

    /// <summary>Unavailable, private, removed, authentication required; retry will not help.</summary>
    Terminal = 2,

    Canceled = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<HealthState>))]
public enum HealthState
{
    Unknown = 0,
    Online = 1,
    Degraded = 2,
    Offline = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<DownloadPriority>))]
public enum DownloadPriority
{
    Prefetch = -10,
    Batch = -1,
    Interactive = 10
}
