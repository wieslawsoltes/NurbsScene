using System.Collections.Immutable;

namespace NurbsScene.Parametric;

/// <summary>Invariant path editing syntax: {0;2;10} or 0;2;10. The empty path
/// is written {}. No floating-point, signed or culture-dependent indices.</summary>
public sealed partial class DataPath
{
    public static bool TryParse(string? text, out DataPath? path)
    {
        path = null;
        if (text is null || text.Length > 2048) return false;
        text = text.Trim();
        if (text.StartsWith('{') || text.EndsWith('}'))
        {
            if (!text.StartsWith('{') || !text.EndsWith('}')) return false;
            text = text[1..^1].Trim();
            if (text.Length == 0) { path = new([]); return true; }
        }
        if (text.Length == 0) return false;
        var tokens = text.Split(';');
        if (tokens.Length > 64) return false;
        var indices = new int[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            if (!int.TryParse(tokens[i].Trim(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out indices[i])) return false;
        path = new(indices);
        return true;
    }
    public static DataPath Parse(string text)
        => TryParse(text, out var path) ? path! : throw new FormatException("Enter a path such as {0;2;10}, using at most 64 nonnegative integer indices.");
}

public sealed partial class DataTree
{
    /// <summary>Extract a branch without renumbering its path. A missing path is an empty tree.</summary>
    public DataTree Branch(DataPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new(Branches.Where(b => b.Path.Equals(path)));
    }

    /// <summary>Replace a path, merging colliding branches in original numeric path order.</summary>
    public DataTree ReplacePath(DataPath source, DataPath target)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(target);
        return new(Branches.Select(b => b.Path.Equals(source) ? b with { Path = target } : b));
    }

    /// <summary>Remove trailing indices from each path. The empty path is valid;
    /// collisions merge their items in numeric source-path order.</summary>
    public DataTree TrimPaths(int count)
    {
        if (count is < 0 or > 64) throw new ArgumentOutOfRangeException(nameof(count));
        return new(Branches.Select(b => b with {
            Path = new DataPath(b.Path.Indices.Take(Math.Max(0, b.Path.Indices.Length - count)))
        }));
    }

    public DataTree Clean(bool removeNullItems = true, bool removeEmptyBranches = true)
    {
        IEnumerable<DataBranch> branches = Branches;
        if (removeNullItems) branches = branches.Select(b => b with {
            Items = b.Items.Where(item => item is not null).ToImmutableArray()
        });
        if (removeEmptyBranches) branches = branches.Where(b => b.Items.Length > 0);
        return new(branches);
    }

    public DataTree Flatten(DataPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return List(AllItems, path);
    }
}

/// <summary>Executable path and tree components. Numeric ordering, empty branches
/// and null items are retained unless an explicitly named operation changes them.</summary>
public static class TreeComponents
{
    public static void Register(ComponentRegistry registry)
    {
        PortDefinition Tree(string name) => new(name, ValueKind.Any, PortAccess.Tree);
        PortDefinition Path(string name) => new(name, ValueKind.Path, Default: DataPath.Root);
        void Add(string id, PortDefinition[] inputs, PortDefinition[] outputs,
            Func<ComponentContext, IReadOnlyDictionary<string, object?>> evaluate, string description)
            => registry.Register(new(id, "Sets", description, inputs.ToImmutableArray(), outputs.ToImmutableArray(),
                c => { c.CancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(evaluate(c)); }));
        static Dictionary<string, object?> One(string name, object value) => new() { [name] = value };
        static int Index(object? value)
        {
            var number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            if (value is null || !double.IsFinite(number) || number < 0 || number > int.MaxValue || number != Math.Truncate(number))
                throw new ArgumentException("Path indices must be nonnegative Int32 values.");
            return (int)number;
        }
        Add("ConstructPath", [new("Indices", ValueKind.Number, PortAccess.List, new object?[] { 0 })], [Path("P")],
            c => One("P", new DataPath(c.Get<ImmutableArray<object?>>("Indices").Select(Index))),
            "Construct a typed path from up to 64 nonnegative indices.");
        Add("DeconstructPath", [Path("P")], [new("Indices", ValueKind.Integer, PortAccess.List)],
            c => One("Indices", c.Get<DataPath>("P").Indices), "Extract the indices of a typed path.");
        Add("TreePaths", [Tree("T")], [new("P", ValueKind.Path, PortAccess.List), new("Counts", ValueKind.Integer, PortAccess.List)],
            c => new Dictionary<string, object?> {
                ["P"] = c.Get<DataTree>("T").Branches.Select(b => b.Path).ToArray(),
                ["Counts"] = c.Get<DataTree>("T").Branches.Select(b => b.Items.Length).ToArray()
            }, "List paths and corresponding branch sizes in numeric order, including empty branches.");
        Add("TreeBranch", [Tree("T"), Path("P")], [Tree("T")],
            c => One("T", c.Get<DataTree>("T").Branch(c.Get<DataPath>("P"))), "Extract a branch while preserving its path.");
        Add("ReplacePath", [Tree("T"), Path("From"), Path("To")], [Tree("T")],
            c => One("T", c.Get<DataTree>("T").ReplacePath(c.Get<DataPath>("From"), c.Get<DataPath>("To"))),
            "Replace one path. Colliding branches merge in numeric source-path order.");
        Add("TrimTreePaths", [Tree("T"), new("N", ValueKind.Number, Default: 1d)], [Tree("T")],
            c => One("T", c.Get<DataTree>("T").TrimPaths(Index(c.Inputs["N"]))), "Remove N trailing path indices; retain empty paths.");
        Add("CleanTree", [Tree("T"), new("Nulls", ValueKind.Boolean, Default: true), new("Empty", ValueKind.Boolean, Default: true)], [Tree("T")],
            c => One("T", c.Get<DataTree>("T").Clean(c.Get<bool>("Nulls"), c.Get<bool>("Empty"))),
            "Explicitly remove null items and/or empty branches without renumbering paths.");
        Add("FlattenToPath", [Tree("T"), Path("P")], [Tree("T")],
            c => One("T", c.Get<DataTree>("T").Flatten(c.Get<DataPath>("P"))), "Flatten items into a chosen typed path.");
    }
}
