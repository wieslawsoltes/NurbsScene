using System.Collections.Immutable;

namespace NurbsScene.Parametric;

/// <summary>Detached immutable selection of nodes and their internal wires.
/// External connections are deliberately omitted. Geometry values are immutable
/// and shared; no component is evaluated while copying or validating a fragment.</summary>
public sealed class GraphFragment
{
    public ImmutableArray<GraphNode> Nodes {get;}
    public ImmutableArray<Wire> Wires {get;}
    private GraphFragment(GraphSnapshot snapshot)
    {
        Nodes=snapshot.Nodes.ToImmutableArray();Wires=snapshot.Wires.ToImmutableArray();
    }
    public static GraphFragment Capture(Graph graph,IEnumerable<string> selection)
    {
        ArgumentNullException.ThrowIfNull(graph);ArgumentNullException.ThrowIfNull(selection);
        var ids=Selection(selection);var snapshot=graph.Snapshot();
        var nodes=snapshot.Nodes.Where(n=>ids.Contains(n.Id)).ToArray();
        if(nodes.Length!=ids.Count)throw new ArgumentException("Selection contains missing nodes.",nameof(selection));
        return new(new(nodes,snapshot.Wires.Where(w=>ids.Contains(w.From.Node)&&ids.Contains(w.To.Node)).ToArray()));
    }
    public static GraphFragment FromSnapshot(ComponentRegistry registry,GraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(registry);ArgumentNullException.ThrowIfNull(snapshot);
        if(snapshot.Nodes is null||snapshot.Wires is null||snapshot.Nodes.Length>10_000||snapshot.Wires.Length>100_000)
            throw new ArgumentException("Invalid fragment or collection budget.");
        // Freeze parameter values, reject missing endpoints/types/cycles and detach
        // arrays before an externally supplied fragment can enter the live graph.
        return new(Graph.FromSnapshot(registry,snapshot).Snapshot());
    }
    public GraphSnapshot Snapshot()=>new(Nodes.ToArray(),Wires.ToArray());
    internal static HashSet<string> Selection(IEnumerable<string> source)
    {
        var ids=new HashSet<string>();var count=0;
        foreach(var id in source)
        {
            if(++count>100_000)throw new ArgumentException("Selection enumeration exceeds its budget.");
            if(string.IsNullOrEmpty(id))throw new ArgumentException("Missing node identity.");
            ids.Add(id);if(ids.Count>10_000)throw new ArgumentException("Too many selected nodes.");
        }
        return ids;
    }
    /// <summary>Paste under new identities in one undoable batch. Offsets are in
    /// canvas/model coordinates, independent of display zoom. Every parameter,
    /// matching, enable/preview flag, port and internal wire transform is retained.</summary>
    public ImmutableArray<string> Paste(GraphSession session,double offsetX=24,double offsetY=24)
    {
        ArgumentNullException.ThrowIfNull(session);
        if(!double.IsFinite(offsetX)||!double.IsFinite(offsetY))throw new ArgumentException("Finite paste offset required.");
        var target=session.Graph;
        if(target.NodeCount+Nodes.Length>10_000||target.WireCount+Wires.Length>100_000)
            throw new InvalidOperationException("Pasted fragment would exceed the graph budget.");
        if(Nodes.IsEmpty)return [];
        var map=Nodes.ToDictionary(n=>n.Id,_=>Guid.NewGuid().ToString("N"));
        var nodes=Nodes.Select(n=>n with{Id=map[n.Id],X=n.X+offsetX,Y=n.Y+offsetY}).ToArray();
        var wires=Wires.Select(w=>w with{Id=Guid.NewGuid().ToString("N"),From=new(map[w.From.Node],w.From.Port),To=new(map[w.To.Node],w.To.Port)}).ToArray();
        // Validate for this registry before touching the destination. Loading a
        // fragment never invokes plugin evaluation or changes registry definitions.
        var prepared=Graph.FromSnapshot(target.Registry,new(nodes,wires)).Snapshot();
        session.Edit("Paste components",g=>{
            foreach(var node in prepared.Nodes)
            {
                g.Add(node.Component,node.X,node.Y,node.Parameters,node.Name,node.Id);
                g.Update(node);
            }
            foreach(var wire in prepared.Wires)g.Connect(wire.From,wire.To,transform:wire.Transform,id:wire.Id);
        });
        return prepared.Nodes.Select(n=>n.Id).ToImmutableArray();
    }
}
