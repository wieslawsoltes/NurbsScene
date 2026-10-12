namespace NurbsScene.Parametric;

public sealed partial class Graph
{
    private IReadOnlyList<GraphNode>? _nodesView;
    private IReadOnlyList<Wire>? _wiresView;
    private long _wiresViewRevision=-1;

    /// <summary>Read-only node snapshot retained until a node is added, removed
    /// or updated. The returned collection never changes after publication.</summary>
    public IReadOnlyList<GraphNode> Nodes
    {
        get {lock(_gate)return _nodesView??=Array.AsReadOnly(_nodes.Values.ToArray());}
    }
    /// <summary>Read-only ordered wire snapshot retained across node-only edits.
    /// Graph.Snapshot still returns detached arrays for serialization/history.</summary>
    public IReadOnlyList<Wire> Wires
    {
        get
        {
            lock(_gate)
            {
                if(_wiresView is null||_wiresViewRevision!=WireRevision)
                {_wiresView=Array.AsReadOnly(_wires.Values.ToArray());_wiresViewRevision=WireRevision;}
                return _wiresView;
            }
        }
    }
}
