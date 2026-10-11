using System.Collections.Immutable;
using NurbsScene.Kernel;
using NurbsScene.Parametric;

internal static class RuntimeReview
{
    internal static IEnumerable<(string Name,Func<Task> Run)> Tests()
    {
        void Check(bool value,string message="Assertion failed"){if(!value)throw new Exception(message);}
        void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
        ComponentRegistry Registry()=>StandardComponents.Create(new GeometryFactory(new ManagedNurbsEvaluator()));
        yield return ("batched graph editing emits one committed notification",()=>{
            var session=new GraphSession(new(Registry()));var notifications=0;session.Graph.Changed+=()=>notifications++;
            session.Edit("pair",g=>{var a=g.Add("Number");var b=g.Add("Add");g.Connect(new(a.Id,"N"),new(b.Id,"A"));});
            Check(notifications==1,$"{notifications} notifications exposed intermediate state");Check(session.CanUndo);return Task.CompletedTask;
        });
        yield return ("nested graph edits are rejected and restore the original snapshot",()=>{
            var session=new GraphSession(new(Registry()));
            Throws<InvalidOperationException>(()=>session.Edit("outer",g=>{g.Add("Number");session.Edit("nested",n=>n.Add("Number"));}));
            Check(session.Graph.Nodes.Count==0&&!session.CanUndo&&!session.CanRedo);return Task.CompletedTask;
        });
        yield return ("history changes and replacement are rejected during a graph edit",()=>{
            var session=new GraphSession(new(Registry()));session.Edit("seed",g=>g.Add("Number"));
            Throws<InvalidOperationException>(()=>session.Edit("bad",g=>session.Undo()));Check(session.Graph.Nodes.Count==1&&session.CanUndo);
            Throws<InvalidOperationException>(()=>session.Edit("bad",g=>session.Replace(new(Registry()))));Check(session.Graph.Nodes.Count==1);return Task.CompletedTask;
        });
        yield return ("failed batch never emits partial Changed events",()=>{
            var session=new GraphSession(new(Registry()));var changed=0;var replaced=0;session.Graph.Changed+=()=>changed++;session.Replaced+=()=>replaced++;
            Throws<ArgumentException>(()=>session.Edit("fail",g=>{g.Add("Number");throw new ArgumentException();}));
            Check(changed==0&&replaced==1&&session.Graph.Nodes.Count==0);return Task.CompletedTask;
        });
        yield return ("no-op graph update keeps revision and does not notify",()=>{
            var graph=new Graph(Registry());var node=graph.Add("Number");var revision=graph.Revision;var changed=0;graph.Changed+=()=>changed++;
            graph.Update(node);Check(graph.Revision==revision&&changed==0);return Task.CompletedTask;
        });
        yield return ("cached solve reuses immutable outputs through a layout-only update",async()=>{
            var g=new Graph(Registry());var a=g.Add("Number");var b=g.Add("Add");g.Connect(new(a.Id,"N"),new(b.Id,"A"));
            var r=await g.SolveAsync();var repeated=await g.SolveAsync();Check(ReferenceEquals(r.Nodes,repeated.Nodes)&&repeated.Cached==2&&repeated.Evaluated==0);
            g.Update(a with{X=17});var moved=await g.SolveAsync();Check(ReferenceEquals(r.Nodes,moved.Nodes)&&moved.Revision==g.Revision);
            var before=g.LastResult;try{await g.SolveAsync(new(true));throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
            Check(ReferenceEquals(before,g.LastResult));
        });
        yield return ("sparse branch lookup does not allocate on hits or misses",()=>{
            var tree=new DataTree(Enumerable.Range(0,10000).Select(i=>new DataBranch(new([2,i*3]),[i])));var path=new DataPath([2,9999]);var missing=new DataPath([2,9998]);
            for(var i=0;i<100;i++)tree.ItemsAt(path);var bytes=GC.GetAllocatedBytesForCurrentThread();
            for(var i=0;i<10000;i++){_ =tree.ItemsAt(path);_ =tree.ItemsAt(missing);}
            var allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;Check(allocated==0,$"Lookup allocated {allocated} bytes");Check((int)tree.ItemsAt(path)[0]! ==3333);return Task.CompletedTask;
        });
        yield return ("multi-wire fan-in preserves stable item order and empty branches",async()=>{
            var registry=Registry();
            var port=new PortDefinition("T",ValueKind.Any,PortAccess.Tree);
            registry.Register(new("PassTree","Test","Exact tree pass-through",[port],[port],
                c=>ValueTask.FromResult<IReadOnlyDictionary<string,object?>>(new Dictionary<string,object?>{{"T",c.Get<DataTree>("T")}})));
            var g=new Graph(registry);var sink=g.Add("PassTree");
            var path=new DataPath([3,7]);var empty=new DataPath([9]);
            for(var i=0;i<100;i++){
                var tree=new DataTree([new(path,[i]),new(empty,[])]);
                var n=g.Add("PassTree",parameters:new Dictionary<string,object?>{{"input:T",tree}});
                g.Connect(new(n.Id,"T"),new(sink.Id,"T"));
            }
            var result=await g.SolveAsync();Check(result.ErrorCount==0);
            var output=result.Nodes[sink.Id].Outputs["T"];
            Check(output.ItemsAt(path).Cast<int>().SequenceEqual(Enumerable.Range(0,100)));
            Check(output.Branches.Length==2&&output.Branches[1].Path.Equals(empty)&&output.Branches[1].Items.IsEmpty);
            var first=g.Nodes.First(n=>n.Id!=sink.Id);g.Remove(first.Id);result=await g.SolveAsync();
            Check(result.Nodes[sink.Id].Outputs["T"].ItemsAt(path).Cast<int>().SequenceEqual(Enumerable.Range(1,99)));
        });
        yield return ("deleting a clean isolated node invalidates the all-cached result",async()=>{
            var g=new Graph(Registry());var a=g.Add("Number");var b=g.Add("Number");await g.SolveAsync();g.Remove(a.Id);
            var result=await g.SolveAsync();Check(result.Nodes.Count==1&&!result.Nodes.ContainsKey(a.Id)&&result.Nodes.ContainsKey(b.Id));
            g.Remove(b.Id);Check((await g.SolveAsync()).Nodes.IsEmpty);
        });
        yield return ("execution plans survive parameter changes but not topology changes",async()=>{
            var g=new Graph(Registry());var a=g.Add("Number");var b=g.Add("Add");var wire=g.Connect(new(a.Id,"N"),new(b.Id,"A"));
            await g.SolveAsync();var builds=g.ExecutionPlanBuilds;
            g.Update(a with{Parameters=a.Parameters.SetItem("value",7d)});var result=await g.SolveAsync();
            Check(g.ExecutionPlanBuilds==builds&&result.ErrorCount==0);
            g.Disconnect(wire.Id);await g.SolveAsync();Check(g.ExecutionPlanBuilds==builds+1);
            g.Connect(new(a.Id,"N"),new(b.Id,"B"));await g.SolveAsync();Check(g.ExecutionPlanBuilds==builds+2);
        });
        yield return ("partial graph solve is forbidden during a synchronous edit",()=>{
            var session=new GraphSession(new(Registry()));
            session.Edit("check",g=>{g.Add("Number");Throws<InvalidOperationException>(()=>g.SolveAsync().GetAwaiter().GetResult());});
            Check(session.Graph.Nodes.Count==1);return Task.CompletedTask;
        });
        yield return ("large partition counts and default immutable arrays are checked",()=>{
            var tree=DataTree.List(new object?[]{1,2,3});Check(tree.Partition(int.MaxValue).ItemCount==3);
            Throws<ArgumentException>(()=>new DataTree([new(DataPath.Root,default)]));
            Throws<ArgumentException>(()=>new DataPath(Enumerable.Repeat(0,66)));return Task.CompletedTask;
        });
        yield return ("replacing parallel incoming wires keeps dependency and plan indexes consistent",async()=>{
            var g=new Graph(Registry());var a=g.Add("Number",parameters:new Dictionary<string,object?>{{"value",2d}});var b=g.Add("Number",parameters:new Dictionary<string,object?>{{"value",5d}});var sum=g.Add("Add");
            g.Connect(new(a.Id,"N"),new(sum.Id,"A"));g.Connect(new(a.Id,"N"),new(sum.Id,"B"));await g.SolveAsync();
            g.Connect(new(b.Id,"N"),new(sum.Id,"A"),replace:true);var result=await g.SolveAsync();Check(Convert.ToDouble(result.Nodes[sum.Id].Outputs["R"].AllItems.Single())==7);
            g.Update(a with{Parameters=a.Parameters.SetItem("value",3d)});result=await g.SolveAsync();Check(Convert.ToDouble(result.Nodes[sum.Id].Outputs["R"].AllItems.Single())==8);
            var roundtrip=Graph.FromSnapshot(g.Registry,g.Snapshot());Check((await roundtrip.SolveAsync()).ErrorCount==0);
        });
    }
}
