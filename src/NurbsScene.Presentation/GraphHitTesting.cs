namespace NurbsScene.Presentation;

public sealed record GraphPortHit(string Node,string Port,bool Output);

/// <summary>Nearest radial port hit, preserving draw-order and input/row ties.
/// Port columns share an X coordinate and rows are monotone in the authored
/// layout: reject distant columns, then test only the two bracketing rows.
/// A query still visits the nodes; this is not a spatial index for all geometry.</summary>
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
                var isOutput=side==1;
                var ports=isOutput?node.Definition.Outputs:node.Definition.Inputs;
                if(ports.IsDefaultOrEmpty)continue;
                var first=node.Port(isOutput,0);
                var dx=first.X-point.X;var horizontal=dx*dx;
                if(!double.IsFinite(horizontal)||!double.IsFinite(first.Y)||horizontal>distance)continue;
                if(node.Slider)
                {
                    Consider(node,0,isOutput,horizontal,first.Y-point.Y,ref distance,ref best,ref portIndex,ref output);
                }
                else if(ports.Length>int.MaxValue/19-1)
                {
                    // PresentedNode.Port uses an integer row offset. Retain an
                    // exhaustive path if a custom catalog exceeds that range.
                    for(var i=0;i<ports.Length;i++)
                        Consider(node,i,isOutput,horizontal,node.Port(isOutput,i).Y-point.Y,ref distance,ref best,ref portIndex,ref output);
                }
                else
                {
                    var upper=LowerBound(node,isOutput,ports.Length,point.Y);
                    if(upper>0)
                    {
                        var lower=upper-1;var y=node.Port(isOutput,lower).Y;
                        // At very large world coordinates several authored rows
                        // round to the same binary64 Y. Choose the first row in
                        // that plateau, exactly as the original ascending scan.
                        if(lower>0&&node.Port(isOutput,lower-1).Y==y)
                            lower=LowerBound(node,isOutput,lower+1,y);
                        Consider(node,lower,isOutput,horizontal,y-point.Y,ref distance,ref best,ref portIndex,ref output);
                    }
                    if(upper<ports.Length)
                        Consider(node,upper,isOutput,horizontal,node.Port(isOutput,upper).Y-point.Y,ref distance,ref best,ref portIndex,ref output);
                }
                // No remaining port can beat an exact hit; equal distances must
                // keep this frontmost node, first side and first row.
                if(best is not null&&distance==0)return Hit(best,portIndex,output);
            }
        }
        return best is null?null:Hit(best,portIndex,output);
    }

    private static int LowerBound(PresentedNode node,bool output,int count,double y)
    {
        var lo=0;var hi=count;
        while(lo<hi)
        {
            var middle=lo+(hi-lo)/2;
            if(node.Port(output,middle).Y<y)lo=middle+1;else hi=middle;
        }
        return lo;
    }

    private static void Consider(PresentedNode node,int index,bool isOutput,double horizontal,double dy,
        ref double distance,ref PresentedNode? best,ref int portIndex,ref bool output)
    {
        var squared=horizontal+dy*dy;
        if(!double.IsFinite(squared)||squared>distance||squared==distance&&best is not null)return;
        distance=squared;best=node;portIndex=index;output=isOutput;
    }

    private static GraphPortHit Hit(PresentedNode node,int index,bool output)
        =>new(node.Node.Id,(output?node.Definition.Outputs:node.Definition.Inputs)[index].Name,output);
}
