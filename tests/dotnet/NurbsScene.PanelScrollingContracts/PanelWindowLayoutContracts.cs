using System.Collections.Immutable;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

internal static class PanelWindowLayoutContracts
{
    public static void Register(List<(string Name, Action Run)> tests, ComponentRegistry registry)
    {
        void Test(string name, Action run) => tests.Add(("scroll review: " + name, run));
        static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
        static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
        var node = new Graph(registry).Add("Panel") with { PanelDisplay = new(false, false, false) };
        static NodeResult Result(DataTree tree) => new(ImmutableDictionary<string, DataTree>.Empty.Add("T", tree), "ok", [], 0);
        static DataTree Items(int count) => DataTree.List(Enumerable.Range(0, count).Select(i => (object?)$"item-{i:D6}"));
        Test("measured row budget matches the actual placement clip for varied ink", () => {
            var random = new Random(54721);
            for (var i = 0; i < 1000; i++) {
                var height = 20 + random.NextDouble() * 1400;
                var layout = PanelDisplayLayout.Create(new(0,0,300,height), 1, 10);
                var metrics = new PanelFontMetrics(-1-random.NextDouble()*80, random.NextDouble()*20);
                var rows = PanelWindowLayout.RowBudget(height, layout, metrics);
                var preview = new PanelPreview(Enumerable.Repeat("x", rows).ToImmutableArray(), "");
                var placement = new PanelTextPlacementCache().Get(preview, PanelTextStyle.Default, 300, height, layout, metrics, 1, _ => 1);
                Check(rows >= 1 && rows <= layout.Rows);
                Check(placement.Rows.Length == rows || rows == 1 && placement.Rows.Length == 0);
            }
        });
        Test("measured row budget rejects invalid ink and clamps before integer conversion", () => {
            var layout = PanelDisplayLayout.Create(new(0,0,300,300), 1, 10);
            Throws<ArgumentException>(() => PanelWindowLayout.RowBudget(double.NaN,layout,new(-10,2)));
            Throws<ArgumentException>(() => PanelWindowLayout.RowBudget(300,layout,new(10,2)));
            Throws<ArgumentException>(() => PanelWindowLayout.RowBudget(300,layout with { Rows = 0 },new(-10,2)));
            Check(PanelWindowLayout.RowBudget(double.MaxValue,layout,new(-10,2))==layout.Rows);
        });
        Test("finite extreme scroll tracks preserve proportional grip and offset", () => {
            var height = double.MaxValue / 2;
            var window = new PanelWindow(new([], ""), 40, 10, 100, 90);
            var bar = PanelScrollBar.Create(new(0,0,10,height),window,12);
            Check(Math.Abs(bar.Grip.Height / height - .1) < 1e-15);
            Check(Math.Abs(bar.Grip.Y / height - .4) < 1e-15);
            Check(bar.Drag(bar.Grip.Y + bar.Grip.Height / 2,bar.Grip.Height / 2)==40);
            Check(bar.Drag(double.MaxValue,0)==90 && bar.Drag(-double.MaxValue,0)==0);
        });
        Test("scroll tracks reject unrepresentable finite endpoint sums", () => {
            var window = new PanelWindow(new([], ""), 40, 10, 100, 90);
            Throws<ArgumentException>(() => PanelScrollBar.Create(new(double.MaxValue,0,double.MaxValue,100),window,12));
            Throws<ArgumentException>(() => PanelScrollBar.Create(new(0,double.MaxValue,10,double.MaxValue),window,12));
        });
        Test("window index survives new result identity sharing the same immutable tree", () => {
            var tree = Items(1000); var c = new PanelWindowCache();
            c.Get(node, Result(tree), 8, 24, 600); var next = c.Get(node, Result(tree), 8, 24, 601);
            Check(c.IndexBuilds == 1 && next.Preview.Lines[0] == "item-000601");
        });
    }
}
