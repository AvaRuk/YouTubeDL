namespace YoutubeCulvert.Datatypes;

/// <summary>Outcome of an account action (like, subscribe, comment, playlist edit).</summary>
public sealed record InteractionResult : YoutubeEntity
{
    /// <summary>
    /// False when YouTube reported a "soft" error inside a 200 response; hard
    /// HTTP failures surface as exceptions instead.
    /// </summary>
    public bool Success { get; init; }

    public int? StatusCode { get; init; }
}
