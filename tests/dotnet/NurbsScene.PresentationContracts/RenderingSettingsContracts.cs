using System.Globalization;
using NurbsScene.Geometry;
using NurbsScene.Presentation;
using NurbsScene.Rendering;

internal static class RenderingSettingsContracts
{
    public static void Register(List<(string, Action)> tests)
    {
        void Check(bool condition)
        {
            if (!condition) throw new Exception("Rendering preference assertion failed.");
        }
        tests.Add(("all studio preferences round-trip exactly across cultures", () =>
        {
            var original = new WebGpuRenderSettings
            {
                Exposure = 1.375f, Metallic = .74f, Roughness = .123f,
                AmbientIntensity = 1.25f, LightIntensity = 7.5f, BloomStrength = .0825f,
                Shadows = false, LightDirection = new Vec3(-.125, .4567890123456789, 2.75)
            };
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new("pl-PL");
                var encoded = RenderSettingsPreferences.Serialize(original);
                Check(RenderSettingsPreferences.TryParse(encoded, out var restored) && restored == original);
            }
            finally { CultureInfo.CurrentCulture = culture; }
        }));
        tests.Add(("older partial version-one studio preferences retain defaults", () =>
        {
            Check(RenderSettingsPreferences.TryParse("{\"version\":1,\"exposure\":2,\"futureField\":true}", out var restored));
            Check(restored == new WebGpuRenderSettings { Exposure = 2 });
            Check(RenderSettingsPreferences.TryParse("{\"version\":1}", out restored) && restored == new WebGpuRenderSettings());
        }));
        tests.Add(("bad studio preferences fall back atomically", () =>
        {
            foreach (var input in new string?[]
            {
                null, "", "not json", "null", "[]", "{}", "{\"version\":0}", "{\"version\":2}",
                "{\"version\":\"1\"}", "{\"version\":1,\"exposure\":0}",
                "{\"version\":1,\"exposure\":NaN}", "{\"version\":1,\"exposure\":1e999}",
                "{\"version\":1,\"metallic\":1.01}", "{\"version\":1,\"roughness\":0.03}",
                "{\"version\":1,\"ambientIntensity\":-1}", "{\"version\":1,\"lightIntensity\":129}",
                "{\"version\":1,\"bloomStrength\":-0.1}", "{\"version\":1,\"shadows\":\"false\"}",
                "{\"version\":1,\"lightDirection\":[0,0,0]}", "{\"version\":1,\"lightDirection\":[1,2]}",
                "{\"version\":1,\"lightDirection\":[1,2,\"3\"]}", new string(' ', 8193)
            })
                Check(!RenderSettingsPreferences.TryParse(input, out var restored) && restored == new WebGpuRenderSettings());
        }));
        tests.Add(("invalid studio settings cannot be persisted", () =>
        {
            foreach (var settings in new[]
            {
                new WebGpuRenderSettings { Exposure = float.NaN },
                new WebGpuRenderSettings { LightDirection = Vec3.Zero },
                new WebGpuRenderSettings { Roughness = 0 }
            })
            {
                try { RenderSettingsPreferences.Serialize(settings); }
                catch (ArgumentException) { continue; }
                throw new Exception("Invalid settings were serialized.");
            }
        }));
    }
}
