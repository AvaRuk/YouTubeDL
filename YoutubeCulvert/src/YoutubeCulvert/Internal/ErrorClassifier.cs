namespace YoutubeCulvert.Internal;

/// <summary>Maps exceptions to <see cref="YoutubeError"/> and classifies HTTP statuses.</summary>
internal static class ErrorClassifier
{
    public static bool CanMap(Exception exception)
    {
        switch (exception)
        {
            case VideoUnavailableException:
            case YoutubeAuthenticationRequiredException:
            case YoutubeAuthenticationException:
            case YoutubeParseException:
            case YoutubeApiException:
            case HttpRequestException:
            case IOException:
            case TimeoutException:
                return true;

            default:
                return false;
        }
    }

    public static bool TryMap(Exception exception, out YoutubeError error)
    {
        switch (exception)
        {
            case VideoUnavailableException unavailable:
                error = new YoutubeError(
                    YoutubeErrorKind.Unavailable,
                    unavailable.Message,
                    404,
                    unavailable.Playability.Raw);
                return true;

            case YoutubeAuthenticationRequiredException required:
                error = new YoutubeError(YoutubeErrorKind.AuthenticationRequired, required.Message);
                return true;

            case YoutubeAuthenticationException authentication:
                error = new YoutubeError(
                    YoutubeErrorKind.AuthenticationRequired,
                    authentication.Message,
                    authentication.StatusCode);
                return true;

            case YoutubeParseException parse:
                error = new YoutubeError(YoutubeErrorKind.ParseFailed, parse.Message);
                return true;

            case YoutubeApiException api when (int)api.StatusCode == 429:
                error = new YoutubeError(
                    YoutubeErrorKind.RateLimited,
                    api.Message,
                    api.StatusCodeValue,
                    ParseBody(api.ResponseBody));
                return true;

            case YoutubeApiException api when api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                error = new YoutubeError(
                    YoutubeErrorKind.AuthenticationRequired,
                    api.Message,
                    api.StatusCodeValue,
                    ParseBody(api.ResponseBody));
                return true;

            case YoutubeApiException api:
                error = new YoutubeError(
                    YoutubeErrorKind.Network,
                    api.Message,
                    api.StatusCodeValue,
                    ParseBody(api.ResponseBody));
                return true;

            case HttpRequestException or IOException or TimeoutException:
                error = new YoutubeError(YoutubeErrorKind.Network, exception.Message);
                return true;

            default:
                error = null!;
                return false;
        }
    }

    /// <summary>
    /// Converts a raw HTTP failure into a more specific exception: 401 is a
    /// failed sign-in, 403 is an operation that requires a valid account.
    /// </summary>
    public static Exception Classify(YoutubeApiException exception) => exception.StatusCode switch
    {
        HttpStatusCode.Unauthorized => new YoutubeAuthenticationException(
            $"YouTube rejected the session (HTTP 401) for '{exception.Endpoint}'.", exception)
        {
            StatusCode = 401
        },

        HttpStatusCode.Forbidden => new YoutubeAuthenticationRequiredException(
            $"YouTube forbade the request (HTTP 403) for '{exception.Endpoint}'. " +
            "The operation needs a signed-in account with valid cookies.", exception),

        _ => exception
    };

    public static JsonElement? ParseBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
