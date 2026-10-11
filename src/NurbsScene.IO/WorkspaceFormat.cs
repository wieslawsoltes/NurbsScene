using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Model;
using NurbsScene.Parametric;
using NurbsScene.Rendering;

namespace NurbsScene.IO;

public sealed record Workspace(SceneDocument Document, Graph Graph);
/// <summary>Versioned, exact managed workspace interchange. Not .3dm, .gh, or the JS validation-harness dialect.</summary>
public static class WorkspaceFormat
{
    public const string Format = "nurbsscene.managed-workspace";
    public const int MaximumBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 64, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public sealed record GeometryData(string Kind, int Degree, int DegreeU, int DegreeV, int CountU, int CountV, double[] Knots, double[] KnotsU, double[] KnotsV, double[][] Controls);
    public sealed record ObjectData(string Id, string Name, string LayerId, ColorRgba? Color, bool Visible, bool Locked, string? Source, GeometryData Geometry);
    public sealed record NodeData(string Id, string Component, double X, double Y, Dictionary<string,JsonElement> Parameters, string? Name, bool Enabled, bool Preview, string Matching);
    public sealed record DocumentData(string Name, string Units, double AbsoluteTolerance, double AngleTolerance, string ActiveLayer, Layer[] Layers, ObjectData[] Objects, string[] Selection);
    public sealed record GraphData(NodeData[] Nodes, Wire[] Wires);
    public sealed record WorkspaceData(string Format, int Version, DocumentData Document, GraphData Graph);
    public static GeometryData EncodeGeometry(IGeometry geometry)
    {
        static double[][] Points(ReadOnlySpan<HPoint> points) => points.ToArray().Select(p => new[] { p.X,p.Y,p.Z,p.W }).ToArray();
        return geometry switch {
            NurbsCurve c => new("nurbs-curve",c.Degree,0,0,0,0,c.Knots.ToArray(),[],[],Points(c.ControlPoints)),
            NurbsSurface s => new("nurbs-surface",0,s.DegreeU,s.DegreeV,s.CountU,s.CountV,[],s.KnotsU.ToArray(),s.KnotsV.ToArray(),Points(s.ControlPoints)),
            _ => throw new NotSupportedException($"No exact serializer for {geometry.GetType().Name}.")
        };
    }
    public static IGeometry DecodeGeometry(INurbsEvaluator evaluator, GeometryData data)
    {
        if(data.Controls is null || data.Controls.Length>1_000_000)throw new FormatException("Invalid geometry control count.");
        var points = data.Controls.Select(p => p is {Length:4} ? new HPoint(p[0],p[1],p[2],p[3]) : throw new FormatException("Expected homogeneous XYZW controls.")).ToArray();
        return data.Kind switch {
            "nurbs-curve" => new NurbsCurve(evaluator,data.Degree,data.Knots,points),
            "nurbs-surface" => new NurbsSurface(evaluator,data.DegreeU,data.DegreeV,data.CountU,data.CountV,data.KnotsU,data.KnotsV,points),
            _ => throw new NotSupportedException($"Unsupported exact geometry: {data.Kind}.")
        };
    }
    private static JsonElement EncodeValue(object? value)
    {
        object? encoded = value switch {
            null or bool or string or int => value,
            double d when double.IsFinite(d) => d,
            DataPath path => new { valueType="path", value=path.Indices.ToArray() },
            Vec3 v when v.IsFinite => new { valueType="vector", value=v.ToArray() },
            IGeometry geometry => new { valueType="geometry", value=EncodeGeometry(geometry) },
            DataTree tree => new { valueType="tree", branches=tree.Branches.Select(b=>new{path=b.Path.Indices.ToArray(),items=b.Items.Select(EncodeValue).ToArray()}).ToArray() },
            IReadOnlyDictionary<string,object?> map => new { valueType="map", value=map.ToDictionary(kv=>kv.Key,kv=>EncodeValue(kv.Value)) },
            System.Collections.IEnumerable items when value is not string => items.Cast<object?>().Select(EncodeValue).ToArray(),
            _ => throw new NotSupportedException($"Parameter type {value.GetType().Name} is not serializable.")
        };
        return JsonSerializer.SerializeToElement(encoded,Options);
    }
    private static object? DecodeValue(INurbsEvaluator evaluator,JsonElement value)
    {
        switch(value.ValueKind)
        {
            case JsonValueKind.Null:return null;
            case JsonValueKind.String:return value.GetString();
            case JsonValueKind.True:return true;
            case JsonValueKind.False:return false;
            case JsonValueKind.Number:if(value.TryGetInt32(out var integer))return integer;return value.GetDouble();
            case JsonValueKind.Array:return value.EnumerateArray().Select(v=>DecodeValue(evaluator,v)).ToImmutableArray();
            case JsonValueKind.Object:
                var type=value.GetProperty("valueType").GetString();
                if(type=="path")return new DataPath(value.GetProperty("value").EnumerateArray().Select(x=>x.GetInt32()));
                if(type=="vector"){var a=value.GetProperty("value").EnumerateArray().Select(x=>x.GetDouble()).ToArray();if(a.Length!=3)throw new FormatException("Expected XYZ.");var p=new Vec3(a[0],a[1],a[2]);if(!p.IsFinite)throw new FormatException("Invalid vector.");return p;}
                if(type=="geometry")return DecodeGeometry(evaluator,value.GetProperty("value").Deserialize<GeometryData>(Options)??throw new FormatException("Missing geometry."));
                if(type=="tree")return new DataTree(value.GetProperty("branches").EnumerateArray().Select(b=>new DataBranch(new DataPath(b.GetProperty("path").EnumerateArray().Select(x=>x.GetInt32())),b.GetProperty("items").EnumerateArray().Select(x=>DecodeValue(evaluator,x)).ToImmutableArray())));
                if(type=="map")return value.GetProperty("value").EnumerateObject().ToImmutableDictionary(p=>p.Name,p=>DecodeValue(evaluator,p.Value));
                throw new FormatException($"Unsupported parameter value type: {type}.");
            default:throw new FormatException("Unsupported parameter value.");
        }
    }
    public static string Serialize(SceneDocument document,Graph graph)
    {
        var d=document.Snapshot();var snapshot=graph.Snapshot();
        var data=new WorkspaceData(Format,1,new(d.Name,d.Units,d.AbsoluteTolerance,d.AngleTolerance,d.ActiveLayer,d.Layers,d.Objects.Select(o=>new ObjectData(o.Id,o.Name,o.LayerId,o.Color,o.Visible,o.Locked,o.Source,EncodeGeometry(o.Geometry))).ToArray(),d.Selection),
            new(snapshot.Nodes.Select(n=>new NodeData(n.Id,n.Component,n.X,n.Y,n.Parameters.ToDictionary(kv=>kv.Key,kv=>EncodeValue(kv.Value)),n.Name,n.Enabled,n.Preview,n.Matching)).ToArray(),snapshot.Wires));
        var json=JsonSerializer.Serialize(data,Options);if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new InvalidOperationException("Workspace exceeds the export budget.");return json;
    }
    public static Workspace Deserialize(INurbsEvaluator evaluator,ComponentRegistry registry,string json)
    {
        if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new FormatException("Workspace exceeds the 64 MiB import limit.");
        var data=JsonSerializer.Deserialize<WorkspaceData>(json,Options)??throw new FormatException("Missing workspace.");
        if(data.Format!=Format||data.Version!=1)throw new NotSupportedException("Not a supported managed NurbsScene workspace. Rhino/Grasshopper and JS-harness documents have different formats.");
        var d=data.Document;var g=data.Graph;
        if(d is null||g is null||d.Objects is null||d.Layers is null||g.Nodes is null||g.Wires is null||d.Objects.Length>100000||d.Layers.Length>10000||g.Nodes.Length>10000||g.Wires.Length>100000)throw new FormatException("Invalid workspace or collection budget.");
        if(d.Units is not("mm" or "cm" or "m" or "in" or "ft")||!double.IsFinite(d.AbsoluteTolerance)||!double.IsFinite(d.AngleTolerance))throw new FormatException("Invalid document units or tolerance.");
        if(d.Layers.Any(l=>!l.Color.IsValid))throw new FormatException("Invalid layer color.");
        var objects=d.Objects.Select(o=>new SceneObject(o.Id,o.Name,DecodeGeometry(evaluator,o.Geometry),o.LayerId,o.Color,o.Visible,o.Locked,o.Source)).ToArray();
        if(objects.Any(o=>o.Color is{} c&&!c.IsValid))throw new FormatException("Invalid object color.");
        var document=SceneDocument.FromSnapshot(new(d.Name,d.Units,d.AbsoluteTolerance,d.AngleTolerance,d.ActiveLayer,d.Layers,objects,d.Selection??[]));
        var graph=Graph.FromSnapshot(registry,new(g.Nodes.Select(n=>new GraphNode(n.Id,n.Component,n.X,n.Y,n.Parameters.ToImmutableDictionary(kv=>kv.Key,kv=>DecodeValue(evaluator,kv.Value)),n.Name,n.Enabled,n.Preview,n.Matching)).ToArray(),g.Wires));
        // Both graphs are prepared and validated before a caller replaces its active workspace.
        return new(document,graph);
    }
    public static string ExportObj(IEnumerable<SceneObject> objects,int segments=64)
    {
        var text=new StringBuilder("# NurbsScene display mesh; not an exact NURBS or B-rep interchange file.\n");var offset=1;
        string F(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
        foreach(var item in objects)
        {
            var mesh=MeshBuilder.Build(item.Geometry,Vec3.Zero,segments);text.Append("o ").AppendLine(string.Concat(item.Name.Select(c=>char.IsLetterOrDigit(c)?c:'_')));
            foreach(var p in mesh.Positions)text.AppendLine($"v {F(p.X)} {F(p.Y)} {F(p.Z)}");
            if(mesh.Indices.Length==0)for(var i=1;i<mesh.Positions.Length;i++)text.AppendLine($"l {offset+i-1} {offset+i}");
            else for(var i=0;i<mesh.Indices.Length;i+=3)text.AppendLine($"f {offset+mesh.Indices[i]} {offset+mesh.Indices[i+1]} {offset+mesh.Indices[i+2]}");
            offset=checked(offset+mesh.Positions.Length);
        }
        return text.ToString();
    }
    public static byte[] ExportStl(IEnumerable<SceneObject> objects,int segments=64)
    {
        var meshes=objects.Where(o=>o.Geometry is NurbsSurface).Select(o=>MeshBuilder.Build(o.Geometry,Vec3.Zero,segments)).ToArray();var count=meshes.Sum(m=>(long)m.Indices.Length/3);if(count>10_000_000)throw new InvalidOperationException("STL triangle budget exceeded.");
        using var stream=new MemoryStream(checked((int)(84+count*50)));using var writer=new BinaryWriter(stream,Encoding.ASCII,true);var header=new byte[80];Encoding.ASCII.GetBytes("NurbsScene display mesh; open surfaces; not a solid certificate.".AsSpan(),header.AsSpan());writer.Write(header);writer.Write((uint)count);
        void Vector(Vec3 p){writer.Write((float)p.X);writer.Write((float)p.Y);writer.Write((float)p.Z);}
        foreach(var mesh in meshes)for(var i=0;i<mesh.Indices.Length;i+=3){var a=mesh.Positions[mesh.Indices[i]];var b=mesh.Positions[mesh.Indices[i+1]];var c=mesh.Positions[mesh.Indices[i+2]];var n=Vec3.Cross(b-a,c-a);Vector(n.Length>0?n.Normalized():Vec3.Zero);Vector(a);Vector(b);Vector(c);writer.Write((ushort)0);}writer.Flush();return stream.ToArray();
    }
}
