using System.Text.Json;

namespace PredatorLite.Platform.Windows.Acer;

public sealed record AcerResponse(int Result, string Request, JsonElement? Data, string Raw)
{
    public bool IsSuccess => Result == 0;

    public bool TryGetInt(string propertyName, out int value)
    {
        value = 0;
        if (Data is not { ValueKind: JsonValueKind.Object } data ||
            !data.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), out value),
            JsonValueKind.True => SetBooleanValue(true, out value),
            JsonValueKind.False => SetBooleanValue(false, out value),
            _ => false
        };
    }

    private static bool SetBooleanValue(bool enabled, out int value)
    {
        value = enabled ? 1 : 0;
        return true;
    }
}
