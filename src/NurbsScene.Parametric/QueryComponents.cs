using System.Collections.Immutable;
using NurbsScene.Geometry;
using NurbsScene.Kernel;

namespace NurbsScene.Parametric;

public static class QueryComponents
{
    public static void Register(ComponentRegistry registry)
    {
        void Add(string id,string category,PortDefinition[] inputs,PortDefinition[] outputs,Func<ComponentContext,IReadOnlyDictionary<string,object?>> execute,string help)
            =>registry.Register(new(id,category,help,inputs.ToImmutableArray(),outputs.ToImmutableArray(),c=>{c.CancellationToken.ThrowIfCancellationRequested();return ValueTask.FromResult(execute(c));}));
        PortDefinition Number(string name,double value=0)=>new(name,ValueKind.Number,Default:value);
        PortDefinition Point(string name,Vec3 value=default)=>new(name,ValueKind.Point,Default:value);
        IReadOnlyDictionary<string,object?> Output(params(string Key,object? Value)[] items)=>items.ToDictionary(p=>p.Key,p=>p.Value);
        var curve=new PortDefinition("C",ValueKind.Curve);
        Add("CurveClosestPoint","Curve",[curve,Point("P"),Number("Tolerance",1e-7)],
            [Point("P"),Number("T"),Number("D"),Number("Gap")],c=>{
                var result=c.Get<NurbsCurve>("C").ClosestPoint(c.Get<Vec3>("P"),new(c.Number("Tolerance")),c.CancellationToken);
                if(!result.Converged)throw new ArithmeticException($"Closest-point tolerance not met; remaining distance gap {result.DistanceGap:G6}.");
                return Output(("P",result.Point),("T",result.Parameter),("D",result.Distance),("Gap",result.DistanceGap));
            },"Global control-hull subdivision on exact NURBS. Gap is a numerical distance estimate, not a parameter bound or certificate.");
        Add("CurveControlPoint","Curve",[curve,Number("I"),Point("P"),new("W",ValueKind.Number,Optional:true)],[curve],c=>{
            var index=c.Number("I");if(index<0||index>int.MaxValue||index!=Math.Truncate(index))throw new ArgumentException("A nonnegative integer control index is required.");
            return Output(("C",c.Get<NurbsCurve>("C").WithControlPoint((int)index,c.Get<Vec3>("P"),c.Inputs["W"] is null?null:c.Number("W"))));
        },"Edit an immutable rational curve control, preserving the original weight when W is omitted.");
        Add("CurveEndPoints","Curve",[curve],[Point("A"),Point("B")],c=>{
            var source=c.Get<NurbsCurve>("C");return Output(("A",source.PointAt(source.Domain.Min)),("B",source.PointAt(source.Domain.Max)));
        },"Evaluate exact endpoints using the actual active knot domain, including unclamped curves.");
        foreach(var reflect in new[]{false,true})
            Add(reflect?"MirrorInPlane":"ProjectToPlane","Transform",[new("G",ValueKind.Geometry),Point("Origin"),new("Normal",ValueKind.Vector,Default:Vec3.UnitZ)],
                [new("G",ValueKind.Geometry)],c=>{
                    var plane=new Plane3(c.Get<Vec3>("Origin"),c.Get<Vec3>("Normal"));
                    return Output(("G",c.Get<IGeometry>("G").Transform(reflect?plane.Reflection:plane.Projection)));
                },reflect?"Affine reflection of authoritative controls in a plane.":"Orthogonal affine projection of authoritative controls onto a plane.");
    }
}
