using System.Net;
using System.Text;
using YoutubeCulvert.Http;

namespace YoutubeCulvert.Downloads;

/// <summary>Resolves a <see cref="StreamRequest"/> against the stream manifest.</summary>
public sealed class StreamResolver : IStreamResolver
{
    private readonly IVideosService _videos;

    public StreamResolver(IVideosService videos) => _videos = videos;

    public async Task<MediaStream?> ResolveAsync(StreamRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VideoId);

        var manifest = await _videos.GetStreamsAsync(request.VideoId, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.AudioTrackId))
            return manifest.GetBestAudioStream(request.AudioTrackId);

        if (request.Itag is { } itag)
            return manifest.GetStreamByItag(itag);

        if (!string.IsNullOrWhiteSpace(request.QualityLabel))
            return manifest.GetStreamByQuality(request.QualityLabel);

        if (request.PreferMuxed && manifest.GetBestMuxedStream(request.MaxHeight) is { } muxed)
            return muxed;

        return manifest.GetBestMuxedStream(request.MaxHeight)
               ?? manifest.GetBestVideoStream(request.MaxHeight)
               ?? manifest.GetBestAudioStream();
    }
}

/// <summary>
/// Range-based download strategy. Resumes partial files, distinguishes 200
/// (server ignored Range → rewrite) from 206 (resume), validates completeness
/// against Content-Length/Content-Range and retries transient failures.
/// </summary>
public sealed class RangeDownloadStrategy : IDownloadStrategy, IDisposable
{
    private const long ChunkSize = 8L * 1024 * 1024;
    private const int MaxAttempts = 5;
    private const int BufferSize = 81_920;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string? _userAgent;

    public RangeDownloadStrategy(HttpMessageHandler? handler = null, string? userAgent = null)
    {
        _userAgent = userAgent;
        _ownsClient = handler is null;

        _http = handler is null
            ? new HttpClient { Timeout = TimeSpan.FromHours(6) }
            : new HttpClient(handler, disposeHandler: false);
    }

    public async Task<DownloadResult> DownloadAsync(
        MediaStream stream,
        string path,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var uri = ValidateUrl(stream);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var complete = await DownloadChunksAsync(stream, uri, fullPath, progress, ct)
                    .ConfigureAwait(false);

                var written = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;

                return new DownloadResult
                {
                    FilePath = fullPath,
                    BytesWritten = written,
                    IsComplete = complete,
                    Stream = stream,
                    AudioTrackId = stream.AudioTrackId,
                    AudioTrackName = stream.AudioTrackName ?? (stream.IsAudioOnly ? "Default" : null)
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or HttpRequestException or YoutubeApiException)
            {
                lastError = exception;

                if (attempt == MaxAttempts)
                    break;

                await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
        }

        throw lastError ?? new IOException("The download failed.");
    }

    private async Task<bool> DownloadChunksAsync(
        MediaStream stream,
        Uri uri,
        string filePath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        long? totalLength = stream.ContentLength;
        double lastReported = -1;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var existingLength = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;

            if (totalLength is > 0 && existingLength >= totalLength.Value)
                return true;

            var start = existingLength;
            var end = totalLength is > 0
                ? Math.Min(start + ChunkSize - 1, totalLength.Value - 1)
                : start + ChunkSize - 1;

            if (end < start)
                return totalLength is not null;

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);
            ApplyHeaders(request);

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (response.Content.Headers.ContentRange?.Length is { } reportedLength)
                totalLength = reportedLength;

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (totalLength is { } completedLength && existingLength >= completedLength)
                    return true;

                TryDelete(filePath);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new YoutubeApiException(
                    response.StatusCode,
                    stream.Url,
                    null,
                    "YouTube rejected the media range request (HTTP 403).");
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                throw new YoutubeApiException(
                    response.StatusCode,
                    stream.Url,
                    HttpTransport.Truncate(body),
                    $"Failed to download the stream: HTTP {(int)response.StatusCode}.",
                    ReadRetryAfter(response));
            }

            var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;

            if (!append && existingLength > 0)
            {
                TryDelete(filePath);
                existingLength = 0;
            }

            long chunkWritten;

            await using (var file = new FileStream(
                             filePath,
                             append ? FileMode.Append : FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: BufferSize,
                             useAsync: true))
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            {
                var buffer = new byte[BufferSize];
                chunkWritten = 0;

                int read;

                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    chunkWritten += read;
                }
            }

            var written = File.Exists(filePath) ? new FileInfo(filePath).Length : existingLength + chunkWritten;

            if (progress is not null && totalLength is > 0)
            {
                var value = Math.Clamp((double)written / totalLength.Value, 0, 1);

                if (value - lastReported >= 0.01 || value >= 1)
                {
                    progress.Report(new DownloadProgress
                    {
                        FilePath = filePath,
                        Bytes = written,
                        Total = totalLength,
                        Percent = value,
                        AudioTrackId = stream.AudioTrackId
                    });

                    lastReported = value;
                }
            }

            if (totalLength is null)
            {
                // Without a server-reported size the file cannot be verified as complete.
                return false;
            }

            if (written >= totalLength.Value)
                return true;
        }
    }

    private Uri ValidateUrl(MediaStream stream)
    {
        if (string.IsNullOrWhiteSpace(stream.Url))
            throw new ArgumentException("The stream does not contain a URL.", nameof(stream));

        if (!Uri.TryCreate(stream.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The stream URL must be an absolute HTTP(S) URL.", nameof(stream));
        }

        return uri;
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_userAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        else if (_ownsClient)
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

        request.Headers.Referrer = new Uri("https://www.youtube.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.youtube.com");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta)
            return delta;

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : null;
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }
}

/// <summary>Links a video file and its companion audio file.</summary>
public sealed class CompanionFileManager : ICompanionFileManager
{
    public void Attach(DownloadResult video, DownloadResult? audio)
    {
        ArgumentNullException.ThrowIfNull(video);

        if (audio is null || ReferenceEquals(video, audio))
            return;

        if (video.Stream?.IsAudioOnly == true)
        {
            video.CompanionVideoPath = audio.FilePath;
            audio.CompanionAudioPath = video.FilePath;
        }
        else
        {
            video.CompanionAudioPath = audio.FilePath;
            audio.CompanionVideoPath = video.FilePath;
        }
    }
}

/// <summary>Orchestrates resolve → download → companion files.</summary>
public sealed class DownloadCoordinator : IDownloadCoordinator
{
    private readonly IStreamResolver _resolver;
    private readonly IDownloadStrategy _strategy;
    private readonly ICompanionFileManager _companions;
    private readonly IVideosService _videos;

    public DownloadCoordinator(
        IStreamResolver resolver,
        IDownloadStrategy strategy,
        ICompanionFileManager companions,
        IVideosService videos)
    {
        _resolver = resolver;
        _strategy = strategy;
        _companions = companions;
        _videos = videos;
    }

    public async Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);

        if (!string.IsNullOrWhiteSpace(request.AudioTrackId))
        {
            var audio = await _resolver
                .ResolveAsync(new StreamRequest(request.VideoId, AudioTrackId: request.AudioTrackId), ct)
                .ConfigureAwait(false)
                ?? throw new YoutubeException(
                    $"No audio stream of track '{request.AudioTrackId}' for video '{request.VideoId}'.");

            return await _strategy.DownloadAsync(audio, request.Path, request.Progress, ct)
                .ConfigureAwait(false);
        }

        var stream = await _resolver
            .ResolveAsync(
                new StreamRequest(request.VideoId, request.MaxHeight, PreferMuxed: true),
                ct)
            .ConfigureAwait(false)
            ?? throw new YoutubeException($"YouTube returned no downloadable stream for '{request.VideoId}'.");

        var result = await _strategy.DownloadAsync(stream, request.Path, request.Progress, ct)
            .ConfigureAwait(false);

        if (stream.IsMuxed || stream.IsAudioOnly)
            return result;

        var manifest = await _videos.GetStreamsAsync(request.VideoId, ct).ConfigureAwait(false);
        var audioStream = manifest.GetBestAudioStream();

        if (audioStream is null)
            return result;

        var audioPath = BuildCompanionPath(request.Path, "audio", audioStream.MimeType);
        var audioResult = await _strategy.DownloadAsync(audioStream, audioPath, progress: null, ct)
            .ConfigureAwait(false);

        _companions.Attach(result, audioResult);

        return result;
    }

    internal static string BuildCompanionPath(string videoPath, string suffix, string? mimeType)
    {
        var fullPath = Path.GetFullPath(videoPath);
        var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(fullPath) + "." + suffix;

        var extension = mimeType switch
        {
            not null when mimeType.StartsWith("audio/mp4", StringComparison.OrdinalIgnoreCase) => ".m4a",
            not null when mimeType.StartsWith("audio/webm", StringComparison.OrdinalIgnoreCase) => ".webm",
            not null when mimeType.StartsWith("video/mp4", StringComparison.OrdinalIgnoreCase) => ".mp4",
            not null when mimeType.StartsWith("video/webm", StringComparison.OrdinalIgnoreCase) => ".webm",
            _ => ".webm"
        };

        return Path.Combine(directory, name + extension);
    }
}
