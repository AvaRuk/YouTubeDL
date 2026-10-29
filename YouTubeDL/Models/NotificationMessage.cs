namespace YouTubeDL.Models;

/// <summary>In-app notification shown above the status bar.</summary>
public sealed class NotificationMessage
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string Title { get; init; }

    public required string Message { get; init; }

    public bool IsError { get; init; }

    public string? ActionLabel { get; init; }

    public Action? Action { get; init; }

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    public bool HasAction => Action is not null && !string.IsNullOrWhiteSpace(ActionLabel);
}
