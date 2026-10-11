using NurbsScene.Geometry;

namespace NurbsScene.Rendering;

public readonly record struct Ray3(Vec3 Origin,Vec3 Direction);
public sealed class Camera(string name="Perspective")
{
    public string Name { get; }=name;
    public Vec3 Target { get; set; }=new(0,0,12);
    public double Distance { get; set; }=125;
    public double Scale { get; set; }=95;
    public double Azimuth { get; set; }=-.95;
    public double Elevation { get; set; }=.58;
    public double FieldOfView { get; set; }=Math.PI/4;
    public bool Perspective=>Name=="Perspective";
    public (Vec3 Eye,Vec3 Forward,Vec3 Right,Vec3 Up) Basis()
    {
        var offset=Name switch{"Top"=>new Vec3(0,0,Distance),"Front"=>new(0,-Distance,0),"Right"=>new(Distance,0,0),_=>new(Distance*Math.Cos(Elevation)*Math.Cos(Azimuth),Distance*Math.Cos(Elevation)*Math.Sin(Azimuth),Distance*Math.Sin(Elevation))};
        var forward=(-offset).Normalized();var right=Vec3.Cross(forward,Name=="Top"?Vec3.UnitY:Vec3.UnitZ).Normalized();return(Target+offset,forward,right,Vec3.Cross(right,forward));
    }
    public Transform3 Matrix(int width,int height,Vec3 origin)
    {
        var(eye,forward,right,up)=Basis();eye-=origin;var backward=-forward;var view=new Transform3([right.X,up.X,backward.X,0,right.Y,up.Y,backward.Y,0,right.Z,up.Z,backward.Z,0,-Vec3.Dot(right,eye),-Vec3.Dot(up,eye),-Vec3.Dot(backward,eye),1]);
        var aspect=width/(double)Math.Max(1,height);var near=Math.Max(.0001,Distance*1e-5);var far=Math.Max(10000,Distance*20);Transform3 projection;
        if(Perspective){var f=1/Math.Tan(FieldOfView/2);projection=new([f/aspect,0,0,0,0,f,0,0,0,0,(far+near)/(near-far),-1,0,0,2*far*near/(near-far),0]);}
        else{var half=Scale/2;projection=new([1/(half*aspect),0,0,0,0,1/half,0,0,0,0,-2/(far-near),0,0,0,-(far+near)/(far-near),1]);}
        return projection*view;
    }
    public void Orbit(double dx,double dy){if(!Perspective)return;Azimuth-=dx*.006;Elevation=Math.Clamp(Elevation+dy*.006,-1.55,1.55);}
    public void Pan(double dx,double dy,double height){var(_,_,right,up)=Basis();var units=Perspective?2*Distance*Math.Tan(FieldOfView/2)/Math.Max(1,height):Scale/Math.Max(1,height);Target+=right*(-dx*units)+up*(dy*units);}
    public void Zoom(double delta){var factor=Math.Exp(Math.Clamp(delta,-1000,1000)*.001);Distance=Math.Clamp(Distance*factor,.001,1e12);Scale=Math.Clamp(Scale*factor,.001,1e12);}
    public void Fit(Bounds3 bounds){if(bounds.IsEmpty)return;Target=bounds.Center;var diagonal=Math.Max(1e-6,bounds.Size.Length);Distance=diagonal/(2*Math.Sin(FieldOfView/2))*1.1;Scale=diagonal*1.12;}
    public Ray3 Ray(double x,double y,double width,double height)
    {
        var(eye,forward,right,up)=Basis();var nx=2*x/width-1;var ny=1-2*y/height;var aspect=width/height;if(Perspective){var tangent=Math.Tan(FieldOfView/2);return new(eye,(forward+right*(nx*tangent*aspect)+up*(ny*tangent)).Normalized());}return new(eye+right*(nx*Scale*aspect/2)+up*(ny*Scale/2),forward);
    }
    public (double X,double Y,double Depth) Project(Vec3 p,double width,double height)
    {
        var(eye,forward,right,up)=Basis();var d=p-eye;var depth=Vec3.Dot(d,forward);var h=Perspective?2*depth*Math.Tan(FieldOfView/2):Scale;return((Vec3.Dot(d,right)/(h*width/height)+.5)*width,(.5-Vec3.Dot(d,up)/h)*height,depth);
    }
}
public sealed record ViewportRectangle(string Name,int X,int Y,int Width,int Height,Camera Camera);
public static class ViewportLayout
{
    public static ViewportRectangle[] Create(int width,int height,IReadOnlyDictionary<string,Camera> cameras,string? maximized=null)
    {
        if(maximized is not null)return[new(maximized,0,0,width,height,cameras[maximized])];var w=(width-2)/2;var h=(height-2)/2;
        return[new("Top",0,0,w,h,cameras["Top"]),new("Perspective",w+2,0,width-w-2,h,cameras["Perspective"]),new("Front",0,h+2,w,height-h-2,cameras["Front"]),new("Right",w+2,h+2,width-w-2,height-h-2,cameras["Right"])];
    }
}
public sealed record PickResult(string ObjectId,Vec3 Point,double Distance);
public static class MeshPicker
{
    public static PickResult? Pick(DisplayScene scene,ViewportRectangle viewport,double x,double y)
    {
        var ray=viewport.Camera.Ray(x,y,viewport.Width,viewport.Height);PickResult? best=null;
        foreach(var item in scene.Items.Where(i=>!i.Locked))
        {
            var mesh=scene.Meshes[item.Geometry.Id];
            for(var i=0;i<mesh.Indices.Length;i+=3)
            {
                var a=mesh.Positions[mesh.Indices[i]];var b=mesh.Positions[mesh.Indices[i+1]];var c=mesh.Positions[mesh.Indices[i+2]];var ab=b-a;var ac=c-a;var p=Vec3.Cross(ray.Direction,ac);var det=Vec3.Dot(ab,p);if(Math.Abs(det)<1e-14*Math.Max(1,ab.Length*ac.Length))continue;var t=ray.Origin-a;var u=Vec3.Dot(t,p)/det;if(u<0||u>1)continue;var q=Vec3.Cross(t,ab);var v=Vec3.Dot(ray.Direction,q)/det;if(v<0||u+v>1)continue;var distance=Vec3.Dot(ac,q)/det;if(distance<0||best is not null&&distance>=best.Distance)continue;var point=ray.Origin+ray.Direction*distance;if(scene.ClipPlane is{} clip&&Vec3.Dot(clip.Normal,point)+clip.Offset<0)continue;best=new(item.Id,point,distance);
            }
            if(mesh.Indices.Length==0)for(var i=1;i<mesh.Positions.Length;i++){var a=viewport.Camera.Project(mesh.Positions[i-1],viewport.Width,viewport.Height);var b=viewport.Camera.Project(mesh.Positions[i],viewport.Width,viewport.Height);if(a.Depth<0||b.Depth<0)continue;var dx=b.X-a.X;var dy=b.Y-a.Y;var length=dx*dx+dy*dy;var t=length==0?0:Math.Clamp(((x-a.X)*dx+(y-a.Y)*dy)/length,0,1);if(Math.Sqrt(Math.Pow(x-a.X-t*dx,2)+Math.Pow(y-a.Y-t*dy,2))>7)continue;var point=Vec3.Lerp(mesh.Positions[i-1],mesh.Positions[i],t);var distance=(point-ray.Origin).Length;if(best is null||distance<best.Distance)best=new(item.Id,point,distance);}
        }
        return best;
    }
}
