using System.Text.Json.Serialization;

namespace PredatorLite.Platform.Windows.Acer;

internal sealed record AcerMonitorRequest(string Function);

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(AcerMonitorRequest))]
internal sealed partial class AcerJsonContext : JsonSerializerContext
{
}
