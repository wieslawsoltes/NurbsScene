#if NURBSSCENE_QUALIFICATION
using NurbsScene.Controls;
using NurbsScene.Presentation;

namespace NurbsScene.App;

public partial class App
{
    // Bounded observation of the authoritative solved panel output and its
    // cached draw preview. This hook never edits a graph or dispatches input.
    private static object ObserveCanvasValues(WorkspaceView view)
    {
        var graph=view.Controller.Graph;
        var panels=graph.Nodes.Where(n=>n.Component=="Panel").Take(32).Select(n=>{
            var result=graph.LastResult?.Nodes.GetValueOrDefault(n.Id);
            var output=result?.Outputs.GetValueOrDefault("T");
            var drawing=view.GraphEditor.Canvas.PanelDrawing(n.Id);
            return new{id=n.Id,drawnFontSize=drawing?.FontSize,drawnRows=drawing?.Rows,zoom=view.GraphEditor.Canvas.Zoom,text=n.Parameters.GetValueOrDefault("text"),singleItem=n.Parameters.GetValueOrDefault("singleItem"),
                itemCount=output?.ItemCount,branchCount=output?.Branches.Length,
                items=output?.AllItems.Take(8).Select(x=>x is string s?(s.Length>200?s[..200]:s):"<non-text>").ToArray(),
                lines=new PanelPreviewCache().Get(n,result).Lines};
        }).ToArray();
        return new{editorOpen=view.GraphEditor.ValueEditorIsOpen,editorText=view.GraphEditor.ValueDraftText,resizeBounds=view.GraphEditor.Canvas.PanelResizeBounds,error=view.GraphEditor.LastCreationError,
            wireCachePasses=view.GraphEditor.Canvas.WireCachePasses,resizePathBuilds=view.GraphEditor.Canvas.ResizePathBuilds,
            previewBuilds=view.GraphEditor.Canvas.ValuePreviewBuilds,panels};
    }
}
#endif
