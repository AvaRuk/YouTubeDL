using YoutubeCulvert.Internal;

namespace YoutubeCulvert.Services;

/// <summary>Maps expected failures to <see cref="Result{T}"/> for the <c>Try*</c> methods.</summary>
internal static class ServiceErrors
{
    public static async Task<Result<T>> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Result<T>.Ok(await action().ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (ErrorClassifier.CanMap(exception))
        {
            ErrorClassifier.TryMap(exception, out var error);
            return Result<T>.Fail(error);
        }
    }
}

/// <summary>Cache keys include <c>hl</c>/<c>gl</c>, so regions never mix.</summary>
internal static class CacheKeys
{
    public static string Video(string videoId, string language, string region) =>
        $"video:{videoId}:{language}:{region}";

    public static string Streams(string videoId, string language, string region) =>
        $"streams:{videoId}:{language}:{region}";

    public static string Transcript(string videoId, string language, bool automatic, string hl, string gl) =>
        $"transcript:{videoId}:{language}:{automatic}:{hl}:{gl}";
}

/// <summary>Shared pagination logic for <see cref="IAsyncEnumerable{T}"/> overloads.</summary>
internal static class Paging
{
    public static int MaxItems(PaginationOptions? options) =>
        options?.MaxItems is > 0 ? options.MaxItems.Value : int.MaxValue;

    public static int MaxPages(PaginationOptions? options) =>
        options?.MaxPages is > 0 ? options.MaxPages.Value : int.MaxValue;
}
