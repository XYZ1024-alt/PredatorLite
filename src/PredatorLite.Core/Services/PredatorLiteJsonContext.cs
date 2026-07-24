using System.Text.Json.Serialization;
using PredatorLite.Core.Models;

namespace PredatorLite.Core.Services;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class PredatorLiteJsonContext : JsonSerializerContext
{
}
