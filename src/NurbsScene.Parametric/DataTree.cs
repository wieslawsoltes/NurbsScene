using System.Collections.Immutable;

namespace NurbsScene.Parametric;

public sealed partial class DataPath : IComparable<DataPath>,IEquatable<DataPath>
{
    public ImmutableArray<int> Indices { get; }
    public static DataPath Root { get; }=new([0]);
    public DataPath(IEnumerable<int> indices){ArgumentNullException.ThrowIfNull(indices);Indices=indices.Take(65).ToImmutableArray();if(Indices.Length>64||Indices.Any(i=>i<0))throw new ArgumentException("Data paths require at most 64 nonnegative indices.");}
    public int CompareTo(DataPath? other){if(other is null)return 1;for(var i=0;i<Math.Min(Indices.Length,other.Indices.Length);i++){var c=Indices[i].CompareTo(other.Indices[i]);if(c!=0)return c;}return Indices.Length.CompareTo(other.Indices.Length);}
    public bool Equals(DataPath? other)=>other is not null&&Indices.SequenceEqual(other.Indices);
    public override bool Equals(object? obj)=>obj is DataPath path&&Equals(path);
    public override int GetHashCode(){var hash=new HashCode();foreach(var i in Indices)hash.Add(i);return hash.ToHashCode();}
    public override string ToString()=>"{"+string.Join(';',Indices)+"}";
    public DataPath Append(int index)=>new(Indices.Add(index));
}
public sealed record DataBranch(DataPath Path,ImmutableArray<object?> Items);
/// <summary>Immutable branches with numeric lexicographic path ordering. Branch matching is explicitly defined by Graph, not claimed to match all Grasshopper cases.</summary>
public sealed partial class DataTree
{
    public ImmutableArray<DataBranch> Branches { get; }
    public int ItemCount { get; }
    public static DataTree Empty { get; }=new([]);
    public DataTree(IEnumerable<DataBranch> branches)
    {
        var map=new SortedDictionary<DataPath,List<object?>>();var count=0;var branchCount=0;
        foreach(var branch in branches){if(++branchCount>1_000_000)throw new ArgumentException("Data tree input branch budget exceeded.");ArgumentNullException.ThrowIfNull(branch);ArgumentNullException.ThrowIfNull(branch.Path);if(!map.TryGetValue(branch.Path,out var items)){if(map.Count>=100_000)throw new ArgumentException("Data tree branch budget exceeded.");map.Add(branch.Path,items=[]);}if(branch.Items.IsDefault||branch.Items.Length>1_000_000-count)throw new ArgumentException("Invalid or excessive data tree items.");items.AddRange(branch.Items);count+=branch.Items.Length;}
        Branches=map.Select(kv=>new DataBranch(kv.Key,kv.Value.ToImmutableArray())).ToImmutableArray();ItemCount=count;
    }
    public static DataTree Item(object? value,DataPath? path=null)=>new([new(path??DataPath.Root,[value])]);
    public static DataTree List(IEnumerable<object?> values,DataPath? path=null)=>new([new(path??DataPath.Root,values.Take(1_000_001).ToImmutableArray())]);
    public IEnumerable<object?> AllItems=>Branches.SelectMany(b=>b.Items);
    public ImmutableArray<object?> ItemsAt(DataPath path,bool broadcastSingletonBranch=false)
    {
        var index=FindBranch(path);
        return index>=0?Branches[index].Items:broadcastSingletonBranch&&Branches.Length==1?Branches[0].Items:[];
    }
    /// <summary>Binary search over immutable numeric-lexicographic branch order.</summary>
    internal int FindBranch(DataPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var lo=0;var hi=Branches.Length-1;
        while(lo<=hi)
        {
            var mid=lo+(hi-lo)/2;var order=Branches[mid].Path.CompareTo(path);
            if(order==0)return mid;
            if(order<0)lo=mid+1;else hi=mid-1;
        }
        return -1;
    }
    public DataTree Merge(DataTree other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.Branches.IsEmpty?this:Branches.IsEmpty?other:new(Branches.Concat(other.Branches));
    }
    public DataTree Flatten()=>List(AllItems);
    public DataTree Graft()=>new(Branches.SelectMany(b=>b.Items.Length==0?new[]{new DataBranch(b.Path,[])}:b.Items.Select((item,i)=>new DataBranch(b.Path.Append(i),[item]))));
    public DataTree Reverse()=>new(Branches.Select(b=>new DataBranch(b.Path,b.Items.Reverse().ToImmutableArray())));
    public DataTree Simplify()
    {
        if(Branches.Length==0)return this;var prefix=0;var minimum=Branches.Min(b=>b.Path.Indices.Length);while(prefix<minimum&&Branches.All(b=>b.Path.Indices[prefix]==Branches[0].Path.Indices[prefix]))prefix++;
        // Preserve one index when every branch has the same singleton path.
        if(Branches.Length==1)prefix=Math.Max(0,prefix-1);
        return new(Branches.Select(b=>new DataBranch(new(b.Path.Indices.Skip(prefix)),b.Items)));
    }
    public DataTree Partition(int size)
    {
        if(size<1)throw new ArgumentOutOfRangeException(nameof(size));return new(Branches.SelectMany(b=>b.Items.Length==0?new[]{new DataBranch(b.Path,[])}:Enumerable.Range(0,1+(b.Items.Length-1)/size).Select(i=>new DataBranch(b.Path.Append(i),b.Items.Skip(i*size).Take(size).ToImmutableArray()))));
    }
}
