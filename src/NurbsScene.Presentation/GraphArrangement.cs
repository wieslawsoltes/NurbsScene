using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

public enum GraphArrangementMode { Left,HorizontalCenter,Right,Top,VerticalCenter,Bottom,DistributeHorizontal,DistributeVertical }

/// <summary>Deterministic canvas alignment using actual node bounds. Alignment
/// uses the selected envelope; distribution equally spaces centers while retaining
/// the first/last center. It may overlap nodes when the available span is small.
/// No topology, parameters or numeric solution outputs are modified.</summary>
public static class GraphArrangement
{
    public static void Apply(GraphSession session,IEnumerable<string> selection,GraphArrangementMode mode)
    {
        ArgumentNullException.ThrowIfNull(session);ArgumentNullException.ThrowIfNull(selection);
        if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));
        var requested=selection.Take(10_001).ToArray();
        if(requested.Length>10_000)throw new ArgumentException("Arrangement selection exceeds the node budget.");
        var ids=requested.ToHashSet();var graph=session.Graph;
        var nodes=graph.Nodes.Where(n=>ids.Contains(n.Id)).ToArray();
        if(nodes.Length!=ids.Count)throw new ArgumentException("Arrangement selection contains missing nodes.");
        if(nodes.Length<2)return;
        var bounds=nodes.Select(n=>GraphPresentation.Bounds(n,graph.Registry.Get(n.Component))).ToArray();
        var x0=bounds.Min(b=>b.X);var y0=bounds.Min(b=>b.Y);
        var x1=bounds.Max(b=>b.X+b.Width);var y1=bounds.Max(b=>b.Y+b.Height);
        var prepared=new GraphNode[nodes.Length];
        if(mode is GraphArrangementMode.DistributeHorizontal or GraphArrangementMode.DistributeVertical)
        {
            if(nodes.Length<3)return;
            var horizontal=mode==GraphArrangementMode.DistributeHorizontal;
            double Center(int i)=>horizontal?bounds[i].X+bounds[i].Width/2:bounds[i].Y+bounds[i].Height/2;
            var order=Enumerable.Range(0,nodes.Length).OrderBy(Center).ThenBy(i=>i).ToArray();
            var start=Center(order[0]);var end=Center(order[^1]);
            for(var k=0;k<order.Length;k++)
            {
                var i=order[k];var fraction=k/(double)(order.Length-1);var center=start*(1-fraction)+end*fraction;
                prepared[i]=horizontal?nodes[i] with{X=center-bounds[i].Width/2}:nodes[i] with{Y=center-bounds[i].Height/2};
            }
        }
        else for(var i=0;i<nodes.Length;i++)
        {
            var n=nodes[i];var b=bounds[i];prepared[i]=mode switch{
                GraphArrangementMode.Left=>n with{X=x0},GraphArrangementMode.Right=>n with{X=x1-b.Width},
                GraphArrangementMode.HorizontalCenter=>n with{X=x0/2+x1/2-b.Width/2},
                GraphArrangementMode.Top=>n with{Y=y0},GraphArrangementMode.Bottom=>n with{Y=y1-b.Height},
                GraphArrangementMode.VerticalCenter=>n with{Y=y0/2+y1/2-b.Height/2},_=>throw new ArgumentOutOfRangeException(nameof(mode))};
        }
        if(prepared.Any(n=>!double.IsFinite(n.X)||!double.IsFinite(n.Y)))throw new ArithmeticException("Arrangement exceeds finite canvas coordinates.");
        if(prepared.SequenceEqual(nodes))return;
        session.Edit("Arrange components: "+mode,g=>{foreach(var node in prepared)g.Update(node);});
    }
}
