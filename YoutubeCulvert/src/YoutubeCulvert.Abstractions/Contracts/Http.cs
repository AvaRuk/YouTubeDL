namespace YoutubeCulvert.Http;

/// <summary>
/// Minimal HTTP reader used by services that work with raw pages or timedtext
/// documents (search suggestions, transcripts, HTML pages). Retries and
/// signing are not part of this contract.
/// </summary>
public interface IHttpTransport
{
    Task<string> GetTextAsync(string url, string accept, CancellationToken ct = default);

    Task<string> GetPageHtmlAsync(string url, CancellationToken ct = default);
}
