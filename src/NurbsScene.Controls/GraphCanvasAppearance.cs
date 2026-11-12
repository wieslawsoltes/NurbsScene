using NurbsScene.Controls.Drawing;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

namespace NurbsScene.Controls;

public sealed partial class GraphCanvas
{
    private bool _gridVisible=true,_componentIconsVisible=true,_componentNamesVisible=true,_fancyWires=true;
    public event Action? AppearanceChanged;
    public bool GridVisible {get=>_gridVisible;set=>SetAppearance(ref _gridVisible,value);}
    public bool ComponentIconsVisible {get=>_componentIconsVisible;set=>SetAppearance(ref _componentIconsVisible,value);}
    public bool ComponentNamesVisible {get=>_componentNamesVisible;set=>SetAppearance(ref _componentNamesVisible,value);}
    public bool FancyWires {get=>_fancyWires;set=>SetAppearance(ref _fancyWires,value);}
    private void SetAppearance(ref bool field,bool value)
    {
        ObjectDisposedException.ThrowIf(_canvasDisposed,this);
        if(field==value)return;field=value;Invalidate();AppearanceChanged?.Invoke();
    }
    private static VectorColor CanvasPaper=>new(236,233,225);
    private static VectorColor CapsuleColor(GraphNode node,NodeResult? result,bool selected)
    {
        if(!node.Enabled)return new(190,191,187);
        if(result?.Status=="error")return new(230,143,126);
        if(result?.Status=="blocked")return new(240,188,100);
        if(selected)return new(185,218,112);
        return node.Component is "Text" or "Panel"?new(249,245,163):new(222,223,218);
    }
    private void DrawCapsule(VectorCanvas canvas,PresentedNode node,VectorRect rect,NodeResult? result,bool selected)
    {
        var panel=PanelLayout.CanResize(node.Node);
        var radius=panel?3:node.Slider||node.Node.Component=="Boolean"?15:9;
        var body=CapsuleColor(node.Node,result,selected);
        Fill(canvas,new(rect.Left+1,rect.Top+2,rect.Right+1,rect.Bottom+2),new(0,0,0,30),radius);
        Fill(canvas,rect,body,radius);
        if(!panel)
        {
            // Two restrained highlight planes retain vector geometry at every
            // scale; no raster gradient or per-component texture is allocated.
            Fill(canvas,new(rect.Left+2,rect.Top+2,rect.Right-2,rect.Top+Math.Min(15,rect.Height*.42f)),new(255,255,255,39),Math.Max(2,radius-2));
            _paint.Style=VectorPaintStyle.Stroke;_paint.StrokeWidth=1;_paint.Color=new(255,255,255,90);
            canvas.DrawLine(rect.Left+radius,rect.Top+1,rect.Right-radius,rect.Top+1,_paint);
        }
        Stroke(canvas,rect,selected?new(67,103,29):new(100,102,95),selected?1.5f:1,radius);
    }
    private void DrawComponentBody(VectorCanvas canvas,PresentedNode node,VectorRect rect,bool selected)
    {
        var core=new VectorRect(rect.MidX-21,rect.Top+7,rect.MidX+21,rect.Bottom-18);
        if(core.Height>15)
        {
            Fill(canvas,core,selected?new(123,165,68,75):new(122,125,118,33),6);
            _paint.Style=VectorPaintStyle.Stroke;_paint.StrokeWidth=.7f;_paint.Color=new(99,101,94,65);
            canvas.DrawLine(core.Left,core.Top+5,core.Left,core.Bottom-5,_paint);
            canvas.DrawLine(core.Right,core.Top+5,core.Right,core.Bottom-5,_paint);
        }
        if(ComponentIconsVisible)
        {
            canvas.Save();canvas.Translate(rect.MidX-11,Math.Max(rect.Top+9,rect.MidY-18));
            canvas.Scale(.92f);IconArtwork.Draw(canvas,node.IconKey);canvas.Restore();
        }
        for(var side=0;side<2;side++)
        {
            var output=side==1;var ports=output?node.Definition.Outputs:node.Definition.Inputs;
            for(var i=0;i<ports.Length;i++)
            {
                var point=node.Port(output,i);var label=ports[i].Name;
                var region=output?new VectorRect(rect.Right-54,(float)point.Y-8,rect.Right-8,(float)point.Y+8)
                    :new VectorRect(rect.Left+8,(float)point.Y-8,rect.Left+54,(float)point.Y+8);
                canvas.Save();canvas.ClipRect(region);_font.Size=9;
                var x=output?region.Right-_font.MeasureText(label):region.Left;
                Text(canvas,label,x,(float)point.Y+3,new(57,60,54),9);canvas.Restore();
            }
        }
        if(ComponentNamesVisible)
        {
            var name=node.Node.Name??node.Node.Component;_font.Size=9;
            canvas.Save();canvas.ClipRect(new(rect.Left+8,rect.Bottom-17,rect.Right-8,rect.Bottom-2));
            Text(canvas,name,rect.MidX-_font.MeasureText(name)/2,rect.Bottom-5,new(54,57,51),9);canvas.Restore();
        }
        if(!node.Node.Preview)
        {
            _paint.Style=VectorPaintStyle.Stroke;_paint.StrokeWidth=1;_paint.Color=new(107,109,102);
            canvas.DrawLine(rect.Right-11,rect.Bottom-11,rect.Right-5,rect.Bottom-5,_paint);
            canvas.DrawLine(rect.Right-11,rect.Bottom-5,rect.Right-5,rect.Bottom-11,_paint);
        }
    }
}
