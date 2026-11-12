namespace NurbsScene.Presentation;

public readonly record struct FloatingPaneBounds(double X,double Y,double Width,double Height);

/// <summary>Normalized placement survives window-size and DPI changes. Resolved
/// bounds stay within the host, including hosts smaller than the preferred size.</summary>
public readonly record struct FloatingPanePlacement(double X,double Y,double Width,double Height,bool Folded=false)
{
    public static FloatingPanePlacement Default=>new(.34,.07,.62,.79);
    public const double FoldedHeight=28;
    private const double Inset=4;

    public void Validate()
    {
        if(!double.IsFinite(X)||!double.IsFinite(Y)||!double.IsFinite(Width)||!double.IsFinite(Height)
            ||X<0||Y<0||Y>1||Width<=0||Height<=0||Width>1||Height>1||X+Width>1+1e-12||(!Folded&&Y+Height>1+1e-12))
            throw new ArgumentException("Floating pane placement must be finite and contained within the workspace.");
    }
    public FloatingPanePlacement WithFolded(bool folded)
    {
        Validate();return this with{Folded=folded,Y=folded?Y:Math.Min(Y,1-Height)};
    }
    public FloatingPaneBounds Resolve(double availableWidth,double availableHeight)
    {
        Validate();ValidateArea(availableWidth,availableHeight);
        var insetX=Math.Min(Inset,availableWidth/2);var insetY=Math.Min(Inset,availableHeight/2);
        var maximumWidth=Math.Max(0,availableWidth-2*insetX);var maximumHeight=Math.Max(0,availableHeight-2*insetY);
        var width=Math.Clamp(Width*availableWidth,Math.Min(560,maximumWidth),maximumWidth);
        var height=Folded?Math.Min(FoldedHeight,maximumHeight):Math.Clamp(Height*availableHeight,Math.Min(320,maximumHeight),maximumHeight);
        // At very large finite dimensions subtracting a small inset can round
        // back to the original extent. Keep the clamp interval ordered.
        return new(Math.Clamp(X*availableWidth,insetX,Math.Max(insetX,availableWidth-insetX-width)),
            Math.Clamp(Y*availableHeight,insetY,Math.Max(insetY,availableHeight-insetY-height)),width,height);
    }
    /// <summary>Store the expanded bounds. Folding is a visibility state and
    /// never overwrites the remembered expanded height.</summary>
    public static FloatingPanePlacement FromBounds(FloatingPaneBounds bounds,double availableWidth,double availableHeight,bool folded=false)
    {
        ValidateArea(availableWidth,availableHeight);
        if(availableWidth==0||availableHeight==0)throw new ArgumentException("A nonempty workspace is required to save placement.");
        if(!double.IsFinite(bounds.X)||!double.IsFinite(bounds.Y)||!double.IsFinite(bounds.Width)||!double.IsFinite(bounds.Height)
            ||bounds.Width<=0||bounds.Height<=0)throw new ArgumentException("Finite positive pane bounds are required.");
        var width=Math.Clamp(bounds.Width,Math.Min(560,availableWidth),availableWidth);
        var height=Math.Clamp(bounds.Height,Math.Min(320,availableHeight),availableHeight);
        var visibleHeight=folded?Math.Min(FoldedHeight,availableHeight):height;
        var x=Math.Clamp(bounds.X,0,availableWidth-width);var y=Math.Clamp(bounds.Y,0,availableHeight-visibleHeight);
        return new(x/availableWidth,y/availableHeight,width/availableWidth,height/availableHeight,folded);
    }
    private static void ValidateArea(double width,double height)
    {
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<0||height<0)
            throw new ArgumentException("Workspace dimensions must be finite and nonnegative.");
    }
}
