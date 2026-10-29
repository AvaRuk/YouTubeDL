using System.Text.Json;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>Persists the download queue (data/queue.json) and supports import/export.</summary>
public sealed class QueueStore
{
    private readonly object _lock = new();

    public QueueStore(string? filePath = null)
    {
        FilePath = filePath ?? AppPaths.QueueFile;
    }

    public string FilePath { get; }

    public IReadOnlyList<QueueItem> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListQueueItem) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<QueueItem> items)
    {
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var temporaryPath = FilePath + ".tmp";
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(items.ToList(), AppJsonContext.Default.ListQueueItem));
                File.Move(temporaryPath, FilePath, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Export(string path, IEnumerable<QueueItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppPaths.DataDirectory);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(items.ToList(), AppJsonContext.Default.ListQueueItem));
    }

    public IReadOnlyList<QueueItem> Import(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListQueueItem) ?? [];
    }
}
