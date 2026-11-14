using System.Collections.Immutable;
using NurbsScene.Geometry;
using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

/// <summary>Small executable definitions inserted through the normal graph history.
/// Examples never replace a graph or alter its host's model document.</summary>
public static class GrasshopperExamples
{
    private const double Gap = 80, Width = 660, Height = 220, CoordinateLimit = 1_000_000;

    /// <summary>Add two number sliders, a circle, a Z vector and an extrusion as
    /// one undoable edit. Returns the terminal surface component for selection/baking.
    /// The caller owns canvas focus, fitting and the usual solve scheduling.</summary>
    public static string InsertExtrusion(GraphSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var graph = session.Graph;
        // Validate before entering Edit: an incompatible registry must preserve
        // the graph identity, completed solution and pending redo history too.
        Require(graph.Registry.Get("Number"), [], [("N", ValueKind.Number)]);
        Require(graph.Registry.Get("Circle"), [("Center", ValueKind.Vector), ("Radius", ValueKind.Number)], [("C", ValueKind.Curve)]);
        Require(graph.Registry.Get("UnitZ"), [("Length", ValueKind.Number)], [("V", ValueKind.Vector)]);
        Require(graph.Registry.Get("Extrude"), [("C", ValueKind.Curve), ("V", ValueKind.Vector)], [("S", ValueKind.Surface)]);
        var origin = Placement(graph);
        var terminal = "";
        session.Edit("Insert extrusion example", g =>
        {
            var radius = g.Add("Number", origin.X, origin.Y,
                new Dictionary<string, object?> { ["value"] = 5d, ["min"] = 1d, ["max"] = 20d }, "Radius");
            var height = g.Add("Number", origin.X, origin.Y + 140,
                new Dictionary<string, object?> { ["value"] = 10d, ["min"] = 1d, ["max"] = 30d }, "Height");
            var circle = g.Add("Circle", origin.X + 240, origin.Y,
                new Dictionary<string, object?> { ["input:Center"] = Vec3.Zero }, "Circle");
            var vector = g.Add("UnitZ", origin.X + 240, origin.Y + 140, name: "Height vector");
            var extrusion = g.Add("Extrude", origin.X + 480, origin.Y + 70, name: "Extrusion");
            g.Connect(new(radius.Id, "N"), new(circle.Id, "Radius"));
            g.Connect(new(height.Id, "N"), new(vector.Id, "Length"));
            g.Connect(new(circle.Id, "C"), new(extrusion.Id, "C"));
            g.Connect(new(vector.Id, "V"), new(extrusion.Id, "V"));
            terminal = extrusion.Id;
        });
        return terminal;
    }

    private static void Require(ComponentDefinition definition,
        ReadOnlySpan<(string Name, ValueKind Kind)> inputs, ReadOnlySpan<(string Name, ValueKind Kind)> outputs)
    {
        void Ports(ImmutableArray<PortDefinition> actual, ReadOnlySpan<(string Name, ValueKind Kind)> expected)
        {
            if (actual.Length != expected.Length)
                throw new InvalidOperationException($"The extrusion example requires the standard {definition.Id} component ports.");
            foreach (var (name, kind) in expected)
            {
                var port = actual.FirstOrDefault(p => p.Name == name);
                if (port is null || port.Kind != kind || port.Access != PortAccess.Item)
                    throw new InvalidOperationException($"The extrusion example requires {definition.Id}.{name} to be an item port of type {kind}.");
            }
        }
        Ports(definition.Inputs, inputs); Ports(definition.Outputs, outputs);
    }

    private static CanvasPoint Placement(Graph graph)
    {
        var bounds = graph.Nodes.Select(n => GraphPresentation.Bounds(n, graph.Registry.Get(n.Component))).ToArray();
        if (bounds.Length == 0) return new(40, 40);
        // Keep the new nodes well within the native float drawing range, even
        // when imported work contains otherwise legal, enormous coordinates.
        var right = bounds.Max(b => b.X + b.Width);
        var x = right + Gap;
        var y = Math.Clamp(bounds.Min(b => b.Y), -CoordinateLimit, CoordinateLimit - Height);
        if (double.IsFinite(x) && x >= -CoordinateLimit && x <= CoordinateLimit - Width)
            return new(x, y);

        // A far-away outlier must not move existing work or overflow new nodes.
        // Find room in one bounded column; sorted obstacles need only one pass.
        x = 40; y = 40;
        foreach (var b in bounds.Where(b => b.X <= x + Width + Gap && b.X + b.Width >= x - Gap).OrderBy(b => b.Y))
        {
            if (b.Y > y + Height + Gap) break;
            if (b.Y + b.Height + Gap < y) continue;
            y = b.Y + b.Height + Gap;
            if (!double.IsFinite(y) || y > CoordinateLimit - Height)
                throw new InvalidOperationException("No safe space for the example near the canvas origin. Move components closer together and try again.");
        }
        return new(x, y);
    }
}
