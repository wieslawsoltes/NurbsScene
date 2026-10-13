using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

/// <summary>Offsets identify data items and optional branch headers, not wrapped
/// visual lines. Existing per-item Unicode/scan limits remain explicit in Preview.
/// No authoritative data, graph history or serialized metadata is changed.</summary>
public sealed record PanelWindow(PanelPreview Preview, int First, int Visible, int Total, int MaximumOffset)
{
    public string DisplayFooter { get; } = MaximumOffset > 0 ? $"{First + 1}–{First + Visible} / {Total} · {Preview.Footer}" : Preview.Footer;
    public bool CanScroll => MaximumOffset > 0;
    public int PageStep => Math.Max(1, Visible - 1);
}

/// <summary>Owner-thread virtualized item windows. Only visible records and a
/// bounded final page are formatted. Warm reads are allocation-free. Source-tree
/// identity retains the O(branch-count) prefix index across presentation edits.</summary>
public sealed class PanelWindowCache
{
    private PanelRecordIndex? _index;
    private NodeResult? _result;
    private string? _kind;
    private PanelDisplayOptions? _options;
    private int _rows, _columns, _last;
    private bool _tailValid;
    private readonly PanelPreviewCache _fallback = new();
    private PanelWindow? _current;
    public PanelWindow? Current => _current;
    public long BuildCount { get; private set; }
    public long IndexBuilds { get; private set; }
    public long FormattedRecords { get; private set; }

    public PanelWindow Get(GraphNode node, NodeResult? result, int rows, int columns, int first = 0)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (rows < 1 || rows > PanelPreviewCache.MaximumRows || columns < 1 || columns > PanelPreviewCache.MaximumColumns)
            throw new ArgumentOutOfRangeException(nameof(rows));
        var options = PanelDisplayOptions.For(node);
        var sameLayout = _rows == rows && _columns == columns && _options == options && _kind == node.Component;
        var tree = result?.Status == "ok" ? result.Outputs.GetValueOrDefault("T") : null;
        if (tree is null)
        {
            var preview = _fallback.Get(node, result, rows, columns);
            _index = null; _tailValid = false; _result = null; _kind = null;
            if (_current is not null && ReferenceEquals(preview, _current.Preview)) return _current;
            BuildCount++; return _current = new(preview, 0, 0, 0, 0);
        }
        if (_index is null || !ReferenceEquals(tree, _index.Tree))
        { _index = new(tree); IndexBuilds++; _tailValid = false; }
        if (!sameLayout) _tailValid = false;
        var total = _index.Count(options.DrawPaths);
        if (!_tailValid)
        {
            // At most 64 records are considered. Fitting both rows and scan units
            // prevents End from hiding the last items behind the global budget.
            _last = Math.Max(0, total - 1); var usedRows = 0; var usedUnits = 0;
            for (var at = total - 1; at >= 0 && total - at <= rows; at--)
            {
                var tail = new PanelTextRows(rows, columns, options.WrapItems);
                _index.Append(tail, at, options); FormattedRecords++;
                if (at != total - 1 && (usedRows + tail.Count > rows || usedUnits + tail.ScannedCodeUnits > PanelPreviewCache.MaximumScannedCodeUnits)) break;
                _last = at; usedRows += tail.Count; usedUnits += tail.ScannedCodeUnits;
                if (usedRows == rows || usedUnits == PanelPreviewCache.MaximumScannedCodeUnits) break;
            }
            _tailValid = true;
        }
        first = Math.Clamp(first, 0, _last);
        if (_current is not null && ReferenceEquals(_result, result) && sameLayout && _current.First == first && _current.Total == total)
            return _current;
        var text = new PanelTextRows(rows, columns, options.WrapItems); var visible = 0;
        for (var at = first; at < total && !text.IsFull; at++)
        { _index.Append(text, at, options); visible++; FormattedRecords++; }
        if (first > 0 || first + visible < total) text.Truncate();
        var footer = $"{tree.ItemCount} items · {tree.Branches.Length} branches" + (text.Truncated ? " · …" : "");
        var next = new PanelWindow(new(text.Snapshot(), footer) { Truncated = text.Truncated }, first, visible, total, _last);
        _rows = rows; _columns = columns; _options = options; _kind = node.Component; _result = result;
        BuildCount++; return _current = next;
    }
}
