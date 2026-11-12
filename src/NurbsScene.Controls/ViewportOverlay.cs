using NurbsScene.Geometry;
using NurbsScene.Rendering;
using NurbsScene.Controls.Drawing;
using Windows.Foundation;

namespace NurbsScene.Controls;

/// <summary>Input-transparent vector axes and live construction preview, separate
/// from authoritative scene geometry and the depth-tested WebGPU renderer.</summary>
public sealed class ViewportOverlay(CadViewport viewport) : WebGpuVectorCanvasElement
{
    protected override void Draw(VectorCanvas canvas,Size area)
    {
        using var paint=new VectorPaint{IsAntialias=true,StrokeWidth=1.4f};using var font=new VectorFont(VectorTypeface.Default,10);
        foreach(var view in ViewportLayout.Create(Math.Max(0,(int)area.Width),Math.Max(0,(int)area.Height),viewport.Cameras,viewport.Maximized))
        {
            if(view.Width<2||view.Height<2)continue;
            paint.Style=VectorPaintStyle.Stroke;paint.StrokeWidth=1;
            paint.Color=view.Name==viewport.ActiveViewport?new(47,101,164):new(156,161,167);
            canvas.DrawRect(new(view.X+.5f,view.Y+.5f,view.X+view.Width-.5f,view.Y+view.Height-.5f),paint);
            if(view.Width<60||view.Height<60)continue;
            canvas.Save();canvas.ClipRect(new(view.X,view.Y,view.X+view.Width,view.Y+view.Height));
            paint.StrokeWidth=1.4f;
            var (_,_,right,up)=view.Camera.Basis();var x=view.X+27f;var y=view.Y+view.Height-28f;
            void Axis(Vec3 vector,string label,VectorColor color){var dx=(float)Vec3.Dot(vector,right)*19;var dy=-(float)Vec3.Dot(vector,up)*19;paint.Color=color;paint.Style=VectorPaintStyle.Stroke;canvas.DrawLine(x,y,x+dx,y+dy,paint);paint.Style=VectorPaintStyle.Fill;canvas.DrawText(label,x+dx+2,y+dy+3,font,paint);}
            Axis(Vec3.UnitX,"X",new(171,48,41));Axis(Vec3.UnitY,"Y",new(44,133,53));Axis(Vec3.UnitZ,"Z",new(44,78,166));
            if(view.Name==viewport.CursorViewport&&viewport.CursorHit is{} cursor)
            {
                var p=view.Camera.Project(cursor.Point,view.Width,view.Height);
                if(p.Depth>view.Camera.NearClip&&p.Depth<view.Camera.FarClip)
                {
                    var px=view.X+(float)p.X;var py=view.Y+(float)p.Y;
                    if(viewport.ConstructionAnchor?.Invoke() is{} anchor)
                    {
                        var a=view.Camera.Project(anchor,view.Width,view.Height);paint.Color=new(50,80,145);paint.Style=VectorPaintStyle.Stroke;
                        canvas.DrawLine(view.X+(float)a.X,view.Y+(float)a.Y,px,py,paint);
                    }
                    if(cursor.Snap.Length>0){paint.Color=new(199,125,18);paint.Style=VectorPaintStyle.Stroke;canvas.DrawRect(px-4,py-4,8,8,paint);paint.Style=VectorPaintStyle.Fill;paint.Color=new(42,42,42);canvas.DrawText(cursor.Snap,px+9,py-7,font,paint);}
                }
            }
            canvas.Restore();
        }
    }
}
