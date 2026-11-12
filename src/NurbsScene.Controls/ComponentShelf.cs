using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

namespace NurbsScene.Controls;

/// <summary>Retained category pages with grouped, executable component palettes.
/// The group footer opens the complete group with native component labels.</summary>
public sealed class ComponentShelf : Grid
{
    private ComponentRegistry? _registry;
    private ComponentDefinition[] _definitions=[];
    private readonly StackPanel _tabs=new(){Orientation=Orientation.Horizontal,Spacing=0};
    private readonly StackPanel _groups=new(){Orientation=Orientation.Horizontal,Spacing=3,Padding=new(4,2,4,3)};
    private readonly Dictionary<string,Button> _categoryButtons=[];
    private readonly Dictionary<string,PalettePage> _pages=[];
    private readonly List<FrameworkElement> _groupMenuElements=[];
    private Dictionary<string,Button> _buttons=[];
    private MenuFlyout? _groupMenu;
    private sealed record PalettePage(UIElement[] Groups,Dictionary<string,Button> Buttons,string[] Names);

    public string Category {get;private set;}="Params";
    public event Action<string>? ComponentRequested;
    public IReadOnlyDictionary<string,Button> Buttons=>_buttons;
    public IReadOnlyDictionary<string,Button> CategoryButtons=>_categoryButtons;
    public IReadOnlyList<string> GroupNames {get;private set;}=Array.Empty<string>();
    public IEnumerable<FrameworkElement> ActionElements=>_groupMenuElements;
    public bool IsGroupMenuOpen {get;private set;}
    public string? OpenedGroup {get;private set;}
    public int PageBuildCount {get;private set;}
    public int ButtonBuildCount {get;private set;}

    public ComponentShelf()
    {
        Background=ClassicChrome.Brush(247,247,245);
        RowDefinitions.Add(new(){Height=new GridLength(25)});
        RowDefinitions.Add(new(){Height=new GridLength(76)});
        var categories=ClassicChrome.Horizontal(_tabs);
        categories.Background=ClassicChrome.Brush(230,231,228);
        Children.Add(categories);
        var strip=ClassicChrome.Horizontal(_groups);Children.Add(strip);SetRow(strip,1);
        Unloaded+=(_,_)=>CloseGroupMenu();
    }
    public void SetRegistry(ComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if(ReferenceEquals(_registry,registry)&&_definitions.Length==registry.Definitions.Count)return;
        CloseGroupMenu();_registry=registry;_definitions=registry.Definitions.ToArray();
        _tabs.Children.Clear();_categoryButtons.Clear();_groups.Children.Clear();_pages.Clear();
        var preferred=new[]{"Params","Maths","Sets","Vector","Curve","Surface","Mesh","Intersect","Transform","Display"};
        foreach(var category in _definitions.Select(d=>d.Category).Distinct().OrderBy(c=>{var i=Array.IndexOf(preferred,c);return i<0?100:i;}))
        {
            var button=ClassicChrome.Button(category,()=>SelectCategory(category),"Category_"+category);
            button.BorderThickness=new(0,0,1,0);button.BorderBrush=ClassicChrome.Brush(210,211,207);
            button.Padding=new(8,1,8,1);button.Height=25;button.FontSize=11;
            ToolTipService.SetToolTip(button,$"{category} components");
            _tabs.Children.Add(button);_categoryButtons.Add(category,button);
        }
        if(_categoryButtons.Count==0){_buttons=[];GroupNames=Array.Empty<string>();return;}
        SelectCategory(_categoryButtons.ContainsKey(Category)?Category:_categoryButtons.Keys.First());
    }
    public void SelectCategory(string category)
    {
        if(!_categoryButtons.ContainsKey(category))throw new ArgumentException("Unknown component category.",nameof(category));
        if(Category==category&&_groups.Children.Count>0)return;
        CloseGroupMenu();Category=category;_groups.Children.Clear();
        foreach(var (name,button) in _categoryButtons)
        {
            button.Background=name==category?ClassicChrome.Brush(250,250,248):ClassicChrome.Brush(230,231,228);
            button.Foreground=name==category?ClassicChrome.Ink:ClassicChrome.Brush(84,85,81);
        }
        if(!_pages.TryGetValue(category,out var page))
        {
            var buttons=new Dictionary<string,Button>();var groups=new List<UIElement>();var names=new List<string>();
            foreach(var group in _definitions.Where(d=>d.Category==category).GroupBy(GroupName))
            {
                names.Add(group.Key);groups.Add(CreateGroup(category,group.Key,group.ToArray(),buttons));
            }
            page=new(groups.ToArray(),buttons,names.ToArray());_pages.Add(category,page);PageBuildCount++;
        }
        _buttons=page.Buttons;GroupNames=page.Names;
        foreach(var group in page.Groups)_groups.Children.Add(group);
    }
    private UIElement CreateGroup(string category,string name,ComponentDefinition[] definitions,Dictionary<string,Button> buttons)
    {
        var container=new Grid{MinWidth=62,Background=ClassicChrome.Brush(247,247,245)};
        container.RowDefinitions.Add(new(){Height=new GridLength(54)});
        container.RowDefinitions.Add(new(){Height=new GridLength(16)});
        var icons=new Grid{HorizontalAlignment=HorizontalAlignment.Center};
        for(var i=0;i<(definitions.Length+1)/2;i++)icons.ColumnDefinitions.Add(new(){Width=new GridLength(27)});
        icons.RowDefinitions.Add(new(){Height=new GridLength(27)});icons.RowDefinitions.Add(new(){Height=new GridLength(27)});
        for(var i=0;i<definitions.Length;i++)
        {
            var definition=definitions[i];
            var button=ClassicChrome.IconButton(definition.Id+"\n"+definition.Description,
                ComponentGlyphs.IconKey(definition.Id,definition.Category),()=>ComponentRequested?.Invoke(definition.Id),"Component_"+definition.Id);
            button.Background=ClassicChrome.Brush(247,247,245);button.Width=27;button.Height=27;
            buttons.Add(definition.Id,button);ButtonBuildCount++;SetColumn(button,i/2);SetRow(button,i%2);icons.Children.Add(button);
        }
        container.Children.Add(icons);
        var footer=ClassicChrome.Button(name,()=>{},"PaletteGroup_"+category+"_"+name);
        footer.Click+=(_,_)=>OpenGroupMenu(footer,name,definitions);
        footer.Height=16;footer.Padding=new(6,0,6,0);footer.CornerRadius=new(0,0,3,3);footer.BorderThickness=new(0);
        footer.Background=ClassicChrome.Brush(65,66,62);footer.Foreground=ClassicChrome.Brush(255,255,255);
        footer.HorizontalAlignment=HorizontalAlignment.Stretch;
        var caption=new Grid();caption.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});caption.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var label=ClassicChrome.Label(name,9);label.Foreground=footer.Foreground;label.HorizontalAlignment=HorizontalAlignment.Center;caption.Children.Add(label);
        var arrow=ClassicChrome.Label("▾",9);arrow.Margin=new(5,0,0,0);arrow.Foreground=footer.Foreground;caption.Children.Add(arrow);SetColumn(arrow,1);footer.Content=caption;
        ToolTipService.SetToolTip(footer,$"All {category} / {name} components");
        container.Children.Add(footer);SetRow(footer,1);return container;
    }
    private void OpenGroupMenu(FrameworkElement anchor,string name,IReadOnlyList<ComponentDefinition> definitions)
    {
        CloseGroupMenu();var menu=new MenuFlyout();_groupMenu=menu;
        foreach(var definition in definitions)
        {
            var item=new MenuFlyoutItem{Text=definition.Id,FontSize=11,MinHeight=27,Padding=new(10,3,12,3)};
            AutomationProperties.SetAutomationId(item,"PaletteComponent_"+definition.Id);
            AutomationProperties.SetName(item,definition.Id);
            ToolTipService.SetToolTip(item,definition.Description);
            item.Click+=(_,_)=>ComponentRequested?.Invoke(definition.Id);
            _groupMenuElements.Add(item);menu.Items.Add(item);
        }
        menu.Closed+=(_,_)=>{if(ReferenceEquals(_groupMenu,menu)){IsGroupMenuOpen=false;OpenedGroup=null;}};
        IsGroupMenuOpen=true;OpenedGroup=name;menu.ShowAt(anchor);
    }
    public void CloseGroupMenu(){_groupMenu?.Hide();_groupMenu=null;_groupMenuElements.Clear();IsGroupMenuOpen=false;OpenedGroup=null;}
    private static string GroupName(ComponentDefinition definition)
    {
        var id=definition.Id;
        return definition.Category switch
        {
            "Params"=>id is "Number" or "Integer" or "Boolean"?"Input":"Util",
            "Maths" when id is "Add" or "Subtract" or "Multiply" or "Divide" or "Minimum" or "Maximum"=>"Operators",
            "Maths" when id is "Sine" or "Cosine" or "Tangent" or "ArcSine" or "ArcCosine" or "ArcTangent" or "Degrees" or "Radians"=>"Trig",
            "Maths" when id is "Power" or "Sqrt" or "Exponential" or "Logarithm"=>"Polynomials",
            "Maths"=>"Util",
            "Sets" when id is "Range" or "Series"=>"Sequence",
            "Sets" when id.Contains("List",StringComparison.Ordinal)||id=="SortNumbers"=>"List",
            "Sets"=>"Tree",
            "Vector" when id.Contains("Point",StringComparison.Ordinal)||id=="Distance"=>"Point",
            "Vector"=>"Vector",
            "Curve" when id is "Line" or "Circle" or "Ellipse" or "Arc"=>"Primitive",
            "Curve" when id is "Polyline" or "Interpolate"=>"Spline",
            "Curve" when id.Contains("Evaluate",StringComparison.Ordinal)||id.Contains("Length",StringComparison.Ordinal)||id.Contains("Divide",StringComparison.Ordinal)||id.Contains("Info",StringComparison.Ordinal)||id.Contains("Closest",StringComparison.Ordinal)=>"Analysis",
            "Curve" when id.Contains("Section",StringComparison.Ordinal)=>"Intersect",
            "Curve"=>"Util",
            "Surface" when id is "Extrude" or "Revolve" or "Loft" or "SaddleSurface" or "SurfaceFromCorners"=>"Freeform",
            "Surface" when id.Contains("Evaluate",StringComparison.Ordinal)||id.Contains("Info",StringComparison.Ordinal)||id.Contains("Area",StringComparison.Ordinal)||id.Contains("Centroid",StringComparison.Ordinal)||id.Contains("Closest",StringComparison.Ordinal)=>"Analysis",
            "Surface"=>"Util",
            "Transform"=>"Euclidean",
            "Display"=>"Preview",
            _=>"Components"
        };
    }
}
