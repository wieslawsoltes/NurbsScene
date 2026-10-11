using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Parametric;

namespace NurbsScene.Controls;

/// <summary>Grasshopper-style category tabs and grouped component icon shelves,
/// generated exclusively from the actual executable component registry.</summary>
public sealed class ComponentShelf : Grid
{
    private ComponentRegistry? _registry;
    private ComponentDefinition[] _definitions=[];
    private readonly StackPanel _tabs=new(){Orientation=Orientation.Horizontal,Spacing=0};
    private readonly StackPanel _groups=new(){Orientation=Orientation.Horizontal,Spacing=5,Padding=new(4,1,4,2)};
    public string Category {get;private set;}="Params";
    public event Action<string>? ComponentRequested;
    public IReadOnlyDictionary<string,Button> Buttons=>_buttons;
    private readonly Dictionary<string,Button> _buttons=[];
    public IReadOnlyDictionary<string,Button> CategoryButtons=>_categoryButtons;
    private readonly Dictionary<string,Button> _categoryButtons=[];
    public ComponentShelf()
    {
        Background=ClassicChrome.Background;RowDefinitions.Add(new(){Height=new GridLength(25)});RowDefinitions.Add(new(){Height=new GridLength(75)});
        Children.Add(ClassicChrome.Horizontal(_tabs));var strip=ClassicChrome.Horizontal(_groups);Children.Add(strip);SetRow(strip,1);
    }
    public void SetRegistry(ComponentRegistry registry)
    {
        if(ReferenceEquals(_registry,registry)&&_definitions.Length==registry.Definitions.Count)return;
        _registry=registry;_definitions=registry.Definitions.ToArray();_tabs.Children.Clear();_categoryButtons.Clear();
        var preferred=new[]{"Params","Maths","Sets","Vector","Curve","Surface","Mesh","Intersect","Transform","Display"};
        foreach(var category in _definitions.Select(d=>d.Category).Distinct().OrderBy(c=>{var i=Array.IndexOf(preferred,c);return i<0?100:i;}))
        {
            var b=ClassicChrome.Button(category,()=>SelectCategory(category),"Category_"+category);b.BorderThickness=new(0);b.Padding=new(6,1,6,1);_tabs.Children.Add(b);_categoryButtons.Add(category,b);
        }
        SelectCategory(_categoryButtons.ContainsKey(Category)?Category:_categoryButtons.Keys.First());
    }
    public void SelectCategory(string category)
    {
        if(!_categoryButtons.ContainsKey(category))throw new ArgumentException("Unknown component category.");
        Category=category;_groups.Children.Clear();_buttons.Clear();
        foreach(var (name,b) in _categoryButtons)b.Background=name==category?ClassicChrome.Brush(255,255,255):ClassicChrome.Background;
        string Group(ComponentDefinition d)=>d.Id.Contains("Info")||d.Id.Contains("Area")||d.Id.Contains("Length")||d.Id.Contains("Centroid")||d.Id.Contains("Curvature")?"Analysis":d.Id.Contains("Split")||d.Id.Contains("Reverse")||d.Id.Contains("Trim")||d.Id.Contains("Knot")||d.Id.Contains("Elevate")||d.Id.Contains("Control")?"Modify":"Components";
        foreach(var group in _definitions.Where(d=>d.Category==category).GroupBy(Group))
        {
            var container=new Grid();container.RowDefinitions.Add(new(){Height=new GridLength(54)});container.RowDefinitions.Add(new(){Height=new GridLength(15)});
            var icons=new Grid();var definitions=group.ToArray();
            for(var i=0;i<(definitions.Length+1)/2;i++)icons.ColumnDefinitions.Add(new(){Width=new GridLength(27)});
            icons.RowDefinitions.Add(new(){Height=new GridLength(27)});icons.RowDefinitions.Add(new(){Height=new GridLength(27)});
            for(var i=0;i<definitions.Length;i++)
            {
                var d=definitions[i];var icon=d.Id.Contains("Area")?"Area":d.Id.Contains("Centroid")?"Centroid":d.Id.Contains("Circle")?"Circle":d.Id.Contains("Line")?"Line":d.Id.Contains("Split")||d.Id.Contains("Shatter")?"Split":d.Id.Contains("Loft")?"Loft":d.Category;
                var button=ClassicChrome.IconButton(d.Id+"\n"+d.Description,icon,()=>ComponentRequested?.Invoke(d.Id),"Component_"+d.Id);
                _buttons[d.Id]=button;SetColumn(button,i/2);SetRow(button,i%2);icons.Children.Add(button);
            }
            container.Children.Add(icons);var label=ClassicChrome.Label(group.Key,9);label.Foreground=ClassicChrome.Brush(255,255,255);label.HorizontalAlignment=HorizontalAlignment.Center;
            var strip=new Border{Background=ClassicChrome.Brush(60,60,60),Child=label,CornerRadius=new(0,0,3,3)};container.Children.Add(strip);SetRow(strip,1);_groups.Children.Add(container);
        }
    }
}
