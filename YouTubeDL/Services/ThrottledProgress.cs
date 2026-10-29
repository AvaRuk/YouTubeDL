using System.Diagnostics;
using YoutubeCulvert.Datatypes;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>
/// Coalesces the high-frequency <see cref="DownloadProgress"/> stream of the
/// library into at most one UI update per <see cref="Interval"/>.
/// </summary>
public sealed class ThrottledProgress : IProgress<DownloadProgress>, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly Action<ProgressSnapshot> _publish;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Timer _timer;

    private ProgressSnapshot? _pending;
    private long _lastBytes;
    private TimeSpan _lastTime;
    private double _speed;
    private TimeSpan _lastPublish = TimeSpan.Zero;
    private bool _disposed;

    public ThrottledProgress(Action<ProgressSnapshot> publish)
    {
        _publish = publish;
        _timer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Report(DownloadProgress value)
    {
        if (_disposed)
            return;

        ProgressSnapshot snapshot;
        var now = _clock.Elapsed;

        lock (_gate)
        {
            var deltaBytes = value.Bytes - _lastBytes;
            var deltaTime = (now - _lastTime).TotalSeconds;

            if (deltaTime > 0.05 && deltaBytes >= 0)
            {
                var instant = deltaBytes / deltaTime;
                _speed = _speed <= 0 ? instant : (_speed * 0.7) + (instant * 0.3);
            }

            _lastBytes = value.Bytes;
            _lastTime = now;

            var total = value.Total;
            var percent = total is > 0
                ? Math.Clamp((double)value.Bytes / total.Value, 0, 1)
                : Math.Clamp(value.Percent, 0, 1);

            TimeSpan? eta = null;
            if (total is > 0 && _speed > 1)
            {
                var remaining = Math.Max(0, total.Value - value.Bytes);
                eta = TimeSpan.FromSeconds(remaining / _speed);
            }

            snapshot = new ProgressSnapshot(
                value.Bytes,
                total,
                percent,
                _speed,
                eta,
                value.AudioTrackId,
                value.FilePath);
        }

        lock (_gate)
        {
            _pending = snapshot;

            if (now - _lastPublish < Interval)
            {
                _timer.Change(
                    Math.Max(1, (int)(Interval - (now - _lastPublish)).TotalMilliseconds),
                    Timeout.Infinite);
                return;
            }

            _lastPublish = now;
            _pending = null;
        }

        _publish(snapshot);
    }

    private void Flush()
    {
        ProgressSnapshot? snapshot;

        lock (_gate)
        {
            if (_disposed || _pending is null)
                return;

            snapshot = _pending;
            _pending = null;
            _lastPublish = _clock.Elapsed;
        }

        _publish(snapshot);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}

/// <summary>Token-bucket bandwidth limiter shared by all streams of one download.</summary>
public sealed class SpeedLimiter
{
    private readonly double _bytesPerSecond;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private double _tokens;
    private TimeSpan _lastRefill;

    private SpeedLimiter(double bytesPerSecond)
    {
        _bytesPerSecond = bytesPerSecond;
        _tokens = bytesPerSecond;
        _lastRefill = _clock.Elapsed;
    }

    public static SpeedLimiter? Create(int kilobytesPerSecond) =>
        kilobytesPerSecond > 0 ? new SpeedLimiter(kilobytesPerSecond * 1024d) : null;

    public async Task WaitAsync(int bytes, CancellationToken cancellationToken)
    {
        if (bytes <= 0)
            return;

        double delaySeconds;

        lock (_gate)
        {
            var now = _clock.Elapsed;
            var elapsed = (now - _lastRefill).TotalSeconds;
            _lastRefill = now;

            if (elapsed > 0)
                _tokens = Math.Min(_bytesPerSecond, _tokens + (elapsed * _bytesPerSecond));

            if (_tokens >= bytes)
            {
                _tokens -= bytes;
                delaySeconds = 0;
            }
            else
            {
                delaySeconds = (bytes - _tokens) / _bytesPerSecond;
                _tokens = 0;
                _lastRefill = now + TimeSpan.FromSeconds(delaySeconds);
            }
        }

        if (delaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
    }
}
