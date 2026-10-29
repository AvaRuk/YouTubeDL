using System.Runtime.InteropServices;
using YouTubeDL.Models;
using YouTubeDL.Services;

namespace YouTubeDL;

/// <summary>
/// Headless mode:
///   YouTubeDL.exe download --url &lt;url&gt; [--url &lt;url2&gt;] [--audio]
///                [--quality 1080] [--format mp4] [--track en-US.4]
///                [--output &lt;folder&gt;] [--playlist] [--channel] [--cookie &lt;header&gt;]
/// </summary>
public static class CommandLineRunner
{
    public static bool IsCommandLine(string[] args) =>
        args.Length > 0 &&
        args[0] is "download" or "dl" or "--cli" or "/download";

    public static async Task<int> RunAsync(string[] args)
    {
        if (OperatingSystem.IsWindows())
            AttachConsole(AttachParentProcess);

        try
        {
            var options = Parse(args);

            if (options.Urls.Count == 0)
            {
                Console.WriteLine("YouTubeDL command line mode");
                Console.WriteLine("Usage: YouTubeDL.exe download --url <url> [--audio] [--quality 1080] [--format mp4] [--track id] [--output dir] [--playlist] [--channel]");
                return 2;
            }

            AppPaths.EnsureCreated();

            var settings = new SettingsService();
            settings.Load();

            if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                settings.Current.VideoFolder = options.OutputDirectory;
                settings.Current.MusicFolder = options.OutputDirectory;
            }

            LocalizationService.Instance.Initialize(settings.Current.Language);

            var log = new DiagnosticsLog();
            var health = new HealthMonitor();
            var manager = new YoutubeClientManager(settings, log, health);

            await manager.InitializeAsync().ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(options.CookieHeader))
                await manager.Client.Auth.SignInWithCookieHeaderAsync(options.CookieHeader).ConfigureAwait(false);

            var downloader = new StreamDownloader(() => manager.HttpHandler, log);
            var ffmpeg = new FfmpegService(settings, log);
            var history = new HistoryStore(settings);
            var resolver = new MediaResolver(manager, settings, health, log);
            var download = new DownloadService(resolver, downloader, ffmpeg, history, settings, manager, log);

            var failures = 0;
            var index = 0;

            foreach (var url in options.Urls)
            {
                index++;

                if (options.Playlist)
                {
                    var batch = new BatchService(manager, settings, history, health, log);
                    var selection = await batch
                        .LoadPlaylistAsync(url, new BatchFilter { MaxItems = 500, OnlyNew = settings.Current.SkipExisting })
                        .ConfigureAwait(false);

                    if (!selection.IsSuccess)
                    {
                        failures++;
                        Console.WriteLine($"Playlist {url}: {selection.Error!.Message}");
                        continue;
                    }

                    foreach (var video in selection.Value!.Items)
                    {
                        if (!await DownloadOneAsync(video.VideoId, options, index, download).ConfigureAwait(false))
                            failures++;
                    }
                }
                else
                {
                    if (!await DownloadOneAsync(url, options, index, download).ConfigureAwait(false))
                        failures++;
                }
            }

            await manager.DisposeAsync().ConfigureAwait(false);
            downloader.Dispose();
            log.Dispose();

            Console.WriteLine(failures == 0
                ? "All downloads completed."
                : $"{failures} download(s) failed.");

            return failures == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<bool> DownloadOneAsync(
        string url,
        CommandLineOptions options,
        int index,
        DownloadService download)
    {
        var request = new DownloadRequest
        {
            Input = url,
            Kind = options.Audio ? DownloadKind.Music : DownloadKind.Video,
            QualityId = options.Quality,
            FormatId = options.Format,
            AudioTrackId = options.AudioTrackId,
            EmbedMetadata = true,
            SaveChapters = true,
            Priority = DownloadPriority.Interactive
        };

        Console.WriteLine($"[{index}] {url}");

        var progress = new Progress<ProgressSnapshot>(snapshot =>
        {
            if (snapshot.Percent <= 0)
                return;

            Console.Write($"\r  {snapshot.Percent,6:P1} {snapshot.SpeedText,-12} {snapshot.EtaText,-10}");
        });

        var result = await download.RunAsync(request, progress).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            Console.WriteLine();
            Console.WriteLine($"  failed: {result.Error!.Message}");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"  saved: {result.Value!.Files.FirstOrDefault()}");
        return true;
    }

    private static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--url":
                case "-u":
                    if (index + 1 < args.Length)
                        options.Urls.Add(args[++index]);
                    break;

                case "--audio":
                case "-a":
                    options.Audio = true;
                    break;

                case "--quality":
                case "-q":
                    if (index + 1 < args.Length)
                        options.Quality = args[++index];
                    break;

                case "--format":
                case "-f":
                    if (index + 1 < args.Length)
                        options.Format = args[++index];
                    break;

                case "--track":
                case "-t":
                    if (index + 1 < args.Length)
                        options.AudioTrackId = args[++index];
                    break;

                case "--output":
                case "-o":
                    if (index + 1 < args.Length)
                        options.OutputDirectory = args[++index];
                    break;

                case "--cookie":
                    if (index + 1 < args.Length)
                        options.CookieHeader = args[++index];
                    break;

                case "--cookie-file":
                    if (index + 1 < args.Length && File.Exists(args[index + 1]))
                    {
                        var headers = CookieImporter.ParseFile(args[index + 1]);
                        options.CookieHeader = headers.FirstOrDefault();
                        index++;
                    }
                    break;

                case "--playlist":
                    options.Playlist = true;
                    break;

                case "--channel":
                    options.Channel = true;
                    break;

                default:
                    if (!argument.StartsWith('-'))
                        options.Urls.Add(argument);
                    break;
            }
        }

        return options;
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private sealed class CommandLineOptions
    {
        public List<string> Urls { get; } = [];

        public bool Audio { get; set; }

        public string? Quality { get; set; }

        public string? Format { get; set; }

        public string? AudioTrackId { get; set; }

        public string? OutputDirectory { get; set; }

        public string? CookieHeader { get; set; }

        public bool Playlist { get; set; }

        public bool Channel { get; set; }
    }
}
