using System.Text;
using System.Text.Json;

namespace YouTubeDL.Services;

/// <summary>
/// Reads a YouTube cookie session from a browser export: a raw cookie header,
/// a Netscape cookies.txt file or a JSON array of {name, value} objects.
/// </summary>
public static class CookieImporter
{
    public static IReadOnlyList<string> ParseFile(string path)
    {
        var text = File.ReadAllText(path);

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            text.TrimStart().StartsWith('[') ||
            text.TrimStart().StartsWith('{'))
        {
            var cookies = ParseJson(text);
            if (cookies.Count > 0)
                return cookies;
        }

        if (LooksLikeCookieHeader(text))
            return [text.Trim()];

        return ParseNetscape(text);
    }

    public static bool LooksLikeCookieHeader(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains('=') && !value.Contains('\t');

    public static string BuildHeader(IEnumerable<(string Name, string Value)> cookies) =>
        string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));

    private static IReadOnlyList<string> ParseJson(string text)
    {
        var result = new List<(string Name, string Value)>();

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("cookies", out var nested))
            {
                root = nested;
            }

            if (root.ValueKind != JsonValueKind.Array)
                return [];

            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                var name = GetString(element, "name");
                var value = GetString(element, "value");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(value))
                    continue;

                result.Add((name, value));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return result.Select(cookie => $"{cookie.Name}={cookie.Value}").ToArray();
    }

    private static IReadOnlyList<string> ParseNetscape(string text)
    {
        var header = new StringBuilder();

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || !trimmed.Contains('\t'))
                continue;

            var parts = trimmed.Split('\t');
            if (parts.Length < 7)
                continue;

            var domain = parts[0];
            var name = parts[5];
            var value = parts[6];

            if (!domain.Contains("youtube", StringComparison.OrdinalIgnoreCase) &&
                !domain.Contains("google", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (header.Length > 0)
                header.Append("; ");

            header.Append(name).Append('=').Append(value);
        }

        return header.Length == 0 ? [] : [header.ToString()];
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
