using System.Collections.Immutable;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.Workbench;

internal static class GrasshopperExampleContracts
{
    public static void Register(List<(string, Action)> tests)
    {
        void Test(string name, Action body) => tests.Add((name, body));
        void Check(bool value, string message = "Extrusion example assertion failed.")
        { if (!value) throw new Exception(message); }
        ComponentRegistry Registry() => StandardComponents.Create(new(new ManagedNurbsEvaluator()));
        NurbsSurface Solve(GraphSession session, string id)
        {
            var result = session.Graph.SolveAsync().GetAwaiter().GetResult();
            Check(!result.Stale && result.Revision == session.Graph.Revision && result.ErrorCount == 0);
            return result.Nodes[id].Outputs["S"].AllItems.Single() as NurbsSurface
                ?? throw new Exception("The example did not produce an exact NURBS surface.");
        }
        void Cylinder(NurbsSurface surface, double radius, double height)
        {
            Check(surface.DegreeU == 2 && surface.DegreeV == 1);
            Check(surface.ControlPoints.ToArray().Any(p => p.W != 1), "Rational circle weights were lost.");
            foreach (var u in new[] { 0d, .125, .25, .375, .5, .75, 1 })
                foreach (var v in new[] { 0d, .4, 1 })
                {
                    var p = surface.PointAt(surface.DomainU.At(u), surface.DomainV.At(v));
                    Check(Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - radius) < 1e-8);
                    Check(Math.Abs(p.Z - height * v) < 1e-8);
                }
        }
        void Same(GraphSnapshot expected, Graph graph)
        {
            var actual = graph.Snapshot();
            Check(expected.Nodes.Length == actual.Nodes.Length && expected.Wires.SequenceEqual(actual.Wires));
            // Undo/Redo reconstructs immutable parameter dictionaries. Compare
            // their scalar/vector contents as well as every node metadata field.
            foreach (var (before, after) in expected.Nodes.Zip(actual.Nodes))
            {
                Check(before == after with { Parameters = before.Parameters });
                Check(before.Parameters.Count == after.Parameters.Count && before.Parameters.All(p =>
                    after.Parameters.TryGetValue(p.Key, out var value) && Equals(p.Value, value)));
            }
        }

        Test("Grasshopper example solves five real components into an exact rational extrusion", () =>
        {
            var session = new GraphSession(new(Registry()));
            var id = GrasshopperExamples.InsertExtrusion(session);
            Check(session.Graph.NodeCount == 5 && session.Graph.WireCount == 4);
            Check(session.Graph.GetNode(id).Component == "Extrude");
            foreach (var name in new[] { "Radius", "Height" })
            {
                var node = session.Graph.Nodes.Single(n => n.Name == name);
                Check(NumberSliderSettings.TryRead(node, out var settings, out var value, out _));
                Check(settings.Minimum == 1 && settings.Maximum == (name == "Radius" ? 20 : 30));
                Check(value == (name == "Radius" ? 5 : 10));
            }
            Cylinder(Solve(session, id), 5, 10);
        });
        Test("Grasshopper example responds to both live sliders without changing the old solved surface", () =>
        {
            var session = new GraphSession(new(Registry()));
            var id = GrasshopperExamples.InsertExtrusion(session); var previous = Solve(session, id);
            session.Edit("Change example dimensions", g =>
            {
                foreach (var (name, value) in new[] { ("Radius", 7.5), ("Height", 18d) })
                {
                    var node = g.Nodes.Single(n => n.Name == name);
                    g.Update(node with { Parameters = node.Parameters.SetItem("value", value) });
                }
            });
            Cylinder(Solve(session, id), 7.5, 18); Cylinder(previous, 5, 10);
        });
        Test("Grasshopper example appends beyond real panel bounds in one independent history entry", () =>
        {
            var session = new GraphSession(new(Registry()));
            session.Edit("Existing definition", g =>
            {
                var number = g.Add("Number", -300, -200);
                var panel = g.Add("Panel", 900, -100);
                g.Update(panel with { CanvasSize = new(2048, 168) });
                g.Connect(new(number.Id, "N"), new(panel.Id, "D"));
            });
            var graph = session.Graph; var before = graph.Snapshot(); var notifications = 0;
            graph.Changed += () => notifications++;
            var terminal = GrasshopperExamples.InsertExtrusion(session); var inserted = graph.Snapshot();
            Check(ReferenceEquals(graph, session.Graph) && notifications == 1);
            Check(before.Nodes.All(n => ReferenceEquals(n, graph.GetNode(n.Id))));
            Check(before.Wires.All(w => graph.Wires.Any(actual => ReferenceEquals(w, actual))));
            var added = graph.Nodes.Where(n => before.Nodes.All(old => old.Id != n.Id)).ToArray();
            Check(added.Length == 5 && added.All(n => n.X >= 900 + 2048 + 80));
            Check(session.Undo()); Same(before, session.Graph);
            Check(session.CanUndo && session.CanRedo, "Insertion discarded existing history.");
            Check(session.Redo()); Same(inserted, session.Graph); Cylinder(Solve(session, terminal), 5, 10);
            Check(session.Undo() && session.Undo() && session.Graph.NodeCount == 0 && !session.CanUndo);
        });
        Test("repeated Grasshopper examples append with fresh identities and undo separately", () =>
        {
            var session = new GraphSession(new(Registry()));
            var first = GrasshopperExamples.InsertExtrusion(session); var before = session.Graph.Snapshot();
            var edge = before.Nodes.Max(n => { var b = GraphPresentation.Bounds(n, session.Graph.Registry.Get(n.Component)); return b.X + b.Width; });
            var second = GrasshopperExamples.InsertExtrusion(session);
            Check(first != second && session.Graph.NodeCount == 10 && session.Graph.WireCount == 8);
            Check(session.Graph.Nodes.Where(n => before.Nodes.All(old => old.Id != n.Id)).All(n => n.X >= edge + 80));
            Cylinder(Solve(session, first), 5, 10); Cylinder(Solve(session, second), 5, 10);
            Check(session.Undo()); Same(before, session.Graph);
            Check(session.Redo() && session.Graph.GetNode(second).Component == "Extrude");
        });
        Test("Grasshopper example leaves existing model objects and document history untouched", () =>
        {
            using var workspace = new WorkspaceController();
            workspace.Document.Transaction("Existing model", () => workspace.Document.Add(workspace.Kernel.Line(Vec3.Zero, Vec3.UnitX), "Existing line"));
            var document = workspace.Document; var revision = document.Revision; var objects = document.Objects.ToArray();
            var canUndo = document.CanUndo; var canRedo = document.CanRedo;
            var terminal = GrasshopperExamples.InsertExtrusion(workspace.GraphSession);
            Cylinder(Solve(workspace.GraphSession, terminal), 5, 10);
            Check(ReferenceEquals(document, workspace.Document) && document.Revision == revision);
            Check(objects.SequenceEqual(document.Objects) && document.CanUndo == canUndo && document.CanRedo == canRedo);
        });
        Test("incompatible example registries fail before mutation and preserve solution and redo identity", () =>
        {
            var standard = Registry();
            foreach (var variant in new[] { "missing", "name", "kind", "access" })
            {
                var registry = new ComponentRegistry();
                foreach (var source in standard.Definitions)
                {
                    if (variant == "missing" && source.Id == "Extrude") continue;
                    var definition = source;
                    if (source.Id == "Circle" && variant == "name")
                        definition = source with { Inputs = source.Inputs.Select(p => p.Name == "Radius" ? p with { Name = "R" } : p).ToImmutableArray() };
                    if (source.Id == "Extrude" && variant == "kind")
                        definition = source with { Outputs = source.Outputs.Select(p => p with { Kind = ValueKind.Curve }).ToImmutableArray() };
                    if (source.Id == "UnitZ" && variant == "access")
                        definition = source with { Inputs = source.Inputs.Select(p => p with { Access = PortAccess.List }).ToImmutableArray() };
                    registry.Register(definition);
                }
                var session = new GraphSession(new(registry));
                session.Edit("Older definition", g => g.Add("Text")); Check(session.Undo());
                var graph = session.Graph; var revision = graph.Revision; var before = graph.Snapshot();
                var solved = graph.SolveAsync().GetAwaiter().GetResult(); var changed = 0; var replaced = 0;
                graph.Changed += () => changed++; session.Replaced += () => replaced++;
                var rejected = false;
                try { GrasshopperExamples.InsertExtrusion(session); }
                catch (Exception error) when (error is NotSupportedException or InvalidOperationException) { rejected = true; }
                Check(rejected && ReferenceEquals(graph, session.Graph) && graph.Revision == revision);
                Same(before, graph); Check(ReferenceEquals(solved, graph.LastResult));
                Check(changed == 0 && replaced == 0 && !session.CanUndo && session.CanRedo);
                Check(session.Redo() && session.Graph.Nodes.Single().Component == "Text");
            }
        });
        Test("extreme existing coordinates cannot overflow example placement or cover nearby work", () =>
        {
            var graph = new Graph(Registry());
            var near = graph.Add("Panel", 20, 20); graph.Update(near with { CanvasSize = new(400, 300) });
            graph.Add("Number", double.MaxValue, double.MaxValue);
            graph.Add("Number", -double.MaxValue, -double.MaxValue);
            var before = graph.Nodes.ToArray(); var session = new GraphSession(graph);
            var id = GrasshopperExamples.InsertExtrusion(session);
            var occupied = PanelLayout.Bounds(graph.GetNode(near.Id));
            foreach (var node in graph.Nodes.Where(n => before.All(old => old.Id != n.Id)))
            {
                var bounds = GraphPresentation.Bounds(node, graph.Registry.Get(node.Component));
                Check(double.IsFinite(bounds.X + bounds.Width) && double.IsFinite(bounds.Y + bounds.Height));
                Check(Math.Abs(bounds.X) <= 1_000_000 && Math.Abs(bounds.Y) <= 1_000_000);
                Check(!bounds.Intersects(occupied));
            }
            Check(before.All(n => ReferenceEquals(n, graph.GetNode(n.Id))));
            Cylinder(Solve(session, id), 5, 10);
        });
    }
}
