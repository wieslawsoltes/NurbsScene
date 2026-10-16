using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Geometry;
using NurbsScene.Presentation;
using NurbsScene.Rendering;
using Windows.Foundation;
using Windows.System;

namespace NurbsScene.Controls;

public sealed partial class WorkspaceView
{
    private readonly List<FrameworkElement> _shellActions=[],_contextActions=[],_layerActions=[];
    private Button? _layerStatus,_modeStatus;
    private TextBlock? _unitsStatus;
    private bool _controlKey,_altKey,_shiftKey;
    public IEnumerable<FrameworkElement> ActionElements=>_shellActions.Concat(_contextActions).Concat(_layerActions);
    public bool ModelContextMenuIsOpen{get;private set;}
    public string ViewportPropertyValidation=>_viewportProperties?.Validation??"";
    public bool ViewportPropertyDraft=>_viewportProperties?.HasDraft??false;

    private MenuFlyoutItem ShellItem(string text,Action action,string id,List<FrameworkElement>? tracking=null)
    {
        var item=new MenuFlyoutItem{Text=text,FontSize=11,MinHeight=26,Padding=new(8,3,8,3)};
        AutomationProperties.SetAutomationId(item,id);(tracking??_shellActions).Add(item);
        item.Click+=(_,_)=>{try{action();}catch(Exception error){Console.WriteLine("Error: "+error.Message);}};
        return item;
    }
    private void ConfigureSplitters()
    {
        AutomationProperties.SetAutomationId(_graphGrip,"DefinitionSplitter");AutomationProperties.SetName(_graphGrip,"Resize definition editor");
        _graphGrip.RelativeTo=_workspace;_graphGrip.ReadValue=()=>Preferences.Split;
        _graphGrip.ValueAt=p=>Preferences.Dock=="Right"&&ActualWidth>=1100?p.X/Math.Max(1,_workspace.ActualWidth):p.Y/Math.Max(1,_workspace.ActualHeight);
        _graphGrip.ApplyValue=(value,persist)=>SetPreferences(Preferences with{Split=value},persist);
        AutomationProperties.SetAutomationId(_sideGrip,"InspectorSplitter");AutomationProperties.SetName(_sideGrip,"Resize properties and layers panel");
        _sideGrip.RelativeTo=_body;_sideGrip.ReadValue=()=>Preferences.InspectorWidth;
        _sideGrip.ValueAt=p=>_body.ActualWidth-p.X;_sideGrip.Minimum=200;_sideGrip.Maximum=480;_sideGrip.Step=16;_sideGrip.Direction=-1;
        _sideGrip.ApplyValue=(value,persist)=>SetPreferences(Preferences with{InspectorWidth=value},persist);
    }
    private void ConfigureShell()
    {
        _viewportProperties=new(Viewport.Canvas,()=>Preferences.InspectorVisible&&ActualWidth>=720
            &&Preferences.InspectorTab=="Properties"&&Controller.Document.Selection.Count==0);
        Inspector.EmptySelectionContent=_viewportProperties;
        Inspector.TabChanged+=tab=>{if(!_disposed&&Preferences.InspectorTab!=tab)SetPreferences(Preferences with{InspectorTab=tab});_viewportProperties.Refresh();};
        Console.HistoryExpandedRequested+=expanded=>SetPreferences(Preferences with{HistoryExpanded=expanded});
        var cad=Viewport.Canvas;cad.RepeatRequested+=AcceptOrRepeat;cad.ContextMenuRequested+=ShowModelContext;
        cad.ViewChanged+=RefreshViewStatus;
        void CanvasKeyDown(object sender,KeyRoutedEventArgs e)
        {
            if(e.Key==VirtualKey.Control){_controlKey=true;return;}
            if(e.Key==VirtualKey.Menu){_altKey=true;return;}
            if(e.Key==VirtualKey.Shift){_shiftKey=true;return;}
            if(_controlKey)
            {
                var command=e.Key switch{VirtualKey.Z=>"Undo",VirtualKey.Y=>"Redo",VirtualKey.A=>"SelAll",VirtualKey.M=>"MaxViewport",_=>null};
                if(command is not null){Run(command);e.Handled=true;return;}
                var viewport=e.Key switch{VirtualKey.F1=>"Top",VirtualKey.F2=>"Front",VirtualKey.F3=>"Right",VirtualKey.F4=>"Perspective",_=>null};
                if(viewport is not null){cad.Activate(viewport);if(cad.Maximized!=viewport)cad.Maximize(viewport);e.Handled=true;return;}
            }
            if(_shiftKey&&e.Key==VirtualKey.F10){ShowModelContext(new(cad.ActualWidth/2,cad.ActualHeight/2));e.Handled=true;return;}
            if(e.Key==VirtualKey.F3&&!_altKey){Run("Properties");e.Handled=true;}
            else if(e.Key==VirtualKey.Escape){if(!cad.CancelNavigation())Run("Cancel");e.Handled=true;}
            else if(e.Key==VirtualKey.Delete&&!_controlKey&&!_altKey){Run("Delete");e.Handled=true;}
            else if(e.Key is VirtualKey.Enter or VirtualKey.Space&&!_controlKey&&!_altKey){AcceptOrRepeat();e.Handled=true;}
        }
        cad.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler(CanvasKeyDown),true);
        cad.KeyUp+=(_,e)=>{if(e.Key==VirtualKey.Control)_controlKey=false;if(e.Key==VirtualKey.Menu)_altKey=false;if(e.Key==VirtualKey.Shift)_shiftKey=false;};
        cad.LostFocus+=(_,_)=>{_controlKey=false;_altKey=false;_shiftKey=false;};
        // The native character event carries the actual keyboard layout. Do not
        // derive text from virtual-key codes or send it into graph/numeric editors.
        cad.CharacterReceived+=(_,e)=>{
            if(_controlKey||_altKey||char.IsControl(e.Character)||char.IsWhiteSpace(e.Character))return;
            Console.BeginCommand(e.Character.ToString());e.Handled=true;
        };
        if(_ownsController)foreach(var camera in cad.Cameras.Values)camera.Target=Vec3.Zero;
    }
    private void AcceptOrRepeat(){if(Controller.PendingCommand.Length>0)Run("Cancel");else Console.Repeat();}
    private void PickObject(PickResult? result,VirtualKeyModifiers modifiers)
    {
        if(Controller.PendingCommand.Length>0)return;
        var shift=(modifiers&VirtualKeyModifiers.Shift)!=0;var control=(modifiers&VirtualKeyModifiers.Control)!=0;
        if(control&&!shift){if(result is not null)Controller.Document.Select(Controller.Document.Selection.Where(id=>id!=result.ObjectId));}
        else if(result is not null)Controller.Document.Select([result.ObjectId],shift);
        else if(!shift)Controller.Document.Select([]);
    }
    private bool RunShellCommand(string command)
    {
        var name=command.TrimStart('_').ToLowerInvariant();
        switch(name)
        {
            case "new":_ =SafeAsync(NewAsync);break;
            case "open":_ =SafeAsync(OpenAsync);break;
            case "save":_ =SafeAsync(SaveAsync);break;
            case "4view":case "fourviews":Viewport.Canvas.RestoreFourViews();break;
            case "maxviewport":Viewport.Canvas.Maximize(Viewport.Canvas.ActiveViewport);break;
            case "grasshopper":case "definition":SetPreferences(Preferences with{DefinitionVisible=!Preferences.DefinitionVisible});break;
            case "commandhistory":SetPreferences(Preferences with{HistoryExpanded=!Preferences.HistoryExpanded});break;
            case "layers":case "properties":case "objects":SetPreferences(Preferences with{InspectorVisible=true,InspectorTab=char.ToUpperInvariant(name[0])+name[1..]});break;
            case "toggleleftsidebar":SetPreferences(Preferences with{SidebarVisible=!Preferences.SidebarVisible});break;
            case "togglerightsidebar":SetPreferences(Preferences with{InspectorVisible=!Preferences.InspectorVisible});break;
            case "modelinglayout":case "resetlayout":SetPreferences(new());break;
            case "definitionlayout":SetPreferences(new(DefinitionVisible:true,InspectorTab:"Layers"));break;
            case "zoomselected":ZoomSelected();break;
            case "grid":Controller.Scene.ShowGrid=!Controller.Scene.ShowGrid;break;
            case "snap":Aids.GridSnap=!Aids.GridSnap;UpdateAids();break;
            case "ortho":Aids.Ortho=!Aids.Ortho;UpdateAids();break;
            case "endsnap":Aids.EndSnap=!Aids.EndSnap;UpdateAids();break;
            default:return false;
        }
        Console.WriteLine("> "+command);return true;
    }
    private void ZoomSelected()
    {
        var bounds=Controller.Document.VisibleObjects.Where(o=>Controller.Document.Selection.Contains(o.Id)).Aggregate(Bounds3.Empty,(b,o)=>b.Union(o.Geometry.Bounds));
        if(bounds.IsEmpty){Console.WriteLine("Select visible objects to zoom.");return;}
        var canvas=Viewport.Canvas;canvas.CancelNavigation();
        var view=ViewportLayout.Create(Math.Max(1,(int)canvas.ActualWidth),Math.Max(1,(int)canvas.ActualHeight),canvas.Cameras,canvas.Maximized).First(v=>v.Name==canvas.ActiveViewport);
        if(view.Width>0&&view.Height>0)ViewportNavigation.Fit(view.Camera,bounds,view.Width,view.Height);
        canvas.NotifyViewChanged();
    }
    private void ShowModelContext(Point point)
    {
        if(XamlRoot is null||_disposed)return;_contextActions.Clear();var menu=new MenuFlyout();
        menu.Items.Add(ShellItem("Repeat last modeling command",AcceptOrRepeat,"ModelContext_Repeat",_contextActions));
        foreach(var(id,label)in new[]{("ZoomExtents","Zoom extents"),("ZoomSelected","Zoom selected"),("SelAll","Select all"),("SelNone","Deselect all"),("Properties","Properties"),("4View","Four viewports")})
        {
            var command=id;var item=ShellItem(label,()=>Run(command),"ModelContext_"+id,_contextActions);if(id=="ZoomSelected")item.IsEnabled=Controller.Document.Selection.Count>0;menu.Items.Add(item);
        }
        menu.Opened+=(_,_)=>ModelContextMenuIsOpen=true;menu.Closed+=(_,_)=>{ModelContextMenuIsOpen=false;_contextActions.Clear();};
        menu.ShowAt(Viewport.Canvas,new FlyoutShowOptions{Position=point});
    }
    private void AddStatusControls(StackPanel row)
    {
        _unitsStatus=ClassicChrome.Label("",10);row.Children.Add(_unitsStatus);
        _layerStatus=ClassicChrome.Button("Default",()=>{},"StatusLayer");_layerStatus.MaxWidth=160;row.Children.Add(_layerStatus);
        var layers=new MenuFlyout();_layerStatus.Flyout=layers;
        layers.Opening+=(_,_)=>{
            layers.Items.Clear();_layerActions.Clear();var document=Controller.Document;var selection=document.Selection.ToArray();var revision=document.Revision;
            foreach(var layer in document.Layers)
            {
                var captured=layer;var label=selection.Length>0?"Move selection to "+layer.Name:(layer.Id==document.ActiveLayer?"✓ ":"")+layer.Name;
                layers.Items.Add(ShellItem(label,()=>{
                    if(!ReferenceEquals(document,Controller.Document)||document.Revision!=revision||!document.Selection.SetEquals(selection)){Console.WriteLine("Layer selection changed; reopen the layer menu.");return;}
                    document.Transaction(selection.Length>0?"Change object layer":"Current layer",()=>{
                        document.GetLayer(captured.Id);
                        if(selection.Length==0)document.ActiveLayer=captured.Id;
                        else foreach(var id in selection){var item=document.GetObject(id);if(!document.CanEdit(item))throw new InvalidOperationException("Cannot change the layer of hidden or locked objects.");document.Update(item with{LayerId=captured.Id});}
                    });
                },"StatusLayer_"+layer.Id,_layerActions));
            }
            layers.Items.Add(new MenuFlyoutSeparator());layers.Items.Add(ShellItem("Layers panel…",()=>Run("Layers"),"StatusLayerPanel",_layerActions));
        };
        layers.Closed+=(_,_)=>_layerActions.Clear();
        var layerContext=new MenuFlyout();layerContext.Items.Add(ShellItem("Layers panel…",()=>Run("Layers"),"StatusLayerContext"));_layerStatus.ContextFlyout=layerContext;
        _modeStatus=ClassicChrome.Button("Shaded",()=>{},"StatusDisplayMode");row.Children.Add(_modeStatus);var modes=new MenuFlyout();_modeStatus.Flyout=modes;
        foreach(var mode in Enum.GetNames<DisplayMode>()){var name=mode;modes.Items.Add(ShellItem(name,()=>Run(name),"StatusMode_"+name));}
    }
    private void RefreshViewStatus()
    {
        // Camera motion must not enumerate the complete document or rebuild its
        // status-layer tooltip. Inspector camera drafts have their own coalescer.
        if(_modeStatus is not null&&_modeStatus.Content?.ToString()!=Controller.Scene.Mode.ToString())
            _modeStatus.Content=Controller.Scene.Mode.ToString();
    }
    private void RefreshStatus()
    {
        if(_unitsStatus is not null)_unitsStatus.Text=Controller.Document.Units;
        if(_layerStatus is not null)
        {
            var layers=Controller.Document.Objects.Where(o=>Controller.Document.Selection.Contains(o.Id)).Select(o=>o.LayerId).Distinct().Take(2).ToArray();
            _layerStatus.Content=layers.Length==0?Controller.Document.GetLayer(Controller.Document.ActiveLayer).Name:layers.Length==1?Controller.Document.GetLayer(layers[0]).Name:"Varies";
            ToolTipService.SetToolTip(_layerStatus,Controller.Document.Selection.Count==0?"Current layer — click to change; right-click for Layers":"Selected object layer — click to change; right-click for Layers");
        }
        if(_modeStatus is not null)_modeStatus.Content=Controller.Scene.Mode.ToString();
    }
}
