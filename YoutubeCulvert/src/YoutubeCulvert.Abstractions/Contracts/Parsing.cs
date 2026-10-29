namespace YoutubeCulvert.Parsing;

/// <summary>
/// Parses a YouTube response into a page of items. Implementations are public
/// so they can be unit tested and replaced without touching services.
/// </summary>
public interface IResponseParser<TInput, TOutput>
{
    ParseResult<TOutput> Parse(TInput input);
}

/// <summary>Result of parsing one response.</summary>
public sealed record ParseResult<T>(
    IReadOnlyList<T> Items,
    string? Continuation,
    ParseDiagnostics Diagnostics)
{
    public bool IsRecognized => Diagnostics.StructureRecognized;

    public static ParseResult<T> Recognized(IReadOnlyList<T> items, string? continuation = null) =>
        new(items, continuation, ParseDiagnostics.Recognized);

    public static ParseResult<T> Unrecognized(string reason, string? path = null) =>
        new([], null, ParseDiagnostics.Unrecognized(reason, path));
}

/// <summary>
/// Diagnostics of a parse attempt. A tolerated-but-unrecognized response must
/// report <see cref="StructureRecognized"/> = false instead of silently
/// returning an empty list.
/// </summary>
public sealed record ParseDiagnostics(
    bool StructureRecognized,
    string? Reason = null,
    string? Path = null)
{
    public static ParseDiagnostics Recognized { get; } = new(true);

    public static ParseDiagnostics Unrecognized(string reason, string? path = null) =>
        new(false, reason, path);
}
