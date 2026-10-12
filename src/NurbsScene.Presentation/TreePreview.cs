using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using NurbsScene.Geometry;
using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

public sealed record TreePreviewBranch(string Path,int ItemCount,ImmutableArray<string> Items);
public sealed record TreePreview(int BranchCount,int ItemCount,ImmutableArray<TreePreviewBranch> Branches,int OmittedBranches)
{
    /// <summary>Bounded, read-only preview. Work depends on preview limits, not
    /// the complete tree size. Never evaluates geometry, flattens a tree or calls
    /// user/plugin ToString implementations just to display a component result.</summary>
    public static TreePreview Create(DataTree tree,int maximumBranches=4,int maximumItems=6,int maximumText=120)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if(maximumBranches is <1 or >64||maximumItems is <1 or >64||maximumText is <16 or >1024)
            throw new ArgumentOutOfRangeException(nameof(maximumBranches),"Preview limits exceed their budgets.");
        var branches=ImmutableArray.CreateBuilder<TreePreviewBranch>(Math.Min(maximumBranches,tree.Branches.Length));
        for(var i=0;i<branches.Capacity;i++)
        {
            var branch=tree.Branches[i];var items=ImmutableArray.CreateBuilder<string>(Math.Min(maximumItems,branch.Items.Length));
            for(var j=0;j<items.Capacity;j++)items.Add(Format(branch.Items[j],maximumText));
            branches.Add(new(Format(branch.Path,maximumText),branch.Items.Length,items.MoveToImmutable()));
        }
        return new(tree.Branches.Length,tree.ItemCount,branches.MoveToImmutable(),Math.Max(0,tree.Branches.Length-maximumBranches));
    }
    private static string Format(object? value,int limit)
    {
        // Even a custom object's type name may be long; the final text is bounded.
        var text=value switch{
            null=>"<null>",string s=>Escape(s,limit),bool b=>b?"True":"False",
            double d=>d.ToString("G17",CultureInfo.InvariantCulture),int n=>n.ToString(CultureInfo.InvariantCulture),
            Vec3 v=>FormattableString.Invariant($"({v.X:G7}, {v.Y:G7}, {v.Z:G7})"),
            DataPath p=>p.ToString(),
            NurbsCurve c=>$"NURBS curve · degree {c.Degree} · {c.Count} controls",
            NurbsSurface s=>$"NURBS surface · degree {s.DegreeU}×{s.DegreeV} · {s.CountU}×{s.CountV} controls",
            _=>"<"+value.GetType().Name+">"};
        if(text.Length<=limit)return text;
        var length=limit-1;
        if(length>0&&char.IsHighSurrogate(text[length-1]))length--;
        return text[..length]+"…";
    }
    private static string Escape(string source,int limit)
    {
        var text=new StringBuilder(Math.Min(source.Length,limit)+2);text.Append('"');
        var i=0;
        for(;i<source.Length&&text.Length<limit-2;i++)
        {
            var c=source[i];
            var token=c switch{'\r'=>"\\r",'\n'=>"\\n",'\t'=>"\\t",'"'=>"\\\"",'\\'=>"\\\\",_=>char.IsControl(c)?"�":c.ToString()};
            if(char.IsHighSurrogate(c)&&i+1<source.Length&&char.IsLowSurrogate(source[i+1]))
            {
                if(text.Length+2>limit-2)break;
                text.Append(c).Append(source[++i]);continue;
            }
            if(char.IsSurrogate(c))token="�";
            if(text.Length+token.Length>limit-2)break;
            text.Append(token);
        }
        if(i<source.Length)text.Append('…');
        text.Append('"');return text.ToString();
    }
}
