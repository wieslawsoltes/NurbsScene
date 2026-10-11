using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Presentation;

namespace NurbsScene.Controls;

public sealed class ModelToolbar : Grid
{
    private readonly StackPanel _tabs=new(){Orientation=Orientation.Horizontal};
    private readonly StackPanel _tools=new(){Orientation=Orientation.Horizontal,Spacing=1,Padding=new(4,1,4,1)};
    private readonly Dictionary<string,Button> _categoryButtons=[];
    private readonly Dictionary<string,Button> _buttons=[];
    public string Category {get;private set;}="Standard";
    public IReadOnlyDictionary<string,Button> CategoryButtons=>_categoryButtons;
    public IReadOnlyDictionary<string,Button> Buttons=>_buttons;
    public event Action<WorkspaceTool>? Invoked;
    public ModelToolbar()
    {
        Background=ClassicChrome.Background;
        RowDefinitions.Add(new(){Height=new GridLength(24)});RowDefinitions.Add(new(){Height=new GridLength(31)});
        Children.Add(ClassicChrome.Horizontal(_tabs));var strip=ClassicChrome.Horizontal(_tools);Children.Add(strip);SetRow(strip,1);
        foreach(var category in WorkspaceCommands.Categories)
        {
            var b=ClassicChrome.Button(category=="Curve"||category=="Surface"?category+" Tools":category,()=>SelectCategory(category),"Toolbar_"+category);b.BorderThickness=new(0);_tabs.Children.Add(b);_categoryButtons.Add(category,b);
        }
        SelectCategory("Standard");
    }
    public void SelectCategory(string category)
    {
        if(!_categoryButtons.ContainsKey(category))throw new ArgumentException("Unknown toolbar category.");
        Category=category;_buttons.Clear();_tools.Children.Clear();
        foreach(var (name,b) in _categoryButtons)b.Background=name==category?ClassicChrome.Brush(255,255,255):ClassicChrome.Background;
        void Add(WorkspaceTool tool){var b=ClassicChrome.IconButton(tool.Label,tool.Icon,()=>Invoked?.Invoke(tool),"Tool_"+tool.Id);_buttons[tool.Id]=b;_tools.Children.Add(b);}
        foreach(var id in new[]{"New","Open","Save","Undo","Redo","Fit","Definition"})Add(WorkspaceCommands.Tools.Single(t=>t.Id==id));
        _tools.Children.Add(new Border{Width=1,Height=22,Margin=new(5,2,5,2),Background=ClassicChrome.Border});
        foreach(var tool in WorkspaceCommands.Category(category))if(!_buttons.ContainsKey(tool.Id))Add(tool);
    }
    public void SetUndo(bool undo,bool redo){_buttons["Undo"].IsEnabled=undo;_buttons["Redo"].IsEnabled=redo;}
}
