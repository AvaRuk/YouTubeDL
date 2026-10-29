using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>
/// Watches the outcome of YouTube requests and derives the connection state
/// shown in the status bar (online / degraded / offline).
/// </summary>
public sealed class HealthMonitor
{
    private readonly object _lock = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _lastSuccess;
    private string? _lastError;
    private HealthState _state = HealthState.Unknown;

    public event EventHandler? Changed;

    public HealthState State => _state;

    public DateTimeOffset? LastSuccess => _lastSuccess;

    public string? LastError => _lastError;

    public bool CircuitOpen => _state == HealthState.Offline;

    public void ReportSuccess()
    {
        lock (_lock)
        {
            _lastSuccess = DateTimeOffset.Now;
            _consecutiveFailures = 0;
            _lastError = null;
            SetState(HealthState.Online);
        }
    }

    public void ReportFailure(string message, bool isNetwork)
    {
        lock (_lock)
        {
            _lastError = message;

            if (isNetwork)
                _consecutiveFailures++;

            SetState(_consecutiveFailures >= 3
                ? HealthState.Offline
                : HealthState.Degraded);
        }
    }

    public void ReportNeutral()
    {
        lock (_lock)
        {
            if (_state == HealthState.Unknown && _lastSuccess is not null)
                SetState(HealthState.Online);
        }
    }

    private void SetState(HealthState state)
    {
        if (_state == state)
            return;

        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
