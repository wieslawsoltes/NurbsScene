using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

public readonly record struct PanelRecord(int Branch, int Item)
{
    public bool IsPath => Item < 0;
}

/// <summary>Immutable item/path index: one integer per branch, never per item or
/// character. Seeking a distant item uses binary search and does not enumerate
/// skipped values. Empty branches participate only when paths are displayed.</summary>
public sealed class PanelRecordIndex
{
    private readonly int[] _starts;
    public DataTree Tree { get; }
    public PanelRecordIndex(DataTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree); Tree = tree;
        _starts = new int[tree.Branches.Length + 1];
        for (var i = 0; i < tree.Branches.Length; i++)
            _starts[i + 1] = checked(_starts[i] + tree.Branches[i].Items.Length);
    }
    public int Count(bool drawPaths) => _starts[^1] + (drawPaths ? Tree.Branches.Length : 0);
    public PanelRecord Locate(int offset, bool drawPaths)
    {
        if (offset < 0 || offset >= Count(drawPaths)) throw new ArgumentOutOfRangeException(nameof(offset));
        var lo = 0; var hi = Tree.Branches.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_starts[mid] + (drawPaths ? mid : 0) <= offset) lo = mid + 1;
            else hi = mid;
        }
        var branch = lo - 1;
        return new(branch, offset - _starts[branch] - (drawPaths ? branch + 1 : 0));
    }
    internal void Append(PanelTextRows rows, int offset, PanelDisplayOptions options)
    {
        var record = Locate(offset, options.DrawPaths); var branch = Tree.Branches[record.Branch];
        if (record.IsPath) rows.Append(branch.Path.ToString());
        else rows.Append(branch.Items[record.Item] as string ?? "<non-text>", options.DrawIndices ? record.Item : -1);
    }
}
