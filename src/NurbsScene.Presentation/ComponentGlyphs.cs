namespace NurbsScene.Presentation;

/// <summary>Authored component-symbol selection shared by shelves and canvas.
/// Operator labels identify actual operations instead of repeating a category icon.</summary>
public static class ComponentGlyphs
{
    public static string IconKey(string id,string category)
    {
        ArgumentNullException.ThrowIfNull(id);ArgumentNullException.ThrowIfNull(category);
        var operation=id switch{
            "Add"=>"+","Subtract"=>"−","Multiply"=>"×","Divide"=>"÷","Power"=>"x^n",
            "Minimum"=>"min","Maximum"=>"max","Sine"=>"sin","Cosine"=>"cos","Tangent"=>"tan",
            "ArcSine"=>"asin","ArcCosine"=>"acos","ArcTangent"=>"atan","Sqrt"=>"sqrt",
            "Absolute"=>"|x|","Floor"=>"floor","Ceiling"=>"ceil","Round"=>"round",
            "Exponential"=>"exp","Logarithm"=>"ln","Degrees"=>"deg","Radians"=>"rad",
            "Negate"=>"−x","Clamp"=>"clamp","Integer"=>"123","Boolean"=>"T/F",_=>null};
        if(operation is not null)return "op:"+operation;
        if(id is "Flatten" or "Graft" or "Simplify")return id;
        if(id=="Text")return "Panel";
        if(id.Contains("Centroid",StringComparison.Ordinal))return "Centroid";
        if(id.Contains("Area",StringComparison.Ordinal))return "Area";
        if(id.Contains("Mirror",StringComparison.Ordinal))return "Mirror";
        if(id.Contains("Project",StringComparison.Ordinal))return "Project";
        if(id.Contains("Reverse",StringComparison.Ordinal))return "Reverse";
        if(id.Contains("Knot",StringComparison.Ordinal))return "Knot";
        if(id.Contains("Elevate",StringComparison.Ordinal))return "Elevate";
        if(id.Contains("Split",StringComparison.Ordinal)||id.Contains("Shatter",StringComparison.Ordinal))return "Split";
        if(id.Contains("Circle",StringComparison.Ordinal))return "Circle";
        if(id.Contains("Line",StringComparison.Ordinal))return "Line";
        if(id.Contains("Extrude",StringComparison.Ordinal))return "Extrude";
        if(id.Contains("Revolve",StringComparison.Ordinal))return "Revolve";
        if(id.Contains("Loft",StringComparison.Ordinal))return "Loft";
        if(id.Contains("Length",StringComparison.Ordinal))return "Measure";
        return category;
    }
}
