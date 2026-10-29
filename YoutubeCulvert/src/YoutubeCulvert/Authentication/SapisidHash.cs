using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YoutubeCulvert.Authentication;

/// <summary>
/// Generates the SAPISIDHASH header used by the YouTube web client to authorize
/// cookie-based requests.
/// </summary>
public static class SapisidHash
{
    public const string DefaultOrigin = "https://www.youtube.com";

    /// <summary>
    /// Returns <c>&lt;timestamp&gt;_&lt;sha1&gt;</c> where SHA1 covers
    /// <c>&lt;timestamp&gt; &lt;sapisid&gt; &lt;origin&gt;</c>.
    /// </summary>
    public static string Create(
        string sapisid,
        string origin = DefaultOrigin,
        DateTimeOffset? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sapisid);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        var unixTimestamp = (timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var timestampText = unixTimestamp.ToString(CultureInfo.InvariantCulture);
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes($"{timestampText} {sapisid} {origin}"));

        return $"{timestampText}_{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>Returns the value of the <c>Authorization</c> header.</summary>
    public static string CreateAuthorizationHeader(
        string sapisid,
        string origin = DefaultOrigin,
        DateTimeOffset? timestamp = null) =>
        "SAPISIDHASH " + Create(sapisid, origin, timestamp);
}
