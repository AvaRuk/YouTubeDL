using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeCulvert.Datatypes;
using YoutubeCulvert.Exceptions;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>
/// End-to-end download pipeline: resolve → download streams → FFmpeg mux or
/// convert → save extra artifacts → history.
/// </summary>
public sealed class DownloadService
{
    private readonly MediaResolver _resolver;
    private readonly StreamDownloader _downloader;
    private readonly FfmpegService _ffmpeg;
    private readonly HistoryStore _history;
    private readonly SettingsService _settings;
    private readonly YoutubeClientManager _clientManager;
    private readonly DiagnosticsLog _log;

    public DownloadService(
        MediaResolver resolver,
        StreamDownloader downloader,
        FfmpegService ffmpeg,
        HistoryStore history,
        SettingsService settings,
        YoutubeClientManager clientManager,
        DiagnosticsLog log)
    {
        _resolver = resolver;
        _downloader = downloader;
        _ffmpeg = ffmpeg;
        _history = history;
        _settings = settings;
        _clientManager = clientManager;
        _log = log;
    }

    public sealed record PreparedDownload(DownloadPlan Plan, HistoryEntry? Existing);

    public sealed record DownloadOutcome(
        DownloadPlan Plan,
        IReadOnlyList<string> Files,
        bool Skipped,
        bool UsedSeparateStreams);

    public async Task<AppResult<PreparedDownload>> PrepareAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        var planResult = await _resolver.BuildPlanAsync(request, cancellationToken).ConfigureAwait(false);
        if (!planResult.IsSuccess)
            return AppResult<PreparedDownload>.Fail(planResult.Error!);

        var plan = planResult.Value!;

        if (_settings.Current.SkipExisting)
        {
            var existing = _history.Find(
                plan.VideoId,
                plan.Kind,
                plan.AudioTrackId,
                plan.QualityLabel,
                ResolveContainerId(plan));

            if (existing is not null)
                return AppResult<PreparedDownload>.Ok(new PreparedDownload(plan, existing));
        }

        if (plan.TotalBytes is { } total && total > 0)
        {
            var directory = Path.GetDirectoryName(plan.OutputPath);
            var free = FileSystemService.GetFreeSpace(
                string.IsNullOrWhiteSpace(directory) ? AppPaths.DataDirectory : directory);

            if (free is { } available && available < total * 2)
            {
                return AppResult<PreparedDownload>.Fail(new ItemError(
                    ErrorCategory.Terminal,
                    LocalizationService.Instance.Format(
                        "ErrorNotEnoughSpace",
                        HistoryEntry.FormatSize(Math.Max(0, available)),
                        HistoryEntry.FormatSize(total * 2))));
            }
        }

        return AppResult<PreparedDownload>.Ok(new PreparedDownload(plan, null));
    }

    public async Task<AppResult<DownloadOutcome>> RunAsync(
        DownloadRequest request,
        IProgress<ProgressSnapshot>? progress = null,
        IProgress<DownloadStage>? stage = null,
        CancellationToken cancellationToken = default,
        Action<DownloadPlan>? preparedCallback = null)
    {
        PreparedDownload? prepared = null;

        try
        {
            var preparedResult = await PrepareAsync(request, cancellationToken).ConfigureAwait(false);

            if (!preparedResult.IsSuccess)
                return AppResult<DownloadOutcome>.Fail(preparedResult.Error!);

            prepared = preparedResult.Value!;
            preparedCallback?.Invoke(prepared.Plan);

            if (prepared.Existing is { } existing)
            {
                _log.Write($"Skipping '{prepared.Plan.Title}' ({prepared.Plan.VideoId}): already downloaded.");
                return AppResult<DownloadOutcome>.Ok(new DownloadOutcome(
                    prepared.Plan,
                    [existing.FilePath],
                    Skipped: true,
                    UsedSeparateStreams: false));
            }

            var plan = prepared.Plan;

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var outcome = await DownloadCoreAsync(plan, progress, stage, cancellationToken)
                        .ConfigureAwait(false);

                    AddHistory(plan, outcome.Files, succeeded: true);
                    return AppResult<DownloadOutcome>.Ok(outcome);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (YoutubeApiException exception) when (
                    exception.StatusCode == HttpStatusCode.Forbidden && attempt == 0)
                {
                    _log.Write("Media URL expired (HTTP 403); re-resolving the streams and retrying once.");

                    var refreshed = await _resolver
                        .BuildPlanAsync(request, cancellationToken)
                        .ConfigureAwait(false);

                    if (!refreshed.IsSuccess)
                        return AppResult<DownloadOutcome>.Fail(refreshed.Error!);

                    plan = refreshed.Value! with { OutputPath = plan.OutputPath };
                }
            }
        }
        catch (OperationCanceledException)
        {
            return AppResult<DownloadOutcome>.Fail(new ItemError(
                ErrorCategory.Canceled,
                LocalizationService.Instance.Get("ErrorCanceled")));
        }
        catch (DownloadException exception)
        {
            AddHistory(prepared?.Plan, [], succeeded: false, exception.Error.Message);
            return AppResult<DownloadOutcome>.Fail(exception.Error);
        }
        catch (LocalizedException exception)
        {
            var error = new ItemError(
                ErrorCategory.Terminal,
                LocalizationService.Instance.Get(exception.ResourceKey));

            _log.WriteError("The download failed.", exception);
            AddHistory(prepared?.Plan, [], succeeded: false, error.Message);
            return AppResult<DownloadOutcome>.Fail(error);
        }
        catch (YoutubeApiException exception)
        {
            var error = Classify(exception);
            _log.WriteError("The download failed.", exception);
            AddHistory(prepared?.Plan, [], succeeded: false, error.Message);
            return AppResult<DownloadOutcome>.Fail(error);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            _log.WriteError("The download failed.", exception);
            var error = new ItemError(ErrorCategory.Retryable, exception.Message, ErrorAction.Retry);
            AddHistory(prepared?.Plan, [], succeeded: false, error.Message);
            return AppResult<DownloadOutcome>.Fail(error);
        }
        catch (Exception exception)
        {
            _log.WriteError("Unexpected download failure.", exception);
            AddHistory(prepared?.Plan, [], succeeded: false, exception.Message);
            return AppResult<DownloadOutcome>.Fail(new ItemError(
                ErrorCategory.Retryable,
                LocalizationService.Instance.Get("ErrorDownloadFailed"),
                ErrorAction.Retry));
        }
    }

    private async Task<DownloadOutcome> DownloadCoreAsync(
        DownloadPlan plan,
        IProgress<ProgressSnapshot>? progress,
        IProgress<DownloadStage>? stage,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(plan.OutputPath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = AppPaths.DataDirectory;

        Directory.CreateDirectory(directory);

        var temporaryFiles = new List<string>();
        var files = new List<string> { plan.OutputPath };
        var usedSeparateStreams = false;
        var speedLimit = plan.SpeedLimitKbps ?? _settings.Current.SpeedLimitKbps;

        try
        {
            stage?.Report(DownloadStage.Resolving);

            var coverPath = await SaveCoverAsync(plan, directory, temporaryFiles, cancellationToken)
                .ConfigureAwait(false);
            var metadata = BuildMetadata(plan);

            if (plan.IsAudioOnly)
            {
                await DownloadAudioOnlyAsync(
                        plan, directory, coverPath, metadata, temporaryFiles,
                        progress, stage, speedLimit, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (plan.NeedsMux && plan.AudioStream is not null)
            {
                var separateFiles = await DownloadAndMuxAsync(
                        plan, directory, coverPath, metadata, temporaryFiles,
                        progress, stage, speedLimit, cancellationToken)
                    .ConfigureAwait(false);

                if (separateFiles is { Count: > 0 })
                {
                    files.Clear();
                    files.AddRange(separateFiles);
                    usedSeparateStreams = true;
                }
            }
            else
            {
                await DownloadSingleVideoAsync(
                        plan, directory, coverPath, metadata, temporaryFiles,
                        progress, stage, speedLimit, cancellationToken)
                    .ConfigureAwait(false);
            }

            stage?.Report(DownloadStage.SavingArtifacts);
            await SaveArtifactsAsync(plan, coverPath, cancellationToken).ConfigureAwait(false);

            progress?.Report(new ProgressSnapshot(0, null, 1, 0, null, null, plan.OutputPath));
            stage?.Report(DownloadStage.Done);

            return new DownloadOutcome(plan, files, Skipped: false, UsedSeparateStreams: usedSeparateStreams);
        }
        finally
        {
            foreach (var temporaryFile in temporaryFiles)
                FileSystemService.TryDelete(temporaryFile);
        }
    }

    private async Task DownloadAudioOnlyAsync(
        DownloadPlan plan,
        string directory,
        string? coverPath,
        IReadOnlyDictionary<string, string> metadata,
        List<string> temporaryFiles,
        IProgress<ProgressSnapshot>? progress,
        IProgress<DownloadStage>? stage,
        int speedLimit,
        CancellationToken cancellationToken)
    {
        if (plan.AudioStream is null)
            throw new DownloadException(new ItemError(
                ErrorCategory.Terminal,
                LocalizationService.Instance.Get("ErrorNoAudioStream")));

        var sourceExtension = StreamExtension(plan.AudioStream, audio: true);
        var temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.audio{sourceExtension}");
        temporaryFiles.Add(temporaryPath);

        stage?.Report(DownloadStage.DownloadingAudio);

        using var throttled = new ThrottledProgress(snapshot =>
            progress?.Report(snapshot with { Percent = snapshot.Percent * 0.85 }));

        await _downloader.DownloadAsync(
                plan.AudioStream,
                temporaryPath,
                throttled,
                speedLimit,
                cancellationToken)
            .ConfigureAwait(false);

        var targetExtension = Path.GetExtension(plan.OutputPath);
        if (plan.NeedsAudioConversion || !PathsEqual(sourceExtension, targetExtension))
        {
            stage?.Report(DownloadStage.Converting);

            if (!_ffmpeg.IsAvailable)
                throw new DownloadException(new ItemError(
                    ErrorCategory.Terminal,
                    LocalizationService.Instance.Get("ErrorFfmpegMissing")));

            var conversion = plan.NeedsAudioConversion
                ? plan.Container.Conversion ?? targetExtension.TrimStart('.')
                : targetExtension.TrimStart('.');

            await _ffmpeg.ConvertAudioAsync(
                    temporaryPath,
                    plan.OutputPath,
                    conversion,
                    metadata,
                    coverPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if ((plan.EmbedMetadata || coverPath is not null) && _ffmpeg.IsAvailable)
        {
            stage?.Report(DownloadStage.Converting);

            await _ffmpeg.TagAsync(
                    temporaryPath,
                    plan.OutputPath,
                    ResolveContainerId(plan),
                    metadata,
                    coverPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            File.Move(temporaryPath, plan.OutputPath, overwrite: true);
        }
    }

    private async Task<IReadOnlyList<string>?> DownloadAndMuxAsync(
        DownloadPlan plan,
        string directory,
        string? coverPath,
        IReadOnlyDictionary<string, string> metadata,
        List<string> temporaryFiles,
        IProgress<ProgressSnapshot>? progress,
        IProgress<DownloadStage>? stage,
        int speedLimit,
        CancellationToken cancellationToken)
    {
        if (plan.VideoStream is null || plan.AudioStream is null)
            throw new DownloadException(new ItemError(
                ErrorCategory.Terminal,
                LocalizationService.Instance.Get("ErrorNoVideoStream")));

        var videoExtension = StreamExtension(plan.VideoStream, audio: false);
        var audioExtension = StreamExtension(plan.AudioStream, audio: true);
        var videoPath = Path.Combine(directory, $".{Guid.NewGuid():N}.video{videoExtension}");
        var audioPath = Path.Combine(directory, $".{Guid.NewGuid():N}.audio{audioExtension}");
        temporaryFiles.Add(videoPath);
        temporaryFiles.Add(audioPath);

        stage?.Report(DownloadStage.DownloadingVideo);

        using (var videoProgress = new ThrottledProgress(snapshot =>
                   progress?.Report(snapshot with { Percent = snapshot.Percent * 0.55 })))
        {
            await _downloader.DownloadAsync(
                    plan.VideoStream,
                    videoPath,
                    videoProgress,
                    speedLimit,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        stage?.Report(DownloadStage.DownloadingAudio);
        progress?.Report(new ProgressSnapshot(0, null, 0.55, 0, null, plan.AudioTrackId, plan.OutputPath));

        using (var audioProgress = new ThrottledProgress(snapshot =>
                   progress?.Report(snapshot with { Percent = 0.55 + (snapshot.Percent * 0.35) })))
        {
            await _downloader.DownloadAsync(
                    plan.AudioStream,
                    audioPath,
                    audioProgress,
                    speedLimit,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!_ffmpeg.IsAvailable)
        {
            // No FFmpeg: keep the streams as separate files and warn the user.
            var videoTarget = Path.ChangeExtension(plan.OutputPath, videoExtension);
            var audioTarget = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(plan.OutputPath) + ".audio" + audioExtension);

            File.Move(videoPath, videoTarget, overwrite: true);
            File.Move(audioPath, audioTarget, overwrite: true);

            _log.Write("FFmpeg is unavailable; the video and audio streams were saved as separate files.");

            return new[] { videoTarget, audioTarget };
        }

        stage?.Report(DownloadStage.Muxing);
        progress?.Report(new ProgressSnapshot(0, null, 0.93, 0, null, null, plan.OutputPath));

        await _ffmpeg.MuxAsync(
                videoPath,
                audioPath,
                plan.OutputPath,
                ResolveContainerId(plan),
                plan.VideoStream.Height,
                plan.VideoStream.Codecs,
                plan.AudioStream.Codecs,
                metadata,
                coverPath,
                plan.Chapters,
                plan.Duration,
                cancellationToken)
            .ConfigureAwait(false);

        return null;
    }

    private async Task<bool> DownloadSingleVideoAsync(
        DownloadPlan plan,
        string directory,
        string? coverPath,
        IReadOnlyDictionary<string, string> metadata,
        List<string> temporaryFiles,
        IProgress<ProgressSnapshot>? progress,
        IProgress<DownloadStage>? stage,
        int speedLimit,
        CancellationToken cancellationToken)
    {
        if (plan.VideoStream is null)
            throw new DownloadException(new ItemError(
                ErrorCategory.Terminal,
                LocalizationService.Instance.Get("ErrorNoVideoStream")));

        var sourceExtension = StreamExtension(plan.VideoStream, audio: false);
        var temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.media{sourceExtension}");
        temporaryFiles.Add(temporaryPath);

        stage?.Report(DownloadStage.DownloadingVideo);

        using (var throttled = new ThrottledProgress(snapshot =>
                   progress?.Report(snapshot with { Percent = snapshot.Percent * 0.9 })))
        {
            await _downloader.DownloadAsync(
                    plan.VideoStream,
                    temporaryPath,
                    throttled,
                    speedLimit,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var targetExtension = Path.GetExtension(plan.OutputPath);
        var needsRemux = plan.NeedsRemux ||
                         !PathsEqual(sourceExtension, targetExtension) ||
                         plan.EmbedMetadata ||
                         coverPath is not null ||
                         plan.Chapters.Count > 0;

        if (needsRemux)
        {
            if (!_ffmpeg.IsAvailable)
            {
                if (!PathsEqual(sourceExtension, targetExtension))
                {
                    throw new DownloadException(new ItemError(
                        ErrorCategory.Terminal,
                        LocalizationService.Instance.Get("ErrorFfmpegMissing")));
                }

                // Without FFmpeg the muxed stream is saved as-is; artifacts are skipped.
                File.Move(temporaryPath, plan.OutputPath, overwrite: true);
                _log.Write("FFmpeg is unavailable; the stream was saved without remuxing.");
                return false;
            }

            stage?.Report(DownloadStage.Muxing);

            await _ffmpeg.RemuxAsync(
                    temporaryPath,
                    plan.OutputPath,
                    ResolveContainerId(plan),
                    plan.VideoStream.Codecs,
                    plan.VideoStream.MimeType?.Contains("webm", StringComparison.OrdinalIgnoreCase) == true
                        ? "opus"
                        : "mp4a",
                    metadata,
                    coverPath,
                    plan.Chapters,
                    plan.Duration,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            File.Move(temporaryPath, plan.OutputPath, overwrite: true);
        }

        return false;
    }

    private async Task<string?> SaveCoverAsync(
        DownloadPlan plan,
        string directory,
        List<string> temporaryFiles,
        CancellationToken cancellationToken)
    {
        if ((!plan.EmbedMetadata && !plan.SaveThumbnail) || plan.ThumbnailUrl is null)
            return null;

        var bytes = await ThumbnailLoader
            .DownloadBytesAsync(plan.ThumbnailUrl, cancellationToken)
            .ConfigureAwait(false);

        if (bytes is null || bytes.Length == 0)
            return null;

        var path = Path.Combine(directory, $".{Guid.NewGuid():N}.cover.jpg");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        temporaryFiles.Add(path);

        if (plan.SaveThumbnail)
        {
            var sidecar = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(plan.OutputPath) + ".jpg");

            try
            {
                await File.WriteAllBytesAsync(sidecar, bytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _log.WriteError("Could not save the thumbnail sidecar.", exception);
            }
        }

        return path;
    }

    private async Task SaveArtifactsAsync(
        DownloadPlan plan,
        string? coverPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(plan.OutputPath) ?? AppPaths.DataDirectory;
        var baseName = Path.GetFileNameWithoutExtension(plan.OutputPath);

        if (plan.SaveDescription && !string.IsNullOrWhiteSpace(plan.Description))
        {
            var path = Path.Combine(directory, baseName + ".description");
            await File.WriteAllTextAsync(path, plan.Description, cancellationToken).ConfigureAwait(false);
        }

        if (plan.SaveJsonMetadata)
        {
            var path = Path.Combine(directory, baseName + ".info.json");
            var json = JsonSerializer.Serialize(
                new
                {
                    id = plan.VideoId,
                    title = plan.Title,
                    channel = plan.ChannelTitle,
                    channelId = plan.ChannelId,
                    duration = plan.Duration.ToString(),
                    uploadDate = plan.UploadDate,
                    viewCount = plan.ViewCount,
                    quality = plan.QualityLabel,
                    container = ResolveContainerId(plan),
                    audioTrackId = plan.AudioTrackId,
                    audioTrackName = plan.AudioTrackName,
                    playlist = plan.PlaylistName,
                    playlistIndex = plan.PlaylistIndex,
                    url = $"https://www.youtube.com/watch?v={plan.VideoId}",
                    description = plan.Description
                },
                new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }

        if (plan.SaveChapters && plan.Chapters.Count > 0)
        {
            var builder = new StringBuilder();

            foreach (var chapter in plan.Chapters)
            {
                builder.Append(chapter.Start.ToString(@"hh\:mm\:ss"))
                    .Append(' ')
                    .AppendLine(chapter.Title);
            }

            var path = Path.Combine(directory, baseName + ".chapters.txt");
            await File.WriteAllTextAsync(path, builder.ToString(), cancellationToken).ConfigureAwait(false);
        }

        if (plan.DownloadSubtitles)
            await SaveSubtitlesAsync(plan, directory, baseName, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveSubtitlesAsync(
        DownloadPlan plan,
        string directory,
        string baseName,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _clientManager.Client;
            var tracksResult = await client.Transcripts
                .TryListTracksAsync(plan.VideoId, cancellationToken)
                .ConfigureAwait(false);

            if (tracksResult.Value is not { Count: > 0 } tracks)
                return;

            foreach (var track in tracks)
            {
                var transcriptResult = await client.Transcripts
                    .TryGetAsync(plan.VideoId, track.LanguageCode, includeAutoGenerated: true, ct: cancellationToken)
                    .ConfigureAwait(false);

                if (transcriptResult.Value is not { Segments.Count: > 0 } transcript)
                    continue;

                var path = Path.Combine(
                    directory,
                    $"{baseName}.{(string.IsNullOrWhiteSpace(track.LanguageCode) ? "und" : track.LanguageCode)}.srt");

                await File.WriteAllTextAsync(path, BuildSrt(transcript.Segments), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is YoutubeException or HttpRequestException or IOException)
        {
            _log.WriteError($"Saving subtitles of '{plan.VideoId}' failed.", exception);
        }
    }

    private static string BuildSrt(IReadOnlyList<TranscriptSegment> segments)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];

            builder.AppendLine((index + 1).ToString())
                .Append(FormatSrtTime(segment.Start))
                .Append(" --> ")
                .AppendLine(FormatSrtTime(segment.End))
                .AppendLine(segment.Text)
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatSrtTime(TimeSpan time) =>
        $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";

    private static Dictionary<string, string> BuildMetadata(DownloadPlan plan)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = plan.Title,
            ["artist"] = plan.ChannelTitle,
            ["album"] = plan.PlaylistName ?? "YouTube",
            ["comment"] = $"https://www.youtube.com/watch?v={plan.VideoId}"
        };

        if (plan.UploadDate is { } uploadDate)
            metadata["date"] = uploadDate.ToString("yyyyMMdd");

        return metadata;
    }

    private void AddHistory(
        DownloadPlan? plan,
        IReadOnlyList<string> files,
        bool succeeded,
        string? error = null)
    {
        if (plan is null)
            return;

        var path = files.FirstOrDefault() ?? plan.OutputPath;
        var size = File.Exists(path) ? new FileInfo(path).Length : 0;

        _history.Add(new HistoryEntry
        {
            VideoId = plan.VideoId,
            Title = plan.Title,
            ChannelTitle = plan.ChannelTitle,
            Kind = plan.Kind,
            AudioTrackId = plan.AudioTrackId,
            AudioTrackName = plan.AudioTrackName,
            Quality = plan.QualityLabel,
            Container = ResolveContainerId(plan),
            FilePath = path,
            FileSize = size,
            Duration = plan.Duration,
            UploadDate = plan.UploadDate,
            PlaylistName = plan.PlaylistName,
            DownloadedAt = DateTimeOffset.Now,
            Succeeded = succeeded,
            ErrorText = error
        });
    }

    private static ItemError Classify(YoutubeApiException exception) => exception.StatusCode switch
    {
        HttpStatusCode.Unauthorized => new ItemError(
            ErrorCategory.Terminal,
            LocalizationService.Instance.Get("ErrorAuthRequired"),
            ErrorAction.SignIn),
        HttpStatusCode.Forbidden => new ItemError(
            ErrorCategory.Retryable,
            LocalizationService.Instance.Get("ErrorStreamForbidden"),
            ErrorAction.Retry),
        HttpStatusCode.TooManyRequests => new ItemError(
            ErrorCategory.Retryable,
            LocalizationService.Instance.Get("ErrorRateLimited"),
            ErrorAction.Retry),
        _ => new ItemError(
            ErrorCategory.Retryable,
            exception.Message,
            ErrorAction.Retry)
    };

    private static string ResolveContainerId(DownloadPlan plan)
    {
        if (plan.Container.Id is not "auto")
            return plan.Container.Id;

        var extension = Path.GetExtension(plan.OutputPath).TrimStart('.').ToLowerInvariant();

        return extension switch
        {
            "webm" => "webm",
            "mp4" => "mp4",
            "m4a" => "m4a",
            "mkv" => "mkv",
            "mp3" => "mp3",
            "opus" => "opus",
            "flac" => "flac",
            _ => "mp4"
        };
    }

    private static string StreamExtension(MediaStream stream, bool audio)
    {
        var mime = stream.MimeType ?? string.Empty;

        if (mime.Contains("webm", StringComparison.OrdinalIgnoreCase))
            return ".webm";

        return audio ? ".m4a" : ".mp4";
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
