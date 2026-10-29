using System.Text.Json.Serialization;

namespace YouTubeDL.Services;

[JsonSerializable(typeof(LanguagePack))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class LanguagePackJsonContext : JsonSerializerContext
{
}

