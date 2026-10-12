namespace NurbsScene.Model;

public sealed partial class SceneDocument
{
    // Dictionary/HashSet enumeration is not creation or pick order, particularly
    // after deletion reuses storage slots. Store chronology independently from
    // O(1) lookup/membership. Batch deletion compacts each list once, not once per
    // removed object. Snapshots/history use these orders without a format change.
    private List<string> _objectOrder = [];
    private List<string> _layerOrder = ["default"];
    private List<string> _selectionOrder = [];
    private IReadOnlyList<string>? _selectionOrderView;

    /// <summary>Retained, read-only selection in explicit pick order. Replacing
    /// selection adopts supplied order; additive selection appends newly selected
    /// objects only. Removing/locking objects retains survivor order. Select-all
    /// receives document creation order. This is not Rhino's geometric sorting
    /// of preselected loft profiles or its interactive seam alignment.</summary>
    public IReadOnlyList<string> SelectionOrder =>
        _selectionOrderView ??= Array.AsReadOnly(_selectionOrder.ToArray());

    private static IReadOnlyList<T> OrderedValues<T>(List<string> order, Dictionary<string, T> values)
    {
        var result = new T[order.Count];
        for (var i = 0; i < result.Length; i++) result[i] = values[order[i]];
        return Array.AsReadOnly(result);
    }
}
