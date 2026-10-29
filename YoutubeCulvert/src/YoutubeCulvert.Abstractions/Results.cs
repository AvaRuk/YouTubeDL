namespace YoutubeCulvert;

/// <summary>Classification of an expected YouTube failure.</summary>
public enum YoutubeErrorKind
{
    /// <summary>The video/playlist/channel is unavailable (private, deleted, restricted).</summary>
    Unavailable,

    /// <summary>The operation needs a signed-in account or the session was rejected.</summary>
    AuthenticationRequired,

    /// <summary>YouTube rate-limited the request (usually HTTP 429).</summary>
    RateLimited,

    /// <summary>The response structure was not recognised.</summary>
    ParseFailed,

    /// <summary>Network/HTTP failure that is not an expected business situation.</summary>
    Network
}

/// <summary>An expected failure returned through <see cref="Result{T}"/>.</summary>
public sealed record YoutubeError(
    YoutubeErrorKind Kind,
    string Message,
    int? StatusCode = null,
    JsonElement? Raw = null);

/// <summary>
/// A success-or-expected-error result. Thrown exceptions remain for programming
/// errors and transport failures; <c>Try*Async</c> methods return this type instead
/// of throwing for unavailable/auth-required/rate-limited responses.
/// </summary>
public readonly record struct Result<T>
{
    public T? Value { get; init; }

    public YoutubeError? Error { get; init; }

    public bool IsSuccess => Error is null;

    public static Result<T> Ok(T value) => new() { Value = value };

    public static Result<T> Fail(YoutubeError error) => new() { Error = error };

    public static Result<T> Fail(YoutubeErrorKind kind, string message, int? statusCode = null) =>
        Fail(new YoutubeError(kind, message, statusCode));
}
