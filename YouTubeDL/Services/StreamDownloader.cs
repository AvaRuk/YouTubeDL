using System.Net;
using System.Text.Json;
using YoutubeCulvert.Datatypes;
using YoutubeCulvert.Exceptions;

namespace YouTubeDL.Services;

/// <summary>
/// Range-based media downloader with resume, a sidecar completeness manifest,
/// bandwidth limiting and coalesced progress. It is used instead of the
/// library strategy because the application needs per-download speed limits.
/// </summary>
public sealed class StreamDownloader : IDisposable
{
    private const long ChunkSize = 8L * 1024 * 1024;
    private const int BufferSize = 81_920;
    private const int MaxAttempts = 4;

    private readonly Func<HttpMessageHandler?> _handlerProvider;
    private readonly DiagnosticsLog _log;

    public StreamDownloader(Func<HttpMessageHandler?> handlerProvider, DiagnosticsLog log)
    {
        _log = log;
        _handlerProvider = handlerProvider;
    }

    private HttpClient CreateClient()
    {
        var handler = _handlerProvider();

        var client = handler is null
            ? new HttpClient { Timeout = TimeSpan.FromHours(6) }
            : new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromHours(6) };

        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

        return client;
    }

    public async Task<DownloadResult> DownloadAsync(
        MediaStream stream,
        string path,
        IProgress<DownloadProgress>? progress = null,
        int speedLimitKbps = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stream.Url))
            throw new YoutubeException("The selected stream has no media URL.");

        var uri = new Uri(stream.Url);
        var fullPath = Path.GetFullPath(path);
        var partPath = fullPath + ".part";
        var manifestPath = fullPath + ".part.json";
        var limiter = SpeedLimiter.Create(speedLimitKbps);

        using var http = CreateClient();

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        PreparePartialFile(stream, partPath, manifestPath);

        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var complete = await DownloadChunksAsync(
                        http,
                        uri,
                        partPath,
                        manifestPath,
                        stream,
                        limiter,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!complete)
                    throw new IOException("The server did not report the total size; the file is incomplete.");

                if (File.Exists(partPath))
                    File.Move(partPath, fullPath, overwrite: true);

                FileSystemService.TryDelete(manifestPath);

                var written = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;

                return new DownloadResult
                {
                    FilePath = fullPath,
                    BytesWritten = written,
                    IsComplete = true,
                    Stream = stream,
                    AudioTrackId = stream.AudioTrackId,
                    AudioTrackName = stream.AudioTrackName
                };
            }
            catch (OperationCanceledException)
            {
                FileSystemService.TryDelete(partPath);
                FileSystemService.TryDelete(manifestPath);
                throw;
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or YoutubeApiException)
            {
                lastError = exception;

                if (exception is YoutubeApiException { StatusCode: HttpStatusCode.Forbidden })
                {
                    // Expired stream URL: the caller re-resolves the manifest.
                    throw;
                }

                _log.WriteError($"Attempt {attempt} of '{Path.GetFileName(fullPath)}' failed.", exception);

                if (attempt == MaxAttempts)
                    break;

                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        FileSystemService.TryDelete(partPath);
        FileSystemService.TryDelete(manifestPath);
        throw lastError ?? new IOException("The download failed.");
    }

    private async Task<bool> DownloadChunksAsync(
        HttpClient http,
        Uri uri,
        string partPath,
        string manifestPath,
        MediaStream stream,
        SpeedLimiter? limiter,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        long? totalLength = stream.ContentLength;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existingLength = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

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
            request.Headers.Referrer = new Uri("https://www.youtube.com/");
            request.Headers.TryAddWithoutValidation("Origin", "https://www.youtube.com");
            request.Headers.TryAddWithoutValidation("Accept", "*/*");

            using var response = await http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.Content.Headers.ContentRange?.Length is { } reportedLength)
                totalLength = reportedLength;

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (totalLength is { } completedLength && existingLength >= completedLength)
                    return true;

                FileSystemService.TryDelete(partPath);
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
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                throw new YoutubeApiException(
                    response.StatusCode,
                    stream.Url,
                    body.Length > 4096 ? body[..4096] : body,
                    $"Failed to download the stream: HTTP {(int)response.StatusCode}.");
            }

            var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;

            if (!append && existingLength > 0)
            {
                FileSystemService.TryDelete(partPath);
                existingLength = 0;
            }

            await using (var file = new FileStream(
                             partPath,
                             append ? FileMode.Append : FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: BufferSize,
                             useAsync: true))
            await using (var source = await response.Content
                             .ReadAsStreamAsync(cancellationToken)
                             .ConfigureAwait(false))
            {
                var buffer = new byte[BufferSize];
                long chunkWritten = 0;

                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    chunkWritten += read;

                    if (limiter is not null)
                        await limiter.WaitAsync(read, cancellationToken).ConfigureAwait(false);

                    var writtenNow = existingLength + chunkWritten;

                    if (progress is not null)
                    {
                        progress.Report(new DownloadProgress
                        {
                            FilePath = partPath,
                            Bytes = writtenNow,
                            Total = totalLength,
                            Percent = totalLength is > 0
                                ? Math.Clamp((double)writtenNow / totalLength.Value, 0, 1)
                                : 0,
                            AudioTrackId = stream.AudioTrackId
                        });
                    }
                }
            }

            WriteManifest(manifestPath, totalLength, response);

            var written = File.Exists(partPath) ? new FileInfo(partPath).Length : existingLength;

            if (totalLength is null)
                return false;

            if (written >= totalLength.Value)
                return true;
        }
    }

    private void PreparePartialFile(MediaStream stream, string partPath, string manifestPath)
    {
        if (!File.Exists(partPath))
            return;

        var manifest = ReadManifest(manifestPath);
        var expected = stream.ContentLength;

        if (manifest?.ContentLength is { } saved &&
            expected is { } current &&
            saved != current)
        {
            _log.Write($"Discarding a partial file with a stale size for '{Path.GetFileName(partPath)}'.");
            FileSystemService.TryDelete(partPath);
            FileSystemService.TryDelete(manifestPath);
            return;
        }

        if (manifest is null && expected is > 0)
        {
            FileSystemService.TryDelete(partPath);
        }
    }

    private static void WriteManifest(string path, long? contentLength, HttpResponseMessage response)
    {
        try
        {
            var manifest = new PartialManifest
            {
                ContentLength = contentLength,
                ETag = response.Headers.ETag?.Tag,
                LastModified = response.Content.Headers.LastModified,
                UpdatedAt = DateTimeOffset.Now
            };

            File.WriteAllText(path, JsonSerializer.Serialize(manifest));
        }
        catch (IOException)
        {
        }
    }

    private static PartialManifest? ReadManifest(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<PartialManifest>(File.ReadAllText(path))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
    }

    internal sealed class PartialManifest
    {
        public long? ContentLength { get; set; }

        public string? ETag { get; set; }

        public DateTimeOffset? LastModified { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}
