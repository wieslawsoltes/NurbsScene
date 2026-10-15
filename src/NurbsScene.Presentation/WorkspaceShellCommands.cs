namespace NurbsScene.Presentation;

/// <summary>Host-owned UI commands, separate from the geometry kernel catalog.</summary>
public static class WorkspaceShellCommands
{
    public static IReadOnlyList<string> Names {get;}=Array.AsReadOnly(new[]{
        "New","Open","Save","4View","MaxViewport","Grasshopper","CommandHistory",
        "Layers","Properties","Objects","ToggleLeftSidebar","ToggleRightSidebar",
        "ModelingLayout","DefinitionLayout","ResetLayout","ZoomSelected","Grid","Snap","Ortho","EndSnap"});
    public static IEnumerable<string> Search(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text=text.Trim().TrimStart('_');
        return text.Length is >0 and <=256?Names.Where(n=>n.StartsWith(text,StringComparison.OrdinalIgnoreCase)):[];
    }
}
