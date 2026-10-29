using YoutubeCulvert;
using YoutubeCulvert.Datatypes;
using YoutubeCulvert.Exceptions;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>Resolves videos, streams and audio tracks and builds a <see cref="DownloadPlan"/>.</summary>
public sealed class MediaResolver
{
    private readonly YoutubeClientManager _clientManager;
    private readonly SettingsService _settings;
    private readonly HealthMonitor _health;
    private readonly DiagnosticsLog _log;

    public MediaResolver(
        YoutubeClientManager clientManager,
        SettingsService settings,
        HealthMonitor health,
        DiagnosticsLog log)
    {
        _clientManager = clientManager;
        _settings = settings;
        _health = health;
        _log = log;
    }

    public async Task<AppResult<Video>> LoadVideoAsync(string input, CancellationToken cancellationToken = default)
    {
        var client = _clientManager.Client;

        if (!client.YoutubeUrl.TryParseVideoId(input, out var videoId) || string.IsNullOrWhiteSpace(videoId))
            return AppResult<Video>.Fail(Terminal("ErrorInvalidUrl"));

        try
        {
            var result = await client.Videos.TryGetAsync(videoId, cancellationToken).ConfigureAwait(false);

            if (result.Error is { } error)
            {
                ReportFailure(error);
                return AppResult<Video>.Fail(Map(error));
            }

            var video = result.Value!;
            _health.ReportSuccess();

            if (!video.PlayabilityStatus.IsOk)
                return AppResult<Video>.Fail(Terminal("ErrorVideoUnavailable", ErrorAction.OpenOnYouTube));

            return AppResult<Video>.Ok(video);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _log.WriteError($"Loading video '{videoId}' failed.", exception);
            _health.ReportFailure(exception.Message, isNetwork: true);
            return AppResult<Video>.Fail(NetworkError(exception));
        }
    }

    public async Task<AppResult<IReadOnlyList<AudioTrackOption>>> LoadAudioTracksAsync(
        string videoId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _clientManager.Client.Videos
                .TryGetAudioTracksAsync(videoId, cancellationToken)
                .ConfigureAwait(false);

            if (result.Error is { } error)
            {
                ReportFailure(error);
                return AppResult<IReadOnlyList<AudioTrackOption>>.Fail(Map(error));
            }

            var options = new List<AudioTrackOption>();

            foreach (var track in result.Value ?? [])
            {
                options.Add(new AudioTrackOption(
                    track.Id,
                    track.DisplayName,
                    track.LanguageCode,
                    track.IsDefault,
                    track.IsOriginal));
            }

            return AppResult<IReadOnlyList<AudioTrackOption>>.Ok(options);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _log.WriteError($"Loading audio tracks of '{videoId}' failed.", exception);
            return AppResult<IReadOnlyList<AudioTrackOption>>.Ok([]);
        }
    }

    public async Task<AppResult<DownloadPlan>> BuildPlanAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        var videoResult = await LoadVideoAsync(request.Input, cancellationToken).ConfigureAwait(false);
        if (!videoResult.IsSuccess)
            return AppResult<DownloadPlan>.Fail(videoResult.Error!);

        var video = videoResult.Value!;
        var client = _clientManager.Client;

        try
        {
            var streamsResult = await client.Videos
                .TryGetStreamsAsync(video.Id, cancellationToken)
                .ConfigureAwait(false);

            if (streamsResult.Error is { } streamsError)
            {
                ReportFailure(streamsError);
                return AppResult<DownloadPlan>.Fail(Map(streamsError));
            }

            var manifest = streamsResult.Value!;

            if (manifest.Streams.Count == 0)
                return AppResult<DownloadPlan>.Fail(Terminal("ErrorNoStreams"));

            IReadOnlyList<Chapter> chapters = request.SaveChapters || request.EmbedMetadata
                ? await LoadChaptersAsync(video.Id, cancellationToken).ConfigureAwait(false)
                : [];

            var plan = CreatePlan(request, video, manifest, chapters);
            return plan is null
                ? AppResult<DownloadPlan>.Fail(Terminal("ErrorNoStreams"))
                : AppResult<DownloadPlan>.Ok(plan);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _log.WriteError($"Resolving streams of '{video.Id}' failed.", exception);
            _health.ReportFailure(exception.Message, isNetwork: true);
            return AppResult<DownloadPlan>.Fail(NetworkError(exception));
        }
    }

    public async Task<IReadOnlyList<int>> GetAvailableHeightsAsync(
        string videoId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _clientManager.Client.Videos
                .TryGetStreamsAsync(videoId, cancellationToken)
                .ConfigureAwait(false);

            var manifest = result.Value;
            if (manifest is null)
                return [];

            var heights = manifest.VideoOnly
                .Select(static stream => stream.Height)
                .Where(static height => height is > 0)
                .Select(static height => height!.Value)
                .Distinct()
                .OrderByDescending(static height => height)
                .ToArray();

            if (heights.Length > 0)
                return heights;

            return manifest.Streams
                .Where(static stream => !stream.IsAudioOnly)
                .Select(static stream => stream.Height)
                .Where(static height => height is > 0)
                .Select(static height => height!.Value)
                .Distinct()
                .OrderByDescending(static height => height)
                .ToArray();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _log.WriteError($"Reading available heights of '{videoId}' failed.", exception);
            return [];
        }
    }

    private async Task<IReadOnlyList<Chapter>> LoadChaptersAsync(string videoId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _clientManager.Client.Videos
                .TryGetChaptersAsync(videoId, cancellationToken)
                .ConfigureAwait(false);

            return result.Value ?? [];
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return [];
        }
    }

    private DownloadPlan? CreatePlan(
        DownloadRequest request,
        Video video,
        StreamManifest manifest,
        IReadOnlyList<Chapter> chapters)
    {
        var settings = _settings.Current;
        var format = MediaFormat.Parse(request.FormatId, request.Kind);
        var quality = VideoQuality.FromId(request.QualityId);
        var maxHeight = quality.MaxHeight;

        var selectedTrackId = ResolveAudioTrackId(request.Kind, manifest, request.AudioTrackId);
        var audioStream = SelectAudioStream(manifest, selectedTrackId, request.Kind, format);
        var audioTrack = manifest.GetAudioTracks().FirstOrDefault(track =>
            string.Equals(track.Id, selectedTrackId ?? StreamManifest.UnnamedAudioTrackId, StringComparison.Ordinal));

        var audioTrackName = audioTrack?.DisplayName ?? audioStream?.AudioTrackName;
        var needsAudioConversion = format.IsAudioConversion;

        MediaStream? videoStream = null;
        MediaStream? muxed = null;
        var needsMux = false;
        var needsRemux = false;

        if (request.Kind == DownloadKind.Music)
        {
            if (audioStream is null)
                return null;

            videoStream = audioStream;
        }
        else
        {
            videoStream = SelectVideoStream(manifest, maxHeight, format.PreferWebM);

            if (videoStream is null)
                return null;

            var canUseMuxed =
                string.IsNullOrWhiteSpace(selectedTrackId) &&
                format.Id is not ("mkv" or "webm") &&
                !request.SaveChapters &&
                !request.EmbedMetadata &&
                !request.SaveThumbnail;

            muxed = SelectMuxedStream(manifest, maxHeight, format.PreferWebM);

            if (canUseMuxed && muxed is not null)
            {
                videoStream = muxed;
                audioStream = null;
            }
            else if (muxed is not null && audioStream is null)
            {
                // Only a muxed stream exists; remux it when the container differs.
                videoStream = muxed;
                needsRemux = format.Id == "mkv";
            }
            else
            {
                needsMux = audioStream is not null;
            }
        }

        var extension = FileNameFormatter.ExtensionFor(videoStream, request.Kind, format);

        if (!string.IsNullOrWhiteSpace(request.CustomFilePath))
        {
            var customExtension = Path.GetExtension(request.CustomFilePath).ToLowerInvariant();

            if (customExtension is ".mp4" or ".webm" or ".mkv" or ".m4a" or ".mp3" or ".opus" or ".flac")
            {
                extension = customExtension;
                format = MediaFormat.Parse(customExtension.TrimStart('.'), request.Kind);

            if (customExtension is ".mkv" or ".mp3" or ".opus" or ".flac")
            {
                needsAudioConversion = customExtension is ".mp3" or ".opus" or ".flac";

                if (customExtension == ".mkv")
                {
                    if (audioStream is not null)
                        needsMux = true;
                    else
                        needsRemux = true;
                }
            }
            }
        }

        var outputPath = BuildOutputPath(request, video, format, extension, quality, audioTrackName, settings);

        if (outputPath is null)
            return null;

        return new DownloadPlan
        {
            VideoId = video.Id,
            Title = video.Title,
            ChannelId = video.ChannelId,
            ChannelTitle = video.ChannelTitle ?? string.Empty,
            Duration = video.Duration,
            UploadDate = video.UploadDate ?? video.PublishDate,
            ViewCount = video.ViewCount,
            Description = video.Description,
            ThumbnailUrl = BestThumbnail(video),
            Kind = request.Kind,
            Container = format,
            OutputPath = outputPath,
            VideoStream = request.Kind == DownloadKind.Music ? null : videoStream,
            AudioStream = audioStream,
            AudioTrackId = selectedTrackId,
            AudioTrackName = audioTrackName,
            IsAudioOnly = request.Kind == DownloadKind.Music,
            NeedsMux = request.Kind != DownloadKind.Music && needsMux,
            NeedsRemux = request.Kind != DownloadKind.Music && needsRemux,
            NeedsAudioConversion = needsAudioConversion,
            MaxHeight = maxHeight,
            FileNameTemplate = request.FileNameTemplate,
            PlaylistName = request.PlaylistName,
            PlaylistIndex = request.PlaylistIndex,
            QualityLabel = quality.MaxHeight is { } height ? $"{height}p" : videoStream?.QualityLabel,
            DownloadSubtitles = request.DownloadSubtitles,
            SaveThumbnail = request.SaveThumbnail,
            EmbedMetadata = request.EmbedMetadata,
            SaveDescription = request.SaveDescription,
            SaveJsonMetadata = request.SaveJsonMetadata,
            SaveChapters = request.SaveChapters,
            Chapters = chapters,
            SpeedLimitKbps = request.SpeedLimitKbps,
            VideoBytes = request.Kind == DownloadKind.Music ? null : videoStream?.ContentLength,
            AudioBytes = audioStream?.ContentLength
        };
    }

    private string? BuildOutputPath(
        DownloadRequest request,
        Video video,
        MediaFormat format,
        string extension,
        VideoQuality quality,
        string? audioTrackName,
        AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(request.CustomFilePath))
        {
            var customPath = request.CustomFilePath!;

            if (string.IsNullOrWhiteSpace(Path.GetExtension(customPath)))
                customPath += extension;

            var customDirectory = Path.GetDirectoryName(customPath);
            if (!string.IsNullOrWhiteSpace(customDirectory))
                Directory.CreateDirectory(customDirectory);

            return customPath;
        }

        var folder = !string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? request.OutputDirectory!
            : request.Kind == DownloadKind.Music
                ? settings.MusicFolder
                : settings.VideoFolder;

        if (!string.IsNullOrWhiteSpace(request.PlaylistName))
            folder = Path.Combine(folder, FileNameFormatter.Sanitize(request.PlaylistName));

        var template = string.IsNullOrWhiteSpace(request.FileNameTemplate)
            ? settings.FileNameTemplate
            : request.FileNameTemplate!;

        Directory.CreateDirectory(folder);

        var qualityText = quality.MaxHeight is { } height
            ? $"{height}p"
            : "best";

        var name = FileNameFormatter.Format(
            template,
            video,
            qualityText,
            extension,
            audioTrackName,
            request.PlaylistName,
            request.PlaylistIndex);

        var directory = folder;
        var fileName = name + (template.Contains("%ext", StringComparison.OrdinalIgnoreCase) ? string.Empty : extension);
        var path = Path.Combine(directory, fileName);

        if (!settings.OverwriteExisting)
            path = FileSystemService.EnsureUniquePath(path);

        return path;
    }

    private string? ResolveAudioTrackId(DownloadKind kind, StreamManifest manifest, string? requestedTrackId)
    {
        if (!string.IsNullOrWhiteSpace(requestedTrackId))
            return requestedTrackId;

        var settings = _settings.Current;

        if (!string.IsNullOrWhiteSpace(settings.PreferredAudioTrackId) &&
            manifest.GetAudioStreams(settings.PreferredAudioTrackId).Count > 0)
        {
            return settings.PreferredAudioTrackId;
        }

        if (settings.PreferOriginalAudioTrack &&
            manifest.GetAudioTracks().FirstOrDefault(static track => track.IsOriginal) is { } original)
        {
            return original.Id;
        }

        return null;
    }

    private static MediaStream? SelectAudioStream(
        StreamManifest manifest,
        string? trackId,
        DownloadKind kind,
        MediaFormat format)
    {
        var candidates = !string.IsNullOrWhiteSpace(trackId)
            ? manifest.GetAudioStreams(trackId)
            : manifest.AudioOnly;

        if (candidates.Count == 0)
            return null;

        if (kind == DownloadKind.Music && format.PreferWebM)
        {
            var webm = candidates
                .Where(static stream => IsWebM(stream))
                .OrderByDescending(static stream => stream.Bitrate ?? 0)
                .FirstOrDefault();

            if (webm is not null)
                return webm;
        }

        if (kind == DownloadKind.Music && !format.PreferWebM)
        {
            var mp4 = candidates
                .Where(static stream => !IsWebM(stream))
                .OrderByDescending(static stream => stream.Bitrate ?? 0)
                .FirstOrDefault();

            if (mp4 is not null)
                return mp4;
        }

        return candidates
            .OrderByDescending(static stream => stream.Bitrate ?? 0)
            .First();
    }

    private static MediaStream? SelectVideoStream(StreamManifest manifest, int? maxHeight, bool preferWebM)
    {
        var videoOnly = manifest.VideoOnly;

        if (videoOnly.Count == 0)
            return null;

        var candidates = preferWebM
            ? videoOnly.Where(static stream => IsWebM(stream)).ToArray()
            : videoOnly.Where(static stream => !IsWebM(stream)).ToArray();

        if (candidates.Length == 0)
            candidates = videoOnly.ToArray();

        var limited = maxHeight is { } limit
            ? candidates.Where(stream => (stream.Height ?? 0) <= limit).ToArray()
            : candidates;

        if (limited.Length == 0)
            limited = candidates;

        return limited
            .OrderByDescending(static stream => stream.Height ?? 0)
            .ThenByDescending(static stream => stream.Fps ?? 0)
            .ThenByDescending(static stream => stream.Bitrate ?? 0)
            .FirstOrDefault();
    }

    private static MediaStream? SelectMuxedStream(StreamManifest manifest, int? maxHeight, bool preferWebM)
    {
        if (manifest.Muxed.Count == 0)
            return null;

        var candidates = preferWebM
            ? manifest.Muxed.Where(static stream => IsWebM(stream)).ToArray()
            : manifest.Muxed.Where(static stream => !IsWebM(stream)).ToArray();

        if (candidates.Length == 0)
            candidates = manifest.Muxed.ToArray();

        var limited = maxHeight is { } limit
            ? candidates.Where(stream => (stream.Height ?? 0) <= limit).ToArray()
            : candidates;

        if (limited.Length == 0)
            return null;

        return limited
            .OrderByDescending(static stream => stream.Height ?? 0)
            .ThenByDescending(static stream => stream.Bitrate ?? 0)
            .First();
    }

    private static bool IsWebM(MediaStream stream) =>
        stream.MimeType?.Contains("webm", StringComparison.OrdinalIgnoreCase) == true;

    private static string? BestThumbnail(Video video) =>
        video.Thumbnails
            .OrderByDescending(static thumbnail => thumbnail.Area)
            .Select(static thumbnail => thumbnail.Url)
            .FirstOrDefault();

    private static ItemError Map(YoutubeError error) => error.Kind switch
    {
        YoutubeErrorKind.Unavailable => Terminal("ErrorVideoUnavailable", ErrorAction.OpenOnYouTube),
        YoutubeErrorKind.AuthenticationRequired => Terminal("ErrorAuthRequired", ErrorAction.SignIn),
        YoutubeErrorKind.RateLimited => Retry("ErrorRateLimited"),
        YoutubeErrorKind.ParseFailed => Retry("ErrorParseFailed"),
        _ => Retry("ErrorNetwork")
    };

    private void ReportFailure(YoutubeError error)
    {
        var isNetwork = error.Kind is YoutubeErrorKind.Network or YoutubeErrorKind.RateLimited;

        if (isNetwork)
            _health.ReportFailure(error.Message, isNetwork: true);
    }

    private static ItemError Terminal(string key, ErrorAction action = ErrorAction.None) =>
        new(ErrorCategory.Terminal, LocalizationService.Instance.Get(key), action);

    private static ItemError Retry(string key) =>
        new(ErrorCategory.Retryable, LocalizationService.Instance.Get(key), ErrorAction.Retry);

    private static ItemError NetworkError(Exception exception) =>
        new(ErrorCategory.Retryable, exception.Message, ErrorAction.Retry);

    private static bool IsExpected(Exception exception) =>
        exception is YoutubeException or HttpRequestException or IOException or InvalidOperationException;
}
