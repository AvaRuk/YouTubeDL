using System.Globalization;
using System.Net;
using System.Text;
using YoutubeCulvert.Configuration;

namespace YoutubeCulvert.Http;

/// <summary>
/// Thin HTTP layer: HttpClient, timeouts, cookies and consent/visitor session.
/// Retries live in <c>RetryMiddleware</c>, never here.
/// </summary>
internal sealed class HttpTransport : IHttpTransport, IDisposable
{
    private const string Origin = "https://www.youtube.com";
    private const int MaxResponseBody = 4_096;

    private readonly YoutubeClientOptions _options;
    private readonly CookieContainer _cookies;
    private readonly HttpClient _client;
    private readonly Uri _baseUri;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly bool _manualCookies;

    private bool _initialized;
    private string? _visitorData;

    public HttpTransport(YoutubeClientOptions options, CookieContainer cookies)
    {
        _options = options;
        _cookies = cookies;

        if (!Uri.TryCreate(options.NormalizedBaseUrl, UriKind.Absolute, out var baseUri))
            throw new ArgumentException("BaseUrl must be an absolute URL.", nameof(options));

        _baseUri = baseUri;

        if (options.HttpMessageHandler is { } handler)
        {
            // The caller owns the handler; cookies are attached as a header instead.
            _client = new HttpClient(handler, disposeHandler: false);
            _manualCookies = true;
        }
        else
        {
            var cookieHandler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = cookies,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All
            };

            _client = new HttpClient(cookieHandler);
        }

        _client.Timeout = options.Timeout;
        AddConsentCookie();
    }

    public string? VisitorData => _visitorData;

    public Uri BaseUri => _baseUri;

    /// <summary>Loads consent/visitor cookies and resolves <c>visitorData</c> once.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            if (_options.InitializeSessionOnFirstRequest)
            {
                try
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        BuildPageUrl(
                            $"/?hl={Uri.EscapeDataString(_options.Language)}" +
                            $"&gl={Uri.EscapeDataString(_options.Region)}"));

                    ApplyPageHeaders(request);

                    using var response = await _client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);

                    CaptureVisitorData(response);
                }
                catch (HttpRequestException)
                {
                    // Warm-up is optional; the first real request still works.
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Timeout of the warm-up request is not fatal.
                }
            }

            await ResolveVisitorDataAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public string CreateInnerTubeUrl(string endpoint, InnerTubeClientProfile profile) =>
        BuildPageUrl(
            $"/{profile.Endpoint}/{endpoint}?key={Uri.EscapeDataString(profile.ApiKey)}" +
            $"&prettyPrint=false&hl={Uri.EscapeDataString(_options.Language)}" +
            $"&gl={Uri.EscapeDataString(_options.Region)}");

    public string CreatePageUrl(string path) => BuildPageUrl(path);

    /// <summary>Sends a prepared request. The caller owns (and disposes) the response.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_manualCookies)
        {
            var header = _cookies.GetCookieHeader(_baseUri);
            if (!string.IsNullOrEmpty(header))
                request.Headers.TryAddWithoutValidation("Cookie", header);
        }

        return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public async Task<string> GetPageHtmlAsync(string url, CancellationToken ct = default) =>
        await GetTextAsync(
            url,
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
            ct).ConfigureAwait(false);

    /// <summary>GET that returns the raw body (pages, timedtext, suggestions).</summary>
    public async Task<string> GetTextAsync(string url, string accept, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("An absolute HTTP(S) URL is required.", nameof(url));
        }

        await InitializeAsync(ct).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyPageHeaders(request);
        request.Headers.TryAddWithoutValidation("Accept", accept);

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        CaptureVisitorData(response);

        if (!response.IsSuccessStatusCode)
        {
            throw new YoutubeApiException(
                response.StatusCode,
                uri.ToString(),
                Truncate(body),
                $"YouTube returned {(int)response.StatusCode} for {uri}.",
                ReadRetryAfter(response));
        }

        return body;
    }

    public async Task<(HttpStatusCode StatusCode, string Body)> PostFormAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>> form,
        CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(form);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        request.Headers.TryAddWithoutValidation("User-Agent", _options.PageUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    public void ApplyPageHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", _options.PageUserAgent);
        request.Headers.TryAddWithoutValidation("Accept-Language", BuildAcceptLanguage());
    }

    public void ApplyInnerTubeHeaders(HttpRequestMessage request, InnerTubeClientProfile profile)
    {
        var userAgent = string.IsNullOrWhiteSpace(_options.UserAgent)
            ? profile.UserAgent
            : _options.UserAgent;

        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Accept-Language", BuildAcceptLanguage());
        request.Headers.TryAddWithoutValidation("Origin", Origin);

        if (profile.NumericId is { } numericId)
        {
            request.Headers.TryAddWithoutValidation(
                "X-Youtube-Client-Name",
                numericId.ToString(CultureInfo.InvariantCulture));
        }

        request.Headers.TryAddWithoutValidation("X-Youtube-Client-Version", profile.ClientVersion);

        if (_options.SendVisitorData && !string.IsNullOrEmpty(_visitorData))
            request.Headers.TryAddWithoutValidation("X-Goog-Visitor-Id", _visitorData);
    }

    public void CaptureVisitorData(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-Goog-Visitor-Id", out var values))
        {
            var headerValue = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                _visitorData = headerValue;
                return;
            }
        }

        if (string.IsNullOrEmpty(_visitorData))
            _visitorData = GetCookieValue("VISITOR_INFO1_LIVE");
    }

    private string? GetCookieValue(string name)
    {
        foreach (Cookie cookie in _cookies.GetCookies(_baseUri))
        {
            if (cookie.Name.Equals(name, StringComparison.Ordinal))
                return cookie.Value;
        }

        return null;
    }

    private async Task ResolveVisitorDataAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, "/sw.js_data"));

            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "com.google.android.youtube/20.10.38 (Linux; U; ANDROID 11) gzip");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return;

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (json.StartsWith(")]}'", StringComparison.Ordinal))
                json = json[4..];

            using var document = JsonDocument.Parse(json);
            var visitorData = document.RootElement[0][2][0][0][13].GetString();

            if (!string.IsNullOrWhiteSpace(visitorData))
                _visitorData = visitorData;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or IndexOutOfRangeException
                or OperationCanceledException)
        {
            // visitorData is optional; many requests work without it.
        }
    }

    private string BuildAcceptLanguage() => $"{_options.Language}-{_options.Region},{_options.Language};q=0.9";

    private string BuildPageUrl(string pathAndQuery) => _options.NormalizedBaseUrl + pathAndQuery;

    private void AddConsentCookie()
    {
        try
        {
            _cookies.Add(
                _baseUri,
                new Cookie("SOCS", BuildSocs(DateTimeOffset.UtcNow))
                {
                    Domain = ".youtube.com",
                    Path = "/"
                });
        }
        catch (CookieException)
        {
            // A custom base URL may reject the domain; consent is not critical.
        }
    }

    // SOCS = base64url(protobuf { 1: 2, 2: { 1:3, 2:"813836352", 3:"en", 4:1 }, 3: { 1:<ts> } })
    private static string BuildSocs(DateTimeOffset now)
    {
        var timestamp = (ulong)new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).ToUnixTimeSeconds();

        var innerTimestamp = new List<byte> { 0x08 };
        WriteVarint(innerTimestamp, timestamp);

        var buffer = new List<byte>
        {
            0x08, 0x02,
            0x12, 0x13,
            0x08, 0x03,
            0x12, 0x09
        };

        buffer.AddRange(Encoding.ASCII.GetBytes("813836352"));
        buffer.AddRange([0x1A, 0x02, 0x65, 0x6E]);
        buffer.AddRange([0x20, 0x01]);
        buffer.Add(0x1A);
        buffer.Add((byte)innerTimestamp.Count);
        buffer.AddRange(innerTimestamp);

        return Convert.ToBase64String(buffer.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void WriteVarint(List<byte> buffer, ulong value)
    {
        while (value >= 0x80)
        {
            buffer.Add((byte)(value | 0x80));
            value >>= 7;
        }

        buffer.Add((byte)value);
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : null;
        }

        return null;
    }

    public static string Truncate(string value) =>
        value.Length <= MaxResponseBody ? value : value[..MaxResponseBody];

    public void Dispose()
    {
        _client.Dispose();
        _initializationLock.Dispose();
    }
}
