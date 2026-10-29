namespace YoutubeCulvert.Protobuf;

/// <summary>
/// Builds the <c>params</c> value of the InnerTube <c>search</c> endpoint
/// (SearchFilter message).
/// <code>
/// field 1 (varint) — sort order (0 relevance, 1 rating, 2 upload date, 3 views)
/// field 2 (message) — filters {
///     1 upload date, 2 type, 3 duration, 4 HD, 5 subtitles, 6 CC, 7 3D,
///     8 live, 9 purchased, 14 4K, 15 360°, 23 location, 25 HDR, 26 VR180
/// }
/// </code>
/// </summary>
internal static class SearchParamsBuilder
{
    public static string? Build(SearchFilter filter, SortOrder sortOrder)
    {
        var filters = new ProtoWriter();
        filters.WriteInt32(1, (int)filter.UploadDate);
        filters.WriteInt32(2, GetTypeValue(filter.Type));
        filters.WriteInt32(3, (int)filter.Duration);

        WriteFeatures(filters, filter.Features);

        // Live is a feature flag rather than a result type.
        if (filter.Type == SearchType.Live)
            filters.WriteBool(8, true);

        var writer = new ProtoWriter();
        writer.WriteInt32(1, (int)sortOrder);
        writer.WriteMessage(2, filters);

        var bytes = writer.ToArray();
        return bytes.Length == 0 ? null : Convert.ToBase64String(bytes);
    }

    private static int GetTypeValue(SearchType type) => type switch
    {
        SearchType.Video or SearchType.Live => 1,
        SearchType.Channel => 2,
        SearchType.Playlist => 3,
        SearchType.Movie => 4,
        SearchType.Shorts => 9,
        _ => 0
    };

    private static void WriteFeatures(ProtoWriter writer, SearchFeatures features)
    {
        if (features == SearchFeatures.None)
            return;

        writer.WriteBool(4, features.HasFlag(SearchFeatures.Hd));
        writer.WriteBool(5, features.HasFlag(SearchFeatures.Subtitles));
        writer.WriteBool(6, features.HasFlag(SearchFeatures.CreativeCommons));
        writer.WriteBool(7, features.HasFlag(SearchFeatures.ThreeD));
        writer.WriteBool(8, features.HasFlag(SearchFeatures.Live));
        writer.WriteBool(9, features.HasFlag(SearchFeatures.Purchased));
        writer.WriteBool(14, features.HasFlag(SearchFeatures.FourK));
        writer.WriteBool(15, features.HasFlag(SearchFeatures.ThreeSixty));
        writer.WriteBool(23, features.HasFlag(SearchFeatures.Location));
        writer.WriteBool(25, features.HasFlag(SearchFeatures.Hdr));
        writer.WriteBool(26, features.HasFlag(SearchFeatures.Vr180));
    }
}
