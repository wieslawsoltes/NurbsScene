using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Geometry;
using NurbsScene.IO;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace NurbsScene.Controls;

public sealed partial class GraphEditorControl
{
    private static string? _applicationClipboard;
    private readonly List<FrameworkElement> _menuElements=[];
    private readonly List<FrameworkElement> _contextElements=[];
    private MenuFlyout? _contextMenu;
    private Flyout? _canvasSearch;
    private AutoSuggestBox? _popupSearch;
    private string? _lastPaste;
    private int _pasteCount;
    private bool _clipboardBusy;
    public INurbsEvaluator ClipboardEvaluator {get;set;}=new ManagedNurbsEvaluator();
    public IEnumerable<FrameworkElement> ActionElements=>_menuElements.Concat(_contextElements);
    public FrameworkElement? SearchElement=>_popupSearch;
    public bool SearchIsOpen {get;private set;}
    public bool ContextMenuIsOpen {get;private set;}

    private void InitializeActions()
    {
        var bar=new MenuBar{MinHeight=24,FontSize=11};
        MenuBarItem Menu(string name)
        {
            var item=new MenuBarItem{Title=name};AutomationProperties.SetAutomationId(item,"GraphMenu_"+name);
            _menuElements.Add(item);bar.Items.Add(item);return item;
        }
        var edit=Menu("Edit");
        foreach(var (id,text) in new[]{("Undo","Undo  Ctrl+Z"),("Redo","Redo  Ctrl+Y"),("Cut","Cut  Ctrl+X"),("Copy","Copy  Ctrl+C"),("Paste","Paste  Ctrl+V"),("Duplicate","Duplicate  Ctrl+D"),("Delete","Delete"),("SelectAll","Select all  Ctrl+A"),("Invert","Invert selection")})
            edit.Items.Add(ActionItem(id,text,_menuElements));
        var arrange=Menu("Arrange");
        foreach(var mode in Enum.GetValues<GraphArrangementMode>())arrange.Items.Add(ArrangeItem(mode,_menuElements));
        var view=Menu("View");view.Items.Add(ActionItem("Fit","Zoom extents",_menuElements));view.Items.Add(ActionItem("Search","Search components…",_menuElements));view.Items.Add(ActionItem("Properties","Component properties",_menuElements));
        foreach(var level in new[]{50,100,200}){var value=level;var item=new MenuFlyoutItem{Text=$"Zoom {value}%"};item.Click+=(_,_)=>Canvas.SetZoom(value/100f);view.Items.Add(item);}
        var solution=Menu("Solution");
        foreach(var(id,text)in new[]{("Enable","Enable selected"),("Disable","Disable selected"),("PreviewOn","Preview on"),("PreviewOff","Preview off"),("Bake","Bake selected")})solution.Items.Add(ActionItem(id,text,_menuElements));
        Children.Add(bar);SetRow(bar,1);
        Canvas.ContextMenuRequested+=ShowContextMenu;
        foreach(var(key,action)in new[]{(VirtualKey.C,"Copy"),(VirtualKey.X,"Cut"),(VirtualKey.V,"Paste"),(VirtualKey.D,"Duplicate"),(VirtualKey.A,"SelectAll"),(VirtualKey.Z,"Undo"),(VirtualKey.Y,"Redo")})
        {
            var id=action;var accelerator=new KeyboardAccelerator{Key=key,Modifiers=VirtualKeyModifiers.Control,ScopeOwner=Canvas};
            accelerator.Invoked+=(sender,e)=>{if(Canvas.FocusState==FocusState.Unfocused)return;_ =RunActionAsync(id);e.Handled=true;};Canvas.KeyboardAccelerators.Add(accelerator);
        }
        var context=new KeyboardAccelerator{Key=VirtualKey.F10,Modifiers=VirtualKeyModifiers.Shift,ScopeOwner=Canvas};
        context.Invoked+=(_,e)=>{if(Canvas.FocusState==FocusState.Unfocused)return;ShowContextMenu(new(Canvas.ActualWidth/2,Canvas.ActualHeight/2));e.Handled=true;};Canvas.KeyboardAccelerators.Add(context);
    }
    private MenuFlyoutItem ActionItem(string id,string text,List<FrameworkElement> tracking)
    {
        var item=new MenuFlyoutItem{Text=text};AutomationProperties.SetAutomationId(item,"GraphAction_"+id);tracking.Add(item);
        item.Click+=(sender,e)=>{_ =RunActionAsync(id);};return item;
    }
    private MenuFlyoutItem ArrangeItem(GraphArrangementMode mode,List<FrameworkElement> tracking)
    {
        var name=mode switch{GraphArrangementMode.HorizontalCenter=>"Align horizontal centers",GraphArrangementMode.VerticalCenter=>"Align vertical centers",GraphArrangementMode.DistributeHorizontal=>"Distribute horizontally",GraphArrangementMode.DistributeVertical=>"Distribute vertically",_=>"Align "+mode.ToString().ToLowerInvariant()};
        var item=new MenuFlyoutItem{Text=name};AutomationProperties.SetAutomationId(item,"GraphArrange_"+mode);tracking.Add(item);
        item.Click+=(_,_)=>{try{if(Session is not null)GraphArrangement.Apply(Session,Canvas.Selection,mode);UpdateStatus();}catch(Exception error){Report(error.Message);}};return item;
    }
    private void ShowContextMenu(Point point)
    {
        if(Session is null)return;
        _contextMenu?.Hide();_contextElements.Clear();
        var menu=new MenuFlyout();_contextMenu=menu;
        var graph=Session.Graph;var ids=Canvas.Selection;var selected=graph.Nodes.Where(n=>ids.Contains(n.Id)).ToArray();
        if(selected.Length>0)
        {
            var heading=new MenuFlyoutItem{Text=selected.Length==1?selected[0].Name??selected[0].Component:$"{selected.Length} components",IsEnabled=false};menu.Items.Add(heading);menu.Items.Add(new MenuFlyoutSeparator());
            var enabled=selected.All(n=>n.Enabled);var preview=selected.All(n=>n.Preview);
            var enabledItem=new ToggleMenuFlyoutItem{Text="Enabled",IsChecked=enabled};AutomationProperties.SetAutomationId(enabledItem,"GraphToggle_Enabled");_contextElements.Add(enabledItem);
            enabledItem.Click+=(_,_)=>ChangeFlags(graph,ids,enabled:!enabled);menu.Items.Add(enabledItem);
            var previewItem=new ToggleMenuFlyoutItem{Text="Preview",IsChecked=preview};AutomationProperties.SetAutomationId(previewItem,"GraphToggle_Preview");_contextElements.Add(previewItem);
            previewItem.Click+=(_,_)=>ChangeFlags(graph,ids,preview:!preview);menu.Items.Add(previewItem);
            foreach(var(id,text)in new[]{("Copy","Copy"),("Cut","Cut"),("Duplicate","Duplicate"),("Delete","Delete"),("Bake","Bake…"),("Properties","Properties…")})menu.Items.Add(ActionItem(id,text,_contextElements));
            var arrange=new MenuFlyoutSubItem{Text="Align / distribute",IsEnabled=selected.Length>1};
            foreach(var mode in Enum.GetValues<GraphArrangementMode>())arrange.Items.Add(ArrangeItem(mode,_contextElements));menu.Items.Add(arrange);
            if(selected.Length==1)
            {
                var node=selected[0];var inputMenu=new MenuFlyoutSubItem{Text="Inputs"};
                foreach(var port in graph.Registry.Get(node.Component).Inputs)
                {
                    var wires=graph.Wires.Where(w=>w.To==new PortAddress(node.Id,port.Name)).ToArray();
                    var item=new MenuFlyoutItem{Text=$"Disconnect {port.Name} ({wires.Length})",IsEnabled=wires.Length>0};AutomationProperties.SetAutomationId(item,"GraphDisconnect_"+port.Name);_contextElements.Add(item);
                    item.Click+=(_,_)=>{if(!ReferenceEquals(Session?.Graph,graph))return;try{Session.Edit("Disconnect input",g=>{foreach(var wire in wires)g.Disconnect(wire.Id);});}catch(Exception error){Report(error.Message);}};inputMenu.Items.Add(item);
                }
                if(inputMenu.Items.Count>0)menu.Items.Add(inputMenu);
            }
        }
        else
        {
            menu.Items.Add(ActionItem("Search","Search components…",_contextElements));
            menu.Items.Add(ActionItem("PasteHere","Paste here",_contextElements));
            menu.Items.Add(ActionItem("SelectAll","Select all",_contextElements));menu.Items.Add(ActionItem("Fit","Zoom extents",_contextElements));
        }
        menu.Closed+=(_,_)=>ContextMenuIsOpen=false;ContextMenuIsOpen=true;menu.ShowAt(Canvas,point);
    }
    private void ChangeFlags(Graph expected,IReadOnlySet<string> ids,bool? enabled=null,bool? preview=null)
    {
        if(Session is null||!ReferenceEquals(Session.Graph,expected))return;
        try{Session.Edit("Component state",g=>{foreach(var id in ids){var n=g.GetNode(id);g.Update(n with{Enabled=enabled??n.Enabled,Preview=preview??n.Preview});}});RefreshInspector();UpdateStatus();}
        catch(Exception error){Report(error.Message);}
    }
    private async Task RunActionAsync(string id)
    {
        if(Session is null||_clipboardBusy||Canvas.IsInteracting)return;
        try
        {
            switch(id)
            {
                case "Undo":Session.Undo();break;case "Redo":Session.Redo();break;
                case "Delete":Canvas.DeleteSelected();break;
                case "Copy":CopySelection(false);break;case "Cut":CopySelection(true);break;
                case "Paste":await PasteAsync(false);break;case "PasteHere":await PasteAsync(true);break;
                case "Duplicate":Canvas.SelectNodes(GraphFragment.Capture(Session.Graph,Canvas.Selection).Paste(Session));break;
                case "SelectAll":Canvas.SelectNodes(Session.Graph.Nodes.Select(n=>n.Id));break;
                case "Invert":Canvas.SelectNodes(Session.Graph.Nodes.Where(n=>!Canvas.IsSelected(n.Id)).Select(n=>n.Id));break;
                case "Fit":Canvas.Fit();break;
                case "Search":ShowCanvasSearch();return;
                case "Properties":_inspectorVisible=true;SizeInspector();RefreshInspector();return;
                case "Enable":ChangeFlags(Session.Graph,Canvas.Selection,enabled:true);break;
                case "Disable":ChangeFlags(Session.Graph,Canvas.Selection,enabled:false);break;
                case "PreviewOn":ChangeFlags(Session.Graph,Canvas.Selection,preview:true);break;
                case "PreviewOff":ChangeFlags(Session.Graph,Canvas.Selection,preview:false);break;
                case "Bake":BakeRequested?.Invoke(Canvas.Selection);break;
            }
            UpdateStatus();Canvas.Focus(FocusState.Programmatic);
        }
        catch(Exception error){Report(error.Message);}
    }
    private void CopySelection(bool cut)
    {
        if(Session is null||Canvas.SelectionCount==0)return;
        var ids=Canvas.Selection;var fragment=GraphFragment.Capture(Session.Graph,ids);
        var text=GraphFragmentFormat.Serialize(fragment,Session.Graph.Registry);
        _applicationClipboard=text;_lastPaste=null;_pasteCount=0;
        try{var data=new DataPackage();data.SetText(text);Clipboard.SetContent(data);Report($"Copied {ids.Count} components; internal wires retained.");}
        catch(Exception error){Report("Copied to application clipboard; system clipboard unavailable: "+error.Message);}
        if(cut){Session.Edit("Cut components",g=>{foreach(var id in ids)g.Remove(id);});Canvas.SelectNodes([]);}
    }
    private async Task PasteAsync(bool here)
    {
        if(Session is null)return;
        var session=Session;var graph=session.Graph;var revision=graph.Revision;var at=Canvas.InsertionPoint;
        _clipboardBusy=true;
        try
        {
            string? text;
            try{var content=Clipboard.GetContent();text=content.Contains(StandardDataFormats.Text)?await content.GetTextAsync():null;}
            catch(Exception error){text=_applicationClipboard;Report("Using application clipboard; system clipboard unavailable: "+error.Message);}
            if(text is null)text=_applicationClipboard;
            if(text is null)throw new InvalidOperationException("The graph clipboard is empty.");
            if(!ReferenceEquals(Session,session)||!ReferenceEquals(session.Graph,graph)||graph.Revision!=revision)
                throw new InvalidOperationException("The definition changed while reading the clipboard; paste was cancelled.");
            var fragment=GraphFragmentFormat.Deserialize(ClipboardEvaluator,graph.Registry,text);
            if(fragment.Nodes.IsEmpty)return;
            var repeat=_lastPaste==text?_pasteCount+1:1;double dx=24*repeat,dy=24*repeat;
            if(here){dx=at.X-fragment.Nodes.Min(n=>n.X);dy=at.Y-fragment.Nodes.Min(n=>n.Y);}
            var ids=fragment.Paste(session,dx,dy);_lastPaste=text;_pasteCount=repeat;Canvas.SelectNodes(ids);
        }
        finally{_clipboardBusy=false;}
    }
    private void ShowCanvasSearch()
    {
        if(Session is null)return;
        if(_canvasSearch is null)
        {
            _popupSearch=new AutoSuggestBox{PlaceholderText="Search components…",Width=290,FontSize=12};
            AutomationProperties.SetAutomationId(_popupSearch,"CanvasComponentSearch");
            var panel=new StackPanel{Spacing=5};panel.Children.Add(ClassicChrome.Label("Search",11));panel.Children.Add(_popupSearch);
            panel.Children.Add(ClassicChrome.Label("Enter adds a component at the canvas cursor",10));
            _canvasSearch=new Flyout{Content=panel,Placement=FlyoutPlacementMode.Bottom};
            _canvasSearch.Opened+=(_,_)=>{SearchIsOpen=true;_popupSearch.Focus(FocusState.Programmatic);};
            _canvasSearch.Closed+=(_,_)=>SearchIsOpen=false;
            _popupSearch.TextChanged+=(_,_)=>{if(Session is null)return;var query=_popupSearch.Text.Trim();_popupSearch.ItemsSource=Session.Graph.Registry.Definitions.Where(d=>d.Id.Contains(query,StringComparison.OrdinalIgnoreCase)||d.Category.Contains(query,StringComparison.OrdinalIgnoreCase)).Take(20).Select(d=>d.Id).ToArray();};
            _popupSearch.QuerySubmitted+=(_,e)=>{var name=e.ChosenSuggestion?.ToString()??_popupSearch.Text.Trim();if(Session?.Graph.Registry.Definitions.Any(d=>d.Id==name)!=true){Report("No executable component named "+name);return;}_popupSearch.IsSuggestionListOpen=false;_canvasSearch.Hide();AddComponent(name);};
        }
        _popupSearch!.Text="";var at=Canvas.InsertionPoint;var center=Canvas.ViewCenter;
        var x=Canvas.ActualWidth/2+(at.X-center.X)*Canvas.Zoom;var y=Canvas.ActualHeight/2+(at.Y-center.Y)*Canvas.Zoom;
        _canvasSearch.ShowAt(Canvas,new FlyoutShowOptions{Position=new Point(Math.Clamp(x,0,Math.Max(0,Canvas.ActualWidth)),Math.Clamp(y,0,Math.Max(0,Canvas.ActualHeight)))});
    }
    private void UpdateActionAvailability()
    {
        var count=Canvas.SelectionCount;
        foreach(var item in _menuElements.OfType<MenuFlyoutItem>())
        {
            var id=AutomationProperties.GetAutomationId(item);
            item.IsEnabled=id switch{
                "GraphAction_Undo"=>Session?.CanUndo==true,"GraphAction_Redo"=>Session?.CanRedo==true,
                "GraphAction_SelectAll" or "GraphAction_Invert"=>Session?.Graph.NodeCount>0,
                "GraphAction_Copy" or "GraphAction_Cut" or "GraphAction_Duplicate" or "GraphAction_Delete" or "GraphAction_Bake" or "GraphAction_Properties" or "GraphAction_Enable" or "GraphAction_Disable" or "GraphAction_PreviewOn" or "GraphAction_PreviewOff"=>count>0,
                "GraphArrange_DistributeHorizontal" or "GraphArrange_DistributeVertical"=>count>2,
                _=>!id.StartsWith("GraphArrange_",StringComparison.Ordinal)||count>1};
        }
    }
    private void CloseActionPopups(){_contextMenu?.Hide();_canvasSearch?.Hide();}
}
