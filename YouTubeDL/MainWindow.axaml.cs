using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using YouTubeDL.Models;
using YouTubeDL.Services;
using YouTubeDL.ViewModels;

namespace YouTubeDL;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.AttachWindow(this);

        try
        {
            await viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            viewModel.Notifications.NotifyError(
                LocalizationService.Instance.Get("NotificationErrorTitle"),
                exception.Message);
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnUrlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.Download.SearchCommand.Execute(null);
        e.Handled = true;
    }

    private async void OnBrowseOutputFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var path = await PickFolderAsync(LocalizationService.Instance.Get("SaveToLabel"));

        if (string.IsNullOrWhiteSpace(path))
            return;

        if (viewModel.Download.IsPlaylistDialogOpen)
            viewModel.Download.PlaylistOutputFolder = path;
        else
            viewModel.Download.OutputFolder = path;
    }

    private async void OnBrowseVideoFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var path = await PickFolderAsync(LocalizationService.Instance.Get("VideoFolderLabel"));

        if (!string.IsNullOrWhiteSpace(path))
            viewModel.Settings.VideoFolder = path;
    }

    private async void OnBrowseMusicFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var path = await PickFolderAsync(LocalizationService.Instance.Get("MusicFolderLabel"));

        if (!string.IsNullOrWhiteSpace(path))
            viewModel.Settings.MusicFolder = path;
    }

    private async void OnBrowseFfmpeg(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Instance.Get("SettingsFfmpegLabel"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("FFmpeg")
                {
                    Patterns = ["ffmpeg.exe", "ffmpeg"]
                }
            ]
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;

        if (!string.IsNullOrWhiteSpace(path))
            viewModel.Settings.FfmpegPath = path;
    }

    private async void OnImportCookies(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Instance.Get("ImportCookieFile"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Cookies")
                {
                    Patterns = ["*.txt", "*.json", "*.cookies"]
                }
            ]
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;

        if (!string.IsNullOrWhiteSpace(path))
            await viewModel.Account.ImportCookieFileAsync(path);
    }

    private async void OnExportQueue(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Instance.Get("QueueExport"),
            SuggestedFileName = "queue.json",
            DefaultExtension = "json",
            ShowOverwritePrompt = true
        });

        var path = file?.TryGetLocalPath();

        if (!string.IsNullOrWhiteSpace(path))
            viewModel.Queue.Export(path);
    }

    private async void OnImportQueue(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Instance.Get("QueueImport"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Queue")
                {
                    Patterns = ["*.json"]
                }
            ]
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;

        if (!string.IsNullOrWhiteSpace(path))
            viewModel.Queue.Import(path);
    }

    private void OnNotificationAction(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (sender is Button { DataContext: NotificationMessage notification })
            viewModel.Notifications.Invoke(notification);
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        string suggestedExtension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = suggestedExtension,
            ShowOverwritePrompt = true
        });

        return file?.TryGetLocalPath();
    }
}
