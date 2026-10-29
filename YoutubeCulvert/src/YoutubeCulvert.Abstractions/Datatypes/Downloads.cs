namespace YoutubeCulvert.Datatypes;

/// <summary>Progress of a stream download.</summary>
public sealed record DownloadProgress : YoutubeEntity
{
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Bytes written so far.</summary>
    public long Bytes { get; init; }

    /// <summary>Total size when the server reported it.</summary>
    public long? Total { get; init; }

    /// <summary>Completed fraction from 0 to 1.</summary>
    public double Percent { get; init; }

    /// <summary>Audio track being downloaded, for audio downloads.</summary>
    public string? AudioTrackId { get; init; }
}

/// <summary>Result of a stream download.</summary>
public sealed class DownloadResult : IExtensible
{
    /// <inheritdoc />
    public JsonElement? Raw { get; set; }

    /// <inheritdoc />
    public T? Get<T>(string jsonPointer) => RawJsonAccessor.Get<T>(Raw, jsonPointer);

    public required string FilePath { get; set; }

    public long BytesWritten { get; set; }

    /// <summary>True when the whole stream was written (not a partial file).</summary>
    public bool IsComplete { get; set; }

    public MediaStream? Stream { get; set; }

    public string? AudioTrackId { get; set; }

    public string? AudioTrackName { get; set; }

    /// <summary>Separate audio file when YouTube offered no muxed stream.</summary>
    public string? CompanionAudioPath { get; set; }

    /// <summary>Separate video file when the requested stream was audio-only.</summary>
    public string? CompanionVideoPath { get; set; }
}
