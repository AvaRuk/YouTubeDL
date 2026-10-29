using System.Text.Json;

namespace YoutubeCulvert.Handle;

/// <summary>
/// Extracts embedded JSON objects (<c>ytInitialPlayerResponse</c>,
/// <c>ytInitialData</c>) and channel ids from YouTube HTML without an HTML parser.
/// </summary>
public static class HtmlJsonExtractor
{
    private const int MaxGapAfterVariable = 512;

    public static JsonElement? ExtractPlayerResponse(string html) => ExtractJsonObject(html, "ytInitialPlayerResponse");

    public static JsonElement? ExtractInitialData(string html) => ExtractJsonObject(html, "ytInitialData");

    /// <summary>Finds a UC channel id in the HTML (<c>externalId</c>, <c>channelId</c>, <c>/channel/</c>).</summary>
    public static string? ExtractChannelId(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        foreach (var marker in new[] { "\"externalId\":\"", "\"channelId\":\"", "/channel/" })
        {
            var index = 0;

            while (index < html.Length)
            {
                index = html.IndexOf(marker, index, StringComparison.Ordinal);
                if (index < 0)
                    break;

                var start = index + marker.Length;

                if (start + 24 <= html.Length)
                {
                    var candidate = html.Substring(start, 24);

                    if (candidate.StartsWith("UC", StringComparison.Ordinal) &&
                        candidate.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                    {
                        return candidate;
                    }
                }

                index = start;
            }
        }

        return null;
    }

    private static JsonElement? ExtractJsonObject(string html, string variableName)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var searchIndex = 0;

        while (searchIndex < html.Length)
        {
            var variableIndex = html.IndexOf(variableName, searchIndex, StringComparison.Ordinal);
            if (variableIndex < 0)
                return null;

            var braceIndex = html.IndexOf('{', variableIndex + variableName.Length);
            if (braceIndex < 0)
                return null;

            // Only an assignment or indexer should appear between the name and the object.
            if (braceIndex - (variableIndex + variableName.Length) > MaxGapAfterVariable)
            {
                searchIndex = variableIndex + variableName.Length;
                continue;
            }

            var json = ExtractBalancedObject(html, braceIndex);

            if (json is not null)
            {
                try
                {
                    using var document = JsonDocument.Parse(json);
                    return document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    // Try the next occurrence of the variable name.
                }
            }

            searchIndex = variableIndex + variableName.Length;
        }

        return null;
    }

    private static string? ExtractBalancedObject(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = startIndex; index < text.Length; index++)
        {
            var character = text[index];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;

                case '{':
                    depth++;
                    break;

                case '}':
                    depth--;
                    if (depth == 0)
                        return text[startIndex..(index + 1)];
                    break;
            }
        }

        return null;
    }
}
