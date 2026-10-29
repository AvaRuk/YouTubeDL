using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using YoutubeCulvert.Datatypes;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>
/// FFmpeg integration: locating/installing the binary, muxing separate video
/// and audio streams, converting audio containers and embedding metadata,
/// cover art and chapters.
/// </summary>
public sealed class FfmpegService
{
    private readonly SettingsService _settings;
    private readonly DiagnosticsLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _locatedPath;
    private bool _searched;

    public FfmpegService(SettingsService settings, DiagnosticsLog log)
    {
        _settings = settings;
        _log = log;
    }

    public string? ExecutablePath
    {
        get
        {
            var configured = _settings.Current.FfmpegPath;
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;

            if (!_searched)
            {
                _searched = true;
                _locatedPath = Locate();
            }

            return _locatedPath;
        }
    }

    public bool IsAvailable => ExecutablePath is not null;

    /// <summary>Re-runs the automatic search, for example after FFmpeg was installed.</summary>
    public void Refresh()
    {
        _searched = false;
        _locatedPath = null;
    }

    public async Task<string> InstallAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.ToolsDirectory);

        var target = Path.Combine(
            AppPaths.ToolsDirectory,
            OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        var archivePath = Path.Combine(AppPaths.ToolsDirectory, $"ffmpeg-{Guid.NewGuid():N}.zip");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

            using var response = await http
                .GetAsync(GetDownloadUrl(), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;

            await using (var source = await response.Content
                             .ReadAsStreamAsync(cancellationToken)
                             .ConfigureAwait(false))
            await using (var destination = new FileStream(
                             archivePath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81_920,
                             useAsync: true))
            {
                var buffer = new byte[81_920];
                long copied = 0;

                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);

                    copied += read;

                    if (total is > 0)
                        progress?.Report(Math.Clamp((double)copied / total.Value, 0, 1));
                }
            }

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var entry = archive.Entries.FirstOrDefault(candidate =>
                    string.Equals(
                        Path.GetFileName(candidate.FullName),
                        OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg",
                        StringComparison.OrdinalIgnoreCase));

                if (entry is null)
                    throw new LocalizedException("ErrorFfmpegMissing");

                entry.ExtractToFile(target, overwrite: true);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    target,
                    File.GetUnixFileMode(target) | UnixFileMode.UserExecute);
            }

            Refresh();

            progress?.Report(1);
            _log.Write($"FFmpeg installed to {target}.");
            return target;
        }
        finally
        {
            FileSystemService.TryDelete(archivePath);
        }
    }

    public async Task MuxAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        string container,
        int? videoHeight,
        string? videoCodec,
        string? audioCodec,
        IReadOnlyDictionary<string, string>? metadata,
        string? coverPath,
        IReadOnlyList<Chapter> chapters,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = ExecutablePath ?? throw new LocalizedException("ErrorFfmpegMissing");
        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", videoPath,
            "-i", audioPath
        };

        var nextInput = 2;
        var coverInput = -1;
        var chaptersInput = -1;
        var hasCover = !string.IsNullOrWhiteSpace(coverPath) && File.Exists(coverPath);

        if (hasCover && !string.Equals(container, "webm", StringComparison.OrdinalIgnoreCase))
        {
            coverInput = nextInput++;
            arguments.Add("-i");
            arguments.Add(coverPath!);
        }

        string? chaptersFile = null;

        if (chapters.Count > 0 &&
            container is "mp4" or "mkv")
        {
            chaptersFile = Path.Combine(Path.GetTempPath(), $"ytdl-chapters-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(chaptersFile, BuildChaptersFile(chapters, duration), cancellationToken)
                .ConfigureAwait(false);

            chaptersInput = nextInput++;
            arguments.Add("-i");
            arguments.Add(chaptersFile);
        }

        arguments.Add("-map");
        arguments.Add("0:v:0");
        arguments.Add("-map");
        arguments.Add("1:a:0");

        var videoCodecArgs = ResolveVideoCodec(container, videoCodec);
        arguments.AddRange(videoCodecArgs);

        var audioCodecArgs = ResolveAudioCodec(container, audioCodec);
        arguments.AddRange(audioCodecArgs);

        if (coverInput >= 0)
        {
            if (container == "mkv")
            {
                arguments.Add("-attach");
                arguments.Add(coverPath!);
                arguments.Add("-metadata:s:t");
                arguments.Add("mimetype=image/jpeg");
                arguments.Add("-metadata:s:t");
                arguments.Add("filename=cover.jpg");
            }
            else
            {
                arguments.Add("-map");
                arguments.Add($"{coverInput}:v:0");
                arguments.Add("-c:v:1");
                arguments.Add("copy");
                arguments.Add("-disposition:v:1");
                arguments.Add("attached_pic");
            }
        }

        if (chaptersInput >= 0)
        {
            arguments.Add("-map_metadata");
            arguments.Add(chaptersInput.ToString());
            arguments.Add("-map_chapters");
            arguments.Add(chaptersInput.ToString());
        }

        AddMetadata(arguments, metadata);

        if (container == "mp4")
        {
            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }

        try
        {
            await RunAsync(ffmpeg, arguments, outputPath, "ErrorMuxFailed", cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (chaptersFile is not null)
                FileSystemService.TryDelete(chaptersFile);
        }

        _log.Write($"Muxed '{Path.GetFileName(outputPath)}' ({container}, {(videoHeight is { } h ? $"{h}p" : "auto")}).");
    }

    public async Task RemuxAsync(
        string inputPath,
        string outputPath,
        string container,
        string? videoCodec,
        string? audioCodec,
        IReadOnlyDictionary<string, string>? metadata,
        string? coverPath,
        IReadOnlyList<Chapter> chapters,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = ExecutablePath ?? throw new LocalizedException("ErrorFfmpegMissing");
        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", inputPath
        };

        var nextInput = 1;
        var coverInput = -1;
        var chaptersInput = -1;
        var hasCover = !string.IsNullOrWhiteSpace(coverPath) &&
                       File.Exists(coverPath) &&
                       !string.Equals(container, "mkv", StringComparison.OrdinalIgnoreCase);

        if (hasCover && !string.Equals(container, "webm", StringComparison.OrdinalIgnoreCase))
        {
            coverInput = nextInput++;
            arguments.Add("-i");
            arguments.Add(coverPath!);
        }

        string? chaptersFile = null;

        if (chapters.Count > 0 && container is "mp4" or "mkv")
        {
            chaptersFile = Path.Combine(Path.GetTempPath(), $"ytdl-chapters-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(chaptersFile, BuildChaptersFile(chapters, duration), cancellationToken)
                .ConfigureAwait(false);

            chaptersInput = nextInput++;
            arguments.Add("-i");
            arguments.Add(chaptersFile);
        }

        arguments.Add("-map");
        arguments.Add("0:v:0");
        arguments.Add("-map");
        arguments.Add("0:a:0?");
        arguments.AddRange(ResolveVideoCodec(container, videoCodec));
        arguments.AddRange(ResolveAudioCodec(container, audioCodec));

        if (coverInput >= 0)
        {
            arguments.Add("-map");
            arguments.Add($"{coverInput}:v:0");
            arguments.Add("-c:v:1");
            arguments.Add("copy");
            arguments.Add("-disposition:v:1");
            arguments.Add("attached_pic");
        }

        if (chaptersInput >= 0)
        {
            arguments.Add("-map_metadata");
            arguments.Add(chaptersInput.ToString());
            arguments.Add("-map_chapters");
            arguments.Add(chaptersInput.ToString());
        }

        AddMetadata(arguments, metadata);

        if (container == "mp4")
        {
            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }

        try
        {
            await RunAsync(ffmpeg, arguments, outputPath, "ErrorMuxFailed", cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (chaptersFile is not null)
                FileSystemService.TryDelete(chaptersFile);
        }

        _log.Write($"Remuxed '{Path.GetFileName(outputPath)}' into {container}.");
    }

    public async Task TagAsync(
        string inputPath,
        string outputPath,
        string container,
        IReadOnlyDictionary<string, string>? metadata,
        string? coverPath,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = ExecutablePath ?? throw new LocalizedException("ErrorFfmpegMissing");
        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", inputPath
        };

        var hasCover = !string.IsNullOrWhiteSpace(coverPath) &&
                       File.Exists(coverPath) &&
                       container is "mp3" or "m4a";

        if (hasCover)
        {
            arguments.Add("-i");
            arguments.Add(coverPath!);
        }

        arguments.AddRange(["-map", "0:a:0", "-c:a", "copy"]);

        if (hasCover)
        {
            arguments.AddRange(["-map", "1:v:0", "-c:v", "copy", "-disposition:v", "attached_pic"]);

            if (container == "mp3")
                arguments.AddRange(["-id3v2_version", "3", "-metadata:s:v", "title=Album cover"]);
        }

        if (container == "m4a")
            arguments.AddRange(["-movflags", "+faststart"]);

        AddMetadata(arguments, metadata);

        await RunAsync(ffmpeg, arguments, outputPath, "ErrorConvertFailed", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ConvertAudioAsync(
        string inputPath,
        string outputPath,
        string targetFormat,
        IReadOnlyDictionary<string, string>? metadata,
        string? coverPath,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = ExecutablePath ?? throw new LocalizedException("ErrorFfmpegMissing");
        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", inputPath
        };

        var hasCover = !string.IsNullOrWhiteSpace(coverPath) && File.Exists(coverPath);
        var embedCover = hasCover && targetFormat is "mp3" or "m4a";

        if (embedCover)
        {
            arguments.Add("-i");
            arguments.Add(coverPath!);
        }

        arguments.Add("-map");
        arguments.Add("0:a:0");

        switch (targetFormat)
        {
            case "mp3":
                arguments.AddRange(["-c:a", "libmp3lame", "-b:a", "192k"]);
                break;

            case "opus":
                arguments.AddRange(["-c:a", "libopus", "-b:a", "160k"]);
                break;

            case "flac":
                arguments.AddRange(["-c:a", "flac", "-compression_level", "5"]);
                break;

            case "m4a":
            default:
                arguments.AddRange(["-c:a", "aac", "-b:a", "192k"]);
                break;
        }

        if (embedCover)
        {
            arguments.AddRange(["-map", "1:v:0", "-c:v", "mjpeg", "-disposition:v", "attached_pic"]);

            if (targetFormat == "mp3")
            {
                arguments.AddRange(["-id3v2_version", "3", "-metadata:s:v", "title=Album cover"]);
            }
            else
            {
                arguments.AddRange(["-movflags", "+faststart"]);
            }
        }

        AddMetadata(arguments, metadata);

        await RunAsync(ffmpeg, arguments, outputPath, "ErrorConvertFailed", cancellationToken)
            .ConfigureAwait(false);

        _log.Write($"Converted '{Path.GetFileName(outputPath)}' to {targetFormat}.");
    }

    private async Task RunAsync(
        string ffmpeg,
        List<string> arguments,
        string outputPath,
        string errorKey,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(outputPath);
        var temporaryPath = outputPath + ".processing" + extension;

        arguments.Add(temporaryPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var startInfo = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            _log.Write($"ffmpeg {string.Join(' ', arguments.Select(Quote))}");

            using var process = Process.Start(startInfo)
                ?? throw new LocalizedException(errorKey);

            var errors = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);

                FileSystemService.TryDelete(temporaryPath);
                throw;
            }

            var errorText = await errors.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                _log.WriteError($"FFmpeg exited with {process.ExitCode}.", new InvalidOperationException(errorText));
                FileSystemService.TryDelete(temporaryPath);
                throw new LocalizedException(errorKey);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            FileSystemService.TryDelete(temporaryPath);
            _gate.Release();
        }
    }

    private static IReadOnlyList<string> ResolveVideoCodec(string container, string? codec)
    {
        var value = (codec ?? string.Empty).ToLowerInvariant();
        var isH264 = value.Contains("avc1") || value.Contains("h264");
        var isVp9 = value.Contains("vp9") || value.Contains("vp09") || value.Contains("vp8");
        var isAv1 = value.Contains("av01");

        return container switch
        {
            "mp4" when isH264 => ["-c:v", "copy"],
            "mp4" => ["-c:v", "libx264", "-preset", "veryfast", "-crf", "20"],
            "webm" when isVp9 || isAv1 => ["-c:v", "copy"],
            "webm" => ["-c:v", "libvpx-vp9", "-crf", "30", "-b:v", "0"],
            _ => ["-c:v", "copy"]
        };
    }

    private static IReadOnlyList<string> ResolveAudioCodec(string container, string? codec)
    {
        var value = (codec ?? string.Empty).ToLowerInvariant();
        var isAac = value.Contains("mp4a") || value.Contains("aac");
        var isOpus = value.Contains("opus");
        var isVorbis = value.Contains("vorbis");

        return container switch
        {
            "mp4" when isAac => ["-c:a", "copy"],
            "mp4" => ["-c:a", "aac", "-b:a", "192k"],
            "webm" when isOpus || isVorbis => ["-c:a", "copy"],
            "webm" => ["-c:a", "libopus", "-b:a", "160k"],
            _ => ["-c:a", "copy"]
        };
    }

    private static void AddMetadata(List<string> arguments, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
            return;

        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            arguments.Add("-metadata");
            arguments.Add($"{key}={value}");
        }
    }

    private static string BuildChaptersFile(IReadOnlyList<Chapter> chapters, TimeSpan duration)
    {
        var builder = new StringBuilder(";FFMETADATA1\n");

        for (var index = 0; index < chapters.Count; index++)
        {
            var chapter = chapters[index];
            var start = chapter.Start;
            var end = chapter.End ?? (index + 1 < chapters.Count ? chapters[index + 1].Start : duration);

            if (end <= start)
                end = start + TimeSpan.FromSeconds(1);

            builder.Append("[CHAPTER]\n")
                .Append("TIMEBASE=1/1000\n")
                .Append("START=").Append((long)start.TotalMilliseconds).Append('\n')
                .Append("END=").Append((long)end.TotalMilliseconds).Append('\n')
                .Append("title=").Append(escape(chapter.Title)).Append('\n');
        }

        return builder.ToString();

        static string escape(string value) => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace("#", "\\#", StringComparison.Ordinal);
    }

    private static string Quote(string argument) =>
        argument.Contains(' ') ? $"\"{argument}\"" : argument;

    private string? Locate()
    {
        var explicitPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        var appDirectory = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(appDirectory, "ffmpeg.exe"),
            Path.Combine(appDirectory, "tools", "ffmpeg.exe"),
            Path.Combine(appDirectory, "ffmpeg"),
            Path.Combine(AppPaths.ToolsDirectory, "ffmpeg.exe"),
            Path.Combine(AppPaths.ToolsDirectory, "ffmpeg"),
            "/usr/bin/ffmpeg",
            "/usr/local/bin/ffmpeg",
            "/opt/homebrew/bin/ffmpeg"
        };

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        candidates.AddRange(
        [
            Path.Combine(programFiles, "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(programFilesX86, "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe"),
            Path.Combine(userProfile, "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe"
        ]);

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string GetDownloadUrl()
    {
        var system = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : "linux";

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };

        return $"https://github.com/Tyrrrz/FFmpegBin/releases/download/8.1/ffmpeg-{system}-{architecture}.zip";
    }
}
