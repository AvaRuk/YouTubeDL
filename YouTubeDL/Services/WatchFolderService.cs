using Avalonia.Threading;

namespace YouTubeDL.Services;

/// <summary>
/// Watches data/inbox for *.txt files with one URL per line, queues them and
/// moves the processed file into data/inbox/processed.
/// </summary>
public sealed class WatchFolderService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly Func<IReadOnlyList<string>, Task> _enqueue;
    private readonly DiagnosticsLog _log;
    private readonly Timer _timer;
    private readonly HashSet<string> _processed = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _disposed;

    public WatchFolderService(
        SettingsService settings,
        Func<IReadOnlyList<string>, Task> enqueue,
        DiagnosticsLog log)
    {
        _settings = settings;
        _enqueue = enqueue;
        _log = log;
        _timer = new Timer(_ => Scan(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        if (_disposed)
            return;

        _timer.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));
    }

    public void Stop() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    public void Refresh()
    {
        if (_settings.Current.WatchFolderEnabled)
            Start();
        else
            Stop();
    }

    private void Scan()
    {
        if (_disposed || !_settings.Current.WatchFolderEnabled)
            return;

        try
        {
            AppPaths.EnsureCreated();

            foreach (var file in Directory.EnumerateFiles(AppPaths.InboxDirectory, "*.txt"))
            {
                lock (_lock)
                {
                    if (!_processed.Add(file))
                        continue;
                }

                var urls = File.ReadAllLines(file)
                    .Select(static line => line.Trim())
                    .Where(static line => line.Length > 0 && !line.StartsWith('#'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (urls.Length == 0)
                {
                    MoveProcessed(file);
                    continue;
                }

                _log.Write($"Watch folder: {urls.Length} URL(s) from '{Path.GetFileName(file)}'.");

                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        await _enqueue(urls).ConfigureAwait(true);
                    }
                    catch (Exception exception)
                    {
                        _log.WriteError("Watch folder enqueue failed.", exception);
                    }
                    finally
                    {
                        MoveProcessed(file);
                    }
                });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.WriteError("Watch folder scan failed.", exception);
        }
    }

    private static void MoveProcessed(string file)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.InboxProcessedDirectory);

            var name = Path.GetFileNameWithoutExtension(file);
            var target = Path.Combine(
                AppPaths.InboxProcessedDirectory,
                $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(file)}");

            File.Move(file, target, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
