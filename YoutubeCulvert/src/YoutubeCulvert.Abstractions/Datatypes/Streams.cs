namespace YoutubeCulvert.Datatypes;

/// <summary>Inclusive byte range from <c>initRange</c> / <c>indexRange</c>.</summary>
public readonly record struct ByteRange(long Start, long End)
{
    public long Length => End >= Start ? End - Start + 1 : 0;
}

/// <summary>
/// Cipher information of a protected stream. The library does not decipher
/// signatures; it only exposes the raw parts so that a caller can convert the
/// URL with an external component.
/// </summary>
public sealed record Cipher(string? Url, string? Signature, string? SignatureParameter, string? Raw);

/// <summary>A single audio or video stream from <c>streamingData</c>.</summary>
public sealed record MediaStream : YoutubeEntity
{
    /// <summary>Direct media URL (<c>url</c> or the <c>url</c> part of <c>signatureCipher</c>).</summary>
    public required string Url { get; init; }

    public int? Itag { get; init; }

    public string? MimeType { get; init; }

    public string? Codecs { get; init; }

    public string? QualityLabel { get; init; }

    public string? AudioQuality { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public int? Fps { get; init; }

    public long? Bitrate { get; init; }

    public long? ContentLength { get; init; }

    public bool IsAudioOnly { get; init; }

    public bool IsVideoOnly { get; init; }

    /// <summary>True for adaptive (DASH) formats; false for muxed progressive formats.</summary>
    public bool IsAdaptive { get; init; }

    /// <summary>Whether the stream already contains both audio and video.</summary>
    public bool IsMuxed => !IsAudioOnly && !IsVideoOnly;

    /// <summary>Value of <c>audioTrack.audioTrackId</c>; null for unnamed tracks.</summary>
    public string? AudioTrackId { get; init; }

    /// <summary>Value of <c>audioTrack.displayName</c>.</summary>
    public string? AudioTrackName { get; init; }

    /// <summary>Value of <c>audioTrack.audioIsDefault</c>.</summary>
    public bool AudioIsDefault { get; init; }

    /// <summary>Language inferred from <see cref="AudioTrackId"/>; null when unknown.</summary>
    public string? LanguageCode { get; init; }

    public TimeSpan? ApproxDuration { get; init; }

    public ByteRange? InitRange { get; init; }

    public ByteRange? IndexRange { get; init; }

    /// <summary>Cipher parts when YouTube returned a protected URL.</summary>
    public Cipher? Cipher { get; init; }
}

/// <summary>The streams of one video, grouped by kind and audio track.</summary>
public sealed record StreamManifest : YoutubeEntity
{
    /// <summary>Identifier used for the audio track of streams that have no <c>audioTrack</c>.</summary>
    public const string UnnamedAudioTrackId = "default";

    public IReadOnlyList<MediaStream> Streams { get; init; } = [];

    public DateTimeOffset? ExpiresAt { get; init; }

    public IReadOnlyList<MediaStream> Muxed =>
        Streams.Where(static stream => stream.IsMuxed).ToArray();

    public IReadOnlyList<MediaStream> VideoOnly =>
        Streams.Where(static stream => !stream.IsAudioOnly && stream.IsVideoOnly).ToArray();

    public IReadOnlyList<MediaStream> AudioOnly =>
        Streams.Where(static stream => stream.IsAudioOnly).ToArray();

    /// <summary>Best muxed stream (audio + video in one file), optionally limited by height.</summary>
    public MediaStream? GetBestMuxedStream(int? maxHeight = null) =>
        SelectBest(Streams.Where(stream =>
            stream.IsMuxed &&
            (maxHeight is null || stream.Height is null || stream.Height <= maxHeight)));

    /// <summary>Best video stream, optionally limited by height.</summary>
    public MediaStream? GetBestVideoStream(int? maxHeight = null) =>
        SelectBest(Streams.Where(stream =>
            !stream.IsAudioOnly &&
            (maxHeight is null || stream.Height is null || stream.Height <= maxHeight)));

    /// <summary>Best audio stream of the default track.</summary>
    public MediaStream? GetBestAudioStream()
    {
        var track = GetDefaultAudioTrack();
        var candidates = track is null ? AudioOnly : GetAudioStreams(track.Id);
        return SelectBest(candidates, preferAudioQuality: true);
    }

    /// <summary>Best audio stream of a specific audio track.</summary>
    public MediaStream? GetBestAudioStream(string audioTrackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioTrackId);
        return SelectBest(GetAudioStreams(audioTrackId), preferAudioQuality: true);
    }

    /// <summary>Best audio stream of the original audio track.</summary>
    public MediaStream? GetBestOriginalAudioStream()
    {
        var original = GetAudioTracks().FirstOrDefault(static track => track.IsOriginal);
        return original is null
            ? GetBestAudioStream()
            : SelectBest(GetAudioStreams(original.Id), preferAudioQuality: true);
    }

    /// <summary>Finds a video stream by quality label, for example <c>"1080p"</c> or <c>"720p60"</c>.</summary>
    public MediaStream? GetStreamByQuality(string qualityLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qualityLabel);

        return Streams.FirstOrDefault(stream =>
            !stream.IsAudioOnly &&
            string.Equals(stream.QualityLabel, qualityLabel, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds a stream by itag.</summary>
    public MediaStream? GetStreamByItag(int itag) =>
        Streams.FirstOrDefault(stream => stream.Itag == itag);

    /// <summary>All audio tracks, ordered as YouTube returned them.</summary>
    public IReadOnlyList<AudioTrack> GetAudioTracks()
    {
        var groups = new List<(string Key, List<MediaStream> Streams)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var stream in AudioOnly)
        {
            var key = stream.AudioTrackId ?? UnnamedAudioTrackId;

            if (!index.TryGetValue(key, out var position))
            {
                position = groups.Count;
                index[key] = position;
                groups.Add((key, []));
            }

            groups[position].Streams.Add(stream);
        }

        if (groups.Count == 0)
            return [];

        var anyMarkedDefault = AudioOnly.Any(static stream => stream.AudioIsDefault);
        var tracks = new List<AudioTrack>(groups.Count);

        for (var position = 0; position < groups.Count; position++)
        {
            var (key, streams) = groups[position];
            var first = streams[0];
            var isDefault = anyMarkedDefault
                ? streams.Any(static stream => stream.AudioIsDefault)
                : position == 0;

            tracks.Add(new AudioTrack
            {
                Id = key,
                DisplayName = first.AudioTrackName ?? "Default",
                LanguageCode = first.LanguageCode ?? LanguageFromTrackId(first.AudioTrackId),
                IsDefault = isDefault,
                IsOriginal = isDefault,
                StreamCount = streams.Count,
                Raw = first.Raw
            });
        }

        return tracks;
    }

    /// <summary>Streams that belong to a specific audio track.</summary>
    public IReadOnlyList<MediaStream> GetAudioStreams(string audioTrackId)
    {
        var key = string.IsNullOrWhiteSpace(audioTrackId)
            ? UnnamedAudioTrackId
            : audioTrackId;

        return AudioOnly
            .Where(stream => string.Equals(
                stream.AudioTrackId ?? UnnamedAudioTrackId, key, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>The default audio track; the first one when YouTube marks none.</summary>
    public AudioTrack? GetDefaultAudioTrack() =>
        GetAudioTracks().FirstOrDefault(static track => track.IsDefault);

    /// <summary>Infers a BCP-47 language from <c>en-US.4</c>; null when the id is not language-like.</summary>
    public static string? LanguageFromTrackId(string? audioTrackId)
    {
        if (string.IsNullOrWhiteSpace(audioTrackId))
            return null;

        var dot = audioTrackId.LastIndexOf('.');
        var language = dot > 0 && int.TryParse(audioTrackId[(dot + 1)..], out _)
            ? audioTrackId[..dot]
            : audioTrackId;

        if (language.Length is < 2 or > 24)
            return null;

        foreach (var character in language)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
                return null;
        }

        return language;
    }

    private static MediaStream? SelectBest(
        IEnumerable<MediaStream> candidates,
        bool preferAudioQuality = false)
    {
        return candidates
            .OrderByDescending(stream => preferAudioQuality ? stream.Bitrate ?? 0 : stream.Height ?? 0)
            .ThenByDescending(static stream => stream.Fps ?? 0)
            .ThenByDescending(static stream => stream.Bitrate ?? 0)
            .FirstOrDefault();
    }
}
