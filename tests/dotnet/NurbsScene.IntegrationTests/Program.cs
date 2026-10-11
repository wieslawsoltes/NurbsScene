using System.Text.Json;
using System.Buffers.Binary;
using System.Collections.Immutable;
using NurbsScene.Geometry;
using NurbsScene.IO;
using NurbsScene.Kernel;
using NurbsScene.Model;
using NurbsScene.Parametric;
using NurbsScene.Rendering;

// Does not claim to test GL without a context. This suite covers managed interchange,
// authoritative document geometry and CPU-side display preparation.
var factory = new GeometryFactory(new ManagedNurbsEvaluator());
var registry=StandardComponents.Create(factory);
var document=new SceneDocument{Name="Integration fixture"};
var curve=factory.Circle(new(1,2,3),4);var surface=factory.Extrude(curve,new(0,0,8));
document.Add(curve,"Exact circle");document.Add(surface,"Exact extrusion");
var graph=new Graph(registry);var integer=graph.Add("Integer",parameters:new Dictionary<string,object?>{{"value",4}});
var circleNode=graph.Add("Circle");graph.Connect(new(integer.Id,"N"),new(circleNode.Id,"Radius"));
var result=await graph.SolveAsync();
void Check(bool condition,string message){if(!condition)throw new Exception(message);}
Check(result.ErrorCount==0,"Initial graph errors");
var json=WorkspaceFormat.Serialize(document,graph);
var decoded=WorkspaceFormat.Deserialize(factory.Evaluator,registry,json);
Check(decoded.Document.Objects.Count==2,"Object count changed on round trip");
Check(decoded.Graph.GetNode(integer.Id).Parameters["value"] is int,"Integer parameter lost its type");
Check((await decoded.Graph.SolveAsync()).ErrorCount==0,"Round-tripped graph errors");
var exact=(NurbsCurve)decoded.Document.Objects.First().Geometry;
for(var i=0;i<=100;i++)Check((exact.PointAt(i/100d)-curve.PointAt(i/100d)).Length<1e-10,"Exact geometry changed");
var scene=new DisplayScene();scene.SetItems(document.Objects.Select(o=>new DisplayItem(o.Id,o.Geometry,new(.3f,.5f,.7f))));
Check(scene.Meshes.Count==2,"Display mesh count");Check(scene.Meshes[surface.Id].Indices.Length>0,"Surface wasn't triangulated");
scene.Rebase(new(1_000_000,2_000_000,3_000_000));
Check(scene.Origin.X==1_000_000,"Rebase failed");
var obj=WorkspaceFormat.ExportObj(document.Objects);
Check(obj.Contains("\nf ")&&obj.Contains("\nl "),"OBJ lost surfaces or curves");
var stl=WorkspaceFormat.ExportStl(document.Objects);
Check(stl.Length>84&&(stl.Length-84)%50==0,"STL binary record size");
var count=document.Objects.Count;
try {WorkspaceFormat.Deserialize(factory.Evaluator,registry,"{\"format\":\"3dm\"}");throw new Exception("Invalid format accepted");}
catch(Exception error) when(error.Message!="Invalid format accepted"){}
Check(document.Objects.Count==count,"Invalid import mutated the original document");
Check(BinaryPrimitives.ReadUInt32LittleEndian(stl.AsSpan(80,4))==(uint)((stl.Length-84)/50),"STL count does not match records");
Check(stl.Take(80).Any(b=>b!=0),"STL header is empty");
var emptyStl=WorkspaceFormat.ExportStl([]);
Check(emptyStl.Length==84&&BinaryPrimitives.ReadUInt32LittleEndian(emptyStl.AsSpan(80))==0,"Empty STL header contract");
var nested=new Dictionary<string,object?>{{"axis",new Vec3(1,2,3)},{"values",new object?[]{1,2d,"three"}},{"nested",new Dictionary<string,object?>{{"enabled",true}}}};
graph.Update(integer with{Parameters=integer.Parameters.SetItem("settings",ParameterValues.FreezeValue(nested))});
var nestedRoundTrip=WorkspaceFormat.Deserialize(factory.Evaluator,registry,WorkspaceFormat.Serialize(document,graph));
var settings=(ImmutableDictionary<string,object?>)nestedRoundTrip.Graph.GetNode(integer.Id).Parameters["settings"]!;
Check((Vec3)settings["axis"]! == new Vec3(1,2,3),"Nested vector lost on round trip");
Check(((ImmutableArray<object?>)settings["values"]!).Length==3,"Nested list lost on round trip");
Check(((ImmutableDictionary<string,object?>)settings["nested"]!)["enabled"] is true,"Nested mapping lost on round trip");
Console.WriteLine(JsonSerializer.Serialize(new{suite="managed-integration",status="passed",graphDefinitions=registry.Definitions.Count,objects=decoded.Document.Objects.Count,triangles=scene.Meshes[surface.Id].Indices.Length/3,gpu="not exercised by this suite"}));
