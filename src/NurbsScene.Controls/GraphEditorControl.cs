using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Geometry;
using NurbsScene.Parametric;

namespace NurbsScene.Controls;

/// <summary>Reusable graph editing workbench. Native controls expose search and properties around the Skia canvas.</summary>
public sealed partial class GraphEditorControl : Grid,IDisposable
{
    public GraphCanvas Canvas {get;}=new();
    public ComponentShelf Shelf {get;}=new();
    private Graph? _graph;
    public GraphSession? Session
    {
        get=>Canvas.Session;
        set
        {
            if(Canvas.Session is{} old)old.Replaced-=OnReplaced;
            DetachGraph();Canvas.Session=value;
            if(value is not null){value.Replaced+=OnReplaced;Shelf.SetRegistry(value.Graph.Registry);AttachGraph(value.Graph);}
            RefreshInspector();UpdateStatus();
        }
    }
    private readonly AutoSuggestBox _search=new(){PlaceholderText="Search components…",Width=180,FontSize=11,MinHeight=25,Height=27};
    private readonly StackPanel _inspector=new(){Spacing=6,Padding=new(8)};
    private readonly TextBlock _status=ClassicChrome.Label("Ready",10);
    private readonly TextBlock _zoom=ClassicChrome.Label("100%",10);
    private readonly ColumnDefinition _inspectorColumn=new(){Width=new GridLength(0)};
    private bool _inspectorVisible;
    private readonly ScrollViewer _inspectorScroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly StackPanel _resultPanel=new(){Spacing=3};
    private NodeResult? _shownResult;
    private bool _rebuilding;
    private long _inspectorGeneration;
    private Graph? _inspectorGraph;
    private string? _inspectorNode;
    public event Action<IReadOnlySet<string>>? BakeRequested;
    public event Action<string>? Message;
    public event Action<string>? DockRequested;
    public Button UndoButton {get;}
    public Button RedoButton {get;}
    public GraphEditorControl()
    {
        Background=ClassicChrome.Background;BorderBrush=ClassicChrome.Border;BorderThickness=new(1);
        foreach(var height in new[]{new GridLength(24),new GridLength(25),GridLength.Auto,new GridLength(31),new GridLength(1,GridUnitType.Star),new GridLength(22)})RowDefinitions.Add(new(){Height=height});
        var title=new Grid{Background=ClassicChrome.Brush(241,241,241)};title.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});title.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var heading=ClassicChrome.Label("Definition — NurbsScene",11);heading.Margin=new(7,0,0,0);title.Children.Add(heading);
        var docking=new StackPanel{Orientation=Orientation.Horizontal};docking.Children.Add(ClassicChrome.IconButton("Dock right","Dock",()=>DockRequested?.Invoke("Right"),"GraphDockRight"));docking.Children.Add(ClassicChrome.IconButton("Dock below viewports","FourViews",()=>DockRequested?.Invoke("Bottom"),"GraphDockBottom"));docking.Children.Add(ClassicChrome.IconButton("Hide definition","Close",()=>DockRequested?.Invoke("Hide"),"GraphHide"));title.Children.Add(docking);SetColumn(docking,1);Children.Add(title);
        Children.Add(Shelf);SetRow(Shelf,2);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=2,Padding=new(4,1,4,1)};
        UndoButton=ClassicChrome.IconButton("Undo definition edit","Undo",()=>Session?.Undo(),"GraphUndo");RedoButton=ClassicChrome.IconButton("Redo definition edit","Redo",()=>Session?.Redo(),"GraphRedo");
        tools.Children.Add(UndoButton);tools.Children.Add(RedoButton);
        tools.Children.Add(ClassicChrome.IconButton("Fit definition","Fit",()=>Canvas.Fit(),"GraphFit"));
        tools.Children.Add(ClassicChrome.IconButton("Zoom out","ZoomOut",()=>Canvas.SetZoom(Canvas.Zoom/1.2f)));tools.Children.Add(_zoom);tools.Children.Add(ClassicChrome.IconButton("Zoom in","ZoomIn",()=>Canvas.SetZoom(Canvas.Zoom*1.2f)));
        tools.Children.Add(ClassicChrome.IconButton("Bake selected geometry","Bake",()=>BakeRequested?.Invoke(Canvas.Selection),"GraphBake"));
        tools.Children.Add(ClassicChrome.IconButton("Delete selected components","Delete",()=>Canvas.DeleteSelected(),"GraphDelete"));
        tools.Children.Add(ClassicChrome.IconButton("Component properties","Properties",()=>{_inspectorVisible=!_inspectorVisible;SizeInspector();}));tools.Children.Add(_search);
        var toolScroll=ClassicChrome.Horizontal(tools);Children.Add(toolScroll);SetRow(toolScroll,3);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(_inspectorColumn);body.Children.Add(Canvas);
        _inspectorScroll.Content=_inspector;SetColumn(_inspectorScroll,1);body.Children.Add(_inspectorScroll);SetRow(body,4);Children.Add(body);SetRow(_status,5);_status.Margin=new(7,0,7,0);Children.Add(_status);
        AutomationProperties.SetAutomationId(_search,"ComponentSearch");AutomationProperties.SetName(_search,"Search components");AutomationProperties.SetAutomationId(Canvas,"ParametricCanvas");
        _search.TextChanged+=(_,_)=>{if(Session is null)return;var query=_search.Text.Trim();_search.ItemsSource=Session.Graph.Registry.Definitions.Where(d=>d.Id.Contains(query,StringComparison.OrdinalIgnoreCase)||d.Category.Contains(query,StringComparison.OrdinalIgnoreCase)).Take(30).Select(d=>d.Id).ToArray();};
        _search.QuerySubmitted+=(_,e)=>{var id=e.ChosenSuggestion?.ToString()??_search.Text.Trim();AddComponent(id);};
        Shelf.ComponentRequested+=AddComponent;
        Canvas.SelectionChanged+=()=>{RefreshInspector();UpdateStatus();};Canvas.SearchRequested+=ShowCanvasSearch;Canvas.Message+=Report;Canvas.ViewChanged+=UpdateStatus;
        SizeChanged+=(_,_)=>SizeInspector();
        InitializeActions();
    }
    private void AddComponent(string id)
    {
        if(Session is null||!Session.Graph.Registry.Definitions.Any(d=>d.Id==id)){Report($"No executable component named {id}.");return;}
        try{Canvas.AddComponent(id);_search.Text="";_search.IsSuggestionListOpen=false;Canvas.Focus(FocusState.Programmatic);}catch(Exception error){Report(error.Message);}
    }
    private void SizeInspector()
    {
        var visible=_inspectorVisible&&ActualWidth>=420;
        _inspectorColumn.Width=new GridLength(visible?190:0);
        _inspectorScroll.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
    }
    private void DetachGraph(){if(_graph is not null){_graph.Changed-=UpdateStatus;_graph.Solved-=OnSolved;}_graph=null;}
    private void AttachGraph(Graph graph){_graph=graph;graph.Changed+=UpdateStatus;graph.Solved+=OnSolved;}
    private void OnReplaced(){DetachGraph();if(Session is{} session)AttachGraph(session.Graph);RefreshInspector();UpdateStatus();}
    private void OnSolved(GraphResult result){UpdateStatus();RefreshResult();}
    private void UpdateStatus()
    {
        if(Session is null)return;var g=Session.Graph;var r=g.LastResult;
        _status.Text=$"{g.NodeCount} components  |  {g.WireCount} wires  |  {r?.ErrorCount??0} errors  |  {r?.Milliseconds??0:F2} ms  |  {r?.Cached??0} cached";
        _zoom.Text=$"{Canvas.Zoom*100:F0}%";UndoButton.IsEnabled=Session.CanUndo;RedoButton.IsEnabled=Session.CanRedo;UpdateActionAvailability();
    }
    private void Report(string text){_status.Text=text;Message?.Invoke(text);}
    private static Button Button(string name,Action action)=>ClassicChrome.Button(name,action);
    private void EditInspector(string name,Action<Graph> action)
    {
        if(_rebuilding||Session is null||!ReferenceEquals(Session.Graph,_inspectorGraph)||_inspectorNode is null)return;
        if(!Canvas.Selection.Contains(_inspectorNode)||!Session.Graph.Nodes.Any(n=>n.Id==_inspectorNode))return;
        try{Session.Edit(name,action);}catch(Exception error){Report(error.Message);}
    }
    private void Label(string text)=>_inspector.Children.Add(new TextBlock{Text=text,FontSize=11,TextWrapping=TextWrapping.Wrap});
    private void RefreshInspector()
    {
        if(_rebuilding)return;
        _rebuilding=true;
        try{BuildInspector();}
        finally{_rebuilding=false;}
    }
    private void BuildInspector()
    {
        var generation=++_inspectorGeneration;
        void Edit(string name,Action<Graph> action){if(generation==_inspectorGeneration)EditInspector(name,action);}
        void EditLive(FrameworkElement source,string name,Action<Graph> action){if(source.IsLoaded)Edit(name,action);}
        _inspectorGraph=null;_inspectorNode=null;_inspector.Children.Clear();if(Session is null)return;
        var selectedIds=Canvas.Selection;var selected=Session.Graph.Nodes.Where(n=>selectedIds.Contains(n.Id)).ToArray();
        if(selected.Length!=1){Label(selected.Length==0?"Select a component to inspect inputs, preview and data matching.":$"{selected.Length} components selected");Label("Drag ports to connect. Shift-drag keeps existing input wires. Drag numbers to scrub. Double-click empty canvas to search.");return;}
        var node=selected[0];_inspectorGraph=Session.Graph;_inspectorNode=node.Id;var definition=Session.Graph.Registry.Get(node.Component);Label($"{definition.Category} / {definition.Id}");Label(definition.Description);
        var name=new TextBox{Text=node.Name??node.Component,Header="Name",FontSize=12};name.LostFocus+=(_,_)=>{if(name.IsLoaded&&name.Text!=(node.Name??node.Component))Edit("Rename component",g=>g.Update(g.GetNode(node.Id) with{Name=name.Text}));};_inspector.Children.Add(name);
        var enabled=new CheckBox{Content="Enabled",IsChecked=node.Enabled,FontSize=12};enabled.Click+=(_,_)=>Edit("Enable component",g=>g.Update(g.GetNode(node.Id) with{Enabled=enabled.IsChecked==true}));_inspector.Children.Add(enabled);
        var preview=new CheckBox{Content="Preview",IsChecked=node.Preview,FontSize=12};preview.Click+=(_,_)=>Edit("Preview component",g=>g.Update(g.GetNode(node.Id) with{Preview=preview.IsChecked==true}));_inspector.Children.Add(preview);
        var matching=new ComboBox{Header="List matching",ItemsSource=new[]{"longest","shortest","cross"},SelectedItem=node.Matching,HorizontalAlignment=HorizontalAlignment.Stretch,FontSize=12};matching.SelectionChanged+=(_,_)=>{if(matching.SelectedItem is string mode)Edit("Change matching",g=>g.Update(g.GetNode(node.Id) with{Matching=mode}));};_inspector.Children.Add(matching);
        if(node.Component is "Number" or "Integer")
        {
            var value=Convert.ToDouble(node.Parameters.GetValueOrDefault("value",1d),CultureInfo.InvariantCulture);
            var box=new NumberBox{Header="Value",Value=value,SmallChange=node.Component=="Integer"?1:.1,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact,FontSize=12};AutomationProperties.SetAutomationId(box,"SelectedNodeValue");
            box.ValueChanged+=(_,e)=>{if(double.IsFinite(e.NewValue))Edit("Change number",g=>{var n=g.GetNode(node.Id);g.Update(n with{Parameters=n.Parameters.SetItem("value",node.Component=="Integer"?Math.Round(e.NewValue):e.NewValue)});});};_inspector.Children.Add(box);
            foreach(var key in new[]{"min","max"}){var limit=new NumberBox{Header=key=="min"?"Slider minimum":"Slider maximum",Value=Convert.ToDouble(node.Parameters.GetValueOrDefault(key,key=="min"?0d:100d),CultureInfo.InvariantCulture),FontSize=12};limit.ValueChanged+=(_,e)=>{if(double.IsFinite(e.NewValue))Edit("Set slider range",g=>{var n=g.GetNode(node.Id);g.Update(n with{Parameters=n.Parameters.SetItem(key,e.NewValue)});});};_inspector.Children.Add(limit);}
        }
        if(node.Component=="Text")
        {
            var text=new TextBox{Header="Text",Text=node.Parameters.GetValueOrDefault("text")?.ToString()??"",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontSize=12};text.LostFocus+=(_,_)=>EditLive(text,"Edit text",g=>{var n=g.GetNode(node.Id);g.Update(n with{Parameters=n.Parameters.SetItem("text",text.Text)});});_inspector.Children.Add(text);
        }
        foreach(var port in definition.Inputs)
        {
            var wires=Session.Graph.Wires.Where(w=>w.To==new PortAddress(node.Id,port.Name)).ToArray();Label($"{port.Name} · {port.Kind} / {port.Access}");
            if(wires.Length>0){_inspector.Children.Add(Button($"Disconnect {wires.Length} wire(s)",()=>{Edit("Disconnect input",g=>{foreach(var w in wires)g.Disconnect(w.Id);});RefreshInspector();}));continue;}
            var key="input:"+port.Name;var value=node.Parameters.GetValueOrDefault(key,port.Default);
            if((port.Kind is ValueKind.Number or ValueKind.Integer)&&port.Access==PortAccess.Item)
            {
                var box=new NumberBox{Value=value is null?double.NaN:Convert.ToDouble(value,CultureInfo.InvariantCulture),FontSize=12};box.ValueChanged+=(_,e)=>{if(double.IsFinite(e.NewValue))Edit("Set input",g=>{var n=g.GetNode(node.Id);object v=port.Kind==ValueKind.Integer?(object)checked((int)e.NewValue):e.NewValue;g.Update(n with{Parameters=n.Parameters.SetItem(key,v)});});};_inspector.Children.Add(box);
            }
            else if((port.Kind is ValueKind.Point or ValueKind.Vector)&&port.Access==PortAccess.Item)
            {
                var p=value is Vec3 vector?vector:Vec3.Zero;var text=new TextBox{Text=string.Join(',',p.ToArray().Select(x=>x.ToString("R",CultureInfo.InvariantCulture))),FontSize=12};text.LostFocus+=(_,_)=>EditLive(text,"Set vector input",g=>{var a=text.Text.Split(',').Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();if(a.Length!=3)throw new ArgumentException("Enter x,y,z.");var n=g.GetNode(node.Id);g.Update(n with{Parameters=n.Parameters.SetItem(key,new Vec3(a[0],a[1],a[2]))});});_inspector.Children.Add(text);
            }
            else if(port.Kind==ValueKind.Path&&port.Access==PortAccess.Item)
            {
                var text=new TextBox{Text=(value as DataPath??DataPath.Root).ToString(),PlaceholderText="{0;2;10}",FontSize=12};
                AutomationProperties.SetName(text,$"{port.Name} data path");
                AutomationProperties.SetAutomationId(text,$"PathInput_{port.Name}");
                text.LostFocus+=(_,_)=>EditLive(text,"Set path input",g=>{var path=DataPath.Parse(text.Text);var n=g.GetNode(node.Id);g.Update(n with{Parameters=n.Parameters.SetItem(key,path)});});
                _inspector.Children.Add(text);
            }
            else Label("Connect a compatible component output.");
        }
        _shownResult=null;_resultPanel.Children.Clear();_inspector.Children.Add(_resultPanel);RefreshResult();
    }
    private void RefreshResult()
    {
        if(_inspectorNode is null||Session?.Graph.LastResult?.Nodes.TryGetValue(_inspectorNode,out var result)!=true||ReferenceEquals(_shownResult,result))return;
        _shownResult=result;_resultPanel.Children.Clear();
        void ResultLine(string text)=>_resultPanel.Children.Add(new TextBlock{Text=text,FontSize=11,TextWrapping=TextWrapping.Wrap});
        ResultLine($"{result.Status} · {result.Milliseconds:F3} ms");
        foreach(var message in result.Messages)ResultLine(message);
        foreach(var (name,tree) in result.Outputs)ResultLine($"{name}: {tree.ItemCount} items in {tree.Branches.Length} branches");
    }
    public void Dispose(){if(Session is{} session)session.Replaced-=OnReplaced;DetachGraph();CloseActionPopups();Canvas.Dispose();}
}
