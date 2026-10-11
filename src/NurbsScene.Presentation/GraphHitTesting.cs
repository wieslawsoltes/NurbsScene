namespace NurbsScene.Presentation;

public sealed record GraphPortHit(string Node,string Port,bool Output);

/// <summary>Shared hit testing for the actual drawing layout. At low zoom a pixel
/// tolerance can span several port rows: choose the closest port, not the first
/// row whose rectangle contains the pointer. Equal-distance ties prefer the
/// frontmost node (the last node drawn).</summary>
public static class GraphHitTesting
{
    public static GraphPortHit? PortAt(GraphDrawing drawing,CanvasPoint point,double tolerance)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        if(!double.IsFinite(point.X)||!double.IsFinite(point.Y)||!double.IsFinite(tolerance)||tolerance<0||tolerance>10_000)
            throw new ArgumentOutOfRangeException(nameof(tolerance),"Finite point and bounded nonnegative tolerance required.");
        var distance=tolerance*tolerance;PresentedNode? best=null;var portIndex=0;var output=false;
        for(var n=drawing.Nodes.Length-1;n>=0;n--)
        {
            var node=drawing.Nodes[n];
            for(var side=0;side<2;side++)
            {
                var ports=side==1?node.Definition.Outputs:node.Definition.Inputs;
                for(var i=0;i<ports.Length;i++)
                {
                    var port=node.Port(side==1,i);var dx=port.X-point.X;var dy=port.Y-point.Y;var squared=dx*dx+dy*dy;
                    if(squared>distance||squared==distance&&best is not null)continue;
                    distance=squared;best=node;portIndex=i;output=side==1;
                }
            }
        }
        return best is null?null:new(best.Node.Id,(output?best.Definition.Outputs:best.Definition.Inputs)[portIndex].Name,output);
    }
}
