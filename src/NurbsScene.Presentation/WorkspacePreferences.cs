using System.Globalization;

namespace NurbsScene.Presentation;

/// <summary>Versioned view-only preferences. Version 1 migrates without changing
/// its visible panels. Invalid records are rejected atomically, never clamped.</summary>
public sealed record WorkspacePreferences(
    string Dock="Right", bool DefinitionVisible=false, bool InspectorVisible=true,
    double Split=.55, double InspectorWidth=250, bool SidebarVisible=true,
    bool HistoryExpanded=false, string InspectorTab="Properties",
    double FloatingX=.34,double FloatingY=.07,double FloatingWidth=.62,double FloatingHeight=.79,bool FloatingFolded=false)
{
    public FloatingPanePlacement Placement=>new(FloatingX,FloatingY,FloatingWidth,FloatingHeight,FloatingFolded);
    public WorkspacePreferences WithPlacement(FloatingPanePlacement placement)
    {
        placement.Validate();
        return this with{FloatingX=placement.X,FloatingY=placement.Y,FloatingWidth=placement.Width,
            FloatingHeight=placement.Height,FloatingFolded=placement.Folded};
    }
    public string Encode()
    {
        if(Dock is not("Right" or "Bottom" or "Float") || !double.IsFinite(Split) || Split<.2 || Split>.8
            || !double.IsFinite(InspectorWidth) || InspectorWidth<200 || InspectorWidth>480
            || InspectorTab is not("Properties" or "Layers" or "Objects"))
            throw new ArgumentException("Invalid workspace layout.");
        Placement.Validate();
        var extended=Dock=="Float"||Placement!=FloatingPanePlacement.Default;
        var common=string.Join('|', extended?"3":"2", Dock, Flag(DefinitionVisible), Flag(InspectorVisible),
            Split.ToString("R",CultureInfo.InvariantCulture), InspectorWidth.ToString("R",CultureInfo.InvariantCulture),
            Flag(SidebarVisible), Flag(HistoryExpanded), InspectorTab);
        return !extended?common:common+"|"+string.Join('|',FloatingX.ToString("R",CultureInfo.InvariantCulture),
            FloatingY.ToString("R",CultureInfo.InvariantCulture),FloatingWidth.ToString("R",CultureInfo.InvariantCulture),
            FloatingHeight.ToString("R",CultureInfo.InvariantCulture),Flag(FloatingFolded));
    }
    private static string Flag(bool value)=>value?"1":"0";
    private static bool Number(string text,out double value)=>
        double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&double.IsFinite(value);
    private static bool Bit(string text)=>text is "0" or "1";
    public static bool TryParse(string? text,out WorkspacePreferences value)
    {
        value=new(); if(text is null || text.Length>256)return false;
        var p=text.Split('|');
        if(p.Length is not(5 or 9 or 14) || p[1] is not("Right" or "Bottom" or "Float") || !Bit(p[2]) || !Bit(p[3])
            || !Number(p[4],out var split) || split<.2 || split>.8)return false;
        if(p.Length==5)
        {
            if(p[0]!="1"||p[1]=="Float")return false;
            value=new(p[1],p[2]=="1",p[3]=="1",split,InspectorTab:"Layers");return true;
        }
        if((p.Length==9?(p[0]!="2"||p[1]=="Float"):p[0]!="3") || !Number(p[5],out var width) || width<200 || width>480 || !Bit(p[6]) || !Bit(p[7])
            || p[8] is not("Properties" or "Layers" or "Objects"))return false;
        var parsed=new WorkspacePreferences(p[1],p[2]=="1",p[3]=="1",split,width,p[6]=="1",p[7]=="1",p[8]);
        if(p.Length==14)
        {
            if(!Number(p[9],out var x)||!Number(p[10],out var y)||!Number(p[11],out var w)||!Number(p[12],out var h)||!Bit(p[13]))return false;
            try{parsed=parsed.WithPlacement(new(x,y,w,h,p[13]=="1"));}catch(ArgumentException){return false;}
        }
        value=parsed;return true;
    }
}
