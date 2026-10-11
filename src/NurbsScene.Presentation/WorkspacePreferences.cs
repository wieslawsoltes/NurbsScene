using System.Globalization;

namespace NurbsScene.Presentation;

/// <summary>Small versioned local UI preference record; never embeds document or
/// executable command data. Invalid persisted values fall back as one unit.</summary>
public sealed record WorkspacePreferences(string Dock="Right",bool DefinitionVisible=true,bool InspectorVisible=true,double Split=.55)
{
    public string Encode()
    {
        if(Dock is not("Right" or "Bottom")||!double.IsFinite(Split)||Split<.2||Split>.8)throw new ArgumentException("Invalid workspace layout.");
        return $"1|{Dock}|{(DefinitionVisible?1:0)}|{(InspectorVisible?1:0)}|{Split.ToString("R",CultureInfo.InvariantCulture)}";
    }
    public static bool TryParse(string? text,out WorkspacePreferences value)
    {
        value=new();if(text is null||text.Length>128)return false;var parts=text.Split('|');
        if(parts.Length!=5||parts[0]!="1"||parts[1] is not("Right" or "Bottom")||parts[2] is not("0" or "1")||parts[3] is not("0" or "1")||!double.TryParse(parts[4],NumberStyles.Float,CultureInfo.InvariantCulture,out var split)||!double.IsFinite(split)||split<.2||split>.8)return false;
        value=new(parts[1],parts[2]=="1",parts[3]=="1",split);return true;
    }
}
