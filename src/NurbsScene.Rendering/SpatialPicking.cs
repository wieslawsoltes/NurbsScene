using System.Numerics;
using System.Runtime.CompilerServices;
using NurbsScene.Geometry;

namespace NurbsScene.Rendering;

public readonly record struct PickStatistics(int BoxTests,int TriangleTests,int CurveSegmentTests,int BuiltTriangleIndices);
public readonly record struct TriangleHit(int Triangle,Vec3 Point,double Distance);

/// <summary>Immutable balanced hierarchy over display triangles, reusable across
/// cameras and origin rebases. Source arrays must not be mutated after construction.
/// Hits describe display triangles, NOT exact NURBS surface intersections.</summary>
public sealed class TrianglePickIndex
{
    private readonly Vec3[] _positions;
    private readonly uint[] _indices;
    internal BoundsHierarchy Tree { get; }
    public int TriangleCount=>_indices.Length/3;
    public TrianglePickIndex(Vec3[] positions,uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(positions);ArgumentNullException.ThrowIfNull(indices);
        if(indices.Length%3!=0 || indices.Length>3_000_000)throw new ArgumentException("Invalid triangle count.");
        if(positions.Any(p=>!p.IsFinite)||indices.Any(i=>i>=positions.Length))throw new ArgumentException("Invalid triangle vertices.");
        _positions=positions;_indices=indices;var bounds=new Bounds3[TriangleCount];
        for(var t=0;t<bounds.Length;t++)bounds[t]=Bounds3.Empty.Include(positions[indices[t*3]]).Include(positions[indices[t*3+1]]).Include(positions[indices[t*3+2]]);
        Tree=new(bounds);
    }
    public TriangleHit? Intersect(Ray3 ray,out PickStatistics statistics,(Vec3 Normal,double Offset)? clipPlane=null)
    {
        MeshPicker.ValidateRay(ray);ray=ray with{Direction=ray.Direction.Normalized()};
        int boxes=0,triangles=0;var hit=Trace(ray,double.PositiveInfinity,clipPlane,ref boxes,ref triangles);
        statistics=new(boxes,triangles,0,0);return hit;
    }
    internal TriangleHit? Trace(Ray3 ray,double maximum,(Vec3 Normal,double Offset)? clip,ref int boxes,ref int triangles)
    {
        if(Tree.IsEmpty)return null;
        TriangleHit? result=null;Span<int> pending=stackalloc int[64];var top=0;pending[top++]=0;
        while(top>0)
        {
            var node=Tree.Nodes[pending[--top]];boxes++;
            if(!BoundsHierarchy.Intersects(node.Bounds,ray,maximum))continue;
            if(node.Count==0){pending[top++]=node.Right;pending[top++]=node.Left;continue;}
            for(var i=0;i<node.Count;i++)
            {
                var index=Tree.Order[node.Start+i];triangles++;
                var a=_positions[_indices[index*3]];var b=_positions[_indices[index*3+1]];var c=_positions[_indices[index*3+2]];
                if(!TriangleDistance(ray,a,b,c,out var distance)||distance>maximum)continue;
                if(result is{} previous&&distance==maximum&&index>=previous.Triangle)continue;
                var point=ray.Origin+ray.Direction*distance;
                if(clip is{} plane&&Vec3.Dot(plane.Normal,point)+plane.Offset<0)continue;
                maximum=distance;result=new(index,point,distance);
            }
        }
        return result;
    }
    internal static bool TriangleDistance(Ray3 ray,Vec3 a,Vec3 b,Vec3 c,out double distance)
    {
        distance=0;var ab=b-a;var ac=c-a;var p=Vec3.Cross(ray.Direction,ac);var det=Vec3.Dot(ab,p);
        var scale=ab.Length*ac.Length;
        if(!double.IsFinite(det)||!double.IsFinite(scale)||scale==0||Math.Abs(det)<=1e-14*scale)return false;
        var t=ray.Origin-a;var u=Vec3.Dot(t,p)/det;if(u<0||u>1)return false;
        var q=Vec3.Cross(t,ab);var v=Vec3.Dot(ray.Direction,q)/det;if(v<0||u+v>1)return false;
        distance=Vec3.Dot(ac,q)/det;return double.IsFinite(distance)&&distance>=0;
    }
}

// Balanced median splits guarantee depth < 32 at the one-million primitive
// budget. Traversal's stack capacity is therefore established, not assumed.
internal sealed class BoundsHierarchy
{
    internal readonly record struct Node(Bounds3 Bounds,int Left,int Right,int Start,int Count);
    public Node[] Nodes {get;}
    public int[] Order {get;}
    public bool IsEmpty=>Order.Length==0;
    public BoundsHierarchy(Bounds3[] bounds)
    {
        if(bounds.Length>1_000_000)throw new ArgumentException("Spatial index primitive budget exceeded.");
        Order=Enumerable.Range(0,bounds.Length).ToArray();
        if(bounds.Length==0){Nodes=[];return;}
        var leaves=BitOperations.RoundUpToPowerOf2((uint)((bounds.Length+7)/8));Nodes=new Node[2*leaves-1];
        var comparers=Enumerable.Range(0,3).Select(axis=>Comparer<int>.Create((a,b)=>{
            var c=Coordinate(bounds[a].Center,axis).CompareTo(Coordinate(bounds[b].Center,axis));return c==0?a.CompareTo(b):c;
        })).ToArray();
        var next=0;
        int Build(int start,int count)
        {
            var id=next++;var box=Bounds3.Empty;var centroids=Bounds3.Empty;
            for(var i=start;i<start+count;i++){box=box.Union(bounds[Order[i]]);centroids=centroids.Include(bounds[Order[i]].Center);}
            if(count<=8){Nodes[id]=new(box,0,0,start,count);return id;}
            var size=centroids.Size;var axis=size.X>=size.Y&&size.X>=size.Z?0:size.Y>=size.Z?1:2;
            Array.Sort(Order,start,count,comparers[axis]);var half=count/2;
            var left=Build(start,half);var right=Build(start+half,count-half);Nodes[id]=new(box,left,right,0,0);return id;
        }
        Build(0,bounds.Length);
    }
    private static double Coordinate(Vec3 p,int axis)=>axis==0?p.X:axis==1?p.Y:p.Z;
    public static bool Intersects(Bounds3 box,Ray3 ray,double maximum)
    {
        if(box.IsEmpty)return false;
        double enter=0,exit=maximum;
        for(var axis=0;axis<3;axis++)
        {
            var origin=Coordinate(ray.Origin,axis);var direction=Coordinate(ray.Direction,axis);
            var lo=Coordinate(box.Min,axis);var hi=Coordinate(box.Max,axis);
            // Conservative padding for large-coordinate slab arithmetic. Degenerate
            // planar boxes and signed-zero directions are handled explicitly.
            var error=16*2.2204460492503131e-16*Math.Max(1,Math.Max(Math.Abs(origin),Math.Max(Math.Abs(lo),Math.Abs(hi))));
            lo-=error;hi+=error;
            if(direction==0){if(origin<lo||origin>hi)return false;continue;}
            var a=(lo-origin)/direction;var b=(hi-origin)/direction;if(a>b)(a,b)=(b,a);
            enter=Math.Max(enter,a);exit=Math.Min(exit,b);if(enter>exit)return false;
        }
        return true;
    }
}

/// <summary>Two-level picking: scene boxes then per-geometry triangle hierarchies.
/// Curve selection remains a screen-space operation, with perspective-correct
/// segment parameters and clip-plane rejection. Statistics exclude index building.</summary>
public static class MeshPicker
{
    private static readonly ConditionalWeakTable<DisplayScene,SceneIndex> Scenes=new();
    private sealed class SceneIndex
    {
        private long _revision=-1;
        private (string Id,IGeometry Geometry)[] _keys=[];
        public BoundsHierarchy Tree {get;private set;}=new([]);
        public int[] Surfaces {get;private set;}=[];
        public int[] Curves {get;private set;}=[];
        public void Update(DisplayScene scene)
        {
            if(_revision==scene.Revision)return;
            var same=_keys.Length==scene.Items.Count;
            if(same)for(var i=0;i<_keys.Length;i++)if(_keys[i].Id!=scene.Items[i].Id||!ReferenceEquals(_keys[i].Geometry,scene.Items[i].Geometry)){same=false;break;}
            if(!same)
            {
                _keys=scene.Items.Select(i=>(i.Id,i.Geometry)).ToArray();
                Surfaces=Enumerable.Range(0,_keys.Length).Where(i=>scene.Meshes[_keys[i].Geometry.Id].Indices.Length!=0).ToArray();
                Curves=Enumerable.Range(0,_keys.Length).Where(i=>scene.Meshes[_keys[i].Geometry.Id].Indices.Length==0).ToArray();
                Tree=new(Surfaces.Select(i=>_keys[i].Geometry.Bounds).ToArray());
            }
            _revision=scene.Revision;
        }
    }
    internal static void ValidateRay(Ray3 ray)
    {
        if(!ray.Origin.IsFinite||!ray.Direction.IsFinite||ray.Direction.Length==0)throw new ArgumentException("A finite nonzero ray is required.");
    }
    public static PickResult? Pick(DisplayScene scene,ViewportRectangle viewport,double x,double y)
        =>Pick(scene,viewport,x,y,out _);
    public static PickResult? Pick(DisplayScene scene,ViewportRectangle viewport,double x,double y,out PickStatistics statistics,double pixelTolerance=7)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if(viewport.Width<=0||viewport.Height<=0||!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(pixelTolerance)||pixelTolerance<0||pixelTolerance>1000)
            throw new ArgumentOutOfRangeException(nameof(pixelTolerance),"Invalid viewport or picking tolerance.");
        var ray=viewport.Camera.Ray(x,y,viewport.Width,viewport.Height);
        var best=PickRay(scene,ray,out statistics);var curveTests=0;
        var index=Scenes.GetValue(scene,_=>new());index.Update(scene);
        var frame=new Projection(viewport.Camera,viewport.Width,viewport.Height);
        foreach(var itemIndex in index.Curves)
        {
            var item=scene.Items[itemIndex];if(item.Locked||!frame.NearBox(item.Geometry.Bounds,x,y,pixelTolerance))continue;
            var positions=scene.Meshes[item.Geometry.Id].Positions;
            for(var i=1;i<positions.Length;i++)
            {
                curveTests++;var a=frame.Project(positions[i-1]);var b=frame.Project(positions[i]);
                if(a.Depth<=0||b.Depth<=0)continue;
                var dx=b.X-a.X;var dy=b.Y-a.Y;var length=dx*dx+dy*dy;
                var t=length==0?0:Math.Clamp(((x-a.X)*dx+(y-a.Y)*dy)/length,0,1);
                var ex=x-a.X-t*dx;var ey=y-a.Y-t*dy;if(ex*ex+ey*ey>pixelTolerance*pixelTolerance)continue;
                if(frame.Perspective)t=t*a.Depth/((1-t)*b.Depth+t*a.Depth);
                var point=Vec3.Lerp(positions[i-1],positions[i],t);
                if(scene.ClipPlane is{} clip&&Vec3.Dot(clip.Normal,point)+clip.Offset<0)continue;
                var distance=(point-ray.Origin).Length;
                if(best is null||distance<best.Distance)best=new(item.Id,point,distance);
            }
        }
        statistics=statistics with{CurveSegmentTests=curveTests};return best;
    }
    /// <summary>Ray picking is triangle-only. Use Pick for screen-tolerance curves.</summary>
    public static PickResult? PickRay(DisplayScene scene,Ray3 ray,out PickStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(scene);ValidateRay(ray);ray=ray with{Direction=ray.Direction.Normalized()};
        var index=Scenes.GetValue(scene,_=>new());index.Update(scene);
        int boxes=0,triangles=0,created=0;PickResult? best=null;var bestItem=int.MaxValue;
        if(!index.Tree.IsEmpty)
        {
            Span<int> pending=stackalloc int[64];var top=0;pending[top++]=0;
            while(top>0)
            {
                var node=index.Tree.Nodes[pending[--top]];boxes++;
                if(!BoundsHierarchy.Intersects(node.Bounds,ray,best?.Distance??double.PositiveInfinity))continue;
                if(node.Count==0){pending[top++]=node.Right;pending[top++]=node.Left;continue;}
                for(var j=0;j<node.Count;j++)
                {
                    var itemIndex=index.Surfaces[index.Tree.Order[node.Start+j]];var item=scene.Items[itemIndex];if(item.Locked)continue;
                    boxes++;if(!BoundsHierarchy.Intersects(item.Geometry.Bounds,ray,best?.Distance??double.PositiveInfinity))continue;
                    var mesh=scene.Meshes[item.Geometry.Id];var tree=mesh.GetPickingIndex(out var built);if(built)created++;
                    var hit=tree.Trace(ray,best?.Distance??double.PositiveInfinity,scene.ClipPlane,ref boxes,ref triangles);
                    if(hit is{} h&&(best is null||h.Distance<best.Distance||h.Distance==best.Distance&&itemIndex<bestItem))
                    {best=new(item.Id,h.Point,h.Distance);bestItem=itemIndex;}
                }
            }
        }
        statistics=new(boxes,triangles,0,created);return best;
    }
    private readonly struct Projection
    {
        private readonly Vec3 _eye,_forward,_right,_up;
        private readonly double _height,_width,_scale,_tangent;
        public bool Perspective {get;}
        public Projection(Camera camera,int width,int height)
        {
            (_eye,_forward,_right,_up)=camera.Basis();_width=width;_height=height;_scale=camera.Scale;_tangent=2*Math.Tan(camera.FieldOfView/2);Perspective=camera.Perspective;
        }
        public (double X,double Y,double Depth) Project(Vec3 p)
        {
            var d=p-_eye;var depth=Vec3.Dot(d,_forward);var h=Perspective?depth*_tangent:_scale;
            return((Vec3.Dot(d,_right)/(h*_width/_height)+.5)*_width,(.5-Vec3.Dot(d,_up)/h)*_height,depth);
        }
        public bool NearBox(Bounds3 bounds,double x,double y,double tolerance)
        {
            double minX=double.PositiveInfinity,minY=minX,maxX=double.NegativeInfinity,maxY=maxX;
            for(var i=0;i<8;i++)
            {
                var p=Project(new((i&1)==0?bounds.Min.X:bounds.Max.X,(i&2)==0?bounds.Min.Y:bounds.Max.Y,(i&4)==0?bounds.Min.Z:bounds.Max.Z));
                if(p.Depth<=0)return true; // Near-plane crossing is not safely screen-cullable.
                minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minY=Math.Min(minY,p.Y);maxY=Math.Max(maxY,p.Y);
            }
            return x>=minX-tolerance&&x<=maxX+tolerance&&y>=minY-tolerance&&y<=maxY+tolerance;
        }
    }
}
