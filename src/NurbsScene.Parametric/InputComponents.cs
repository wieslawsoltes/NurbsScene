using System.Collections.Immutable;
using System.Globalization;
using NurbsScene.Geometry;

namespace NurbsScene.Parametric;

/// <summary>Persistent input/display components. A Panel converts data to text;
/// it does not change source geometry or claim exact CAD serialization.</summary>
public static class InputComponents
{
    public const int MaximumTextLength = 65_536;
    public const int MaximumOutputCharacters = 1_000_000;

    public static bool BooleanValue(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("value", out var value)) return false;
        return value is bool b ? b : throw new ArgumentException("Boolean value must be True or False.");
    }

    public static void Register(ComponentRegistry registry) => registry.Register(new(
        "Panel", "Params", "Text input and connected data-tree display. Connected empty data stays empty.",
        [new("D", ValueKind.Any, PortAccess.Tree, Optional: true)],
        [new("T", ValueKind.Text, PortAccess.Tree)], c => ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(
            new Dictionary<string, object?> { ["T"] = EvaluatePanel(c) })));

    public static DataTree EvaluatePanel(ComponentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        var data = context.Get<DataTree>("D");
        if (!context.IsConnected("D") && !context.Parameters.ContainsKey("input:D"))
        {
            var text = context.Parameters.GetValueOrDefault("text", "") is string s
                ? s : throw new ArgumentException("Panel text must be a string.");
            if (text.Length > MaximumTextLength) throw new ArgumentException("Panel text exceeds 65,536 characters.");
            var single = context.Parameters.GetValueOrDefault("singleItem", false) is bool flag
                ? flag : throw new ArgumentException("Panel single-item option must be Boolean.");
            return single ? DataTree.Item(text) : DataTree.List(text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Cast<object?>());
        }
        var characters = 0;
        var branches = ImmutableArray.CreateBuilder<DataBranch>(data.Branches.Length);
        foreach (var branch in data.Branches)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var items = ImmutableArray.CreateBuilder<object?>(branch.Items.Length);
            foreach (var item in branch.Items)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var text = Format(item);
                if (text.Length > MaximumTextLength || (characters += text.Length) > MaximumOutputCharacters)
                    throw new InvalidOperationException("Panel text output exceeds its character budget.");
                items.Add(text);
            }
            branches.Add(new(branch.Path, items.MoveToImmutable()));
        }
        return new(branches.MoveToImmutable());
    }

    /// <summary>Invariant supported-type text conversion. Never calls plugin
    /// ToString; unsupported data produces an explicit component error.</summary>
    public static string Format(object? value) => value switch
    {
        null => "<null>", string s => s, bool b => b ? "True" : "False",
        int i => i.ToString(CultureInfo.InvariantCulture),
        double d when double.IsFinite(d) => d.ToString("G17", CultureInfo.InvariantCulture),
        Vec3 v => FormattableString.Invariant($"({v.X:G17}, {v.Y:G17}, {v.Z:G17})"),
        DataPath path => path.ToString(),
        NurbsCurve c => $"NURBS curve: degree {c.Degree}, {c.Count} controls",
        NurbsSurface s => $"NURBS surface: degree {s.DegreeU} x {s.DegreeV}, {s.CountU} x {s.CountV} controls",
        _ => throw new ArgumentException("The panel cannot convert this value type to text.")
    };
}
