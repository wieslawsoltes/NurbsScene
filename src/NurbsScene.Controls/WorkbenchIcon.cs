using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace NurbsScene.Controls;

/// <summary>Original vector artwork; no proprietary icons, emoji or platform
/// symbol font dependency. Pictures are shared by key and replayed at device DPI.</summary>
public sealed class WorkbenchIcon : SKCanvasElement
{
    private readonly string _key;
    public WorkbenchIcon(string key){_key=key;Width=22;Height=22;IsHitTestVisible=false;}
    protected override void RenderOverride(SKCanvas canvas,Size size)
    {
        canvas.Save();canvas.Scale((float)size.Width/24,(float)size.Height/24);IconArtwork.Draw(canvas,_key);canvas.Restore();
    }
}
public static class IconArtwork
{
    private static readonly Dictionary<string,SKPicture> Pictures=new(StringComparer.Ordinal);
    public static void Draw(SKCanvas canvas,string key)
    {
        SKPicture picture;lock(Pictures)
        {
            if(!Pictures.TryGetValue(key,out picture!))
            {
                // Category/command keys are bounded by the application catalog.
                // Arbitrary caller strings share one generic key beyond the cap.
                if(Pictures.Count>=128)key="Generic";
                if(!Pictures.TryGetValue(key,out picture!))
                {
                    using var recorder=new SKPictureRecorder();var target=recorder.BeginRecording(new(0,0,24,24));Record(target,key);picture=recorder.EndRecording();Pictures[key]=picture;
                }
            }
        }
        canvas.DrawPicture(picture);
    }
    private static void Record(SKCanvas c,string key)
    {
        using var p=new SKPaint{IsAntialias=true,StrokeWidth=1.25f,StrokeCap=SKStrokeCap.Round,StrokeJoin=SKStrokeJoin.Round};
        var ink=new SKColor(56,68,77);var blue=new SKColor(58,131,179);var pale=new SKColor(170,211,228);var gold=new SKColor(230,175,52);var green=new SKColor(94,147,63);
        void Line(float x,float y,float a,float b,SKColor? color=null){p.Style=SKPaintStyle.Stroke;p.Color=color??ink;c.DrawLine(x,y,a,b,p);}
        void Rect(float x,float y,float w,float h,SKColor fill){p.Style=SKPaintStyle.Fill;p.Color=fill;c.DrawRect(x,y,w,h,p);p.Style=SKPaintStyle.Stroke;p.Color=ink;c.DrawRect(x,y,w,h,p);}
        void Oval(float x,float y,float w,float h,SKColor? fill=null){p.Style=fill is null?SKPaintStyle.Stroke:SKPaintStyle.Fill;p.Color=fill??blue;c.DrawOval(new(x,y,x+w,y+h),p);if(fill is not null){p.Style=SKPaintStyle.Stroke;p.Color=ink;c.DrawOval(new(x,y,x+w,y+h),p);}}
        void Poly(SKColor fill,params float[] pts){using var path=new SKPath();path.MoveTo(pts[0],pts[1]);for(var i=2;i<pts.Length;i+=2)path.LineTo(pts[i],pts[i+1]);path.Close();p.Style=SKPaintStyle.Fill;p.Color=fill;c.DrawPath(path,p);p.Style=SKPaintStyle.Stroke;p.Color=ink;c.DrawPath(path,p);}
        void Arrow(float x,float y,float a,float b,SKColor? color=null){Line(x,y,a,b,color);var dx=a-x;var dy=b-y;var l=MathF.Sqrt(dx*dx+dy*dy);dx/=l;dy/=l;Line(a,b,a-4*dx+2.5f*dy,b-4*dy-2.5f*dx,color);Line(a,b,a-4*dx-2.5f*dy,b-4*dy+2.5f*dx,color);}
        void Curve(){using var path=new SKPath();path.MoveTo(3,19);path.CubicTo(7,1,15,24,21,4);p.Style=SKPaintStyle.Stroke;p.Color=blue;p.StrokeWidth=2;c.DrawPath(path,p);p.StrokeWidth=1.25f;}
        void Patch(){Poly(pale,3,8,15,3,21,16,8,21);Line(7,6,12,19,blue);Line(11,5,16,18,blue);Line(5,12,17,7,blue);Line(6,16,19,12,blue);}
        if(key.StartsWith("op:",StringComparison.Ordinal))
        {
            Rect(2,3,20,18,new(236,234,220));
            var label=key[3..];using var font=new SKFont(SKTypeface.Default,label.Length<=2?16:label.Length<=3?11:8);
            p.Style=SKPaintStyle.Fill;p.Color=ink;
            c.DrawText(label,12-font.MeasureText(label)/2,16,font,p);return;
        }
        switch(key)
        {
            case "Panel":Rect(2,3,20,18,new(249,245,163));Line(5,8,18,8);Line(5,12,18,12);Line(5,16,13,16);break;
            case "Flatten":Rect(2,3,7,3,pale);Rect(2,8,10,3,pale);Rect(2,13,7,3,pale);Arrow(18,3,18,16,green);Line(2,21,22,21,blue);break;
            case "Graft":Line(12,2,12,8,green);Line(4,8,20,8,green);Line(4,8,4,15,green);Line(12,8,12,15,green);Line(20,8,20,15,green);Rect(2,16,5,5,pale);Rect(10,16,5,5,pale);Rect(18,16,5,5,pale);break;
            case "Simplify":Rect(2,3,4,17,pale);Line(2,3,6,20,new(181,61,45));Arrow(9,12,21,12,green);break;
            case "Dropdown":Poly(ink,6,9,18,9,12,15);break;
            case "New":Poly(SKColors.White,5,2,15,2,20,7,20,22,5,22);Line(15,2,15,7);Line(15,7,20,7);break;
            case "Open":Poly(gold,2,7,2,20,20,20,22,9,10,9,8,6);Line(3,11,20,11);break;
            case "Save":Rect(3,2,18,20,blue);Rect(6,3,11,6,SKColors.White);Rect(6,14,12,8,SKColors.White);Line(15,4,15,8);break;
            case "Undo":case "Redo":
                if(key=="Redo"){c.Translate(24,0);c.Scale(-1,1);}Arrow(13,6,3,6,blue);using(var path=new SKPath()){path.MoveTo(3,6);path.CubicTo(24,2,24,21,7,20);p.Style=SKPaintStyle.Stroke;p.Color=blue;p.StrokeWidth=2;c.DrawPath(path,p);}break;
            case "Delete":case "Close":Line(5,5,19,19,new(181,61,45));Line(19,5,5,19,new(181,61,45));break;
            case "Select":case "SelectNone":Poly(SKColors.White,5,2,18,13,12,14,15,21,11,23,8,16,4,20);if(key=="SelectNone")Line(2,22,22,2,new(177,50,44));break;
            case "Fit":Oval(4,3,13,13,SKColors.White);Line(15,15,22,22);Arrow(10,10,6,6,blue);Arrow(10,10,14,14,blue);break;
            case "FourViews":Rect(2,2,9,9,SKColors.White);Rect(13,2,9,9,pale);Rect(2,13,9,9,pale);Rect(13,13,9,9,SKColors.White);break;
            case "Grid":for(var i=3;i<=21;i+=6){Line(i,3,i,21,new(133,143,143));Line(3,i,21,i,new(133,143,143));}break;
            case "Graph":case "Dock":Rect(2,5,7,6,new(173,207,138));Rect(15,14,7,6,new(199,199,205));Line(9,8,18,8);Line(18,8,18,14);break;
            case "Line":Line(4,20,20,4,blue);Rect(2,18,4,4,gold);Rect(18,2,4,4,gold);break;
            case "Circle":Oval(3,3,18,18);Rect(10,10,3,3,gold);break;
            case "Ellipse":Oval(2,6,20,12);Line(2,12,22,12,pale);Line(12,6,12,18,pale);Rect(10.5f,10.5f,3,3,gold);break;
            case "Curve":Curve();Rect(2,17,4,4,gold);Rect(18,2,4,4,gold);break;
            case "Reverse":Curve();Arrow(17,19,5,19,green);break;
            case "Knot":Curve();Rect(10,10,5,5,gold);break;
            case "Elevate":Curve();Arrow(18,22,18,10,green);break;
            case "Split":Curve();Line(3,3,21,21,new(185,57,44));break;
            case "Surface":case "Loft":case "Boundary":case "Area":Patch();if(key=="Boundary"){p.StrokeWidth=2.2f;Line(3,8,15,3,gold);Line(15,3,21,16,gold);}break;
            case "Extrude":Poly(pale,4,10,15,6,20,10,9,15);Poly(blue,4,10,9,15,9,22,4,17);Poly(new(129,182,212),9,15,20,10,20,18,9,22);Arrow(12,8,12,1,green);break;
            case "Revolve":case "Rotate":Oval(4,4,15,15,pale);Arrow(16,19,21,14,green);Line(11,2,11,22);break;
            case "Move":case "Vector":Arrow(12,12,12,2,blue);Arrow(12,12,22,12,new(173,70,52));Arrow(12,12,4,20,green);break;
            case "Copy":Rect(3,3,11,13,SKColors.White);Rect(10,9,11,13,pale);break;
            case "Scale":Rect(2,13,8,8,pale);Line(2,10,2,2);Line(2,2,22,2);Line(22,2,22,22);Arrow(11,12,20,4,green);break;
            case "Mirror":Line(12,1,12,23);Poly(pale,2,5,9,12,2,20);Poly(new(194,210,172),22,5,15,12,22,20);break;
            case "Project":Poly(pale,2,17,15,13,22,18,9,22);Oval(8,2,6,5,gold);Arrow(11,8,11,17,blue);break;
            case "Measure":Poly(gold,2,15,18,2,23,8,7,22);for(var i=0;i<4;i++)Line(6+i*3,13-i*2.5f,8+i*3,16-i*2.5f);break;
            case "Point":case "Centroid":Oval(6,6,12,12);Line(12,1,12,23);Line(1,12,23,12);Oval(10,10,4,4,gold);break;
            case "Section":Patch();Line(2,14,23,9,new(181,66,46));break;
            case "Shaded":case "Ghosted":case "Wireframe":Oval(3,3,18,18,key=="Wireframe"?null:key=="Ghosted"?new(209,219,223):pale);Oval(9,3,6,18);Line(3,12,21,12);break;
            case "Layer":Poly(pale,2,7,12,2,22,7,12,12);Poly(new(201,207,211),2,13,12,18,22,13,22,17,12,22,2,17);break;
            case "Properties":Rect(4,2,16,20,SKColors.White);Line(7,7,17,7,blue);Line(7,12,17,12,blue);Line(7,17,17,17,blue);break;
            case "Bake":Rect(4,10,16,12,gold);Arrow(12,1,12,14,blue);Line(4,16,20,16);break;
            case "Play":Poly(green,6,3,21,12,6,21);break;
            case "ZoomIn":case "ZoomOut":Oval(3,3,14,14,SKColors.White);Line(15,15,22,22);Line(6,10,14,10,blue);if(key=="ZoomIn")Line(10,6,10,14,blue);break;
            case "Maximize":Rect(3,3,18,18,SKColors.White);Line(3,7,21,7,blue);break;
            case "Maths":Line(3,6,10,6,blue);Line(6.5f,2.5f,6.5f,9.5f,blue);Line(15,5,22,5,blue);Line(3,15,10,22,blue);Line(3,22,10,15,blue);Line(15,16,22,16,blue);Line(15,20,22,20,blue);break;
            case "Sets":Rect(3,4,18,5,pale);Rect(3,10,14,5,pale);Rect(3,16,10,5,pale);break;
            case "Params":Rect(2,8,20,8,new(196,210,179));Rect(12,5,4,14,gold);break;
            default:Rect(4,4,16,16,new(195,201,181));Line(1,9,4,9);Line(1,15,4,15);Line(20,12,23,12);break;
        }
    }
}
