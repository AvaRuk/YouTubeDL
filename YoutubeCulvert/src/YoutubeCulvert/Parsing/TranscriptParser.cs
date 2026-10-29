using System.Globalization;
using YoutubeCulvert.Internal;

namespace YoutubeCulvert.Parsing;

/// <summary>Parses <c>api/timedtext?...&amp;fmt=json3</c>.</summary>
public sealed class TimedTextParser : IResponseParser<JsonElement, TranscriptSegment>
{
    /// <summary>Parses a timedtext <c>json3</c> document from its raw text.</summary>
    public static ParseResult<TranscriptSegment> Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return ParseResult<TranscriptSegment>.Unrecognized("empty timedtext body", "events");

        try
        {
            using var document = JsonDocument.Parse(body);
            return new TimedTextParser().Parse(document.RootElement);
        }
        catch (JsonException exception)
        {
            return ParseResult<TranscriptSegment>.Unrecognized(
                "invalid timedtext JSON: " + exception.Message,
                "events");
        }
    }

    public ParseResult<TranscriptSegment> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            return ParseResult<TranscriptSegment>.Unrecognized(
                "timedtext response has no events array",
                "events");
        }

        var segments = new List<TranscriptSegment>();

        foreach (var @event in events.EnumerateArray())
        {
            if (@event.ValueKind != JsonValueKind.Object)
                continue;

            var startMs = @event.GetDouble("tStartMs") ?? 0;
            var durationMs = @event.GetDouble("dDurationMs") ?? 0;

            if (!@event.TryGetProperty("segs", out var segs) || segs.ValueKind != JsonValueKind.Array)
                continue;

            var text = string.Empty;

            foreach (var seg in segs.EnumerateArray())
            {
                if (seg.ValueKind != JsonValueKind.Object)
                    continue;

                var piece = seg.GetString("utf8");
                if (!string.IsNullOrEmpty(piece))
                    text += piece;
            }

            text = text.Replace("\r", string.Empty, StringComparison.Ordinal).Trim();

            if (text.Length == 0)
                continue;

            segments.Add(new TranscriptSegment
            {
                Start = TimeSpan.FromMilliseconds(startMs),
                Duration = TimeSpan.FromMilliseconds(durationMs),
                Text = text,
                Raw = @event.Clone()
            });
        }

        return ParseResult<TranscriptSegment>.Recognized(segments);
    }
}

/// <summary>Parses transcript panel segments from a <c>get_transcript</c> response.</summary>
public sealed class TranscriptPanelParser : IResponseParser<JsonElement, TranscriptSegment>
{
    public ParseResult<TranscriptSegment> Parse(JsonElement root)
    {
        var segments = new List<TranscriptSegment>();
        var sawRenderer = false;

        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("transcriptSegmentRenderer", out var renderer) ||
                renderer.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            sawRenderer = true;

            var startMs = ParseMilliseconds(renderer.GetString("startMs"));
            var endMs = ParseMilliseconds(renderer.GetString("endMs"));
            var text = renderer.TryGetProperty("snippet", out var snippet)
                ? snippet.GetText()?.Trim()
                : null;

            if (string.IsNullOrEmpty(text))
                continue;

            segments.Add(new TranscriptSegment
            {
                Start = startMs,
                Duration = endMs > startMs ? endMs - startMs : TimeSpan.Zero,
                Text = text,
                Raw = renderer.Clone()
            });
        }

        if (!sawRenderer && root.FindFirstObjectWithProperty("continuationContents") is null)
        {
            return ParseResult<TranscriptSegment>.Unrecognized(
                "response has no transcriptSegmentRenderer items",
                "continuationContents");
        }

        return ParseResult<TranscriptSegment>.Recognized(segments);
    }

    private static TimeSpan ParseMilliseconds(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            ? TimeSpan.FromMilliseconds(ms)
            : TimeSpan.Zero;
}

/// <summary>Helpers shared by the transcript service.</summary>
public static class TranscriptParser
{
    /// <summary>Finds the continuation token of the transcript engagement panel.</summary>
    public static string? FindTranscriptContinuation(JsonElement root)
    {
        foreach (var element in root.Descendants())
        {
            var identifier = element.GetString("panelIdentifier");

            if (!string.Equals(identifier, "engagement-panel-searchable-transcript", StringComparison.Ordinal))
                continue;

            var token = element.FindContinuationToken();
            if (token is not null)
                return token;
        }

        return null;
    }

    /// <summary>Parses <c>translationLanguages[]</c> into language codes.</summary>
    public static IReadOnlyList<string> ParseTranslationLanguages(JsonElement root)
    {
        var languages = root.Path("captions", "playerCaptionsTracklistRenderer", "translationLanguages");

        if (languages is not { ValueKind: JsonValueKind.Array } array)
            return [];

        return array
            .EnumerateArray()
            .Select(language => language.GetString("languageCode"))
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Select(static code => code!)
            .ToArray();
    }

    /// <summary>Returns the language titles listed in the transcript panel footer.</summary>
    public static IReadOnlyList<string> ParsePanelLanguages(JsonElement root)
    {
        var result = new List<string>();

        foreach (var element in root.Descendants())
        {
            if (!element.TryGetProperty("subMenuItems", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var title = item.GetTextProperty("title");
                if (string.IsNullOrWhiteSpace(title) ||
                    result.Contains(title, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(title);
            }
        }

        return result;
    }

    /// <summary>
    /// Rewrites a timedtext URL to request JSON, replacing the format and
    /// translation parameters that YouTube put into the signed URL.
    /// </summary>
    public static string BuildTimedTextUrl(string baseUrl, string? translateTo)
    {
        var queryIndex = baseUrl.IndexOf('?');
        var path = queryIndex < 0 ? baseUrl : baseUrl[..queryIndex];
        var query = queryIndex < 0 ? string.Empty : baseUrl[(queryIndex + 1)..];

        var parameters = new List<KeyValuePair<string, string>>();

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];

            if (name is "fmt" or "tlang")
                continue;

            parameters.Add(new KeyValuePair<string, string>(
                name,
                separator < 0 ? string.Empty : pair[(separator + 1)..]));
        }

        parameters.Add(new KeyValuePair<string, string>("fmt", "json3"));

        if (!string.IsNullOrWhiteSpace(translateTo))
            parameters.Add(new KeyValuePair<string, string>("tlang", translateTo));

        return path + "?" + string.Join('&', parameters.Select(static p => p.Key + "=" + p.Value));
    }
}
