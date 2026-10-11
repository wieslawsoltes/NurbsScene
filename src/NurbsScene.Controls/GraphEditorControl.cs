using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Geometry;
using NurbsScene.Parametric;

namespace NurbsScene.Controls;

/// <summary>Reusable graph editing workbench. Native controls expose search and properties around the Skia canvas.</summary>
public sealed class GraphEditorControl : Grid,IDisposable
{
    public GraphCanvas Canvas {get;}=new();
    public GraphSession? Session {get=>Canvas.Session;set{if(Canvas.Session is{} old)old.Replaced-=RefreshInspector;Canvas.Session=value;if(value is not null)value.Replaced+=RefreshInspector;RefreshInspector();}}
    private readonly AutoSuggestBox _search=new(){PlaceholderText="Search components…",MinWidth=210,FontSize=12,MinHeight=28};
    private readonly StackPanel _inspector=new(){Spacing=8,Padding=new(10)};
    private readonly TextBlock _status=new(){FontSize=11,Margin=new(8,3,8,3),Text="Typed visual programming"};
    private readonly ColumnDefinition _inspectorColumn=new(){Width=new GridLength(190)};
    public event Action<IReadOnlySet<string>>? BakeRequested;
    public event Action<string>? Message;
    public GraphEditorControl()
    {
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});RowDefinitions.Add(new(){Height=GridLength.Auto});
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=3,Padding=new(5)};tools.Children.Add(_search);
        tools.Children.Add(Button("Undo",()=>Session?.Undo()));tools.Children.Add(Button("Redo",()=>Session?.Redo()));tools.Children.Add(Button("Fit",()=>Canvas.Fit()));tools.Children.Add(Button("Delete",()=>Canvas.DeleteSelected()));tools.Children.Add(Button("Bake",()=>BakeRequested?.Invoke(Canvas.Selection)));
        var toolScroll=new ScrollViewer{Content=tools,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};Children.Add(toolScroll);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(_inspectorColumn);body.Children.Add(Canvas);
        var inspectScroll=new ScrollViewer{Content=_inspector,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};SetColumn(inspectScroll,1);body.Children.Add(inspectScroll);SetRow(body,1);Children.Add(body);SetRow(_status,2);Children.Add(_status);
        AutomationProperties.SetAutomationId(_search,"ComponentSearch");AutomationProperties.SetName(_search,"Search components");AutomationProperties.SetAutomationId(Canvas,"ParametricCanvas");
        _search.TextChanged+=(_,_)=>{if(Session is null)return;var query=_search.Text.Trim();_search.ItemsSource=Session.Graph.Registry.Definitions.Where(d=>d.Id.Contains(query,StringComparison.OrdinalIgnoreCase)||d.Category.Contains(query,StringComparison.OrdinalIgnoreCase)).Take(30).Select(d=>d.Id).ToArray();};
        _search.QuerySubmitted+=(_,e)=>{var id=e.ChosenSuggestion?.ToString()??_search.Text.Trim();if(Session is null||!Session.Graph.Registry.Definitions.Any(d=>d.Id==id)){Report($"No executable component named {id}.");return;}try{Canvas.AddComponent(id);_search.Text="";}catch(Exception error){Report(error.Message);}};
        Canvas.SelectionChanged+=RefreshInspector;Canvas.SearchRequested+=()=>_search.Focus(FocusState.Programmatic);Canvas.Message+=Report;
        SizeChanged+=(_,_)=>_inspectorColumn.Width=ActualWidth>=750?new GridLength(190):new GridLength(0);
    }
    private void Report(string text){_status.Text=text;Message?.Invoke(text);}
    private static Button Button(string name,Action action){var button=new Button{Content=name,Padding=new(8,3,8,3),FontSize=11,MinHeight=28,CornerRadius=new(0)};button.Click+=(_,_)=>action();return button;}
    private void Edit(string name,Action<Graph> action){try{Session?.Edit(name,action);}catch(Exception error){Report(error.Message);}}
    private void Label(string text)=>_inspector.Children.Add(new TextBlock{Text=text,FontSize=11,TextWrapping=TextWrapping.Wrap});
    private void RefreshInspector()
    {
        _inspector.Children.Clear();if(Session is null)return;
        var selected=Session.Graph.Nodes.Where(n=>Canvas.Selection.Contains(n.Id)).ToArray();
        if(selected.Length!=1){Label(selected.Length==0?"Select a component to inspect inputs, preview and data matching.":$"{selected.Length} components selected");Label("Drag ports to connect. Shift-drag keeps existing input wires. Drag numbers to scrub. Double-click empty canvas to search.");return;}
        var node=selected[0];var definition=Session.Graph.Registry.Get(node.Component);Label($"{definition.Category} / {definition.Id}");Label(definition.Description);
        var name=new TextBox{Text=node.Name??node.Component,Header="Name",FontSize=12};name.LostFocus+=(_,_)=>{if(name.Text!=node.Name)Edit("Rename component",g=>g.Update(g.GetNode(node.Id) with{Name=name.Text}));};_inspector.Children.Add(name);
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
            var text=new TextBox{Header="Text",Text=node.Parameters.GetValueOrDefault("text")?.ToString()??"",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontSize=12};text.LostFocus+=(_,_)=>Edit("Edit text",g=>{var n=g.GetNode(node.Id);g.Update(n with{Parameters=n.Parameters.SetItem("text",text.Text)});});_inspector.Children.Add(text);
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
                var p=value is Vec3 vector?vector:Vec3.Zero;var text=new TextBox{Text=string.Join(',',p.ToArray().Select(x=>x.ToString("R",CultureInfo.InvariantCulture))),FontSize=12};text.LostFocus+=(_,_)=>Edit("Set vector input",g=>{var a=text.Text.Split(',').Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();if(a.Length!=3)throw new ArgumentException("Enter x,y,z.");var n=g.GetNode(node.Id);g.Update(n with{Parameters=n.Parameters.SetItem(key,new Vec3(a[0],a[1],a[2]))});});_inspector.Children.Add(text);
            }
            else Label("Connect a compatible component output.");
        }
        if(Session.Graph.LastResult?.Nodes.TryGetValue(node.Id,out var result)==true){Label($"{result.Status} · {result.Milliseconds:F3} ms");foreach(var message in result.Messages)Label(message);foreach(var (port,tree) in result.Outputs)Label($"{port}: {tree.ItemCount} items in {tree.Branches.Length} branches");}
    }
    public void Dispose(){if(Session is{} session)session.Replaced-=RefreshInspector;Canvas.Dispose();}
}
