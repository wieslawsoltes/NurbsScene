using NurbsScene.Geometry;
using NurbsScene.Kernel;

namespace NurbsScene.Rendering;

public readonly record struct RenderColor(float R,float G,float B,float A=1);
public enum DisplayMode { Wireframe,Shaded,Ghosted }
public sealed record DisplayItem(string Id,IGeometry Geometry,RenderColor Color,bool Selected=false,bool Locked=false);
public sealed record DisplayMesh(Guid GeometryId,Vec3[] Positions,float[] Vertices,uint[] Indices,float[] Lines,Bounds3 Bounds);
public sealed class DisplayScene
{
    public Vec3 Origin { get; private set; }
    public long Revision { get; private set; }
    public int SurfaceSegments { get; set; }=32;
    public double CurveTolerance { get; set; }=.02;
    public DisplayMode Mode { get; set; }=DisplayMode.Shaded;
    public bool ShowGrid { get; set; }=true;
    public bool ShowIsocurves { get; set; }=true;
    public (Vec3 Normal,double Offset)? ClipPlane { get; set; }
    public IReadOnlyList<DisplayItem> Items { get; private set; }=[];
    private readonly Dictionary<Guid,DisplayMesh> _meshes=[];
    public IReadOnlyDictionary<Guid,DisplayMesh> Meshes=>_meshes;
    public Bounds3 Bounds=>Items.Aggregate(Bounds3.Empty,(b,i)=>b.Union(i.Geometry.Bounds));
    public void SetItems(IEnumerable<DisplayItem> items)=>Commit(items.ToArray(),Origin,false);
    private void Commit(DisplayItem[] next,Vec3 origin,bool rebuild)
    {
        var prepared=new Dictionary<Guid,DisplayMesh>();
        foreach(var item in next)
        {
            if(prepared.ContainsKey(item.Geometry.Id))continue;
            prepared.Add(item.Geometry.Id,!rebuild&&_meshes.TryGetValue(item.Geometry.Id,out var existing)?existing:MeshBuilder.Build(item.Geometry,origin,SurfaceSegments,CurveTolerance));
        }
        // Only publish the new scene and coordinate frame after every mesh has been prepared.
        _meshes.Clear();foreach(var pair in prepared)_meshes.Add(pair.Key,pair.Value);Items=next;Origin=origin;Revision++;
    }
    public void Rebase(Vec3 origin){if(!origin.IsFinite)throw new ArgumentException("Invalid scene origin.");if(origin==Origin)return;Commit(Items.ToArray(),origin,true);}
    public void Rebuild()=>Commit(Items.ToArray(),Origin,true);
}
/// <summary>Display-only triangulation and control-hull-bounded curve tessellation. Never an exact modeling substitute.</summary>
public static class MeshBuilder
{
    public static DisplayMesh Build(IGeometry geometry,Vec3 origin,int segments=32,double curveTolerance=.02)
    {
        if(segments is <2 or >512)throw new ArgumentOutOfRangeException(nameof(segments));
        Vec3[] positions;Vec3[] normals;uint[] indices;var lines=new List<float>();
        void Line(Vec3 a,Vec3 b){a-=origin;b-=origin;lines.AddRange([(float)a.X,(float)a.Y,(float)a.Z,(float)b.X,(float)b.Y,(float)b.Z]);}
        if(geometry is NurbsCurve curve)
        {
            var tessellation=curve.Tessellate(curveTolerance);if(!tessellation.ToleranceMet)throw new ArithmeticException("Curve tessellation did not meet its display tolerance.");positions=tessellation.Positions;normals=new Vec3[positions.Length];indices=[];for(var i=1;i<positions.Length;i++)Line(positions[i-1],positions[i]);
        }
        else if(geometry is NurbsSurface surface)
        {
            var width=segments+1;positions=new Vec3[width*width];normals=new Vec3[positions.Length];indices=new uint[segments*segments*6];var cursor=0;
            for(var i=0;i<=segments;i++)for(var j=0;j<=segments;j++){var d=surface.Derivatives(surface.DomainU.At(i/(double)segments),surface.DomainV.At(j/(double)segments),1,1);var k=i*width+j;positions[k]=d[0,0];var normal=Vec3.Cross(d[1,0],d[0,1]);normals[k]=normal.Length>1e-18?normal.Normalized():Vec3.Zero;}
            for(var i=0;i<segments;i++)for(var j=0;j<segments;j++){var a=(uint)(i*width+j);var b=a+(uint)width;indices[cursor++]=a;indices[cursor++]=b;indices[cursor++]=a+1;indices[cursor++]=a+1;indices[cursor++]=b;indices[cursor++]=b+1;}
            var sums=new Vec3[positions.Length];for(var i=0;i<indices.Length;i+=3){var a=(int)indices[i];var b=(int)indices[i+1];var c=(int)indices[i+2];var n=Vec3.Cross(positions[b]-positions[a],positions[c]-positions[a]);sums[a]+=n;sums[b]+=n;sums[c]+=n;}for(var i=0;i<normals.Length;i++)if(normals[i].Length==0&&sums[i].Length>0)normals[i]=sums[i].Normalized();
            var step=Math.Max(1,segments/5);for(var i=0;i<=segments;i++)for(var j=0;j<segments;j++){if(i%step==0||i==segments)Line(positions[i*width+j],positions[i*width+j+1]);if(i%step==0||i==segments)Line(positions[j*width+i],positions[(j+1)*width+i]);}
        }
        else throw new NotSupportedException($"No display tessellator registered for {geometry.GetType().Name}.");
        var vertices=new float[positions.Length*6];for(var i=0;i<positions.Length;i++){var p=positions[i]-origin;vertices[i*6]=(float)p.X;vertices[i*6+1]=(float)p.Y;vertices[i*6+2]=(float)p.Z;vertices[i*6+3]=(float)normals[i].X;vertices[i*6+4]=(float)normals[i].Y;vertices[i*6+5]=(float)normals[i].Z;}
        if(vertices.Any(v=>!float.IsFinite(v))||lines.Any(v=>!float.IsFinite(v)))throw new ArithmeticException("Display coordinates exceed Float32 range after rebasing.");return new(geometry.Id,positions,vertices,indices,lines.ToArray(),geometry.Bounds);
    }
    public static float[] Grid(Vec3 origin,string view,int extent=100,int spacing=5)
    {
        var output=new List<float>();Vec3 World(double x,double y)=>view switch{"Front"=>new(x,0,y),"Right"=>new(0,x,y),_=>new(x,y,0)};
        void Add(Vec3 p){p-=origin;output.AddRange([(float)p.X,(float)p.Y,(float)p.Z]);}
        for(var i=-extent;i<=extent;i+=spacing){Add(World(i,-extent));Add(World(i,extent));Add(World(-extent,i));Add(World(extent,i));}return output.ToArray();
    }
}
