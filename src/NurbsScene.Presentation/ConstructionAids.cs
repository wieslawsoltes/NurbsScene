using System.Runtime.CompilerServices;
using NurbsScene.Geometry;
using NurbsScene.Rendering;

namespace NurbsScene.Presentation;

public sealed record ConstructionHit(Vec3 Point,string Snap);
/// <summary>World-aligned construction aids: XY (Top/Perspective), XZ (Front),
/// YZ (Right). End snaps take precedence over ortho/grid. Not full Rhino Osnap,
/// arbitrary CPlanes or occlusion-aware snapping. Hidden/locked items are excluded.</summary>
public sealed class ConstructionAids
{
    public bool GridSnap {get;set;}
    public bool Ortho {get;set;}
    public bool EndSnap {get;set;}
    private double _spacing=1;
    public double Spacing{get=>_spacing;set{if(!double.IsFinite(value)||value<=0)throw new ArgumentOutOfRangeException(nameof(value));_spacing=value;}}
    private sealed record Endpoints(Vec3 Start,Vec3 End);
    private readonly ConditionalWeakTable<NurbsCurve,Endpoints> _ends=new();
    public ConstructionHit Resolve(Vec3 point,string view,Vec3? anchor=null)
    {
        if(!point.IsFinite||anchor is{IsFinite:false})throw new ArgumentException("Finite construction points required.");
        var x=view=="Right"?point.Y:point.X;var y=view=="Top"||view=="Perspective"?point.Y:point.Z;
        var ax=anchor is{} a?(view=="Right"?a.Y:a.X):0;var ay=anchor is{} b?(view=="Top"||view=="Perspective"?b.Y:b.Z):0;
        if(GridSnap){x=Snap(x);y=Snap(y);}
        if(Ortho&&anchor.HasValue){if(Math.Abs(x-ax)>=Math.Abs(y-ay))y=ay;else x=ax;}
        var output=view switch{"Right"=>new Vec3(0,x,y),"Front"=>new(x,0,y),"Top" or "Perspective"=>new(x,y,0),_=>throw new ArgumentException("Unknown construction view.")};
        if(!output.IsFinite)throw new ArithmeticException("Construction snap exceeds binary64 range.");
        return new(output,Ortho&&anchor.HasValue?"Ortho":GridSnap?"Grid":"");
    }
    private double Snap(double x)
    {
        var units=x/Spacing;if(!double.IsFinite(units))throw new ArithmeticException("Grid coordinate overflow.");
        return Math.Round(units,MidpointRounding.AwayFromZero)*Spacing;
    }
    public ConstructionHit? At(DisplayScene scene,ViewportRectangle view,double x,double y,Vec3? anchor=null,double tolerance=8)
    {
        if(!double.IsFinite(tolerance)||tolerance<0||tolerance>1000)throw new ArgumentOutOfRangeException(nameof(tolerance));
        if(view.Width<=0||view.Height<=0)return null;
        var ray=view.Camera.Ray(x,y,view.Width,view.Height);
        if(EndSnap)
        {
            Vec3? best=null;var distance=tolerance*tolerance;var depth=double.PositiveInfinity;
            foreach(var item in scene.Items)
            {
                if(item.Locked||item.Geometry is not NurbsCurve c)continue;
                var ends=_ends.GetValue(c,static curve=>new(curve.PointAt(curve.Domain.Min),curve.PointAt(curve.Domain.Max)));
                void Candidate(Vec3 p)
                {
                    if(scene.ClipPlane is{} clip&&Vec3.Dot(clip.Normal,p)+clip.Offset<0)return;
                    var projected=view.Camera.Project(p,view.Width,view.Height);
                    if(projected.Depth<view.Camera.NearClip||projected.Depth>view.Camera.FarClip)return;
                    var d=(projected.X-x)*(projected.X-x)+(projected.Y-y)*(projected.Y-y);
                    if(d<distance||d==distance&&projected.Depth<depth){best=p;distance=d;depth=projected.Depth;}
                }
                Candidate(ends.Start);Candidate(ends.End);
            }
            if(best is{} p)return new(p,"End");
        }
        var normal=view.Name switch{"Front"=>Vec3.UnitY,"Right"=>Vec3.UnitX,_=>Vec3.UnitZ};
        var dot=Vec3.Dot(ray.Direction,normal);if(Math.Abs(dot)<1e-12)return null;
        var t=-Vec3.Dot(ray.Origin,normal)/dot;if(t<0||!double.IsFinite(t))return null;
        return Resolve(ray.Origin+ray.Direction*t,view.Name,anchor);
    }
}
