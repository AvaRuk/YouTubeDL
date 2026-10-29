namespace YoutubeCulvert.Datatypes;

/// <summary>
/// A DTO that keeps the original JSON payload, so fields not yet modelled by
/// the library remain reachable through <see cref="Get{T}(string)"/>.
/// </summary>
public interface IExtensible
{
    /// <summary>Raw JSON object this DTO was parsed from, when available.</summary>
    JsonElement? Raw { get; }

    /// <summary>
    /// Reads a value by a JSON pointer, for example <c>"videoDetails/author"</c>
    /// or <c>"contents/0/title"</c>.
    /// </summary>
    T? Get<T>(string jsonPointer);
}

/// <summary>Base class of all YouTube DTOs; stores <see cref="IExtensible.Raw"/>.</summary>
public abstract record YoutubeEntity : IExtensible
{
    /// <inheritdoc />
    [System.Text.Json.Serialization.JsonIgnore]
    public JsonElement? Raw { get; init; }

    /// <inheritdoc />
    public T? Get<T>(string jsonPointer) => RawJsonAccessor.Get<T>(Raw, jsonPointer);
}

/// <summary>Minimal JSON-pointer reader used by <see cref="YoutubeEntity"/>.</summary>
internal static class RawJsonAccessor
{
    public static T? Get<T>(JsonElement? raw, string jsonPointer)
    {
        if (raw is not { } element || string.IsNullOrWhiteSpace(jsonPointer))
            return default;

        var current = element;

        foreach (var rawPart in jsonPointer.TrimStart('/').Split('/'))
        {
            if (rawPart.Length == 0)
                continue;

            var part = rawPart.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);

            if (current.ValueKind == JsonValueKind.Object &&
                current.TryGetProperty(part, out var property))
            {
                current = property;
            }
            else if (current.ValueKind == JsonValueKind.Array &&
                     int.TryParse(part, out var index) &&
                     index >= 0 && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else
            {
                return default;
            }
        }

        return Convert<T>(current);
    }

    private static T? Convert<T>(JsonElement element)
    {
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (target == typeof(JsonElement))
            return (T)(object)element.Clone();

        if (target == typeof(string))
            return (T)(object)(element.ValueKind == JsonValueKind.String
                ? element.GetString()!
                : element.GetRawText());

        if (target == typeof(bool))
            return element.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? (T)(object)element.GetBoolean()
                : default;

        if (target == typeof(int) && element.TryGetInt32(out var intValue))
            return (T)(object)intValue;

        if (target == typeof(long) && element.TryGetInt64(out var longValue))
            return (T)(object)longValue;

        if (target == typeof(double) && element.TryGetDouble(out var doubleValue))
            return (T)(object)doubleValue;

        if (target == typeof(decimal) && element.TryGetDecimal(out var decimalValue))
            return (T)(object)decimalValue;

        if (target == typeof(DateTimeOffset) &&
            element.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(element.GetString(), out var date))
        {
            return (T)(object)date;
        }

        if (target == typeof(TimeSpan) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetDouble(out var seconds))
        {
            return (T)(object)TimeSpan.FromSeconds(seconds);
        }

        try
        {
            return element.Deserialize<T>();
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

/// <summary>A thumbnail image with its intrinsic size.</summary>
public sealed record Thumbnail : YoutubeEntity
{
    public required string Url { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Pixel area; useful for picking the largest thumbnail.</summary>
    public long Area => (long)Width * Height;
}

/// <summary>Availability of a video as reported in <c>playabilityStatus</c>.</summary>
public sealed record PlayabilityStatus : YoutubeEntity
{
    public string Status { get; init; } = "OK";

    public string? Reason { get; init; }

    /// <summary>More detailed explanation (<c>playabilityStatus.errorScreen</c>).</summary>
    public string? SubReason { get; init; }

    public bool IsOk => string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase);

    public static PlayabilityStatus Ok { get; } = new();
}

/// <summary>A chapter marker of a video.</summary>
public sealed record Chapter : YoutubeEntity
{
    public required string Title { get; init; }

    /// <summary>Offset from the start of the video.</summary>
    public TimeSpan Start { get; init; }

    /// <summary>End of the chapter, when the next chapter is known.</summary>
    public TimeSpan? End { get; init; }

    public string? ThumbnailUrl { get; init; }
}

/// <summary>A distinct audio track (original language, translation or dub).</summary>
public sealed record AudioTrack : YoutubeEntity
{
    /// <summary>
    /// Track identifier from <c>audioTrack.audioTrackId</c>, for example
    /// <c>"en-US.4"</c>. Streams without the field are grouped under
    /// <see cref="StreamManifest.UnnamedAudioTrackId"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Human readable name, for example <c>"English (US)"</c>.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Language inferred from <see cref="Id"/> (<c>en-US.4</c> → <c>en-US</c>).</summary>
    public string? LanguageCode { get; init; }

    /// <summary>Whether YouTube marks this track as the default one.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Whether this track is the original audio of the video.</summary>
    public bool IsOriginal { get; init; }

    /// <summary>Number of streams (different itags / bitrates) in this track.</summary>
    public int StreamCount { get; init; }
}
