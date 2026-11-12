using System.Globalization;
using NurbsScene.Presentation;

internal static class FloatingLayoutContracts
{
    public static void Register(List<(string, Action)> tests)
    {
        void Test(string name, Action body) => tests.Add((name, body));
        void Check(bool condition, string message = "Floating layout assertion failed.")
        { if (!condition) throw new Exception(message); }
        void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < 1e-8, $"{expected} != {actual}");
        void BoundsNear(FloatingPaneBounds expected, FloatingPaneBounds actual)
        { Near(expected.X, actual.X); Near(expected.Y, actual.Y); Near(expected.Width, actual.Width); Near(expected.Height, actual.Height); }
        void Throws(Action body)
        { try { body(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid layout rejection."); }
        void Contained(FloatingPaneBounds b, double width, double height)
        {
            Check(double.IsFinite(b.X) && double.IsFinite(b.Y) && double.IsFinite(b.Width) && double.IsFinite(b.Height));
            Check(b.X >= 0 && b.Y >= 0 && b.Width >= 0 && b.Height >= 0);
            Check(b.X + b.Width <= width + 1e-8 && b.Y + b.Height <= height + 1e-8);
        }

        Test("floating panes stay reachable across empty, narrow and large hosts", () =>
        {
            var placements = new[] { FloatingPanePlacement.Default, new(0, 0, .01, .01),
                new(.75, .75, .25, .25), new(0, 0, 1, 1), FloatingPanePlacement.Default with { Folded = true } };
            foreach (var area in new[] { (0d, 0d), (1d, 1d), (8d, 4d), (9d, 9d), (320d, 180d),
                         (680d, 1050d), (1600d, 900d), (4096d, 2160d) })
                foreach (var placement in placements)
                    Contained(placement.Resolve(area.Item1, area.Item2), area.Item1, area.Item2);
        });
        Test("normalized floating geometry scales with available viewport size", () =>
        {
            var placement = FloatingPanePlacement.Default;
            var a = placement.Resolve(1600, 900); var b = placement.Resolve(3200, 1800);
            BoundsNear(new(a.X * 2, a.Y * 2, a.Width * 2, a.Height * 2), b);
        });
        Test("very large finite hosts cannot invert floating clamp limits through rounding", () =>
        {
            foreach (var area in new[] { (1e18, 1e18), (1e308, 1e308), (double.MaxValue, 1d), (1d, double.MaxValue) })
                foreach (var placement in new[] { new FloatingPanePlacement(0, 0, 1, 1),
                             new(0, 0, 1, 1, true), new(.5, .5, .5, .5) })
                    Contained(placement.Resolve(area.Item1, area.Item2), area.Item1, area.Item2);
        });
        Test("floating minimum sizes yield to a smaller host without losing its title", () =>
        {
            var placement = new FloatingPanePlacement(0, 0, .01, .01);
            BoundsNear(new(4, 4, 560, 320), placement.Resolve(800, 500));
            BoundsNear(new(4, 4, 392, 192), placement.Resolve(400, 200));
        });
        Test("saving and folding preserves the expanded floating rectangle", () =>
        {
            var expanded = new FloatingPaneBounds(300, 100, 720, 480);
            var placement = FloatingPanePlacement.FromBounds(expanded, 1600, 900);
            BoundsNear(expanded, placement.Resolve(1600, 900));
            var folded = FloatingPanePlacement.FromBounds(expanded, 1600, 900, folded: true);
            Check(folded.Folded); Near(placement.Height, folded.Height);
            BoundsNear(new(300, 100, 720, FloatingPanePlacement.FoldedHeight), folded.Resolve(1600, 900));
            BoundsNear(expanded, folded.WithFolded(false).Resolve(1600, 900));
        });
        Test("folded title can park at the bottom and unfold back inside its host", () =>
        {
            var folded = FloatingPanePlacement.FromBounds(new(300, 870, 720, 480), 1600, 900, folded: true);
            folded.Validate(); Check(folded.Y + folded.Height > 1);
            BoundsNear(new(300, 868, 720, 28), folded.Resolve(1600, 900));
            var saved = new WorkspacePreferences("Float", true).WithPlacement(folded);
            Check(WorkspacePreferences.TryParse(saved.Encode(), out var restored) && restored == saved);
            var expanded = restored.Placement.WithFolded(false); expanded.Validate();
            Check(!expanded.Folded); Near(folded.Height, expanded.Height); Near(folded.Width, expanded.Width);
            BoundsNear(new(300, 416, 720, 480), expanded.Resolve(1600, 900));
        });
        Test("extreme drag and resize coordinates produce a contained saved placement", () =>
        {
            foreach (var bounds in new[] { new FloatingPaneBounds(-1000, -500, 10000, 10000),
                         new(1e300, 1e300, 600, 400), new(-1e300, -1e300, 1, 1) })
            {
                var placement = FloatingPanePlacement.FromBounds(bounds, 1200, 800);
                placement.Validate(); Contained(placement.Resolve(1200, 800), 1200, 800);
            }
        });
        Test("invalid floating dimensions reject before placement can be persisted", () =>
        {
            foreach (var placement in new[] { new FloatingPanePlacement(-.1, 0, .5, .5),
                         new(0, -.1, .5, .5), new(0, 0, 0, .5), new(0, 0, .5, -1),
                         new(0, 0, 2, .5), new(0, 0, .5, 2), new(double.NaN, 0, .5, .5),
                         new(0, 0, .5, double.PositiveInfinity) })
                Throws(() => new WorkspacePreferences().WithPlacement(placement));
            foreach (var area in new[] { (-1d, 10d), (10d, -1d), (double.NaN, 10d), (10d, double.PositiveInfinity) })
                Throws(() => FloatingPanePlacement.Default.Resolve(area.Item1, area.Item2));
            Throws(() => FloatingPanePlacement.FromBounds(new(0, 0, 1, 1), 0, 20));
            Throws(() => FloatingPanePlacement.FromBounds(new(0, 0, double.NaN, 1), 20, 20));
        });
        Test("custom floating placement round trips for every dock and culture", () =>
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                foreach (var culture in new[] { "en-US", "pl-PL", "de-DE" })
                {
                    CultureInfo.CurrentCulture = new(culture);
                    foreach (var dock in new[] { "Float", "Right", "Bottom" })
                    {
                        var value = new WorkspacePreferences(dock, true, true, .63, 317.25, false, true, "Layers")
                            .WithPlacement(new(.125, .0625, .625, .8125, true));
                        Check(value.Encode().StartsWith("3|", StringComparison.Ordinal));
                        Check(WorkspacePreferences.TryParse(value.Encode(), out var restored) && restored == value);
                    }
                }
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });
        Test("default docked preferences retain their version two encoding", () =>
        {
            var value = new WorkspacePreferences("Bottom", true, false, .63, 317.25, false, true, "Layers");
            Check(value.Encode() == "2|Bottom|1|0|0.63|317.25|0|1|Layers");
            Check(WorkspacePreferences.TryParse(value.Encode(), out var restored)
                && restored.Placement == FloatingPanePlacement.Default && restored == value);
            Check(!WorkspacePreferences.TryParse("2|Float|1|1|.5|250|1|0|Layers", out _));
        });
        Test("malformed floating preference records fall back atomically", () =>
        {
            foreach (var text in new[] {
                         "3|Float|1|1|.5|250|1|0|Layers|.1|.1|.6|.7",
                         "3|Float|1|1|.5|250|1|0|Layers|NaN|.1|.6|.7|0",
                         "3|Float|1|1|.5|250|1|0|Layers|.1|.1|0|.7|0",
                         "3|Float|1|1|.5|250|1|0|Layers|.1|.1|.6|.7|2",
                         "3|Float|1|1|.5|250|1|0|Layers|.1|.1|.6|.7|0|extra",
                         "4|Float|1|1|.5|250|1|0|Layers|.1|.1|.6|.7|0" })
                Check(!WorkspacePreferences.TryParse(text, out var value) && value == new WorkspacePreferences());
        });
    }
}
