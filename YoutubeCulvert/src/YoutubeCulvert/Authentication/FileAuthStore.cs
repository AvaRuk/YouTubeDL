using System.Text.Json;
using System.Text.Json.Serialization;

namespace YoutubeCulvert.Authentication;

/// <summary>Source-generated serializer context for <see cref="AuthState"/> (AOT safe).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AuthState))]
internal partial class AuthJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Stores the authentication state in a JSON file, by default
/// <c>%LOCALAPPDATA%\YoutubeCulvert\auth.json</c>.
/// </summary>
/// <remarks>
/// The file contains OAuth tokens and cookies in plaintext. Protect it with
/// operating-system facilities (DPAPI, Keychain, libsecret) or replace
/// <see cref="IAuthStore"/> for production scenarios.
/// </remarks>
public sealed class FileAuthStore : IAuthStore
{
    public FileAuthStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public static FileAuthStore CreateDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YoutubeCulvert");

        return new FileAuthStore(Path.Combine(directory, "auth.json"));
    }

    public async Task<AuthState?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(FilePath))
            return null;

        try
        {
            await using var stream = new FileStream(
                FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true);

            return await JsonSerializer
                .DeserializeAsync(stream, AuthJsonContext.Default.AuthState, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // A corrupted file must not block application startup.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(AuthState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = FilePath + ".tmp";

        await using (var stream = new FileStream(
                         temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                         bufferSize: 4096, useAsync: true))
        {
            await JsonSerializer
                .SerializeAsync(stream, state, AuthJsonContext.Default.AuthState, ct)
                .ConfigureAwait(false);

            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (File.Exists(FilePath))
            File.Delete(FilePath);

        return Task.CompletedTask;
    }
}
