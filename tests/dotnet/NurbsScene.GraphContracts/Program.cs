using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.IO;
using NurbsScene.Kernel;
using NurbsScene.Model;
using NurbsScene.Parametric;

var factory = new GeometryFactory(new ManagedNurbsEvaluator());
var registry = StandardComponents.Create(factory);
var tests = new List<(string Name, Func<Task> Run)>();
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> action) => tests.Add((name, action));
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
var source = new DataTree([new(new([2, 3]), [1d, null, 2d]), new(new([2, 10]), []), new(new([7]), [4d])]);
async Task<NodeResult> Run(string id, IReadOnlyDictionary<string, object?> values)
{
    var graph = new Graph(registry);
    var node = graph.Add(id, parameters: values.ToDictionary(p => "input:" + p.Key, p => p.Value));
    var result = await graph.SolveAsync();
    Check(result.ErrorCount == 0, string.Join("; ", result.Nodes[node.Id].Messages));
    return result.Nodes[node.Id];
}
AsyncTest("persistent tree input retains paths, null items and empty branches", async () => {
    var result = await Run("TreeStatistics", new Dictionary<string, object?> { ["T"] = source });
    Check((int)result.Outputs["Branches"].AllItems.Single()! == 3);
    Check((int)result.Outputs["Items"].AllItems.Single()! == 4);
});
AsyncTest("typed persistent tree performs item matching without wrapping a tree object", async () => {
    var tree = new DataTree([new(new([3]), [1d, 2d]), new(new([7]), [4d])]);
    var result = await Run("Add", new Dictionary<string, object?> { ["A"] = tree, ["B"] = 10d });
    Check(result.Outputs["R"].Branches.Select(b => b.Path.ToString()).SequenceEqual(["{3}", "{7}"]));
    Check(result.Outputs["R"].AllItems.Cast<double>().SequenceEqual([11d, 12d, 14d]));
});
AsyncTest("list matching does not invent the scalar default's root branch", async () => {
    var definitions = new ComponentRegistry();
    definitions.Register(new("CountPlus", "Test", "", [new("L", Access: PortAccess.List), new("N", ValueKind.Number, Default: 1d)],
        [new("R", ValueKind.Number)], c => ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(
            new Dictionary<string, object?> { ["R"] = c.Get<ImmutableArray<object?>>("L").Length + c.Number("N") })));
    var g = new Graph(definitions);
    var n = g.Add("CountPlus", parameters: new Dictionary<string, object?> { ["input:L"] = source });
    var r = await g.SolveAsync();
    Check(r.ErrorCount == 0);
    Check(r.Nodes[n.Id].Outputs["R"].Branches.Select(b => b.Path.ToString()).SequenceEqual(["{2;3}", "{2;10}", "{7}"]));
});
AsyncTest("ConstructPath and DeconstructPath use typed integer indices", async () => {
    var constructed = await Run("ConstructPath", new Dictionary<string, object?> { ["Indices"] = new object?[] { 2d, 10d, 3 } });
    var path = (DataPath)constructed.Outputs["P"].AllItems.Single()!;
    Check(path.Equals(new DataPath([2, 10, 3])));
    var result = await Run("DeconstructPath", new Dictionary<string, object?> { ["P"] = path });
    Check(result.Outputs["Indices"].AllItems.Cast<int>().SequenceEqual([2, 10, 3]));
});
AsyncTest("TreePaths includes empty branches in numeric order", async () => {
    var r = await Run("TreePaths", new Dictionary<string, object?> { ["T"] = source });
    Check(r.Outputs["P"].AllItems.Cast<DataPath>().Select(p => p.ToString()).SequenceEqual(["{2;3}", "{2;10}", "{7}"]));
    Check(r.Outputs["Counts"].AllItems.Cast<int>().SequenceEqual([3, 0, 1]));
});
AsyncTest("TreeBranch preserves an empty branch and distinguishes a missing branch", async () => {
    var r = await Run("TreeBranch", new Dictionary<string, object?> { ["T"] = source, ["P"] = new DataPath([2, 10]) });
    Check(r.Outputs["T"].Branches.Length == 1 && r.Outputs["T"].ItemCount == 0);
    var missing = await Run("TreeBranch", new Dictionary<string, object?> { ["T"] = source, ["P"] = new DataPath([99]) });
    Check(missing.Outputs["T"].Branches.Length == 0);
});
AsyncTest("ReplacePath merges collisions without discarding or reordering items", async () => {
    var r = await Run("ReplacePath", new Dictionary<string, object?> { ["T"] = source, ["From"] = new DataPath([7]), ["To"] = new DataPath([2, 3]) });
    Check(r.Outputs["T"].Branches.Length == 2);
    Check(r.Outputs["T"].ItemsAt(new([2, 3])).SequenceEqual(new object?[] { 1d, null, 2d, 4d }));
});
AsyncTest("TrimTreePaths retains empty paths and merges branch items", async () => {
    var r = await Run("TrimTreePaths", new Dictionary<string, object?> { ["T"] = source, ["N"] = 1d });
    Check(r.Outputs["T"].Branches.Select(b => b.Path.ToString()).SequenceEqual(["{}", "{2}"]));
    Check(r.Outputs["T"].ItemCount == source.ItemCount);
});
AsyncTest("CleanTree and FlattenToPath are explicit, immutable transforms", async () => {
    var r = await Run("CleanTree", new Dictionary<string, object?> { ["T"] = source });
    Check(r.Outputs["T"].Branches.Length == 2 && r.Outputs["T"].ItemCount == 3);
    var flat = await Run("FlattenToPath", new Dictionary<string, object?> { ["T"] = source, ["P"] = new DataPath([5, 8]) });
    Check(flat.Outputs["T"].Branches.Single().Path.Equals(new DataPath([5, 8])));
    Check(flat.Outputs["T"].ItemCount == 4 && source.ItemCount == 4 && source.Branches.Length == 3);
});
AsyncTest("path ports connect, survive workspace serialization, and reject number outputs", async () => {
    var g = new Graph(registry);var a = g.Add("ConstructPath", parameters: new Dictionary<string, object?> { ["input:Indices"] = new object?[] { 2, 3 } });
    var b = g.Add("TreeBranch", parameters: new Dictionary<string, object?> { ["input:T"] = source });
    g.Connect(new(a.Id, "P"), new(b.Id, "P"));
    var number = g.Add("Number");Throws<ArgumentException>(() => g.Connect(new(number.Id, "N"), new(b.Id, "P")));
    var stored = g.Add("DeconstructPath", parameters: new Dictionary<string, object?> { ["input:P"] = new DataPath([8, 9]) });
    var saved = WorkspaceFormat.Serialize(new SceneDocument(), g);
    var loaded = WorkspaceFormat.Deserialize(factory.Evaluator, registry, saved).Graph;
    Check(loaded.GetNode(stored.Id).Parameters["input:P"] is DataPath p && p.Equals(new DataPath([8, 9])));
    var r = await loaded.SolveAsync();Check(r.ErrorCount == 0);Check(r.Nodes[b.Id].Outputs["T"].ItemCount == 3);
});
AsyncTest("path input validation fails before it can create invalid output", async () => {
    foreach (var indices in new[] { new object?[] { -1d }, new object?[] { .5 }, Enumerable.Repeat<object?>(1,65).ToArray() }) {
        var g = new Graph(registry);var node = g.Add("ConstructPath", parameters: new Dictionary<string, object?> { ["input:Indices"] = indices });
        var r = await g.SolveAsync();Check(r.Nodes[node.Id].Status == "error");Check(r.Nodes[node.Id].Outputs["P"].ItemCount == 0);
    }
});
AsyncTest("registered defaults are detached from mutable caller-owned lists", async () => {
    var values = new List<object?> { 1d, 2d };var r = new ComponentRegistry();
    r.Register(new("DefaultList", "Test", "", [new("L", ValueKind.Number, PortAccess.List, values)], [new("L", ValueKind.Number, PortAccess.List)],
        c => ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?> { ["L"] = c.Inputs["L"] })));
    values[0] = 999d;var g = new Graph(r);var n = g.Add("DefaultList");var result = await g.SolveAsync();
    Check(result.Nodes[n.Id].Outputs["L"].AllItems.Cast<double>().SequenceEqual([1d, 2d]));
});
AsyncTest("dependency index retains multiple wires between a pair of nodes", async () => {
    var g = new Graph(registry);var a = g.Add("Number", parameters: new Dictionary<string, object?> { ["value"] = 3d });var b = g.Add("Add");var c = g.Add("Add");
    var one = g.Connect(new(a.Id, "N"), new(b.Id, "A"));g.Connect(new(a.Id, "N"), new(b.Id, "B"));g.Connect(new(b.Id, "R"), new(c.Id, "A"));
    await g.SolveAsync();g.Disconnect(one.Id);await g.SolveAsync();g.Update(a with { Parameters = a.Parameters.SetItem("value", 10d) });
    var changed = await g.SolveAsync();Check(changed.Evaluated == 3);Check((double)changed.Nodes[c.Id].Outputs["R"].AllItems.Single()! == 12d);
    Throws<InvalidOperationException>(() => g.Connect(new(c.Id,"R"),new(b.Id,"A")));
    g.Remove(b.Id);var removed=await g.SolveAsync();Check(removed.ErrorCount==0);Check((double)removed.Nodes[c.Id].Outputs["R"].AllItems.Single()! == 2d);
});
AsyncTest("replacing a connection updates cycle detection and dirty propagation", async () => {
    var g = new Graph(registry);var a=g.Add("Add");var b=g.Add("Add");var c=g.Add("Add");
    g.Connect(new(a.Id,"R"),new(b.Id,"A"));g.Connect(new(c.Id,"R"),new(b.Id,"A"),replace:true);
    g.Connect(new(b.Id,"R"),new(a.Id,"A"));
    var r=await g.SolveAsync();Check(r.ErrorCount==0);Check(g.Wires.Count==2);
});
AsyncTest("snapshots remain internally consistent during concurrent graph edits", async () => {
    var g=new Graph(registry);var anchor=g.Add("Number");
    var edit=Task.Run(()=>{for(var i=0;i<1000;i++){var n=g.Add("Add");g.Connect(new(anchor.Id,"N"),new(n.Id,"A"));g.Remove(n.Id);}});
    do { var snapshot=g.Snapshot();var ids=snapshot.Nodes.Select(n=>n.Id).ToHashSet();Check(snapshot.Wires.All(w=>ids.Contains(w.From.Node)&&ids.Contains(w.To.Node))); } while(!edit.IsCompleted);
    await edit;
});
AsyncTest("large DAG solve visits indexed edges and preserves incremental results", async () => {
    const int count=2000;var g=new Graph(registry);var first=g.Add("Number",parameters:new Dictionary<string,object?>{{"value",2d}});var previous=first;
    for(var i=0;i<count;i++){var next=g.Add("Add");g.Connect(new(previous.Id,previous.Id==first.Id?"N":"R"),new(next.Id,"A"));previous=next;}
    var watch=Stopwatch.StartNew();var result=await g.SolveAsync();watch.Stop();Check(result.ErrorCount==0);Check(result.Evaluated==count+1);
    Check((double)result.Nodes[previous.Id].Outputs["R"].AllItems.Single()! == count+2d);
    var cached=await g.SolveAsync();Check(cached.Cached==count+1&&cached.Evaluated==0);
    Console.WriteLine(JsonSerializer.Serialize(new{scenario="2001-node-chain",milliseconds=watch.Elapsed.TotalMilliseconds,edges=g.Wires.Count}));
});
Test("transaction selection does not expose partially committed or rolled-back geometry",()=>{
    var doc=new SceneDocument();var observed=new List<DocumentSnapshot>();doc.Changed+=_=>observed.Add(doc.Snapshot());
    Throws<InvalidOperationException>(()=>doc.Transaction("Fail",()=>{var item=doc.Add(factory.Circle(Vec3.Zero,1));doc.Select([item.Id]);throw new InvalidOperationException();}));
    Check(observed.Count==1);Check(observed[0].Objects.Length==0&&observed[0].Selection.Length==0);
});
Test("path editor syntax is invariant and round-trips including the empty path",()=>{
    foreach(var text in new[]{"{}","{0;2;10}"," 0 ; 2147483647 ","{ 3 ; 8 }"}){
        var path=DataPath.Parse(text);Check(DataPath.Parse(path.ToString()).Equals(path));
    }
    foreach(var invalid in new[]{"","{","}","{-1}","{1.2}","{+1}","{1;;2}","{2147483648}","{1,2}",string.Join(';',Enumerable.Repeat("1",65))})
        Check(!DataPath.TryParse(invalid,out _),"Accepted invalid path: "+invalid);
    Check(!DataPath.TryParse(null,out _));Throws<FormatException>(()=>DataPath.Parse("bad"));
});
Test("empty branch budget and null paths are validated",()=>{
    Throws<ArgumentException>(()=>new DataTree(Enumerable.Range(0,100001).Select(i=>new DataBranch(new([i]),[]))));
    Throws<ArgumentNullException>(()=>new DataTree([new DataBranch(null!,[])]));
});
int failed=0;
foreach(var(name,test)in tests)try{await test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="graph-and-tree-contracts",total=tests.Count,passed=tests.Count-failed,failed,definitions=registry.Definitions.Count}));
return failed==0?0:1;
