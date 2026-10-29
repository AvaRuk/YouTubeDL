using YoutubeCulvert.Datatypes;

namespace YoutubeCulvert.Downloads;

/// <summary>Describes the stream to resolve for a download.</summary>
public sealed record StreamRequest(
    string VideoId,
    int? MaxHeight = null,
    string? AudioTrackId = null,
    bool PreferMuxed = false,
    string? QualityLabel = null,
    int? Itag = null);

/// <summary>A complete download request.</summary>
public sealed record DownloadRequest(
    string VideoId,
    string Path,
    int? MaxHeight = null,
    string? AudioTrackId = null,
    IProgress<DownloadProgress>? Progress = null);

/// <summary>Selects one <see cref="MediaStream"/> for a request.</summary>
public interface IStreamResolver
{
    Task<MediaStream?> ResolveAsync(StreamRequest request, CancellationToken ct = default);
}

/// <summary>Writes a stream to a file (range resume, retries, completeness checks).</summary>
public interface IDownloadStrategy
{
    Task<DownloadResult> DownloadAsync(
        MediaStream stream,
        string path,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>Links a video file with its companion audio (or the other way round).</summary>
public interface ICompanionFileManager
{
    void Attach(DownloadResult video, DownloadResult? audio);
}

/// <summary>Orchestrates resolve → download → companion files.</summary>
public interface IDownloadCoordinator
{
    Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken ct = default);
}

/// <summary>Public download facade.</summary>
public interface IYoutubeDownloader
{
    Task<DownloadResult> DownloadAsync(
        string videoId,
        string path,
        int? maxHeight = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    Task<DownloadResult> DownloadAsync(
        MediaStream stream,
        string path,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    Task<DownloadResult> DownloadAudioAsync(
        string videoId,
        string path,
        string? audioTrackId = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<DownloadResult>> DownloadAllAudioTracksAsync(
        string videoId,
        string directory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);
}
