using System.Text.Json.Serialization;

namespace YouTubeDL.Models;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<QueueItem>))]
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
internal partial class AppJsonContext : JsonSerializerContext
{
}
