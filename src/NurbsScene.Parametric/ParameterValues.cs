using System.Collections.Immutable;
using NurbsScene.Geometry;

namespace NurbsScene.Parametric;

/// <summary>Opt-in contract for immutable plug-in values. Mutating an implementation violates the graph cache contract.</summary>
public interface IImmutableGraphValue { }
public static class ParameterValues
{
    public static ImmutableDictionary<string,object?> Freeze(IReadOnlyDictionary<string,object?>? parameters)
        => parameters is null?ImmutableDictionary<string,object?>.Empty:parameters.ToImmutableDictionary(kv=>kv.Key,kv=>FreezeValue(kv.Value));
    public static object? FreezeValue(object? value,int depth=0)
    {
        if(depth>64)throw new ArgumentException("Graph values exceed the nesting limit.");
        switch(value)
        {
            case null or bool or int:return value;
            case double d when double.IsFinite(d):return d;
            case string text when text.Length<=1_000_000:return text;
            case Vec3 vector when vector.IsFinite:return vector;
            case NurbsCurve or NurbsSurface or IImmutableGraphValue:return value;
            case DataTree tree:return new DataTree(tree.Branches.Select(b=>new DataBranch(b.Path,b.Items.Select(v=>FreezeValue(v,depth+1)).ToImmutableArray())));
            case IReadOnlyDictionary<string,object?> map:return map.ToImmutableDictionary(kv=>kv.Key,kv=>FreezeValue(kv.Value,depth+1));
            case System.Collections.IEnumerable sequence:
                var output=ImmutableArray.CreateBuilder<object?>();foreach(var item in sequence){if(output.Count>=1_000_000)throw new ArgumentException("Graph value exceeds the item limit.");output.Add(FreezeValue(item,depth+1));}return output.ToImmutable();
            default:throw new ArgumentException($"Non-finite, mutable or unsupported graph value: {value.GetType().Name}.");
        }
    }
}
