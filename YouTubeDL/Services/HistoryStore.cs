using System.Text.Json;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>Download history (data/history.json) used for duplicates and the History tab.</summary>
public sealed class HistoryStore
{
    private readonly object _lock = new();
    private readonly List<HistoryEntry> _entries = [];
    private readonly string _filePath;
    private readonly SettingsService _settings;
    private bool _loaded;

    public HistoryStore(SettingsService settings, string? filePath = null)
    {
        _settings = settings;
        _filePath = filePath ?? AppPaths.HistoryFile;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<HistoryEntry> Items
    {
        get
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _entries.ToArray();
            }
        }
    }

    public void Add(HistoryEntry entry)
    {
        lock (_lock)
        {
            EnsureLoaded();

            var existing = _entries.FirstOrDefault(candidate =>
                candidate.Succeeded &&
                string.Equals(candidate.VideoId, entry.VideoId, StringComparison.Ordinal) &&
                candidate.Kind == entry.Kind &&
                string.Equals(candidate.AudioTrackId, entry.AudioTrackId, StringComparison.Ordinal) &&
                string.Equals(candidate.Container, entry.Container, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
                _entries.Remove(existing);

            _entries.Insert(0, entry);

            var limit = Math.Max(50, _settings.Current.MaxHistoryEntries);
            if (_entries.Count > limit)
                _entries.RemoveRange(limit, _entries.Count - limit);

            SaveCore();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public HistoryEntry? Find(string videoId, DownloadKind kind, string? audioTrackId, string? quality, string container)
    {
        lock (_lock)
        {
            EnsureLoaded();

            return _entries.FirstOrDefault(entry =>
                entry.Succeeded &&
                string.Equals(entry.VideoId, videoId, StringComparison.Ordinal) &&
                entry.Kind == kind &&
                string.Equals(entry.AudioTrackId, audioTrackId, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(quality) ||
                 string.Equals(entry.Quality, quality, StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(entry.Container, container, StringComparison.OrdinalIgnoreCase) &&
                entry.FileExists);
        }
    }

    public bool ContainsVideo(string videoId)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _entries.Any(entry =>
                entry.Succeeded && string.Equals(entry.VideoId, videoId, StringComparison.Ordinal));
        }
    }

    public void Remove(HistoryEntry entry)
    {
        lock (_lock)
        {
            EnsureLoaded();

            if (_entries.Remove(entry))
                SaveCore();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_lock)
        {
            EnsureLoaded();
            _entries.Clear();
            SaveCore();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;

        try
        {
            if (!File.Exists(_filePath))
                return;

            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListHistoryEntry) ?? [];
            _entries.AddRange(loaded);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
        }
    }

    private void SaveCore()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(_entries, AppJsonContext.Default.ListHistoryEntry));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
