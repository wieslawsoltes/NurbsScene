using System.Buffers;
using System.Collections.ObjectModel;
using NurbsScene.Geometry;
using NurbsScene.Kernel;

namespace NurbsScene.Rendering;

public readonly record struct RenderColor(float R,float G,float B,float A=1);
public enum DisplayMode { Wireframe,Shaded,Ghosted }
public sealed record DisplayItem(string Id,IGeometry Geometry,RenderColor Color,bool Selected=false,bool Locked=false);
/// <summary>Published display arrays are read-only by contract. Authoritative
/// positions stay binary64; LineIndices permits lossless-origin re-encoding.</summary>
public sealed record DisplayMesh(Guid GeometryId,Vec3[] Positions,float[] Vertices,uint[] Indices,float[] Lines,Bounds3 Bounds)
{
    public uint[]? LineIndices { get; init; }
    private TrianglePickIndex? _pickIndex;
    internal TrianglePickIndex GetPickingIndex(out bool created)
    {
        created=_pickIndex is null;
        return LazyInitializer.EnsureInitialized(ref _pickIndex,()=>new TrianglePickIndex(Positions,Indices));
    }
}
public sealed class DisplayScene
{
    public Vec3 Origin { get; private set; }
    public long Revision { get; private set; }
    /// <summary>Changes only when CPU mesh buffers change, not on selection/color edits.</summary>
    public long MeshRevision { get; private set; }
    public long MeshBuildCount { get; private set; }
    public long MeshRebaseCount { get; private set; }
    private int _segments=32;
    private double _tolerance=.02;
    public int SurfaceSegments
    {
        get=>_segments;
        set { if(value is <2 or >512)throw new ArgumentOutOfRangeException(nameof(value)); if(value!=_segments)Commit(Items.ToArray(),Origin,true,value,_tolerance); }
    }
    public double CurveTolerance
    {
        get=>_tolerance;
        set { if(!double.IsFinite(value)||value<=0)throw new ArgumentOutOfRangeException(nameof(value)); if(value!=_tolerance)Commit(Items.ToArray(),Origin,true,_segments,value); }
    }
    public DisplayMode Mode { get; set; }=DisplayMode.Shaded;
    public bool ShowGrid { get; set; }=true;
    public bool ShowIsocurves { get; set; }=true;
    public (Vec3 Normal,double Offset)? ClipPlane { get; set; }
    public IReadOnlyList<DisplayItem> Items { get; private set; }=Array.Empty<DisplayItem>();
    private IReadOnlyDictionary<Guid,DisplayMesh> _meshes=new ReadOnlyDictionary<Guid,DisplayMesh>(new Dictionary<Guid,DisplayMesh>());
    public IReadOnlyDictionary<Guid,DisplayMesh> Meshes=>_meshes;
    public Bounds3 Bounds { get; private set; }=Bounds3.Empty;
    public void SetItems(IEnumerable<DisplayItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Commit(items.ToArray(),Origin,false,_segments,_tolerance);
    }
    private void Commit(DisplayItem[] next,Vec3 origin,bool rebuild,int segments,double tolerance)
    {
        if(!rebuild && origin==Origin && segments==_segments && tolerance==_tolerance && next.SequenceEqual(Items))return;
        var prepared=new Dictionary<Guid,DisplayMesh>();var identities=new Dictionary<Guid,IGeometry>();
        var ids=new HashSet<string>(StringComparer.Ordinal);var bounds=Bounds3.Empty;long built=0,rebased=0;
        foreach(var item in next)
        {
            if(item is null || item.Geometry is null || string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id))
                throw new ArgumentException("Display items require distinct nonempty identities and geometry.");
            if(identities.TryGetValue(item.Geometry.Id,out var geometry) && !ReferenceEquals(geometry,item.Geometry))
                throw new ArgumentException("Different geometries cannot share a display identity.");
            identities[item.Geometry.Id]=item.Geometry;bounds=bounds.Union(item.Geometry.Bounds);
            if(prepared.ContainsKey(item.Geometry.Id))continue;
            DisplayMesh mesh;
            if(!rebuild && _meshes.TryGetValue(item.Geometry.Id,out var existing))
            {
                if(origin==Origin)mesh=existing;
                else if(existing.LineIndices is not null) { mesh=MeshBuilder.Rebase(existing,origin);rebased++; }
                else { mesh=MeshBuilder.Build(item.Geometry,origin,segments,tolerance);built++; }
            }
            else { mesh=MeshBuilder.Build(item.Geometry,origin,segments,tolerance);built++; }
            prepared.Add(item.Geometry.Id,mesh);
        }
        var meshChanged=prepared.Count!=_meshes.Count || prepared.Any(p=>!_meshes.TryGetValue(p.Key,out var m)||!ReferenceEquals(m,p.Value));
        // Publish only after every buffer and identity has been prepared. Settings
        // changes that fail keep the previous scene AND its quality settings.
        if(meshChanged) { _meshes=new ReadOnlyDictionary<Guid,DisplayMesh>(prepared);MeshRevision++; }
        Items=Array.AsReadOnly(next);Origin=origin;Bounds=bounds;_segments=segments;_tolerance=tolerance;
        MeshBuildCount+=built;MeshRebaseCount+=rebased;Revision++;
    }
    public void Rebase(Vec3 origin)
    {
        if(!origin.IsFinite)throw new ArgumentException("Invalid scene origin.");
        if(origin!=Origin)Commit(Items.ToArray(),origin,false,_segments,_tolerance);
    }
    public void Rebuild()=>Commit(Items.ToArray(),Origin,true,_segments,_tolerance);
}
/// <summary>Display-only triangulation and control-hull-bounded curve tessellation. Never an exact modeling substitute.</summary>
public static class MeshBuilder
{
    public static DisplayMesh Build(IGeometry geometry,Vec3 origin,int segments=32,double curveTolerance=.02)
    {
        if(segments is <2 or >512)throw new ArgumentOutOfRangeException(nameof(segments));
        Vec3[] positions;Vec3[] normals;uint[] indices;var lineIndices=new List<uint>();
        void Line(int a,int b){lineIndices.Add((uint)a);lineIndices.Add((uint)b);}
        if(geometry is NurbsCurve curve)
        {
            var tessellation=curve.Tessellate(curveTolerance);if(!tessellation.ToleranceMet)throw new ArithmeticException("Curve tessellation did not meet its display tolerance.");positions=tessellation.Positions;normals=new Vec3[positions.Length];indices=[];for(var i=1;i<positions.Length;i++)Line(i-1,i);
        }
        else if(geometry is NurbsSurface surface)
        {
            var width=segments+1;positions=new Vec3[width*width];normals=new Vec3[positions.Length];indices=new uint[segments*segments*6];var cursor=0;
            if(surface.Evaluator is INurbsGridEvaluator grid)
            {
                var u=new double[width];var v=new double[width];
                for(var i=0;i<width;i++){u[i]=surface.DomainU.At(i/(double)segments);v[i]=surface.DomainV.At(i/(double)segments);}
                var jets=ArrayPool<SurfaceJet>.Shared.Rent(positions.Length);
                try
                {
                    grid.EvaluateGrid(surface,u,v,jets.AsSpan(0,positions.Length));
                    for(var k=0;k<positions.Length;k++){positions[k]=jets[k].Point;var normal=Vec3.Cross(jets[k].Du,jets[k].Dv);normals[k]=normal.Length>1e-18?normal.Normalized():Vec3.Zero;}
                }
                finally{ArrayPool<SurfaceJet>.Shared.Return(jets);}
            }
            else for(var i=0;i<=segments;i++)for(var j=0;j<=segments;j++){var d=surface.Derivatives(surface.DomainU.At(i/(double)segments),surface.DomainV.At(j/(double)segments),1,1);var k=i*width+j;positions[k]=d[0,0];var normal=Vec3.Cross(d[1,0],d[0,1]);normals[k]=normal.Length>1e-18?normal.Normalized():Vec3.Zero;}
            for(var i=0;i<segments;i++)for(var j=0;j<segments;j++){var a=(uint)(i*width+j);var b=a+(uint)width;indices[cursor++]=a;indices[cursor++]=b;indices[cursor++]=a+1;indices[cursor++]=a+1;indices[cursor++]=b;indices[cursor++]=b+1;}
            var sums=new Vec3[positions.Length];for(var i=0;i<indices.Length;i+=3){var a=(int)indices[i];var b=(int)indices[i+1];var c=(int)indices[i+2];var n=Vec3.Cross(positions[b]-positions[a],positions[c]-positions[a]);sums[a]+=n;sums[b]+=n;sums[c]+=n;}for(var i=0;i<normals.Length;i++)if(normals[i].Length==0&&sums[i].Length>0)normals[i]=sums[i].Normalized();
            var step=Math.Max(1,segments/5);for(var i=0;i<=segments;i++)for(var j=0;j<segments;j++){if(i%step==0||i==segments)Line(i*width+j,i*width+j+1);if(i%step==0||i==segments)Line(j*width+i,(j+1)*width+i);}
        }
        else throw new NotSupportedException($"No display tessellator registered for {geometry.GetType().Name}.");
        var vertices=new float[positions.Length*6];for(var i=0;i<positions.Length;i++){var p=positions[i]-origin;vertices[i*6]=(float)p.X;vertices[i*6+1]=(float)p.Y;vertices[i*6+2]=(float)p.Z;vertices[i*6+3]=(float)normals[i].X;vertices[i*6+4]=(float)normals[i].Y;vertices[i*6+5]=(float)normals[i].Z;}
        var edges=lineIndices.ToArray();var lines=EncodeLines(positions,edges,origin);
        if(vertices.Any(v=>!float.IsFinite(v)))throw new ArithmeticException("Display coordinates exceed Float32 range after rebasing.");
        return new(geometry.Id,positions,vertices,indices,lines,geometry.Bounds){LineIndices=edges};
    }
    private static float[] EncodeLines(Vec3[] positions,uint[] edges,Vec3 origin)
    {
        var lines=new float[edges.Length*3];
        for(var i=0;i<edges.Length;i++)
        {
            var p=positions[edges[i]]-origin;
            lines[i*3]=(float)p.X;lines[i*3+1]=(float)p.Y;lines[i*3+2]=(float)p.Z;
        }
        if(lines.Any(v=>!float.IsFinite(v)))throw new ArithmeticException("Display lines exceed Float32 range after rebasing.");
        return lines;
    }
    /// <summary>Re-encode relative floats from retained world-space positions;
    /// neither NURBS evaluation nor cumulative float translation is used.</summary>
    public static DisplayMesh Rebase(DisplayMesh mesh,Vec3 origin)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if(!origin.IsFinite)throw new ArgumentException("Invalid display origin.");
        if(mesh.LineIndices is null)throw new NotSupportedException("Mesh has no retained line connectivity.");
        var vertices=(float[])mesh.Vertices.Clone();
        for(var i=0;i<mesh.Positions.Length;i++)
        {
            var p=mesh.Positions[i]-origin;vertices[i*6]=(float)p.X;vertices[i*6+1]=(float)p.Y;vertices[i*6+2]=(float)p.Z;
        }
        if(vertices.Any(v=>!float.IsFinite(v)))throw new ArithmeticException("Display coordinates exceed Float32 range after rebasing.");
        return mesh with{Vertices=vertices,Lines=EncodeLines(mesh.Positions,mesh.LineIndices,origin)};
    }
    public static float[] Grid(Vec3 origin,string view,int extent=100,int spacing=5)
    {
        var output=new List<float>();Vec3 World(double x,double y)=>view switch{"Front"=>new(x,0,y),"Right"=>new(0,x,y),_=>new(x,y,0)};
        void Add(Vec3 p){p-=origin;output.AddRange([(float)p.X,(float)p.Y,(float)p.Z]);}
        for(var i=-extent;i<=extent;i+=spacing){Add(World(i,-extent));Add(World(i,extent));Add(World(-extent,i));Add(World(extent,i));}return output.ToArray();
    }
}
