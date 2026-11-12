using System.Text;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Rendering;

namespace NurbsScene.Presentation;

/// <summary>Versioned, invariant display preferences, independent from model and graph documents.</summary>
public static class RenderSettingsPreferences
{
    public static string Serialize(WebGpuRenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteNumber("exposure", settings.Exposure);
            writer.WriteNumber("metallic", settings.Metallic);
            writer.WriteNumber("roughness", settings.Roughness);
            writer.WriteNumber("ambientIntensity", settings.AmbientIntensity);
            writer.WriteNumber("lightIntensity", settings.LightIntensity);
            writer.WriteNumber("bloomStrength", settings.BloomStrength);
            writer.WriteBoolean("shadows", settings.Shadows);
            writer.WriteStartArray("lightDirection");
            writer.WriteNumberValue(settings.LightDirection.X);
            writer.WriteNumberValue(settings.LightDirection.Y);
            writer.WriteNumberValue(settings.LightDirection.Z);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Malformed, unsupported or invalid records fall back atomically. Missing
    /// version-one fields retain their defaults, allowing older records to load.
    /// </summary>
    public static bool TryParse(string? json, out WebGpuRenderSettings settings)
    {
        settings = new();
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8192) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version)
                || !version.TryGetInt32(out var schema) || schema != 1) return false;
            float Number(string name, float fallback)
            {
                if (!root.TryGetProperty(name, out var value)) return fallback;
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number))
                    throw new ArgumentException("Invalid rendering preference: " + name);
                return number;
            }
            var defaults = settings;
            var shadows = defaults.Shadows;
            if (root.TryGetProperty("shadows", out var shadowValue))
            {
                if (shadowValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                shadows = shadowValue.GetBoolean();
            }
            var direction = defaults.LightDirection;
            if (root.TryGetProperty("lightDirection", out var light))
            {
                if (light.ValueKind != JsonValueKind.Array || light.GetArrayLength() != 3) return false;
                double Coordinate(int index)
                {
                    var value = light[index];
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
                        throw new ArgumentException("Invalid light direction.");
                    return number;
                }
                direction = new Vec3(Coordinate(0), Coordinate(1), Coordinate(2));
            }
            var candidate = defaults with
            {
                Exposure = Number("exposure", defaults.Exposure),
                Metallic = Number("metallic", defaults.Metallic),
                Roughness = Number("roughness", defaults.Roughness),
                AmbientIntensity = Number("ambientIntensity", defaults.AmbientIntensity),
                LightIntensity = Number("lightIntensity", defaults.LightIntensity),
                BloomStrength = Number("bloomStrength", defaults.BloomStrength),
                Shadows = shadows,
                LightDirection = direction
            };
            candidate.Validate();
            settings = candidate;
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }
}
