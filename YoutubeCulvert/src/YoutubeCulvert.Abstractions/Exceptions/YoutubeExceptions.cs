using System.Net;
using YoutubeCulvert.Datatypes;

namespace YoutubeCulvert.Exceptions;

/// <summary>Base exception of the library.</summary>
public class YoutubeException : Exception
{
    public YoutubeException(string message) : base(message)
    {
    }

    public YoutubeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>A YouTube/InnerTube HTTP request failed.</summary>
public sealed class YoutubeApiException : YoutubeException
{
    public YoutubeApiException(
        HttpStatusCode statusCode,
        string endpoint,
        string? responseBody,
        string message,
        TimeSpan? retryAfter = null)
        : base(message)
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ResponseBody = responseBody;
        RetryAfter = retryAfter;
    }

    public YoutubeApiException(
        HttpStatusCode statusCode,
        string endpoint,
        string? responseBody,
        string message,
        Exception innerException,
        TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ResponseBody = responseBody;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Numeric status code, convenient for comparisons with 401/403/429.</summary>
    public int StatusCodeValue => (int)StatusCode;

    public string Endpoint { get; }

    /// <summary>Response body, truncated to 16 KB for diagnostics.</summary>
    public string? ResponseBody { get; }

    /// <summary>Value of the <c>Retry-After</c> header, when present.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>Sign-in failed or the session is invalid.</summary>
public sealed class YoutubeAuthenticationException : YoutubeException
{
    public YoutubeAuthenticationException(string message) : base(message)
    {
    }

    public YoutubeAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int? StatusCode { get; init; }
}

/// <summary>The operation requires an authenticated user.</summary>
public sealed class YoutubeAuthenticationRequiredException : YoutubeException
{
    public YoutubeAuthenticationRequiredException(string message) : base(message)
    {
    }

    public YoutubeAuthenticationRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The video is unavailable (private, deleted, age/region restricted).</summary>
public sealed class VideoUnavailableException : YoutubeException
{
    public VideoUnavailableException(string videoId, PlayabilityStatus playability)
        : base($"Video '{videoId}' is unavailable: {playability.Status}" +
               (string.IsNullOrWhiteSpace(playability.Reason) ? "." : $" — {playability.Reason}"))
    {
        VideoId = videoId;
        Playability = playability;
    }

    public string VideoId { get; }

    public PlayabilityStatus Playability { get; }
}

/// <summary>The YouTube response could not be parsed.</summary>
public sealed class YoutubeParseException : YoutubeException
{
    public YoutubeParseException(string message) : base(message)
    {
    }

    public YoutubeParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>The supplied <see cref="YoutubeClientOptions"/> are invalid.</summary>
public sealed class YoutubeConfigurationException : YoutubeException
{
    public YoutubeConfigurationException(string message) : base(message)
    {
    }
}
