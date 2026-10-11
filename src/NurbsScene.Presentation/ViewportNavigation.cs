using NurbsScene.Geometry;
using NurbsScene.Rendering;

namespace NurbsScene.Presentation;

/// <summary>Fit the control-hull box to both viewport axes. Camera orientation is
/// retained; calculations complete before camera state is changed. This is not
/// an exact visible-geometry bounding box or an orbit/zoom performance metric.</summary>
public static class ViewportNavigation
{
    public static void Fit(Camera camera,Bounds3 bounds,int width,int height,double padding=1.1)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if(width<=0||height<=0||!double.IsFinite(padding)||padding<1||padding>10)
            throw new ArgumentOutOfRangeException(nameof(width),"Positive dimensions and padding in [1,10] are required.");
        if(bounds.IsEmpty)return;
        if(!bounds.Min.IsFinite||!bounds.Max.IsFinite)throw new ArgumentException("Finite bounds required.");
        var (_,forward,right,up)=camera.Basis();var target=bounds.Center;
        var aspect=width/(double)height;var tangent=Math.Tan(camera.FieldOfView/2);
        double distance=.001,halfScale=.000001,depth=0;
        for(var i=0;i<8;i++)
        {
            var point=new Vec3((i&1)==0?bounds.Min.X:bounds.Max.X,(i&2)==0?bounds.Min.Y:bounds.Max.Y,(i&4)==0?bounds.Min.Z:bounds.Max.Z);
            var delta=point-target;var x=Math.Abs(Vec3.Dot(delta,right));var y=Math.Abs(Vec3.Dot(delta,up));var z=Vec3.Dot(delta,forward);
            var extent=Math.Max(x/aspect,y)*padding;
            halfScale=Math.Max(halfScale,extent);depth=Math.Max(depth,Math.Abs(z));
            distance=Math.Max(distance,extent/tangent-z);
        }
        distance=Math.Max(distance,depth*1.1+.001);
        var scale=2*halfScale;
        if(!target.IsFinite||!double.IsFinite(distance)||!double.IsFinite(scale)||!double.IsFinite(distance*20))
            throw new ArithmeticException("Camera fit exceeds binary64 range.");
        camera.Target=target;camera.Distance=distance;camera.Scale=scale;
    }
}
