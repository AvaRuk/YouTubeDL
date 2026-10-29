using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YoutubeCulvert.Internal;

/// <summary>Tolerant helpers for reading YouTube's JSON.</summary>
internal static partial class JsonHelpers
{
    private const int MaxDepth = 64;

    /// <summary>Walks all objects of the document in depth-first appearance order.</summary>
    public static IEnumerable<JsonElement> Descendants(this JsonElement root)
    {
        foreach (var element in Descend(root, 0))
            yield return element;
    }

    private static IEnumerable<JsonElement> Descend(JsonElement element, int depth)
    {
        if (depth > MaxDepth)
            yield break;

        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;

            foreach (var property in element.EnumerateObject())
            {
                foreach (var child in Descend(property.Value, depth + 1))
                    yield return child;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var child in Descend(item, depth + 1))
                    yield return child;
            }
        }
    }

    public static IEnumerable<JsonElement> ObjectsWithProperty(this JsonElement root, string propertyName)
    {
        foreach (var element in root.Descendants())
        {
            if (element.TryGetProperty(propertyName, out var value))
                yield return value;
        }
    }

    public static JsonElement? FindFirstObjectWithProperty(this JsonElement root, string propertyName)
    {
        foreach (var value in root.ObjectsWithProperty(propertyName))
        {
            if (value.ValueKind == JsonValueKind.Object)
                return value;
        }

        return null;
    }

    public static JsonElement? FindFirstObject(
        this JsonElement root,
        string propertyName,
        string nestedPropertyName)
    {
        foreach (var element in root.Descendants())
        {
            if (element.TryGetProperty(propertyName, out var nested) &&
                nested.ValueKind == JsonValueKind.Object &&
                nested.TryGetProperty(nestedPropertyName, out _))
            {
                return element;
            }
        }

        return null;
    }

    public static JsonElement? Path(this JsonElement element, params string[] path)
    {
        var current = element;

        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                return null;
        }

        return current;
    }

    public static string? GetString(this JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool? GetBool(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    public static int? GetInt32(this JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result)
            ? result
            : null;

    public static long? GetInt64(this JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var result)
            ? result
            : null;

    public static double? GetDouble(this JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var result)
            ? result
            : null;

    /// <summary>
    /// Reads YouTube text structures: a string, <c>simpleText</c>,
    /// <c>runs[].text</c>, <c>content</c>, <c>text</c> and nested view models.
    /// </summary>
    public static string? GetText(this JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                return element.GetRawText();

            case JsonValueKind.Object:
                if (element.TryGetProperty("simpleText", out var simpleText))
                {
                    return simpleText.ValueKind == JsonValueKind.String
                        ? simpleText.GetString()
                        : simpleText.GetText();
                }

                if (element.TryGetProperty("runs", out var runs) &&
                    runs.ValueKind == JsonValueKind.Array)
                {
                    var builder = new StringBuilder();

                    foreach (var run in runs.EnumerateArray())
                    {
                        if (run.ValueKind == JsonValueKind.Object &&
                            run.TryGetProperty("text", out var text) &&
                            text.ValueKind == JsonValueKind.String)
                        {
                            builder.Append(text.GetString());
                        }
                        else
                        {
                            builder.Append(run.GetText());
                        }
                    }

                    return builder.ToString();
                }

                if (element.TryGetProperty("content", out var content))
                    return content.ValueKind == JsonValueKind.String ? content.GetString() : content.GetText();

                if (element.TryGetProperty("text", out var textElement))
                    return textElement.ValueKind == JsonValueKind.String
                        ? textElement.GetString()
                        : textElement.GetText();

                return null;

            default:
                return null;
        }
    }

    public static string? GetTextProperty(this JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value)
            ? value.GetText()
            : null;

    /// <summary>Finds the first string value of a property anywhere in the document.</summary>
    public static string? FindFirstString(this JsonElement root, string propertyName)
    {
        foreach (var element in root.Descendants())
        {
            if (element.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    public static IReadOnlyList<Thumbnail> ParseThumbnails(this JsonElement container)
    {
        if (container.ValueKind == JsonValueKind.Array)
            return ParseThumbnailArray(container);

        if (container.ValueKind != JsonValueKind.Object)
            return [];

        if (container.TryGetProperty("thumbnails", out var thumbnails) &&
            thumbnails.ValueKind == JsonValueKind.Array)
        {
            return ParseThumbnailArray(thumbnails);
        }

        if (container.TryGetProperty("sources", out var sources) &&
            sources.ValueKind == JsonValueKind.Array)
        {
            return ParseThumbnailArray(sources);
        }

        if (container.TryGetProperty("thumbnail", out var thumbnail))
            return thumbnail.ParseThumbnails();

        return [];
    }

    private static IReadOnlyList<Thumbnail> ParseThumbnailArray(JsonElement array)
    {
        var result = new List<Thumbnail>();

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var url = item.GetString("url");
            if (string.IsNullOrWhiteSpace(url))
                continue;

            result.Add(new Thumbnail
            {
                Url = url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url,
                Width = item.GetInt32("width") ?? 0,
                Height = item.GetInt32("height") ?? 0
            });
        }

        return result;
    }

    public static string? BestThumbnailUrl(this JsonElement container) =>
        container.ParseThumbnails()
            .OrderByDescending(static thumbnail => (long)thumbnail.Width * thumbnail.Height)
            .Select(static thumbnail => thumbnail.Url)
            .FirstOrDefault();

    public static TimeSpan? TextToDuration(this JsonElement element) => ParseDuration(element.GetText());

    public static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        if (value.Equals("LIVE", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("UPCOMING", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("PREMIERE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = value.Split(':');
        if (parts.Length is < 2 or > 3)
            return null;

        var numbers = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[index]))
                return null;
        }

        return parts.Length == 2
            ? new TimeSpan(0, 0, numbers[0], numbers[1])
            : new TimeSpan(numbers[0], numbers[1], numbers[2]);
    }

    /// <summary>Extracts a duration token such as "12:34" from a larger text.</summary>
    public static TimeSpan? ParseDurationLoose(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var exact = ParseDuration(value);
        if (exact is not null)
            return exact;

        var match = DurationPattern().Match(value);
        return match.Success ? ParseDuration(match.Value) : null;
    }

    [GeneratedRegex(@"(?<!\d)\d{1,2}:\d{2}(?::\d{2})?(?!\d)")]
    private static partial Regex DurationPattern();

    public static TimeSpan TimeSpanFromSeconds(string? seconds) =>
        long.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? TimeSpan.FromSeconds(value)
            : TimeSpan.Zero;

    public static TimeSpan? TimeSpanFromMilliseconds(string? milliseconds) =>
        double.TryParse(milliseconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? TimeSpan.FromMilliseconds(value)
            : null;

    /// <summary>Parses counts such as "1.2M views" and localized suffixes.</summary>
    public static long? ParseCount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value
            .Trim()
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal);

        var index = 0;
        var dotSeen = false;

        while (index < text.Length)
        {
            var character = text[index];

            if (char.IsAsciiDigit(character))
            {
                index++;
                continue;
            }

            if (character == '.' && !dotSeen)
            {
                dotSeen = true;
                index++;
                continue;
            }

            break;
        }

        if (index == 0 ||
            !double.TryParse(text[..index], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var suffix = text[index..].ToLowerInvariant();
        var multiplier = 1d;

        // English K/M/B plus Russian thousand/million/billion abbreviations.
        if (suffix.StartsWith("k", StringComparison.Ordinal) ||
            suffix.StartsWith("\u0442\u044B\u0441", StringComparison.Ordinal))
        {
            multiplier = 1_000d;
        }
        else if (suffix.StartsWith("m", StringComparison.Ordinal) ||
                 suffix.StartsWith("\u043C\u043B\u043D", StringComparison.Ordinal))
        {
            multiplier = 1_000_000d;
        }
        else if (suffix.StartsWith("b", StringComparison.Ordinal) ||
                 suffix.StartsWith("\u043C\u043B\u0440\u0434", StringComparison.Ordinal))
        {
            multiplier = 1_000_000_000d;
        }

        return (long)Math.Round(number * multiplier, MidpointRounding.AwayFromZero);
    }

    public static int? ParseItemCount(string? value)
    {
        var count = ParseCount(value);
        return count is null ? null : (int)Math.Min(count.Value, int.MaxValue);
    }

    public static long? ParseInt64(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result)
            ? result
            : null;

    public static string? FindContinuationToken(this JsonElement root)
    {
        foreach (var element in root.Descendants())
        {
            var token = ReadContinuationToken(element);
            if (token is not null)
                return token;
        }

        return null;
    }

    public static string? FindLastContinuationToken(this JsonElement root)
    {
        string? token = null;

        foreach (var element in root.Descendants())
            token = ReadContinuationToken(element) ?? token;

        return token;
    }

    private static string? ReadContinuationToken(JsonElement element)
    {
        if (element.TryGetProperty("continuationCommand", out var command) &&
            command.ValueKind == JsonValueKind.Object &&
            command.TryGetProperty("token", out var token) &&
            token.ValueKind == JsonValueKind.String)
        {
            return token.GetString();
        }

        if (element.TryGetProperty("nextContinuationData", out var next) &&
            next.ValueKind == JsonValueKind.Object &&
            next.TryGetProperty("continuation", out var continuation) &&
            continuation.ValueKind == JsonValueKind.String)
        {
            return continuation.GetString();
        }

        return null;
    }

    public static string? FindChannelId(this JsonElement root)
    {
        foreach (var element in root.Descendants())
        {
            if (element.TryGetProperty("browseEndpoint", out var browse) &&
                browse.ValueKind == JsonValueKind.Object &&
                browse.TryGetProperty("browseId", out var browseId) &&
                browseId.ValueKind == JsonValueKind.String)
            {
                var value = browseId.GetString();
                if (value is { Length: 24 } && value.StartsWith("UC", StringComparison.Ordinal))
                    return value;
            }
        }

        return null;
    }

    /// <summary>Reads <c>{"start":"12","end":"34"}</c> into a byte range.</summary>
    public static ByteRange? ParseByteRange(this JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } range)
            return null;

        var start = ParseInt64(range.GetString("start"));
        var end = ParseInt64(range.GetString("end"));

        return start is null || end is null ? null : new ByteRange(start.Value, end.Value);
    }

    /// <summary>Returns the array at <paramref name="path"/>, or an empty array.</summary>
    public static JsonElement.ArrayEnumerator? EnumerateArrayOrNull(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : null;
}
