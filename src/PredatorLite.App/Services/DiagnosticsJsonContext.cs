using System.Text.Json.Serialization;
using PredatorLite.Core.Models;

namespace PredatorLite.App.Services;

[JsonSourceGenerationOptions(
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(DeviceCapabilities))]
[JsonSerializable(typeof(HardwareSnapshot))]
[JsonSerializable(typeof(IReadOnlyDictionary<DeviceSettingId, DeviceSettingState>))]
[JsonSerializable(typeof(IReadOnlyList<ManagedServiceInfo>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ApplicationDiagnostics))]
internal sealed partial class DiagnosticsJsonContext : JsonSerializerContext
{
}

internal sealed record ApplicationDiagnostics(
    string? Version,
    string Runtime,
    string Architecture,
    DateTimeOffset ExportedUtc);
