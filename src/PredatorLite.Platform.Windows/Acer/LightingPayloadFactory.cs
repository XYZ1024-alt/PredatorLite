using System.Text.Json.Nodes;
using PredatorLite.Core.Models;

namespace PredatorLite.Platform.Windows.Acer;

internal static class LightingPayloadFactory
{
    public static JsonObject CreateKeyboardPayload(LightingProfile profile)
    {
        int brightness = Math.Clamp(profile.Brightness, 1, 5);
        if (profile.Effect == LightingEffect.Static)
        {
            JsonArray leds = [];
            IReadOnlyList<string> colors = NormalizeZoneColors(profile.ZoneColors, profile.PrimaryColor);
            for (int index = 0; index < 4; index++)
            {
                leds.Add(new JsonObject
                {
                    ["LED_id"] = index,
                    ["color"] = colors[index],
                    ["status"] = 1
                });
            }

            return new JsonObject
            {
                ["device"] = 1,
                ["effect"] = "STATIC",
                ["speed"] = 3,
                ["duration"] = 3,
                ["direction"] = 0,
                ["brightness"] = brightness,
                ["color"] = "#ffffff",
                ["colortype"] = 1,
                ["LEDs"] = leds,
                ["subindex"] = new JsonObject { ["1"] = "STATIC", ["2"] = "STATIC" }
            };
        }

        string effect = ToServiceEffect(profile.Effect);
        return new JsonObject
        {
            ["device"] = 0,
            ["effect"] = effect,
            ["speed"] = Math.Clamp(profile.Speed, 1, 5),
            ["duration"] = 3,
            ["direction"] = Math.Clamp(profile.Direction, 1, 4),
            ["brightness"] = brightness,
            ["color"] = NormalizeColor(profile.PrimaryColor),
            ["colortype"] = 1,
            ["subindex"] = new JsonObject
            {
                ["1"] = effect,
                ["2"] = SecondaryEffect(effect)
            }
        };
    }

    public static JsonObject CreateLogoPayload(LightingProfile profile)
    {
        string effect = profile.LogoEnabled ? "STATIC" : "STATIC";
        return new JsonObject
        {
            ["device"] = 4,
            ["effect"] = effect,
            ["speed"] = Math.Clamp(profile.Speed, 1, 5),
            ["duration"] = 3,
            ["direction"] = 0,
            ["brightness"] = profile.LogoEnabled ? Math.Clamp(profile.Brightness, 1, 5) : 0,
            ["color"] = NormalizeColor(profile.PrimaryColor),
            ["colortype"] = 1,
            ["LEDs"] = new JsonArray(),
            ["subindex"] = new JsonObject
            {
                ["1"] = effect,
                ["2"] = effect,
                ["3"] = effect,
                ["4"] = effect
            }
        };
    }

    public static string NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return "#00a8e8";
        }

        string normalized = color.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('#'))
        {
            normalized = "#" + normalized;
        }

        return normalized.Length == 7 && normalized.Skip(1).All(Uri.IsHexDigit)
            ? normalized
            : "#00a8e8";
    }

    private static IReadOnlyList<string> NormalizeZoneColors(IReadOnlyList<string> colors, string fallback)
    {
        string defaultColor = NormalizeColor(fallback);
        return Enumerable.Range(0, 4)
            .Select(index => index < colors.Count ? NormalizeColor(colors[index]) : defaultColor)
            .ToArray();
    }

    private static string ToServiceEffect(LightingEffect effect) => effect switch
    {
        LightingEffect.Breathing => "BREATHING",
        LightingEffect.Neon => "NEON",
        LightingEffect.Wave => "WAVE",
        LightingEffect.Ripple => "SHIFTING",
        LightingEffect.Zoom => "ZOOM",
        LightingEffect.Snake => "METEOR",
        LightingEffect.Disco => "TWINKLING",
        _ => "STATIC"
    };

    private static string SecondaryEffect(string effect) => effect switch
    {
        "WAVE" => "NEON",
        "SHIFTING" or "ZOOM" or "METEOR" => "STATIC",
        "TWINKLING" => "BREATHING",
        _ => effect
    };
}
