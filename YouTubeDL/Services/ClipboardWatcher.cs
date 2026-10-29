using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using YoutubeCulvert.Url;

namespace YouTubeDL.Services;

/// <summary>Polls the clipboard and reports YouTube links (optional feature).</summary>
public sealed class ClipboardWatcher : IDisposable
{
    private readonly Func<TopLevel?> _owner;
    private readonly IYoutubeUrlParser _urls;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastText;
    private bool _disposed;

    public ClipboardWatcher(Func<TopLevel?> owner, IYoutubeUrlParser urls)
    {
        _owner = owner;
        _urls = urls;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _timer.Tick += OnTick;
    }

    public event Action<string>? LinkFound;

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        if (_disposed)
            return;

        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private async void OnTick(object? sender, EventArgs e)
    {
        try
        {
            var clipboard = _owner()?.Clipboard;
            if (clipboard is null)
                return;

            var text = (await clipboard.TryGetTextAsync().ConfigureAwait(true))?.Trim();

            if (string.IsNullOrWhiteSpace(text) || text == _lastText)
                return;

            _lastText = text;

            var isVideo = _urls.TryParseVideoId(text, out var videoId) && videoId is not null;
            var isPlaylist = _urls.TryParsePlaylistId(text, out var playlistId) && playlistId is not null;

            if (!isVideo && !isPlaylist)
                return;

            if (!_seen.Add(text))
                return;

            LinkFound?.Invoke(text);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
