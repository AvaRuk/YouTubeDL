using System.Text;
using YoutubeCulvert.Logging;

namespace YouTubeDL.Services;

/// <summary>Writes library and application events to data/diagnostics.log.</summary>
public sealed class DiagnosticsLog : IYoutubeLogger, IDisposable
{
    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly YoutubeLogLevel _minimumLevel;
    private bool _disposed;

    public DiagnosticsLog(string? filePath = null, YoutubeLogLevel minimumLevel = YoutubeLogLevel.Information)
    {
        _filePath = filePath ?? AppPaths.DiagnosticsFile;
        _minimumLevel = minimumLevel;

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }
        catch (IOException)
        {
        }
    }

    public event Action<string>? Message;

    public string FilePath => _filePath;

    public bool IsEnabled(YoutubeLogLevel level) => (int)level >= (int)_minimumLevel;

    public void Log(
        YoutubeLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? fields = null)
    {
        if (!IsEnabled(level))
            return;

        var builder = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [")
            .Append(level)
            .Append("] ")
            .Append(eventName)
            .Append(": ")
            .Append(message);

        if (fields is { Count: > 0 })
        {
            builder.Append(" | ");
            builder.AppendJoin(
                ", ",
                fields.Select(pair => $"{pair.Key}={pair.Value}"));
        }

        if (exception is not null)
            builder.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);

        Write(builder.ToString());
    }

    public void Write(string message) => Append($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");

    public void WriteError(string message, Exception? exception = null) =>
        Log(YoutubeLogLevel.Error, "App", message, exception);

    private void Append(string line)
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Diagnostics must never break the application.
            }
        }

        Message?.Invoke(line);
    }

    public void Dispose() => _disposed = true;
}
