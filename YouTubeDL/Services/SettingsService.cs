using System.Text.Json;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

public sealed class SettingsService
{
    private readonly object _saveLock = new();

    public SettingsService(string? filePath = null)
    {
        FilePath = filePath ?? AppPaths.SettingsFile;
    }

    public string FilePath { get; }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings);
                if (loaded is not null)
                    Current = loaded;
            }
        }
        catch (JsonException)
        {
            Current = new AppSettings();
        }
        catch (IOException)
        {
            Current = new AppSettings();
        }

        Current.Normalize();
        return Current;
    }

    public void Save()
    {
        Current.Normalize();

        lock (_saveLock)
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(Current, AppJsonContext.Default.AppSettings));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
