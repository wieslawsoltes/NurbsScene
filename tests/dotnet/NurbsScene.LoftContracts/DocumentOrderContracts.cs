using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Model;
using NurbsScene.Workbench;
using NurbsScene.IO;
using NurbsScene.Parametric;

internal static class DocumentOrderContracts
{
    public static void Register(List<(string Name, Action Run)> tests, GeometryFactory kernel)
    {
        void Test(string name, Action run) => tests.Add((name, run));
        void Check(bool value) { if (!value) throw new Exception("Document order assertion failed."); }
        void Names(SceneDocument doc, params string[] names) => Check(doc.Objects.Select(o => o.Name).SequenceEqual(names));
        SceneObject Add(SceneDocument doc, string name) => doc.Add(kernel.Line(Vec3.Zero, Vec3.UnitX), name, id: name);
        Test("deleted storage slots cannot reorder subsequently created objects", () => {
            var d = new SceneDocument(); Add(d, "A"); Add(d, "B"); Add(d, "C");
            var before = d.Objects; d.Delete(["B"]); Add(d, "D"); Names(d, "A", "C", "D");
            Check(before.Select(o => o.Name).SequenceEqual(new[] { "A", "B", "C" }));
            d.Delete(d.Objects.Select(o => o.Id)); Add(d, "E"); Add(d, "F"); Add(d, "G"); Names(d, "E", "F", "G");
        });
        Test("object updates keep order and a reused identity appends", () => {
            var d = new SceneDocument(); Add(d, "A"); var b = Add(d, "B"); Add(d, "C");
            d.Update(b with { Name = "Renamed" }); Names(d, "A", "Renamed", "C");
            d.Delete(["B"]); Add(d, "B"); Names(d, "A", "C", "B");
        });
        Test("layer deletion and insertion retain explicit layer order", () => {
            var d = new SceneDocument(); d.AddLayer("A", ColorRgba.Default, "A"); d.AddLayer("B", ColorRgba.Default, "B");
            d.AddLayer("C", ColorRgba.Default, "C"); d.RemoveLayer("B"); d.AddLayer("D", ColorRgba.Default, "D");
            Check(d.Layers.Select(l => l.Id).SequenceEqual(new[] { "default", "A", "C", "D" }));
        });
        Test("loft after deleting a populated document follows creation order", () => {
            using var host = new WorkspaceController(); host.LoadPavilion(); host.Execute("SelAll"); host.Execute("Delete");
            for (var i = 0; i < 3; i++) host.Execute($"Ellipse 1,2,{i * 3} {8 + i} {4 + i * .5}");
            host.Execute("SelAll"); host.Execute("Loft"); Check(host.Document.Objects.Count == 4);
            Check(host.Document.Objects.Last().Name == "Loft");
            var loft = (NurbsSurface)host.Document.Objects.Last().Geometry;
            for (var i = 0; i < 3; i++) Check((loft.PointAt(0, i / 2d) - new Vec3(9 + i, 2, 3 * i)).Length < 1e-9);
        });
        Test("selection after deletion retains survivor order and appends new picks", () => {
            var d = new SceneDocument(); Add(d, "A"); Add(d, "B"); Add(d, "C"); Add(d, "D");
            d.Select(["A", "B", "C"]); d.Delete(["B"]); d.Select(["D", "A"], add: true);
            Check(d.Snapshot().Selection.SequenceEqual(new[] { "A", "C", "D" }));
            d.Select(["D", "C", "D", "A"]); Check(d.Snapshot().Selection.SequenceEqual(new[] { "D", "C", "A" }));
        });
        Test("locked and hidden selections are removed without reordering survivors", () => {
            var d = new SceneDocument(); var a = Add(d, "A"); var b = Add(d, "B"); Add(d, "C");
            d.Select(["C", "B", "A"]); d.Update(b with { Visible = false });
            Check(d.Snapshot().Selection.SequenceEqual(new[] { "C", "A" }));
            var layer = d.AddLayer("Hidden", ColorRgba.Default); d.Update(a with { LayerId = layer.Id });
            d.UpdateLayer(layer with { Locked = true }); Check(d.Snapshot().Selection.SequenceEqual(new[] { "C" }));
        });
        Test("object and selection order survive atomic rollback undo and redo", () => {
            var d = new SceneDocument(); Add(d, "A"); Add(d, "B"); Add(d, "C"); d.Select(["C", "A", "B"]);
            d.Transaction("Replace", () => { d.Delete(["B"]); Add(d, "D"); d.Select(["D"], add: true); });
            Names(d, "A", "C", "D"); Check(d.Undo()); Names(d, "A", "B", "C");
            Check(d.Snapshot().Selection.SequenceEqual(new[] { "C", "A", "B" }));
            Check(d.Redo()); Names(d, "A", "C", "D");
            try { d.Transaction("Invalid", () => { d.Delete(["A"]); Add(d, "E"); throw new InvalidOperationException("rollback"); }); }
            catch (InvalidOperationException) { }
            Names(d, "A", "C", "D"); Check(d.Snapshot().Selection.SequenceEqual(new[] { "C", "A", "D" }));
        });
        Test("ordered document and selection round-trip without a format change", () => {
            var d = new SceneDocument(); Add(d, "A"); Add(d, "B"); Add(d, "C"); d.Delete(["B"]); Add(d, "D"); d.Select(["D", "A", "C"]);
            var registry = StandardComponents.Create(kernel);
            var restored = WorkspaceFormat.Deserialize(kernel.Evaluator, registry, WorkspaceFormat.Serialize(d, new Graph(registry))).Document;
            Names(restored, "A", "C", "D"); Check(restored.Snapshot().Selection.SequenceEqual(new[] { "D", "A", "C" }));
        });
        Test("failed additions and selection requests cannot corrupt ordering", () => {
            var d = new SceneDocument(); Add(d, "A"); Add(d, "B"); d.Select(["B", "A"]);
            try { Add(d, "A"); } catch (ArgumentException) { }
            try { d.Select(["A", "missing"]); } catch (KeyNotFoundException) { }
            Names(d, "A", "B"); Check(d.Snapshot().Selection.SequenceEqual(new[] { "B", "A" }));
        });
        Test("batch deletion preserves order against an independent list model", () => {
            var d = new SceneDocument(); var expected = new List<string>(); var random = new Random(8821); var serial = 0;
            for (var iteration = 0; iteration < 1000; iteration++) {
                if (expected.Count > 0 && random.Next(3) == 0) {
                    var remove = expected.Where(_ => random.Next(4) == 0).ToArray(); d.Delete(remove); expected.RemoveAll(remove.Contains);
                } else { var id = "Item" + serial++; Add(d, id); expected.Add(id); }
                Check(d.Objects.Select(o => o.Id).SequenceEqual(expected));
            }
        });
        Test("ordered selection is immutable cached and used by the loft command", () => {
            using var h = new WorkspaceController();
            for (var i = 0; i < 4; i++) h.Execute($"Ellipse 0,0,{i * 3} 8 4");
            var ids = h.Document.Objects.Select(o => o.Id).ToArray();
            h.Document.Select(ids.Take(3)); var prior = h.Document.SelectionOrder;
            h.Document.Delete([ids[1]]); h.Document.Select([ids[3]], add: true);
            Check(h.Document.SelectionOrder.SequenceEqual(new[] { ids[0], ids[2], ids[3] }));
            Check(prior.SequenceEqual(ids.Take(3)) && ReferenceEquals(h.Document.SelectionOrder, h.Document.SelectionOrder));
            h.Execute("Loft"); var surface = (NurbsSurface)h.Document.Objects.Last().Geometry;
            Check(Math.Abs(surface.PointAt(0, .5).Z - 6) < 1e-9 && Math.Abs(surface.PointAt(0, 1).Z - 9) < 1e-9);
        });
        Test("batch delete and retained order remain valid for ten thousand objects", () => {
            var d = new SceneDocument(); var geometry = kernel.Line(Vec3.Zero, Vec3.UnitX);
            for (var i = 0; i < 10000; i++) d.Add(geometry, i.ToString(), id: "Item" + i);
            var remove = Enumerable.Range(0, 5000).Select(i => "Item" + (i * 2)).ToArray(); d.Delete(remove);
            for (var i = 10000; i < 12000; i++) d.Add(geometry, i.ToString(), id: "Item" + i);
            var expected = Enumerable.Range(0, 5000).Select(i => "Item" + (i * 2 + 1)).Concat(Enumerable.Range(10000, 2000).Select(i => "Item" + i));
            Check(d.Objects.Select(o => o.Id).SequenceEqual(expected)); d.Select(d.Objects.Select(o => o.Id));
            var order = d.SelectionOrder; var before = GC.GetAllocatedBytesForCurrentThread(); var count = 0;
            for (var i = 0; i < 10000; i++) count += d.SelectionOrder.Count;
            Check(GC.GetAllocatedBytesForCurrentThread() - before == 0 && count == 70000000 && ReferenceEquals(order, d.SelectionOrder));
        });
        Test("warm ordered object and layer snapshots retain zero-allocation reads", () => {
            var d = new SceneDocument(); for (var i = 0; i < 100; i++) Add(d, "Item" + i);
            var objects = d.Objects; var layers = d.Layers; var total = 0;
            for (var i = 0; i < 100; i++) total += d.Objects.Count + d.Layers.Count;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++) total += d.Objects.Count + d.Layers.Count;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(bytes == 0 && total > 0 && ReferenceEquals(objects, d.Objects) && ReferenceEquals(layers, d.Layers));
            Console.WriteLine($"METRIC ordered-document-reads-10000={bytes} B");
        });
    }
}
