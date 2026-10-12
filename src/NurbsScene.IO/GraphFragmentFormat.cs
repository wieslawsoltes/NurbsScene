using System.Text;
using NurbsScene.Geometry;
using NurbsScene.Model;
using NurbsScene.Parametric;

namespace NurbsScene.IO;

/// <summary>Versioned text clipboard payload using the existing exact workspace
/// value codec. It contains an empty document and only the selected definition.
/// This is not Grasshopper's proprietary clipboard, .gh or .ghx format.</summary>
public static class GraphFragmentFormat
{
    public const string Header="NurbsScene.GraphFragment/1\n";
    public const int MaximumBytes=8*1024*1024;
    public static string Serialize(GraphFragment fragment,ComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(fragment);ArgumentNullException.ThrowIfNull(registry);
        var graph=Graph.FromSnapshot(registry,fragment.Snapshot());
        var text=Header+WorkspaceFormat.Serialize(new SceneDocument(),graph);
        if(Encoding.UTF8.GetByteCount(text)>MaximumBytes)throw new InvalidOperationException("Graph clipboard exceeds 8 MiB.");
        return text;
    }
    public static GraphFragment Deserialize(INurbsEvaluator evaluator,ComponentRegistry registry,string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if(!text.StartsWith(Header,StringComparison.Ordinal))throw new FormatException("Clipboard does not contain a NurbsScene graph fragment.");
        if(text.Length>MaximumBytes||Encoding.UTF8.GetByteCount(text)>MaximumBytes)throw new FormatException("Graph clipboard exceeds 8 MiB.");
        var workspace=WorkspaceFormat.Deserialize(evaluator,registry,text[Header.Length..]);
        if(workspace.Document.Objects.Count!=0)throw new FormatException("A graph fragment cannot include document objects.");
        return GraphFragment.Capture(workspace.Graph,workspace.Graph.Nodes.Select(n=>n.Id));
    }
}
