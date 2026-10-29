using System.Text;

namespace YoutubeCulvert.Downloads;

/// <summary>
/// Download facade over <see cref="IDownloadCoordinator"/> and
/// <see cref="IDownloadStrategy"/>.
/// </summary>
public sealed class YoutubeDownloader : IYoutubeDownloader, IDisposable
{
    private readonly IDownloadCoordinator _coordinator;
    private readonly IDownloadStrategy _strategy;
    private readonly IVideosService _videos;
    private readonly YoutubeClientOptions _options;
    private readonly IDisposable? _ownedStrategy;

    public YoutubeDownloader(
        IDownloadCoordinator coordinator,
        IDownloadStrategy strategy,
        IVideosService videos,
        YoutubeClientOptions options,
        IDisposable? ownedStrategy = null)
    {
        _coordinator = coordinator;
        _strategy = strategy;
        _videos = videos;
        _options = options;
        _ownedStrategy = ownedStrategy;
    }

    public Task<DownloadResult> DownloadAsync(
        string videoId,
        string path,
        int? maxHeight = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default) =>
        _coordinator.DownloadAsync(new DownloadRequest(videoId, path, maxHeight, null, progress), ct);

    public Task<DownloadResult> DownloadAsync(
        MediaStream stream,
        string path,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default) =>
        _strategy.DownloadAsync(stream, path, progress, ct);

    public async Task<DownloadResult> DownloadAudioAsync(
        string videoId,
        string path,
        string? audioTrackId = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var manifest = await _videos.GetStreamsAsync(videoId, ct).ConfigureAwait(false);
        var stream = SelectAudioStream(manifest, audioTrackId)
                     ?? throw new YoutubeException($"YouTube returned no audio stream for '{videoId}'.");

        var result = await _strategy.DownloadAsync(stream, path, progress, ct).ConfigureAwait(false);

        result.AudioTrackId ??= stream.AudioTrackId ?? StreamManifest.UnnamedAudioTrackId;
        result.AudioTrackName ??= stream.AudioTrackName ?? "Default";

        return result;
    }

    public async Task<IReadOnlyList<DownloadResult>> DownloadAllAudioTracksAsync(
        string videoId,
        string directory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var manifest = await _videos.GetStreamsAsync(videoId, ct).ConfigureAwait(false);
        var tracks = manifest.GetAudioTracks();

        if (tracks.Count == 0)
            return [];

        Directory.CreateDirectory(directory);

        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jobs = new List<(AudioTrack Track, MediaStream Stream, string Path)>();

        foreach (var track in tracks)
        {
            var stream = manifest.GetBestAudioStream(track.Id);
            if (stream is null)
                continue;

            var extension = ExtensionFor(stream.MimeType);
            var fileName = SanitizeFileName(track.DisplayName);
            var path = EnsureUniquePath(Path.Combine(directory, fileName + extension), usedPaths);

            jobs.Add((track, stream, path));
        }

        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentAudioTracks));
        var results = new List<DownloadResult>(jobs.Count);
        var lockObject = new object();

        var tasks = jobs.Select(async job =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var result = await _strategy
                    .DownloadAsync(job.Stream, job.Path, progress, ct)
                    .ConfigureAwait(false);

                result.AudioTrackId = job.Track.Id;
                result.AudioTrackName = job.Track.DisplayName;

                lock (lockObject)
                    results.Add(result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // A broken track must not fail the other downloads.
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return results
            .OrderBy(result => result.AudioTrackName, StringComparer.Ordinal)
            .ToArray();
    }

    private MediaStream? SelectAudioStream(StreamManifest manifest, string? audioTrackId)
    {
        var trackId = audioTrackId
                      ?? _options.PreferredAudioTrackId
                      ?? (_options.PreferOriginalAudioTrack
                          ? manifest.GetAudioTracks().FirstOrDefault(static t => t.IsOriginal)?.Id
                          : null);

        if (!string.IsNullOrWhiteSpace(trackId))
        {
            var stream = manifest.GetBestAudioStream(trackId);
            if (stream is not null)
                return stream;
        }

        return manifest.GetBestOriginalAudioStream() ?? manifest.GetBestAudioStream();
    }

    private static string ExtensionFor(string? mimeType) => mimeType switch
    {
        not null when mimeType.StartsWith("audio/mp4", StringComparison.OrdinalIgnoreCase) => ".m4a",
        not null when mimeType.StartsWith("audio/webm", StringComparison.OrdinalIgnoreCase) => ".webm",
        _ => ".webm"
    };

    private static string SanitizeFileName(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            builder.Append(character switch
            {
                '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' => '_',
                _ when char.IsControl(character) => '_',
                _ => character
            });
        }

        var result = builder.ToString().Trim().TrimEnd('.');

        return result.Length == 0 ? "audio" : result;
    }

    private static string EnsureUniquePath(string path, HashSet<string> used)
    {
        if (used.Add(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");

            if (used.Add(candidate))
                return candidate;
        }
    }

    public void Dispose() => _ownedStrategy?.Dispose();
}
