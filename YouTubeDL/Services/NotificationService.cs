using System.Collections.ObjectModel;
using Avalonia.Threading;
using YouTubeDL.Models;

namespace YouTubeDL.Services;

/// <summary>In-app notifications with an optional action button.</summary>
public sealed class NotificationService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(9);

    public NotificationService()
    {
        Items = new ObservableCollection<NotificationMessage>();
    }

    public ObservableCollection<NotificationMessage> Items { get; }

    public void Notify(string title, string message, string? actionLabel = null, Action? action = null, bool isError = false)
    {
        var notification = new NotificationMessage
        {
            Title = title,
            Message = message,
            ActionLabel = actionLabel,
            Action = action,
            IsError = isError
        };

        Post(() => Items.Insert(0, notification));

        _ = Task.Delay(Lifetime).ContinueWith(
            _ => Post(() => { Items.Remove(notification); }),
            TaskScheduler.Default);
    }

    public void NotifyError(string title, string message, string? actionLabel = null, Action? action = null) =>
        Notify(title, message, actionLabel, action, isError: true);

    public void Remove(NotificationMessage notification) => Post(() => { Items.Remove(notification); });

    public void Clear() => Post(Items.Clear);

    public void Invoke(NotificationMessage notification)
    {
        if (notification.Action is { } action)
        {
            Remove(notification);
            action();
        }
    }

    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
