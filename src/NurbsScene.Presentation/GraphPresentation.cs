using System.Collections.Immutable;
using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

public readonly record struct CanvasPoint(double X,double Y);
public readonly record struct CanvasBox(double X,double Y,double Width,double Height)
{
    public bool Contains(CanvasPoint p)=>p.X>=X&&p.X<=X+Width&&p.Y>=Y&&p.Y<=Y+Height;
    public bool Intersects(CanvasBox b)=>X<=b.X+b.Width&&X+Width>=b.X&&Y<=b.Y+b.Height&&Y+Height>=b.Y;
}
public sealed record PresentedNode(GraphNode Node,ComponentDefinition Definition,CanvasBox Bounds)
{
    public bool Slider=>Node.Component is "Number" or "Integer";
    public CanvasPoint Port(bool output,int index)=>new(Bounds.X+(output?Bounds.Width:0),Bounds.Y+(Slider?19:19+index*19));
}
public sealed record PresentedWire(Wire Wire,CanvasPoint Start,CanvasPoint End,CanvasBox Bounds);
public sealed record GraphDrawing(ImmutableArray<PresentedNode> Nodes,ImmutableArray<PresentedWire> Wires);
/// <summary>Owner-thread drawing cache with immutable snapshots. A node update
/// reuses every unchanged node and wire record, recomputing only incident paths
/// when its bounds move. Topology changes rebuild the adjacency index. Changed
/// revisions still scan a node snapshot; this is not constant-time graph editing.</summary>
public sealed class GraphPresentation
{
    private Graph? _graph;
    private long _revision=-1,_topology=-1;
    private GraphDrawing _drawing=new([],[]);
    private readonly Dictionary<string,int> _nodeIndex=[];
    private readonly Dictionary<string,List<int>> _incident=[];
    public long BuildCount {get;private set;}
    public long NodeBuilds {get;private set;}
    public long WireBuilds {get;private set;}
    public long TopologyBuilds {get;private set;}

    public GraphDrawing Get(Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if(ReferenceEquals(graph,_graph)&&graph.Revision==_revision)return _drawing;
        if(!ReferenceEquals(graph,_graph)||graph.TopologyRevision!=_topology)Rebuild(graph);
        else UpdateNodes(graph);
        _graph=graph;_revision=graph.Revision;_topology=graph.TopologyRevision;BuildCount++;
        return _drawing;
    }
    public static CanvasBox Bounds(GraphNode node,ComponentDefinition definition)
    {
        var slider=node.Component is "Number" or "Integer";
        var height=slider?38:node.Component=="Text"?76:Math.Max(56,Math.Max(definition.Inputs.Length,definition.Outputs.Length)*19+24);
        return new(node.X,node.Y,slider?180:156,height);
    }
    private PresentedNode Present(GraphNode node,ComponentDefinition definition)
    {
        NodeBuilds++;
        return new(node,definition,Bounds(node,definition));
    }
    private PresentedWire Present(Wire wire,ImmutableArray<PresentedNode> nodes)
    {
        var from=nodes[_nodeIndex[wire.From.Node]];var to=nodes[_nodeIndex[wire.To.Node]];
        static int Port(ImmutableArray<PortDefinition> ports,string name)
        {
            for(var i=0;i<ports.Length;i++)if(ports[i].Name==name)return i;
            throw new InvalidOperationException("Drawing contains a missing port.");
        }
        var a=from.Port(true,Port(from.Definition.Outputs,wire.From.Port));
        var b=to.Port(false,Port(to.Definition.Inputs,wire.To.Port));
        var delta=Math.Max(35,Math.Abs(b.X-a.X)*.48);
        var min=Math.Min(a.X,b.X-delta);var max=Math.Max(a.X+delta,b.X);
        WireBuilds++;
        return new(wire,a,b,new(min,Math.Min(a.Y,b.Y)-3,max-min,Math.Abs(b.Y-a.Y)+6));
    }
    private void Rebuild(Graph graph)
    {
        var snapshot=graph.Snapshot();_nodeIndex.Clear();_incident.Clear();
        // Graph identity matters: undo/load may use identical ids for new objects.
        var oldNodes=ReferenceEquals(graph,_graph)?_drawing.Nodes.ToDictionary(n=>n.Node.Id):new();
        var oldWires=ReferenceEquals(graph,_graph)?_drawing.Wires.ToDictionary(w=>w.Wire.Id):new();
        var nodes=ImmutableArray.CreateBuilder<PresentedNode>(snapshot.Nodes.Length);
        foreach(var node in snapshot.Nodes)
        {
            _nodeIndex.Add(node.Id,nodes.Count);
            nodes.Add(oldNodes.TryGetValue(node.Id,out var old)&&ReferenceEquals(old.Node,node)?old:Present(node,graph.Registry.Get(node.Component)));
        }
        var n=nodes.MoveToImmutable();
        var wires=ImmutableArray.CreateBuilder<PresentedWire>(snapshot.Wires.Length);
        foreach(var wire in snapshot.Wires)
        {
            void Index(string id){if(!_incident.TryGetValue(id,out var list))_incident.Add(id,list=[]);list.Add(wires.Count);}
            Index(wire.From.Node);Index(wire.To.Node);
            if(oldWires.TryGetValue(wire.Id,out var previous)&&previous.Wire==wire&&
                oldNodes.TryGetValue(wire.From.Node,out var from)&&from.Bounds==n[_nodeIndex[wire.From.Node]].Bounds&&
                oldNodes.TryGetValue(wire.To.Node,out var to)&&to.Bounds==n[_nodeIndex[wire.To.Node]].Bounds)wires.Add(previous);
            else wires.Add(Present(wire,n));
        }
        _drawing=new(n,wires.MoveToImmutable());TopologyBuilds++;
    }
    private void UpdateNodes(Graph graph)
    {
        var source=graph.Nodes;
        ImmutableArray<PresentedNode>.Builder? changed=null;
        HashSet<int>? affected=null;
        for(var i=0;i<source.Count;i++)
        {
            var prior=_drawing.Nodes[i];var node=source[i];
            if(ReferenceEquals(prior.Node,node))continue;
            var next=Present(node,prior.Definition);
            (changed??=_drawing.Nodes.ToBuilder())[i]=next;
            if(next.Bounds!=prior.Bounds&&_incident.TryGetValue(node.Id,out var wires))
                foreach(var index in wires)(affected??=[]).Add(index);
        }
        if(changed is null)return;
        var nodes=changed.ToImmutable();var paths=_drawing.Wires;
        if(affected is not null)
        {
            var wires=paths.ToBuilder();
            foreach(var i in affected)wires[i]=Present(paths[i].Wire,nodes);
            paths=wires.ToImmutable();
        }
        _drawing=new(nodes,paths);
    }
}
