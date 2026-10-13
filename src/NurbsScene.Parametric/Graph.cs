using System.Collections.Immutable;
using System.Diagnostics;
using NurbsScene.Geometry;

namespace NurbsScene.Parametric;

public enum PortAccess { Item,List,Tree }
public enum ValueKind { Any,Number,Integer,Boolean,Text,Point,Vector,Curve,Surface,Geometry,Path }
public sealed record PortDefinition(string Name,ValueKind Kind=ValueKind.Any,PortAccess Access=PortAccess.Item,object? Default=null,bool Optional=false);
public sealed record ComponentContext(IReadOnlyDictionary<string,object?> Inputs,IReadOnlyDictionary<string,object?> Parameters,DataPath Path,CancellationToken CancellationToken)
{
    /// <summary>Connected ports at this solve's captured revision, including
    /// connected empty trees. Persistent/default inputs are not connections.</summary>
    public ImmutableArray<string> ConnectedInputs { get; init; } = [];
    public bool IsConnected(string name) => !ConnectedInputs.IsDefaultOrEmpty && ConnectedInputs.Contains(name);
    public double Number(string name)=>Convert.ToDouble(Inputs[name],System.Globalization.CultureInfo.InvariantCulture);
    public T Get<T>(string name)=>Inputs[name] is T value?value:throw new InvalidCastException($"Input {name} is not {typeof(T).Name}.");
    public double Parameter(string name,double fallback=0)=>Parameters.TryGetValue(name,out var v)?Convert.ToDouble(v,System.Globalization.CultureInfo.InvariantCulture):fallback;
}
public sealed record ComponentDefinition(string Id,string Category,string Description,ImmutableArray<PortDefinition> Inputs,ImmutableArray<PortDefinition> Outputs,Func<ComponentContext,ValueTask<IReadOnlyDictionary<string,object?>>> Evaluate);
public sealed partial record GraphNode(string Id,string Component,double X,double Y,ImmutableDictionary<string,object?> Parameters,string? Name=null,bool Enabled=true,bool Preview=true,string Matching="longest");
public sealed record PortAddress(string Node,string Port);
public sealed record Wire(string Id,PortAddress From,PortAddress To,string Transform="none");
public sealed record NodeResult(ImmutableDictionary<string,DataTree> Outputs,string Status,ImmutableArray<string> Messages,double Milliseconds);
public sealed record GraphResult(long Revision,bool Stale,ImmutableDictionary<string,NodeResult> Nodes,int Evaluated,int Cached,double Milliseconds)
{
    public int ErrorCount=>Nodes.Count(kv=>kv.Value.Status is "error" or "blocked");
}
public sealed record GraphSnapshot(GraphNode[] Nodes,Wire[] Wires);

/// <summary>Typed, cycle-rejecting incremental DAG evaluator. A solve publishes atomically only at its captured revision.</summary>
public sealed partial class Graph(ComponentRegistry registry)
{
    private readonly Dictionary<string,GraphNode> _nodes=[];private readonly Dictionary<string,Wire> _wires=[];private Dictionary<string,NodeResult> _cache=[];private readonly HashSet<string> _dirty=[];private readonly object _gate=new();private long _generation;
    private readonly Dictionary<string, Dictionary<string,int>> _outgoing=[];
    private readonly Dictionary<PortAddress,List<Wire>> _inputWires=[];
    private const int MaximumWires=100_000;
    private ExecutionPlan? _plan;
    private sealed record ExecutionPlan(string[] Order,Dictionary<PortAddress,Wire[]> Inputs,Dictionary<string,ImmutableArray<string>> ConnectedPorts);
    public long ExecutionPlanBuilds {get;private set;}
    private bool _editing,_pendingChange;
    internal void BeginEdit()
    {
        lock(_gate){if(_editing)throw new InvalidOperationException("Nested graph editing is not supported.");_editing=true;_pendingChange=false;}
    }
    internal void EndEdit(bool publish)
    {
        bool changed;lock(_gate){changed=_pendingChange;_editing=false;_pendingChange=false;}
        if(publish&&changed)Changed?.Invoke();
    }
    private void NotifyChanged()
    {
        lock(_gate){if(_editing){_pendingChange=true;return;}}
        Changed?.Invoke();
    }
    // Called under the gate. Edits to parameters, enabled state or presentation
    // cannot change topology, so they reuse this immutable execution plan.
    private ExecutionPlan Plan()
    {
        if(_plan is not null)return _plan;
        var wires=_wires.Values.ToArray();
        var order=Topological(_nodes,wires,wires.ToLookup(w=>w.From.Node));
        var inputs=wires.GroupBy(w=>w.To).ToDictionary(g=>g.Key,g=>g.ToArray());
        var connections=new Dictionary<string,ImmutableArray<string>>();
        foreach(var node in _nodes.Values)
        {
            ImmutableArray<string>.Builder? names=null;
            // Preserve definition order, not the order in which wires were added.
            foreach(var port in Registry.Get(node.Component).Inputs)
                if(inputs.ContainsKey(new(node.Id,port.Name)))(names??=ImmutableArray.CreateBuilder<string>()).Add(port.Name);
            if(names is not null)connections.Add(node.Id,names.ToImmutable());
        }
        _plan=new(order,inputs,connections);ExecutionPlanBuilds++;return _plan;
    }

    private void Link(Wire wire)
    {
        if(!_outgoing.TryGetValue(wire.From.Node,out var children))_outgoing.Add(wire.From.Node,children=[]);
        children[wire.To.Node]=children.GetValueOrDefault(wire.To.Node)+1;
        if(!_inputWires.TryGetValue(wire.To,out var inputs))_inputWires.Add(wire.To,inputs=[]);
        inputs.Add(wire);
    }
    private void Unlink(Wire wire)
    {
        var children=_outgoing[wire.From.Node];
        if(--children[wire.To.Node]==0)children.Remove(wire.To.Node);
        if(children.Count==0)_outgoing.Remove(wire.From.Node);
        var inputs=_inputWires[wire.To];inputs.Remove(wire);if(inputs.Count==0)_inputWires.Remove(wire.To);
    }
    public ComponentRegistry Registry { get; }=registry??throw new ArgumentNullException(nameof(registry));
    public event Action? Changed;public event Action<GraphResult>? Solved;
    public long Revision { get; private set; }
    /// <summary>Changes only for added/removed nodes or wires, not parameter or layout edits.</summary>
    public long TopologyRevision { get; private set; }
    public int NodeCount { get { lock(_gate)return _nodes.Count; } }
    public int WireCount { get { lock(_gate)return _wires.Count; } }
    public GraphResult? LastResult { get; private set; }
    public GraphNode GetNode(string id){lock(_gate)return _nodes.TryGetValue(id,out var n)?n:throw new KeyNotFoundException(id);}
    public bool TryGetNode(string id,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GraphNode? node)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock(_gate)return _nodes.TryGetValue(id,out node);
    }
    private void Invalidate(string id){var queue=new Queue<string>();var seen=new HashSet<string>();queue.Enqueue(id);while(queue.TryDequeue(out var n)){if(!seen.Add(n))continue;_dirty.Add(n);if(_outgoing.TryGetValue(n,out var children))foreach(var child in children.Keys)queue.Enqueue(child);}}
    public GraphNode Add(string component,double x=0,double y=0,IReadOnlyDictionary<string,object?>? parameters=null,string? name=null,string? id=null)
    {
        Registry.Get(component);if(!double.IsFinite(x)||!double.IsFinite(y))throw new ArgumentException("Node position must be finite.");var node=new GraphNode(id??Guid.NewGuid().ToString("N"),component,x,y,ParameterValues.Freeze(parameters),name);
        lock(_gate){if(_nodes.Count>=10000)throw new InvalidOperationException("Node budget exceeded.");_nodes.Add(node.Id,node);_nodesView=null;TopologyRevision++;_plan=null;Invalidate(node.Id);Revision++;}NotifyChanged();return node;
    }
    public void Update(GraphNode node)
    {
        node.CanvasSize?.Validate();
        node.PanelStyle?.Validate();
        if(node.PanelStyle is not null && node.Component is not ("Panel" or "Text"))throw new ArgumentException("Typography belongs to text panels only.");
        if(!double.IsFinite(node.X)||!double.IsFinite(node.Y)||node.Matching is not("longest" or "shortest" or "cross"))throw new ArgumentException("Invalid node position or matching mode.");
        lock(_gate){var previous=GetNode(node.Id);if(previous==node)return;if(previous.Parameters!=node.Parameters)node=node with{Parameters=ParameterValues.Freeze(node.Parameters)};if(previous.Component!=node.Component)throw new InvalidOperationException("Cannot replace a component type in place.");_nodes[node.Id]=node;_nodesView=null;if(previous.Parameters!=node.Parameters||previous.Enabled!=node.Enabled||previous.Matching!=node.Matching)Invalidate(node.Id);Revision++;}NotifyChanged();
    }
    public void Remove(string id)
    {
        lock(_gate)
        {
            GetNode(id);
            var successors=_outgoing.TryGetValue(id,out var children)?children.Keys.ToArray():[];
            foreach(var wire in _wires.Values.Where(w=>w.From.Node==id||w.To.Node==id).ToArray()){_wires.Remove(wire.Id);Unlink(wire);WireRevision++;}
            foreach(var next in successors)Invalidate(next);
            _nodes.Remove(id);_nodesView=null;TopologyRevision++;_plan=null;_cache.Remove(id);_dirty.Remove(id);Revision++;
        }
        NotifyChanged();
    }
    private bool WouldCycle(string from,string to){var stack=new Stack<string>();var seen=new HashSet<string>();stack.Push(to);while(stack.TryPop(out var id)){if(id==from)return true;if(!seen.Add(id))continue;if(_outgoing.TryGetValue(id,out var children))foreach(var child in children.Keys)stack.Push(child);}return false;}
    public Wire Connect(PortAddress from,PortAddress to,bool replace=false,string transform="none",string? id=null)
    {
        Wire wire;lock(_gate){var a=Registry.Get(GetNode(from.Node).Component).Outputs.SingleOrDefault(p=>p.Name==from.Port)??throw new ArgumentException("Source port is missing.");var b=Registry.Get(GetNode(to.Node).Component).Inputs.SingleOrDefault(p=>p.Name==to.Port)??throw new ArgumentException("Target port is missing.");if(!Compatible(a.Kind,b.Kind))throw new ArgumentException("Incompatible port types.");if(WouldCycle(from.Node,to.Node))throw new InvalidOperationException("Connection would introduce a cycle.");if(transform is not("none" or "flatten" or "graft" or "simplify"))throw new ArgumentException("Unknown wire transform.");if(_inputWires.TryGetValue(to,out var existing)&&existing.Any(w=>w.From==from))throw new ArgumentException("Duplicate wire.");if(id is not null&&_wires.ContainsKey(id))throw new ArgumentException("Duplicate wire identity.");var replaced=replace&&_inputWires.TryGetValue(to,out var target)?target.ToArray():[];if(_wires.Count-replaced.Length>=MaximumWires)throw new InvalidOperationException("Wire budget exceeded.");foreach(var old in replaced){_wires.Remove(old.Id);Unlink(old);}wire=new(id??Guid.NewGuid().ToString("N"),from,to,transform);_wires.Add(wire.Id,wire);WireRevision++;TopologyRevision++;_plan=null;Link(wire);Invalidate(to.Node);Revision++;}NotifyChanged();return wire;
    }
    public void Disconnect(string id){lock(_gate){var w=_wires[id];_wires.Remove(id);WireRevision++;TopologyRevision++;_plan=null;Unlink(w);Invalidate(w.To.Node);Revision++;}NotifyChanged();}
    private static bool Compatible(ValueKind a,ValueKind b)=>a==ValueKind.Any||b==ValueKind.Any||a==b||(a==ValueKind.Integer&&b==ValueKind.Number)||((a is ValueKind.Point or ValueKind.Vector)&&(b is ValueKind.Point or ValueKind.Vector))||(b==ValueKind.Geometry&&(a is ValueKind.Curve or ValueKind.Surface));
    private static void Validate(object? value,PortDefinition port)
    {
        var valid=port.Kind switch{ValueKind.Any=>true,ValueKind.Number=>value is double d&&double.IsFinite(d)||value is int,ValueKind.Integer=>value is int,ValueKind.Boolean=>value is bool,ValueKind.Text=>value is string,ValueKind.Point or ValueKind.Vector=>value is Vec3 p&&p.IsFinite,ValueKind.Curve=>value is NurbsCurve,ValueKind.Surface=>value is NurbsSurface,ValueKind.Geometry=>value is IGeometry,ValueKind.Path=>value is DataPath,_=>false};if(!valid)throw new ArgumentException($"Port {port.Name} requires {port.Kind}.");
    }
    private static string[] Topological(Dictionary<string,GraphNode> nodes,Wire[] wires,ILookup<string,Wire> outgoing)
    {
        var degree=nodes.Keys.ToDictionary(k=>k,_=>0);foreach(var wire in wires)degree[wire.To.Node]++;var ready=new Queue<string>(degree.Where(kv=>kv.Value==0).Select(kv=>kv.Key));var result=new List<string>();while(ready.TryDequeue(out var id)){result.Add(id);foreach(var wire in outgoing[id])if(--degree[wire.To.Node]==0)ready.Enqueue(wire.To.Node);}if(result.Count!=nodes.Count)throw new InvalidOperationException("Graph contains a cycle.");return result.ToArray();
    }
    public async Task<GraphResult> SolveAsync(CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string,GraphNode> nodes;HashSet<string> dirty;Dictionary<string,NodeResult> cache;
        long revision,generation;ExecutionPlan plan;GraphResult? reused=null;
        var stopwatch=Stopwatch.StartNew();
        lock(_gate)
        {
            if(_editing)throw new InvalidOperationException("Cannot solve a partially edited graph.");
            generation=++_generation;revision=Revision;
            if(_plan is not null&&_dirty.Count==0&&LastResult is{} last)
            {
                reused=last with{Revision=revision,Stale=false,Evaluated=0,Cached=_nodes.Count,Milliseconds=stopwatch.Elapsed.TotalMilliseconds};
                LastResult=reused;
            }
            nodes=reused is null?new(_nodes):null!;dirty=reused is null?new(_dirty):null!;
            cache=reused is null?new(_cache):null!;plan=reused is null?Plan():null!;
        }
        if(reused is not null){Solved?.Invoke(reused);return reused;}
        var results=new Dictionary<string,NodeResult>(nodes.Count);var evaluated=0;var cached=0;
        foreach(var id in plan.Order)
        {
            cancellationToken.ThrowIfCancellationRequested();lock(_gate)if(generation!=_generation)throw new OperationCanceledException("Superseded graph solve.");
            if(!dirty.Contains(id)&&cache.TryGetValue(id,out var previous)){results[id]=previous;cached++;continue;}
            var node=nodes[id];var definition=Registry.Get(node.Component);var start=Stopwatch.GetTimestamp();var output=definition.Outputs.ToImmutableDictionary(p=>p.Name,_=>DataTree.Empty);var status="ok";ImmutableArray<string> messages=[];
            try
            {
                if(!node.Enabled)status="disabled";
                else
                {
                    var inputs=new Dictionary<string,DataTree>();
                    var connected=plan.ConnectedPorts.GetValueOrDefault(id,ImmutableArray<string>.Empty);
                    foreach(var port in definition.Inputs)
                    {
                        var incoming=plan.Inputs.GetValueOrDefault(new PortAddress(id,port.Name))??[];var tree=DataTree.Empty;
                        DataTree Resolve(Wire wire)
                        {
                            var upstream=results[wire.From.Node];
                            if(upstream.Status is "error" or "blocked")throw new UpstreamException("An upstream component failed.");
                            var t=upstream.Outputs[wire.From.Port];
                            return wire.Transform switch{"flatten"=>t.Flatten(),"graft"=>t.Graft(),"simplify"=>t.Simplify(),_=>t};
                        }
                        if(incoming.Length==1)tree=Resolve(incoming[0]);
                        else if(incoming.Length>1)tree=new DataTree(incoming.SelectMany(w=>Resolve(w).Branches));
                        if(incoming.Length==0){var key="input:"+port.Name;var value=node.Parameters.TryGetValue(key,out var v)?v:port.Default;if(value is null&&!port.Optional)throw new ArgumentException($"Required input {port.Name} is not connected.");if(value is not null)tree=value is DataTree persistent?persistent:port.Access==PortAccess.List&&value is System.Collections.IEnumerable list&&value is not string?DataTree.List(list.Cast<object?>()):DataTree.Item(value);}
                        foreach(var value in tree.AllItems)Validate(value,port);inputs.Add(port.Name,tree);
                    }
                    output=await EvaluateDefinition(definition,node,inputs,connected,cancellationToken);
                }
            }
            catch(OperationCanceledException){throw;}
            catch(Exception error){status=error is UpstreamException?"blocked":"error";messages=[error.Message];}
            results[id]=new(output,status,messages,Stopwatch.GetElapsedTime(start).TotalMilliseconds);evaluated++;
        }
        cancellationToken.ThrowIfCancellationRequested();GraphResult result;lock(_gate){if(generation!=_generation)throw new OperationCanceledException("Superseded solve.");result=new(revision,Revision!=revision,results.ToImmutableDictionary(),evaluated,cached,stopwatch.Elapsed.TotalMilliseconds);if(!result.Stale){_cache=results;_dirty.Clear();LastResult=result;}}if(!result.Stale)Solved?.Invoke(result);return result;
    }
    private static async ValueTask<ImmutableDictionary<string,DataTree>> EvaluateDefinition(ComponentDefinition definition,GraphNode node,Dictionary<string,DataTree> inputs,ImmutableArray<string> connected,CancellationToken cancellationToken)
    {
        var branches=definition.Outputs.ToDictionary(p=>p.Name,_=>new SortedDictionary<DataPath,List<object?>>());
        var nonTreeInputs=definition.Inputs.Where(p=>p.Access!=PortAccess.Tree).Select(p=>inputs[p.Name]).ToArray();
        var structural=nonTreeInputs.Where(t=>t.Branches.Length>1).ToArray();
        // Single-branch inputs broadcast into structural branches; their own
        // path must not fabricate an extra result branch when a list is empty.
        var paths=(structural.Length>0?structural:nonTreeInputs).SelectMany(t=>t.Branches.Select(b=>b.Path)).Distinct().Order().ToArray();
        if(paths.Length==0)paths=[DataPath.Root];
        var count=0;var invocations=0;var branchVisits=0;
        void Add(PortDefinition port,DataPath path,object? value){if(!branches[port.Name].TryGetValue(path,out var list))branches[port.Name].Add(path,list=[]);Validate(value,port);list.Add(ParameterValues.FreezeValue(value));if(++count>100000)throw new InvalidOperationException("Component output budget exceeded.");}
        foreach(var path in paths)
        {
            var fixedValues=new Dictionary<string,object?>();var itemPorts=new List<PortDefinition>();var arrays=new List<ImmutableArray<object?>>();var missing=false;
            foreach(var port in definition.Inputs){var tree=inputs[port.Name];if(port.Access==PortAccess.Tree)fixedValues.Add(port.Name,tree);else{var items=tree.ItemsAt(path,true);if(port.Access==PortAccess.List)fixedValues.Add(port.Name,items);else if(items.Length==0&&port.Optional)fixedValues.Add(port.Name,null);else{missing|=items.Length==0;itemPorts.Add(port);arrays.Add(items);}}}if(missing)continue;
            var rows=arrays.Count==0?1:node.Matching=="cross"?arrays.Aggregate(1,(n,a)=>checked(n*a.Length)):node.Matching=="shortest"?arrays.Min(a=>a.Length):arrays.Max(a=>a.Length);if(rows>100000)throw new InvalidOperationException("Data matching budget exceeded.");
            for(var row=0;row<rows;row++)
            {
                cancellationToken.ThrowIfCancellationRequested();var values=new Dictionary<string,object?>(fixedValues);var remainder=row;for(var i=0;i<arrays.Count;i++){var index=node.Matching=="cross"?remainder%arrays[i].Length:Math.Min(row,arrays[i].Length-1);if(node.Matching=="cross")remainder/=arrays[i].Length;values[itemPorts[i].Name]=arrays[i][index];}
                if(++invocations>100_000)throw new InvalidOperationException("Component invocation budget exceeded.");
                var output=await definition.Evaluate(new(values,node.Parameters,path,cancellationToken){ConnectedInputs=connected});
                foreach(var port in definition.Outputs){if(!output.TryGetValue(port.Name,out var value))throw new InvalidOperationException($"Component omitted output {port.Name}.");if(value is DataTree tree){foreach(var branch in tree.Branches){if(++branchVisits>1_000_000)throw new InvalidOperationException("Component branch work budget exceeded.");if(!branches[port.Name].ContainsKey(branch.Path))branches[port.Name].Add(branch.Path,[]);foreach(var item in branch.Items)Add(port,branch.Path,item);}}else if(port.Access==PortAccess.List&&value is System.Collections.IEnumerable list&&value is not string){if(!branches[port.Name].ContainsKey(path))branches[port.Name].Add(path,[]);foreach(var item in list.Cast<object?>())Add(port,path,item);}else Add(port,path,value);}
            }
        }
        return branches.ToImmutableDictionary(kv=>kv.Key,kv=>new DataTree(kv.Value.Select(b=>new DataBranch(b.Key,b.Value.ToImmutableArray()))));
    }
    public GraphSnapshot Snapshot(){lock(_gate)return new(_nodes.Values.ToArray(),_wires.Values.ToArray());}
    public static Graph FromSnapshot(ComponentRegistry registry,GraphSnapshot snapshot){var graph=new Graph(registry);foreach(var node in snapshot.Nodes){graph.Add(node.Component,node.X,node.Y,node.Parameters,node.Name,node.Id);graph.Update(node);}foreach(var wire in snapshot.Wires)graph.Connect(wire.From,wire.To,transform:wire.Transform,id:wire.Id);return graph;}
    private sealed class UpstreamException(string message):Exception(message);
}
