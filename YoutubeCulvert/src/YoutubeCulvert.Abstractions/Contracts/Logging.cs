namespace YoutubeCulvert.Logging;

/// <summary>Severity used by <see cref="IYoutubeLogger"/>.</summary>
public enum YoutubeLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error
}

/// <summary>
/// Logging abstraction of the core. <c>YoutubeCulvert.Telemetry</c> adapts it
/// to <c>Microsoft.Extensions.Logging.ILogger</c>.
/// </summary>
public interface IYoutubeLogger
{
    bool IsEnabled(YoutubeLogLevel level);

    /// <summary>
    /// Writes a structured event. <paramref name="fields"/> must never contain
    /// cookies, bearer tokens or full response bodies.
    /// </summary>
    void Log(
        YoutubeLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? fields = null);
}

/// <summary>Logger that discards everything; the default when no logger is configured.</summary>
public sealed class NullLogger : IYoutubeLogger
{
    public static NullLogger Instance { get; } = new();

    public bool IsEnabled(YoutubeLogLevel level) => false;

    public void Log(
        YoutubeLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? fields = null)
    {
    }
}
