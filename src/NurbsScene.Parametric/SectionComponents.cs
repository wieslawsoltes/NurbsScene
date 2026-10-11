using System.Collections.Immutable;
using NurbsScene.Geometry;
using NurbsScene.Kernel;

namespace NurbsScene.Parametric;

public static class SectionComponents
{
    public static void Register(ComponentRegistry registry)
    {
        PortDefinition C(string name="C",PortAccess access=PortAccess.Item)=>new(name,ValueKind.Curve,access);
        PortDefinition N(string name,PortAccess access=PortAccess.Item,double value=0)=>new(name,ValueKind.Number,access,Default:value);
        PortDefinition[] PlaneInputs()=>[C(),new("Origin",ValueKind.Point,Default:Vec3.Zero),new("Normal",ValueKind.Vector,Default:Vec3.UnitZ),N("Tolerance",value:1e-7)];
        Plane3 Plane(ComponentContext c)=>new(c.Get<Vec3>("Origin"),c.Get<Vec3>("Normal"));
        void Add(string id,PortDefinition[] inputs,PortDefinition[] outputs,Func<ComponentContext,IReadOnlyDictionary<string,object?>> run,string description)
            =>registry.Register(new(id,"Curve",description,inputs.ToImmutableArray(),outputs.ToImmutableArray(),c=>{c.CancellationToken.ThrowIfCancellationRequested();return ValueTask.FromResult(run(c));}));
        Add("CurvePlaneSection",PlaneInputs(),[new("P",ValueKind.Point,PortAccess.List),N("T",PortAccess.List),C("Overlaps",PortAccess.List)],c=>{
            var curve=c.Get<NurbsCurve>("C");var result=curve.IntersectPlane(Plane(c),new(c.Number("Tolerance")),c.CancellationToken);
            if(!result.Complete)throw new ArithmeticException("Curve-plane search has unresolved intervals.");
            return new Dictionary<string,object?>{{"P",result.Points.Select(p=>p.Point).ToArray()},{"T",result.Points.Select(p=>p.Parameter).ToArray()},{"Overlaps",result.Overlaps.Select(curve.Trim).ToArray()}};
        },"Numerical sections of exact rational curves. Coplanar spans are returned as overlap curves; unresolved roots are errors.");
        Add("ShatterCurve",[C(),new("T",ValueKind.Number,PortAccess.List,Default:new[]{.5})],[C(access:PortAccess.List)],c=>
            new Dictionary<string,object?>{{"C",c.Get<NurbsCurve>("C").SplitMany(c.Get<ImmutableArray<object?>>("T").Select(Convert.ToDouble),c.CancellationToken)}},
            "Balanced multi-parameter splitting on the active knot domain; exact repeated parameters are deduplicated.");
    }
}
