namespace NurbsScene.Parametric;

public readonly record struct NodePosition(string Id,double X,double Y);
public static class GraphPresentationEdits
{
    /// <summary>Owner-thread pointer move: validate the entire set before editing,
    /// then publish one notification. Each changed node keeps its normal revision
    /// increment; numerical results and topology remain clean. The caller owns
    /// history (one GraphSession.Record per completed gesture).</summary>
    public static void MoveNodes(this Graph graph,IReadOnlyList<NodePosition> positions)
    {
        ArgumentNullException.ThrowIfNull(graph);ArgumentNullException.ThrowIfNull(positions);
        if(positions.Count>10_000)throw new ArgumentException("Node move budget exceeded.");
        var prepared=new List<GraphNode>(positions.Count);var ids=new HashSet<string>();
        foreach(var p in positions)
        {
            if(!ids.Add(p.Id)||!double.IsFinite(p.X)||!double.IsFinite(p.Y))throw new ArgumentException("Distinct identities and finite positions required.");
            var node=graph.GetNode(p.Id);if(node.X!=p.X||node.Y!=p.Y)prepared.Add(node with{X=p.X,Y=p.Y});
        }
        if(prepared.Count==0)return;
        graph.BeginEdit();
        try{foreach(var node in prepared)graph.Update(node);}
        finally{graph.EndEdit(true);}
    }
}
