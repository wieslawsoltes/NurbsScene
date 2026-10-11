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
/// <summary>Owner-thread presentation cache. An unchanged draw does not copy graph
/// nodes/wires, enumerate ports, allocate endpoints or rebuild layout. Geometry
/// caches are independent of solve results and selection. Culling remains linear.</summary>
public sealed class GraphPresentation
{
    private Graph? _graph;
    private long _revision=-1;
    private GraphDrawing _drawing=new([],[]);
    public long BuildCount {get;private set;}
    public GraphDrawing Get(Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if(ReferenceEquals(graph,_graph)&&graph.Revision==_revision)return _drawing;
        return Build(graph);
    }
    private GraphDrawing Build(Graph graph)
    {
        var snapshot=graph.Snapshot();
        var nodes=snapshot.Nodes.Select(n=>{
            var d=graph.Registry.Get(n.Component);
            var height=n.Component is "Number" or "Integer"?38:n.Component=="Text"?76:Math.Max(56,Math.Max(d.Inputs.Length,d.Outputs.Length)*19+24);
            return new PresentedNode(n,d,new(n.X,n.Y,n.Component is "Number" or "Integer"?180:156,height));
        }).ToImmutableArray();
        var ports=new Dictionary<PortAddress,CanvasPoint>();
        var outputs=new Dictionary<PortAddress,CanvasPoint>();
        foreach(var n in nodes)
        {
            for(var i=0;i<n.Definition.Inputs.Length;i++)ports.Add(new(n.Node.Id,n.Definition.Inputs[i].Name),n.Port(false,i));
            for(var i=0;i<n.Definition.Outputs.Length;i++)outputs.Add(new(n.Node.Id,n.Definition.Outputs[i].Name),n.Port(true,i));
        }
        var wires=snapshot.Wires.Select(w=>{
            var a=outputs[w.From];var b=ports[w.To];var delta=Math.Max(35,Math.Abs(b.X-a.X)*.48);
            var min=Math.Min(a.X,b.X-delta);var max=Math.Max(a.X+delta,b.X);
            return new PresentedWire(w,a,b,new(min,Math.Min(a.Y,b.Y)-3,max-min,Math.Abs(b.Y-a.Y)+6));
        }).ToImmutableArray();
        _drawing=new(nodes,wires);_graph=graph;_revision=graph.Revision;BuildCount++;return _drawing;
    }
}
