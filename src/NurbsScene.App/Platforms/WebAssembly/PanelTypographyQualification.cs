#if NURBSSCENE_QUALIFICATION
using NurbsScene.Controls;
using NurbsScene.Parametric;

namespace NurbsScene.App;

public partial class App
{
    // Read-only bounded snapshots from the renderer actually used by GraphCanvas.
    // No font loading, measurement, graph edits or input dispatch occur here.
    private static object ObserveTypography(WorkspaceView view)
    {
        static object Style(PanelTextStyle s) => new { fontFamily=s.FontFamily,size=s.Size,bold=s.Bold,italic=s.Italic,
            horizontal=s.Horizontal.ToString(),vertical=s.Vertical.ToString() };
        var canvas=view.GraphEditor.Canvas;
        var panels=view.Controller.Graph.Nodes.Where(n=>n.Component is "Panel" or "Text").Take(32).Select(n=>{
            var rendered=canvas.RenderedTypography(n.Id);
            return new {id=n.Id,hasStyle=n.PanelStyle is not null,style=Style(PanelTextStyle.For(n)),
                rendered=rendered is null?null:new {style=Style(rendered.Style),family=rendered.ResolvedFamily,
                    size=rendered.FontSize,ascent=rendered.Metrics.Ascent,descent=rendered.Metrics.Descent,
                    clip=rendered.Placement.Clip,clipped=rendered.Placement.Clipped,
                    rows=rendered.Placement.Rows.Take(8).Select(r=>new {text=r.Text,x=r.X,baseline=r.Baseline,advance=r.Advance}).ToArray()}};
        }).ToArray();
        return new {editorOpen=view.GraphEditor.PanelTypographyEditorIsOpen,validation=view.GraphEditor.PanelTypographyValidation,
            glyphBuilds=canvas.PanelGlyphBuilds,measurements=canvas.PanelMeasurementBuilds,placements=canvas.PanelPlacementBuilds,panels};
    }
}
#endif
