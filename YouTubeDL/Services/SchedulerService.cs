using System.Globalization;
using Avalonia.Threading;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>
/// Applies the schedule from settings: start/stop time and "do not download
/// during working hours". Raises pause/resume requests once per transition.
/// </summary>
public sealed class SchedulerService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly DiagnosticsLog _log;
    private readonly Timer _timer;
    private bool? _pausedBySchedule;
    private bool _disposed;

    public SchedulerService(SettingsService settings, DiagnosticsLog log)
    {
        _settings = settings;
        _log = log;
        _timer = new Timer(_ => Evaluate(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event Func<bool, Task>? PauseRequested;

    public void Start() => _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

    public void Stop() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private void Evaluate()
    {
        if (_disposed)
            return;

        var settings = _settings.Current;
        var now = DateTime.Now;
        var shouldPause = ShouldPause(settings, now);

        if (_pausedBySchedule == shouldPause)
            return;

        _pausedBySchedule = shouldPause;
        _log.Write(shouldPause
            ? "Scheduler: downloads paused."
            : "Scheduler: downloads resumed.");

        Dispatcher.UIThread.Post(async () =>
        {
            if (PauseRequested is { } handler)
                await handler(shouldPause).ConfigureAwait(true);
        });
    }

    private static bool ShouldPause(AppSettings settings, DateTime now)
    {
        if (settings.SkipInWorkingHours &&
            TryParse(settings.WorkHoursStart, out var workStart) &&
            TryParse(settings.WorkHoursEnd, out var workEnd) &&
            IsWithin(now.TimeOfDay, workStart, workEnd))
        {
            return true;
        }

        if (TryParse(settings.ScheduleStart, out var start) && now.TimeOfDay < start)
            return true;

        if (TryParse(settings.ScheduleStop, out var stop) && now.TimeOfDay >= stop)
            return true;

        return false;
    }

    private static bool IsWithin(TimeSpan value, TimeSpan start, TimeSpan end) =>
        start <= end
            ? value >= start && value < end
            : value >= start || value < end;

    private static bool TryParse(string? value, out TimeSpan time)
    {
        time = default;

        return !string.IsNullOrWhiteSpace(value) &&
               TimeSpan.TryParseExact(value.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out time);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
