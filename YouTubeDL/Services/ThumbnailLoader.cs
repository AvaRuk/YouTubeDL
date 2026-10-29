using System.Diagnostics;
using Avalonia.Media.Imaging;

namespace YouTubeDL.Services;

public static class ThumbnailLoader
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly SemaphoreSlim Gate = new(4, 4);

    static ThumbnailLoader()
    {
        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        Client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept",
            "image/jpeg,image/png,image/*;q=0.8");
    }

    public static async Task<Bitmap?> LoadAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            using var response = await Client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(true);

            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content
                .ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);

            using var memory = new MemoryStream(bytes);
            return new Bitmap(memory);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or ArgumentException)
        {
            Debug.WriteLine(exception);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<byte[]?> DownloadBytesAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            return await Client.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return null;
        }
    }
}
