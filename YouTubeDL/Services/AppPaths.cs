namespace YouTubeDL.Services;

/// <summary>
/// Runtime folder layout (next to the executable):
///   data/settings.json, data/queue.json, data/history.json
///   data/diagnostics.log, data/languages/, data/inbox/, data/tools/
///   data/profiles/&lt;name&gt;/auth.json
/// </summary>
public static class AppPaths
{
    public static string BaseDirectory { get; } = AppContext.BaseDirectory;

    public static string DataDirectory { get; } = Path.Combine(BaseDirectory, "data");

    public static string LanguagesDirectory { get; } = Path.Combine(DataDirectory, "languages");

    public static string ToolsDirectory { get; } = Path.Combine(DataDirectory, "tools");

    public static string InboxDirectory { get; } = Path.Combine(DataDirectory, "inbox");

    public static string InboxProcessedDirectory { get; } = Path.Combine(InboxDirectory, "processed");

    public static string ProfilesDirectory { get; } = Path.Combine(DataDirectory, "profiles");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string QueueFile { get; } = Path.Combine(DataDirectory, "queue.json");

    public static string HistoryFile { get; } = Path.Combine(DataDirectory, "history.json");

    public static string DiagnosticsFile { get; } = Path.Combine(DataDirectory, "diagnostics.log");

    public static string DownloadLogFile { get; } = Path.Combine(DataDirectory, "download.log");

    /// <summary>File with a cookie header for headless sign-in (optional).</summary>
    public static string CookieFile { get; } = Path.Combine(DataDirectory, "cookies.txt");

    public static string GetProfileDirectory(string profile) =>
        Path.Combine(ProfilesDirectory, SanitizeProfile(profile));

    public static string GetProfileAuthFile(string profile) =>
        Path.Combine(GetProfileDirectory(profile), "auth.json");

    public static string GetInboxFile(string name) => Path.Combine(InboxDirectory, name);

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LanguagesDirectory);
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(InboxDirectory);
        Directory.CreateDirectory(InboxProcessedDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
    }

    public static void EnsureProfile(string profile) =>
        Directory.CreateDirectory(GetProfileDirectory(profile));

    private static string SanitizeProfile(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return "default";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(profile.Trim()
            .Select(character => invalid.Contains(character) || character is '.' or '/' or '\\' ? '_' : character)
            .ToArray());

        return string.IsNullOrWhiteSpace(cleaned) ? "default" : cleaned;
    }
}
