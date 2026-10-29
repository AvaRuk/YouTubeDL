using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace YouTubeDL.Services;

public sealed class LanguagePack
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Dictionary<string, string> Strings { get; set; } = new(StringComparer.Ordinal);
}

public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// Localization loaded from JSON files in data/languages. Any file placed in
/// that folder is picked up at startup, after "Reload languages" or when the
/// folder changes.
/// </summary>
public sealed class LocalizationService
{
    private readonly List<LanguagePack> _packs = [];
    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private string _currentCode = "en";
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private readonly object _reloadLock = new();

    public static LocalizationService Instance { get; } = new();

    private LocalizationService()
    {
    }

    public event EventHandler? LanguageChanged;

    public event EventHandler? LanguagesChanged;

    public string CurrentLanguage => _currentCode;

    public IReadOnlyList<LanguageInfo> AvailableLanguages =>
        _packs.Select(static pack => new LanguageInfo(pack.Code, pack.Name)).ToArray();

    public bool Initialize(string preferredLanguage)
    {
        if (!ReloadPacks())
            return false;

        Apply(preferredLanguage);
        StartWatching();
        return true;
    }

    public bool ReloadLanguages()
    {
        var current = _currentCode;

        if (!ReloadPacks())
            return false;

        Apply(current);
        LanguagesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Apply(string languageCode)
    {
        var pack = FindPack(languageCode) ?? FindPack("en") ?? _packs.FirstOrDefault();
        if (pack is null)
        {
            _strings = new Dictionary<string, string>(StringComparer.Ordinal);
            _currentCode = languageCode;
            return;
        }

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        var fallback = FindPack("en");
        if (fallback is not null && !ReferenceEquals(fallback, pack))
        {
            foreach (var pair in fallback.Strings)
                merged[pair.Key] = pair.Value;
        }

        foreach (var pair in pack.Strings)
            merged[pair.Key] = pair.Value;

        _strings = merged;
        _currentCode = pack.Code;

        if (Application.Current is { } application)
        {
            var dictionary = new ResourceDictionary();
            foreach (var pair in merged)
                dictionary[pair.Key] = pair.Value;

            application.Resources.MergedDictionaries.Clear();
            application.Resources.MergedDictionaries.Add(dictionary);
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key) =>
        _strings.TryGetValue(key, out var value) ? value : $"[{key}]";

    public string Format(string key, params object?[] arguments) =>
        string.Format(Get(key), arguments);

    private bool ReloadPacks()
    {
        AppPaths.EnsureCreated();

        var loaded = new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(AppPaths.LanguagesDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var pack = JsonSerializer.Deserialize(json, LanguagePackJsonContext.Default.LanguagePack);
                if (pack is null || string.IsNullOrWhiteSpace(pack.Code) || pack.Strings.Count == 0)
                    continue;

                pack.Code = pack.Code.Trim();
                pack.Name = string.IsNullOrWhiteSpace(pack.Name) ? pack.Code : pack.Name;
                loaded[pack.Code] = pack;
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        foreach (var embedded in LoadEmbeddedPacks())
            loaded.TryAdd(embedded.Code, embedded);

        if (loaded.Count == 0)
            return _packs.Count > 0;

        _packs.Clear();
        _packs.AddRange(loaded.Values.OrderBy(static pack => pack.Code, StringComparer.OrdinalIgnoreCase));
        return true;
    }

    private static IEnumerable<LanguagePack> LoadEmbeddedPacks()
    {
        foreach (var (resourceName, code) in new[]
                 {
                     ("YouTubeDL.Languages.en.json", "en"),
                     ("YouTubeDL.Languages.ru.json", "ru")
                 })
        {
            LanguagePack? pack = null;

            try
            {
                using var stream = typeof(LocalizationService).Assembly
                    .GetManifestResourceStream(resourceName);
                if (stream is not null)
                {
                    pack = JsonSerializer.Deserialize(
                        stream,
                        LanguagePackJsonContext.Default.LanguagePack);
                }
            }
            catch (JsonException)
            {
                pack = null;
            }

            if (pack is null || pack.Strings.Count == 0)
                continue;

            pack.Code = code;
            pack.Name = string.IsNullOrWhiteSpace(pack.Name) ? code : pack.Name;
            yield return pack;
        }
    }

    private void StartWatching()
    {
        if (_watcher is not null)
            return;

        try
        {
            _watcher = new FileSystemWatcher(AppPaths.LanguagesDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnLanguageFileChanged;
            _watcher.Changed += OnLanguageFileChanged;
            _watcher.Deleted += OnLanguageFileChanged;
            _watcher.Renamed += OnLanguageFileRenamed;
        }
        catch (IOException)
        {
            _watcher = null;
        }
    }

    private void OnLanguageFileChanged(object sender, FileSystemEventArgs e) => ScheduleReload();

    private void OnLanguageFileRenamed(object sender, RenamedEventArgs e) => ScheduleReload();

    private void ScheduleReload()
    {
        lock (_reloadLock)
        {
            _reloadTimer?.Dispose();
            _reloadTimer = new Timer(
                _ => Dispatcher.UIThread.Post(() => ReloadLanguages()),
                null,
                TimeSpan.FromMilliseconds(700),
                Timeout.InfiniteTimeSpan);
        }
    }

    private LanguagePack? FindPack(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : _packs.FirstOrDefault(pack =>
                pack.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}
