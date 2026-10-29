namespace YouTubeDL.Models;

/// <summary>Application-level result used by services that must not throw for expected failures.</summary>
public readonly record struct AppResult<T>
{
    public T? Value { get; init; }

    public ItemError? Error { get; init; }

    public bool IsSuccess => Error is null;

    public static AppResult<T> Ok(T value) => new() { Value = value };

    public static AppResult<T> Fail(ItemError error) => new() { Error = error };

    public static AppResult<T> Fail(string message, ErrorCategory category = ErrorCategory.Terminal,
        ErrorAction action = ErrorAction.None) =>
        new() { Error = new ItemError(category, message, action) };
}

/// <summary>Thrown by the download pipeline; carries a classified, localized error.</summary>
public sealed class DownloadException : Exception
{
    public DownloadException(ItemError error)
        : base(error.Message)
    {
        Error = error;
    }

    public DownloadException(ItemError error, Exception inner)
        : base(error.Message, inner)
    {
        Error = error;
    }

    public ItemError Error { get; }
}
