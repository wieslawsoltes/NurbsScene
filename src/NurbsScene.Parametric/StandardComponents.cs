using System.Collections.Immutable;
using NurbsScene.Geometry;
using NurbsScene.Kernel;

namespace NurbsScene.Parametric;

/// <summary>Executable managed catalog. It is deliberately not advertised as the complete Grasshopper catalog.</summary>
public static class StandardComponents
{
    private static PortDefinition N(string name,double value=0)=>new(name,ValueKind.Number,Default:value);
    private static PortDefinition V(string name,Vec3 value=default)=>new(name,ValueKind.Vector,Default:value);
    private static PortDefinition P(string name,ValueKind kind=ValueKind.Any,PortAccess access=PortAccess.Item,object? value=null,bool optional=false)=>new(name,kind,access,value,optional);
    private static IReadOnlyDictionary<string,object?> Out(params (string Name,object? Value)[] values)=>values.ToDictionary(v=>v.Name,v=>v.Value);
    private static int Integer(double value,int min=0,int max=100000){if(!double.IsFinite(value)||value!=Math.Truncate(value)||value<min||value>max)throw new ArgumentOutOfRangeException(nameof(value));return(int)value;}
    public static ComponentRegistry Create(GeometryFactory kernel)
    {
        var registry=new ComponentRegistry();
        void Add(string id,string category,PortDefinition[] inputs,PortDefinition[] outputs,Func<ComponentContext,IReadOnlyDictionary<string,object?>> evaluate,string description="")=>registry.Register(new(id,category,description,inputs.ToImmutableArray(),outputs.ToImmutableArray(),c=>ValueTask.FromResult(evaluate(c))));
        Add("Number","Params",[],[P("N",ValueKind.Number)],c=>Out(("N",c.Parameter("value",1))),"Persistent binary64 number.");
        Add("Integer","Params",[],[P("N",ValueKind.Integer)],c=>Out(("N",Integer(c.Parameter("value",1),int.MinValue,int.MaxValue))));
        Add("Boolean","Params",[],[P("B",ValueKind.Boolean)],c=>Out(("B",c.Parameters.TryGetValue("value",out var v)&&v is true)));
        Add("Text","Params",[],[P("T",ValueKind.Text)],c=>Out(("T",c.Parameters.GetValueOrDefault("text")?.ToString()??"NurbsScene")));
        foreach(var(id,fn)in new Dictionary<string,Func<double,double,double>>{["Add"]=(a,b)=>a+b,["Subtract"]=(a,b)=>a-b,["Multiply"]=(a,b)=>a*b,["Divide"]=(a,b)=>b==0?throw new DivideByZeroException():a/b,["Power"]=Math.Pow,["Minimum"]=Math.Min,["Maximum"]=Math.Max})Add(id,"Maths",[N("A",1),N("B",1)],[P("R",ValueKind.Number)],c=>Out(("R",fn(c.Number("A"),c.Number("B")))));
        foreach(var(id,fn)in new Dictionary<string,Func<double,double>>{["Sine"]=Math.Sin,["Cosine"]=Math.Cos,["Tangent"]=Math.Tan,["ArcSine"]=Math.Asin,["ArcCosine"]=Math.Acos,["ArcTangent"]=Math.Atan,["Sqrt"]=Math.Sqrt,["Absolute"]=Math.Abs,["Floor"]=Math.Floor,["Ceiling"]=Math.Ceiling,["Round"]=x=>Math.Round(x,MidpointRounding.ToEven),["Exponential"]=Math.Exp,["Logarithm"]=Math.Log,["Degrees"]=x=>x*180/Math.PI,["Radians"]=x=>x*Math.PI/180,["Negate"]=x=>-x})Add(id,"Maths",[N("X")],[P("R",ValueKind.Number)],c=>Out(("R",fn(c.Number("X")))));
        Add("Clamp","Maths",[N("X"),N("Min"),N("Max",1)],[P("R",ValueKind.Number)],c=>Out(("R",Math.Clamp(c.Number("X"),c.Number("Min"),c.Number("Max")))));
        Add("Range","Sets",[N("Start"),N("End",1),N("N",10)],[P("R",ValueKind.Number,PortAccess.List)],c=>{var count=Integer(c.Number("N"),1);return Out(("R",Enumerable.Range(0,count+1).Select(i=>(object?)(c.Number("Start")+(c.Number("End")-c.Number("Start"))*i/count)).ToArray()));});
        Add("Series","Sets",[N("Start"),N("Step",1),N("Count",10)],[P("S",ValueKind.Number,PortAccess.List)],c=>Out(("S",Enumerable.Range(0,Integer(c.Number("Count"))).Select(i=>(object?)(c.Number("Start")+i*c.Number("Step"))).ToArray())));
        Add("ListLength","Sets",[P("L",access:PortAccess.List)],[P("N",ValueKind.Integer)],c=>Out(("N",c.Get<ImmutableArray<object?>>("L").Length)));
        Add("ListItem","Sets",[P("L",access:PortAccess.List),N("Index")],[P("Item")],c=>{var list=c.Get<ImmutableArray<object?>>("L");var index=Integer(c.Number("Index"),int.MinValue,int.MaxValue);if(list.Length==0)throw new ArgumentException("Empty list.");return Out(("Item",list[(index%list.Length+list.Length)%list.Length]));});
        Add("ReverseList","Sets",[P("L",access:PortAccess.List)],[P("R",access:PortAccess.List)],c=>Out(("R",c.Get<ImmutableArray<object?>>("L").Reverse().ToArray())));
        Add("SortNumbers","Sets",[P("L",ValueKind.Number,PortAccess.List)],[P("R",ValueKind.Number,PortAccess.List)],c=>Out(("R",c.Get<ImmutableArray<object?>>("L").OrderBy(value=>Convert.ToDouble(value)).ToArray())));
        foreach(var(id,fn)in new Dictionary<string,Func<DataTree,DataTree>>{["Flatten"]=t=>t.Flatten(),["Graft"]=t=>t.Graft(),["Simplify"]=t=>t.Simplify()})Add(id,"Sets",[P("T",access:PortAccess.Tree)],[P("R",access:PortAccess.Tree)],c=>Out(("R",fn(c.Get<DataTree>("T")))));
        Add("Merge","Sets",[P("A",access:PortAccess.Tree,optional:true),P("B",access:PortAccess.Tree,optional:true)],[P("R",access:PortAccess.Tree)],c=>Out(("R",c.Get<DataTree>("A").Merge(c.Get<DataTree>("B")))));
        Add("Partition","Sets",[P("T",access:PortAccess.Tree),N("Size",3)],[P("R",access:PortAccess.Tree)],c=>Out(("R",c.Get<DataTree>("T").Partition(Integer(c.Number("Size"),1)))));
        Add("TreeStatistics","Sets",[P("T",access:PortAccess.Tree)],[P("Branches",ValueKind.Integer),P("Items",ValueKind.Integer)],c=>Out(("Branches",c.Get<DataTree>("T").Branches.Length),("Items",c.Get<DataTree>("T").ItemCount)));
        Add("ConstructPoint","Vector",[N("X"),N("Y"),N("Z")],[P("P",ValueKind.Point)],c=>Out(("P",new Vec3(c.Number("X"),c.Number("Y"),c.Number("Z")))));
        Add("ConstructVector","Vector",[N("X"),N("Y"),N("Z",1)],[P("V",ValueKind.Vector)],c=>Out(("V",new Vec3(c.Number("X"),c.Number("Y"),c.Number("Z")))));
        Add("DeconstructPoint","Vector",[V("P")],[P("X",ValueKind.Number),P("Y",ValueKind.Number),P("Z",ValueKind.Number)],c=>{var p=c.Get<Vec3>("P");return Out(("X",p.X),("Y",p.Y),("Z",p.Z));});
        foreach(var(id,axis)in new[]{("UnitX",Vec3.UnitX),("UnitY",Vec3.UnitY),("UnitZ",Vec3.UnitZ)})Add(id,"Vector",[N("Length",1)],[P("V",ValueKind.Vector)],c=>Out(("V",axis*c.Number("Length"))));
        Add("VectorBetween","Vector",[V("A"),V("B",Vec3.UnitX)],[P("V",ValueKind.Vector)],c=>Out(("V",c.Get<Vec3>("B")-c.Get<Vec3>("A"))));
        Add("VectorLength","Vector",[V("V",Vec3.UnitZ)],[P("L",ValueKind.Number)],c=>Out(("L",c.Get<Vec3>("V").Length)));
        Add("Unitize","Vector",[V("V",Vec3.UnitZ)],[P("V",ValueKind.Vector)],c=>Out(("V",c.Get<Vec3>("V").Normalized())));
        Add("Amplitude","Vector",[V("V",Vec3.UnitZ),N("Length",1)],[P("V",ValueKind.Vector)],c=>Out(("V",c.Get<Vec3>("V").Normalized()*c.Number("Length"))));
        Add("DotProduct","Vector",[V("A",Vec3.UnitX),V("B",Vec3.UnitY)],[P("D",ValueKind.Number)],c=>Out(("D",Vec3.Dot(c.Get<Vec3>("A"),c.Get<Vec3>("B")))));
        Add("CrossProduct","Vector",[V("A",Vec3.UnitX),V("B",Vec3.UnitY)],[P("V",ValueKind.Vector)],c=>Out(("V",Vec3.Cross(c.Get<Vec3>("A"),c.Get<Vec3>("B")))));
        Add("Distance","Vector",[V("A"),V("B",Vec3.UnitX)],[P("D",ValueKind.Number)],c=>Out(("D",(c.Get<Vec3>("B")-c.Get<Vec3>("A")).Length)));
        Add("Line","Curve",[V("A"),V("B",new(10,0,0))],[P("C",ValueKind.Curve)],c=>Out(("C",kernel.Line(c.Get<Vec3>("A"),c.Get<Vec3>("B")))));
        Add("Circle","Curve",[V("Center"),N("Radius",10)],[P("C",ValueKind.Curve)],c=>Out(("C",kernel.Circle(c.Get<Vec3>("Center"),c.Number("Radius")))));
        Add("Arc","Curve",[V("Center"),N("Radius",10),N("Start"),N("Sweep",Math.PI)],[P("C",ValueKind.Curve)],c=>Out(("C",kernel.Arc(c.Get<Vec3>("Center"),c.Number("Radius"),c.Number("Start"),c.Number("Sweep")))));
        Add("Polyline","Curve",[P("Points",ValueKind.Point,PortAccess.List)],[P("C",ValueKind.Curve)],c=>Out(("C",kernel.Polyline(c.Get<ImmutableArray<object?>>("Points").Cast<Vec3>()))));
        Add("Interpolate","Curve",[P("Points",ValueKind.Point,PortAccess.List),N("Degree",3)],[P("C",ValueKind.Curve)],c=>Out(("C",kernel.Interpolate(c.Get<ImmutableArray<object?>>("Points").Cast<Vec3>().ToArray(),Integer(c.Number("Degree"),1,128)))));
        Add("EvaluateCurve","Curve",[P("C",ValueKind.Curve),N("T",.5)],[P("P",ValueKind.Point),P("T",ValueKind.Vector),P("K",ValueKind.Number)],c=>{var curve=c.Get<NurbsCurve>("C");var t=c.Number("T");return Out(("P",curve.PointAt(t)),("T",curve.TangentAt(t)),("K",curve.CurvatureAt(t)));});
        Add("DivideParameter","Curve",[P("C",ValueKind.Curve),N("N",12)],[P("P",ValueKind.Point,PortAccess.List)],c=>{var curve=c.Get<NurbsCurve>("C");var count=Integer(c.Number("N"),1);return Out(("P",Enumerable.Range(0,count+1).Select(i=>(object?)curve.PointAt(curve.Domain.At(i/(double)count))).ToArray()));},"Uniform parameter samples; not equal arc-length division.");
        Add("ReverseCurve","Curve",[P("C",ValueKind.Curve)],[P("C",ValueKind.Curve)],c=>Out(("C",c.Get<NurbsCurve>("C").Reverse())));
        Add("InsertKnot","Curve",[P("C",ValueKind.Curve),N("T",.5),N("Count",1)],[P("C",ValueKind.Curve)],c=>Out(("C",c.Get<NurbsCurve>("C").InsertKnot(c.Number("T"),Integer(c.Number("Count"),0,128)))));
        Add("ElevateDegree","Curve",[P("C",ValueKind.Curve),N("Count",1)],[P("C",ValueKind.Curve)],c=>Out(("C",c.Get<NurbsCurve>("C").ElevateDegree(Integer(c.Number("Count"),0,127)))));
        Add("SplitCurve","Curve",[P("C",ValueKind.Curve),N("T",.5)],[P("A",ValueKind.Curve),P("B",ValueKind.Curve)],c=>{var(a,b)=c.Get<NurbsCurve>("C").Split(c.Number("T"));return Out(("A",a),("B",b));});
        Add("SubCurve","Curve",[P("C",ValueKind.Curve),N("Start",.2),N("End",.8)],[P("C",ValueKind.Curve)],c=>Out(("C",c.Get<NurbsCurve>("C").Trim(new(c.Number("Start"),c.Number("End"))))));
        Add("CurveControlPoints","Curve",[P("C",ValueKind.Curve)],[P("P",ValueKind.Point,PortAccess.List)],c=>Out(("P",c.Get<NurbsCurve>("C").ControlPoints.ToArray().Select(p=>(object?)p.Position).ToArray())));
        Add("Extrude","Surface",[P("C",ValueKind.Curve),V("V",new(0,0,10))],[P("S",ValueKind.Surface)],c=>Out(("S",kernel.Extrude(c.Get<NurbsCurve>("C"),c.Get<Vec3>("V")))));
        Add("Revolve","Surface",[P("C",ValueKind.Curve),V("Origin"),V("Axis",Vec3.UnitZ),N("Sweep",2*Math.PI)],[P("S",ValueKind.Surface)],c=>Out(("S",kernel.Revolve(c.Get<NurbsCurve>("C"),c.Get<Vec3>("Origin"),c.Get<Vec3>("Axis"),c.Number("Sweep")))));
        Add("Loft","Surface",[P("Curves",ValueKind.Curve,PortAccess.List),N("Degree",3)],[P("S",ValueKind.Surface)],c=>Out(("S",kernel.Loft(c.Get<ImmutableArray<object?>>("Curves").Cast<NurbsCurve>().ToArray(),Integer(c.Number("Degree"),1,128)))));
        Add("SaddleSurface","Surface",[N("Width",60),N("Depth",42),N("Height",12)],[P("S",ValueKind.Surface)],c=>Out(("S",kernel.Saddle(c.Number("Width"),c.Number("Depth"),c.Number("Height")))),"Exact bicubic demonstration surface.");
        Add("SurfaceFromCorners","Surface",[V("A",new(-10,-10,0)),V("B",new(10,-10,0)),V("C",new(10,10,0)),V("D",new(-10,10,0))],[P("S",ValueKind.Surface)],c=>Out(("S",kernel.FromCorners(c.Get<Vec3>("A"),c.Get<Vec3>("B"),c.Get<Vec3>("C"),c.Get<Vec3>("D")))));
        Add("IsoCurve","Surface",[P("S",ValueKind.Surface),N("T",.5)],[P("C",ValueKind.Curve)],c=>Out(("C",kernel.IsoCurve(c.Get<NurbsSurface>("S"),c.Number("T"),c.Parameters.GetValueOrDefault("direction")?.ToString()!="v"))));
        Add("EvaluateSurface","Surface",[P("S",ValueKind.Surface),N("U",.5),N("V",.5)],[P("P",ValueKind.Point),P("N",ValueKind.Vector),P("Gaussian",ValueKind.Number),P("Mean",ValueKind.Number)],c=>{var s=c.Get<NurbsSurface>("S");var u=c.Number("U");var v=c.Number("V");var curvature=s.CurvatureAt(u,v);return Out(("P",s.PointAt(u,v)),("N",s.NormalAt(u,v)),("Gaussian",curvature.Gaussian),("Mean",curvature.Mean));});
        Add("TransposeSurface","Surface",[P("S",ValueKind.Surface)],[P("S",ValueKind.Surface)],c=>Out(("S",c.Get<NurbsSurface>("S").Transpose())));
        Add("Move","Transform",[P("G",ValueKind.Geometry),V("V",new(10,0,0))],[P("G",ValueKind.Geometry)],c=>Out(("G",c.Get<IGeometry>("G").Transform(Transform3.Translation(c.Get<Vec3>("V"))))));
        Add("Rotate","Transform",[P("G",ValueKind.Geometry),V("Axis",Vec3.UnitZ),N("Angle",Math.PI/4)],[P("G",ValueKind.Geometry)],c=>Out(("G",c.Get<IGeometry>("G").Transform(Transform3.Rotation(c.Get<Vec3>("Axis"),c.Number("Angle"))))));
        Add("Scale","Transform",[P("G",ValueKind.Geometry),N("Factor",2)],[P("G",ValueKind.Geometry)],c=>{var f=c.Number("Factor");if(f==0)throw new ArgumentException("Scale cannot be zero.");return Out(("G",c.Get<IGeometry>("G").Transform(Transform3.Scale(new(f,f,f)))));});
        Add("BoundingBox","Display",[P("G",ValueKind.Geometry)],[P("Min",ValueKind.Point),P("Max",ValueKind.Point),P("Center",ValueKind.Point)],c=>{var b=c.Get<IGeometry>("G").Bounds;return Out(("Min",b.Min),("Max",b.Max),("Center",b.Center));});
        Add("Preview","Display",[P("G",access:PortAccess.Tree)],[P("G",access:PortAccess.Tree)],c=>Out(("G",c.Get<DataTree>("G"))));
        GeometryComponents.Register(registry);
        return registry;
    }
}
