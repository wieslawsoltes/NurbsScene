using System.Collections.Immutable;

namespace NurbsScene.Presentation;

public sealed record WorkspaceTool(string Id,string Label,string Category,string Icon,string Command,bool Prompt=false);
/// <summary>Only executable entry points are advertised. Host operations start
/// with @ and must be handled by the host, never sent to the modeling kernel.</summary>
public static class WorkspaceCommands
{
    public static ImmutableArray<WorkspaceTool> Tools {get;} = [
        new("New","New workspace","Standard","New","@New"),new("Open","Open workspace","Standard","Open","@Open"),
        new("Save","Save workspace","Standard","Save","@Save"),new("Undo","Undo","Standard","Undo","Undo"),
        new("Redo","Redo","Standard","Redo","Redo"),new("Delete","Delete selected","Standard","Delete","Delete"),
        new("SelectAll","Select all","Standard","Select","SelAll"),new("SelectNone","Clear selection","Standard","SelectNone","SelNone"),
        new("Fit","Zoom extents","View","Fit","ZoomExtents"),new("FourViews","Four viewports","View","FourViews","@FourViews"),
        new("Wireframe","Wireframe","View","Wireframe","Wireframe"),new("Shaded","Shaded","View","Shaded","Shaded"),
        new("Ghosted","Ghosted","View","Ghosted","Ghosted"),new("Grid","Show grid","View","Grid","@Grid"),
        new("Definition","Definition panel","View","Graph","@Definition"),
        new("Line","Line: pick two points","Curve","Line","Line"),new("Circle","Circle","Curve","Circle","Circle 0,0,0 10",true),
        new("Interpolate","Interpolate points","Curve","Curve","Interpolate -20,0,0 -8,12,3 8,-8,6 20,0,0",true),
        new("Reverse","Reverse curve","Curve","Reverse","Reverse"),new("InsertKnot","Insert knot","Curve","Knot","InsertKnot 0.5",true),
        new("ElevateDegree","Elevate degree","Curve","Elevate","ElevateDegree"),new("SplitCurve","Split curve","Curve","Split","SplitCurve .25 .5",true),
        new("Extrude","Extrude curve","Surface","Extrude","Extrude 0,0,12",true),
        new("Revolve","Revolve curve","Surface","Revolve","Revolve 0,0,0 0,0,1 360",true),
        new("Loft","Loft curves","Surface","Loft","Loft"),new("Saddle","Bicubic saddle","Surface","Surface","Saddle"),
        new("Boundaries","Surface boundaries","Surface","Boundary","SurfaceBoundaries"),
        new("Move","Move","Transform","Move","Move 5,0,0",true),new("Copy","Copy","Transform","Copy","Copy 5,0,0",true),
        new("Scale","Scale","Transform","Scale","Scale 2",true),new("Rotate","Rotate","Transform","Rotate","Rotate 30",true),
        new("Mirror","Mirror in plane","Transform","Mirror","MirrorPlane 0,0,0 1,0,0",true),
        new("Project","Project to plane","Transform","Project","ProjectToPlane 0,0,0 0,0,1",true),
        new("Length","Curve length","Analyze","Measure","CurveLength"),new("ClosestPoint","Closest point","Analyze","Point","ClosestPoint 0,0,0",true),
        new("Section","Curve-plane section","Analyze","Section","CurvePlane 0,0,0 1,0,0",true),
        new("Area","Surface area","Analyze","Area","SurfaceArea"),new("Centroid","Surface centroid","Analyze","Centroid","SurfaceCentroid")
    ];
    public static ImmutableArray<string> Categories {get;}=["Standard","View","Curve","Surface","Transform","Analyze"];
    public static ImmutableArray<WorkspaceTool> Category(string category)=>Tools.Where(t=>t.Category==category).ToImmutableArray();
    public static ImmutableArray<WorkspaceTool> Search(string text,int limit=12)
    {
        ArgumentNullException.ThrowIfNull(text);if(limit is <1 or >100)throw new ArgumentOutOfRangeException(nameof(limit));
        text=text.Trim().TrimStart('_');if(text.Length>256)return [];
        return Tools.Where(t=>!t.Command.StartsWith('@')&&(t.Command.StartsWith(text,StringComparison.OrdinalIgnoreCase)||t.Label.Contains(text,StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t=>t.Command.StartsWith(text,StringComparison.OrdinalIgnoreCase)?0:1).ThenBy(t=>t.Label,StringComparer.OrdinalIgnoreCase).Take(limit).ToImmutableArray();
    }
}

/// <summary>Bounded command recall that preserves the draft when navigating down
/// past the most recent command. Empty submission repeats the last command.</summary>
public sealed class CommandRecall(int capacity=100)
{
    private readonly List<string> _items=[];
    private int _cursor;
    private string _draft="";
    public int Count=>_items.Count;
    public string? Submit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);text=text.Trim();
        if(text.Length>16_384)throw new ArgumentException("Command exceeds 16,384 characters.");
        if(text.Length==0)text=_items.LastOrDefault()??"";
        if(text.Length==0)return null;
        if(_items.LastOrDefault()!=text)_items.Add(text);
        if(_items.Count>Math.Clamp(capacity,1,1000))_items.RemoveAt(0);
        _cursor=_items.Count;_draft="";return text;
    }
    public string Previous(string draft)
    {
        if(_cursor==_items.Count)_draft=draft;
        if(_items.Count==0)return draft;
        _cursor=Math.Max(0,_cursor-1);return _items[_cursor];
    }
    public string Next(){_cursor=Math.Min(_items.Count,_cursor+1);return _cursor==_items.Count?_draft:_items[_cursor];}
    public void ResetNavigation(){_cursor=_items.Count;_draft="";}
}
