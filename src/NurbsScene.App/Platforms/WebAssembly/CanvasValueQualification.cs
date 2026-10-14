#if NURBSSCENE_QUALIFICATION
using NurbsScene.Controls;
using NurbsScene.Presentation;
using NurbsScene.Parametric;

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
            var options=PanelDisplayOptions.For(n);
            var drawn=view.GraphEditor.Canvas.GetRenderedPanelPreview(n.Id);
            var drawing=view.GraphEditor.Canvas.PanelDrawing(n.Id);
            return new{id=n.Id,scroll=view.GraphEditor.Canvas.PanelScrolling(n.Id),drawPaths=options.DrawPaths,drawIndices=options.DrawIndices,wrapItems=options.WrapItems,drawnLines=drawn?.Lines,drawnTruncated=drawn?.Truncated,drawnFontSize=drawing?.FontSize,drawnRows=drawing?.Rows,zoom=view.GraphEditor.Canvas.Zoom,text=n.Parameters.GetValueOrDefault("text"),singleItem=n.Parameters.GetValueOrDefault("singleItem"),
                itemCount=output?.ItemCount,branchCount=output?.Branches.Length,
                items=output?.AllItems.Take(8).Select(x=>x is string s?(s.Length>200?s[..200]:s):"<non-text>").ToArray(),
                lines=new PanelPreviewCache().Get(n,result).Lines};
        }).ToArray();
        // IsLoaded can become false before ShowAsync resumes its commit. Observe
        // the existing dialog lifetime separately; never infer commit from visibility.
        return new{editorOpen=view.GraphEditor.ValueEditorIsOpen,editorPending=view.GraphEditor.ValueEditor is not null,editorText=view.GraphEditor.ValueDraftText,resizeBounds=view.GraphEditor.Canvas.PanelResizeBounds,error=view.GraphEditor.LastCreationError,
            panelScrollSequence=view.GraphEditor.Canvas.PanelScrollInputSequence,panelScrollActive=view.GraphEditor.Canvas.PanelScrollIsActive,panelIndexBuilds=view.GraphEditor.Canvas.PanelIndexBuilds,formattedPanelRecords=view.GraphEditor.Canvas.PanelFormattedRecords,retainedPanelWindows=view.GraphEditor.Canvas.RetainedPanelWindows,wireCachePasses=view.GraphEditor.Canvas.WireCachePasses,resizePathBuilds=view.GraphEditor.Canvas.ResizePathBuilds,
            solvedRevision=graph.LastResult?.Revision,evaluated=graph.LastResult?.Evaluated,previewBuilds=view.GraphEditor.Canvas.ValuePreviewBuilds,panels};
    }
}
#endif
