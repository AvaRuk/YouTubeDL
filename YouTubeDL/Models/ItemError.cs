namespace YouTubeDL.Models;

/// <summary>An action offered next to a failed item.</summary>
public enum ErrorAction
{
    None = 0,
    Retry = 1,
    SignIn = 2,
    OpenOnYouTube = 3,
    OpenFolder = 4
}

public sealed record ItemError(ErrorCategory Category, string Message, ErrorAction Action = ErrorAction.None)
{
    public bool IsRetryable => Category == ErrorCategory.Retryable;

    public static ItemError Retry(string message) =>
        new(ErrorCategory.Retryable, message, ErrorAction.Retry);

    public static ItemError Terminal(string message, ErrorAction action = ErrorAction.None) =>
        new(ErrorCategory.Terminal, message, action);

    public static ItemError Canceled(string message) => new(ErrorCategory.Canceled, message);
}
