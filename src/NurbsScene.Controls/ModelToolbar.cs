using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Presentation;

namespace NurbsScene.Controls;

/// <summary>Retained toolbar containers. Selecting a tab changes the presented
/// page, never the command objects or their native button identities.</summary>
public sealed class ModelToolbar : Grid
{
    private sealed record Page(StackPanel Content,Dictionary<string,Button> Buttons);
    private static readonly (string Id,string Label)[] Categories=[
        ("Standard","Standard"),("SetViews","Set Views"),("View","Viewport Layout"),
        ("Display","Display"),("Select","Select"),("Transform","Transform"),
        ("Curve","Curve Tools"),("Surface","Surface Tools"),("Analyze","Analyze")];
    private readonly StackPanel _tabs=new(){Orientation=Orientation.Horizontal};
    private readonly ScrollViewer _strip=ClassicChrome.CompactHorizontal(null);
    private readonly Dictionary<string,Button> _categoryButtons=[];
    private readonly Dictionary<string,Page> _pages=[];
    private IReadOnlyDictionary<string,Button> _buttons=new Dictionary<string,Button>();
    private bool _canUndo,_canRedo;
    public string Category {get;private set;}="Standard";
    public int PageBuildCount {get;private set;}
    public IReadOnlyDictionary<string,Button> CategoryButtons=>_categoryButtons;
    public IReadOnlyDictionary<string,Button> Buttons=>_buttons;
    public event Action<WorkspaceTool>? Invoked;
    public event Action<string>? CategoryChanged;
    public ModelToolbar()
    {
        Background=ClassicChrome.Background;BorderBrush=ClassicChrome.Border;BorderThickness=new(0,1,0,1);
        RowDefinitions.Add(new(){Height=new GridLength(23)});RowDefinitions.Add(new(){Height=new GridLength(30)});
        Children.Add(ClassicChrome.CompactHorizontal(_tabs));Children.Add(_strip);SetRow(_strip,1);
        foreach(var (id,label) in Categories)
        {
            var button=ClassicChrome.TabButton(label,()=>SelectCategory(id),"Toolbar_"+id);
            _tabs.Children.Add(button);_categoryButtons.Add(id,button);
        }
        SelectCategory("Standard");
    }
    public static string CategoryLabel(string category)=>Categories.FirstOrDefault(c=>c.Id==category).Label??category;
    public static IReadOnlyList<WorkspaceTool> CategoryTools(string category)
    {
        string[]? ids=category switch{
            "SetViews"=>["ViewPerspective","ViewTop","ViewFront","ViewRight","FourViews","Fit"],
            "Display"=>["Wireframe","Shaded","Ghosted","Grid","Rendering"],
            "Select"=>["SelectAll","SelectNone","Delete"],
            "View"=>["FourViews","Fit","ZoomSelected","Definition","FloatingDefinition","Properties","Layers"],
            _=>null};
        return ids is null?WorkspaceCommands.Category(category):ids.Select(id=>WorkspaceCommands.Tools.Single(t=>t.Id==id)).ToArray();
    }
    public void SelectCategory(string category)
    {
        if(!_categoryButtons.ContainsKey(category))throw new ArgumentException("Unknown toolbar category.");
        if(!_pages.TryGetValue(category,out var page))
        {
            var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=0,Padding=new(3,1,3,1),VerticalAlignment=VerticalAlignment.Center};
            var buttons=new Dictionary<string,Button>();
            tools.Children.Add(new WorkbenchIcon("Grip"){Width=7,Height=22,VerticalAlignment=VerticalAlignment.Center});
            void Add(WorkspaceTool tool)
            {
                if(buttons.ContainsKey(tool.Id))return;
                var button=ClassicChrome.IconButton(tool.Label,tool.Icon,()=>Invoked?.Invoke(tool),"Tool_"+tool.Id);
                buttons.Add(tool.Id,button);tools.Children.Add(button);
            }
            foreach(var id in new[]{"New","Open","Save"})Add(WorkspaceCommands.Tools.Single(t=>t.Id==id));
            tools.Children.Add(ClassicChrome.Separator());
            foreach(var id in new[]{"Undo","Redo","Fit","Definition"})Add(WorkspaceCommands.Tools.Single(t=>t.Id==id));
            tools.Children.Add(ClassicChrome.Separator());
            foreach(var tool in CategoryTools(category))Add(tool);
            if(category=="Standard")
            {
                tools.Children.Add(ClassicChrome.Separator());
                foreach(var id in new[]{"FourViews","Wireframe","Shaded","Ghosted","Rendering","Properties","Layers"})Add(WorkspaceCommands.Tools.Single(t=>t.Id==id));
            }
            page=new(tools,buttons);_pages.Add(category,page);PageBuildCount++;
        }
        var changed=Category!=category;Category=category;_buttons=page.Buttons;
        if(!ReferenceEquals(_strip.Content,page.Content))_strip.Content=page.Content;
        foreach(var (name,button) in _categoryButtons)ClassicChrome.SelectTab(button,name==category);
        SetUndo(_canUndo,_canRedo);
        if(changed)CategoryChanged?.Invoke(category);
    }
    public void SetUndo(bool undo,bool redo)
    {
        _canUndo=undo;_canRedo=redo;
        foreach(var page in _pages.Values){page.Buttons["Undo"].IsEnabled=undo;page.Buttons["Redo"].IsEnabled=redo;}
    }
}
