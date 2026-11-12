#if NURBSSCENE_QUALIFICATION
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Controls;
using NurbsScene.Geometry;
using NurbsScene.Presentation;
using NurbsScene.Rendering;
using Windows.Foundation;

namespace NurbsScene.App;

public partial class App
{
    // Observation only during pointer/keyboard tests: no command dispatch or
    // mutation API. The single finish flag restores the disposable test session.
    // Omitted entirely from ordinary builds and idle unless qualification=1.
    private static async Task ObserveWorkspaceUiAsync(WorkspaceView view)
    {
        var controller=view.Controller;var source=controller.Save();var preferences=view.Preferences;
        var renderingSettings=view.Viewport.Canvas.RenderSettings;
        var observationLifetime=TimeSpan.FromMinutes(15);
        var watch=Stopwatch.StartNew();var drawing=new GraphPresentation();long sample=0;
        // A bounded, passive trace explains native input ordering without sending
        // input, changing focus, retrying actions, or keeping handlers after qualification.
        var inputTrace=new Queue<object>();
        var pointerTrace=new Queue<object>();
        var modelCanvas=view.Viewport.Canvas;
        var commandInput=(AutoSuggestBox)view.Console.InputElement;
        TextBox? FindInput(DependencyObject element)
        {
            if(element is TextBox box)return box;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)
                if(FindInput(VisualTreeHelper.GetChild(element,i)) is{} found)return found;
            return null;
        }
        string Limited(string text,int maximum)=>text.Length<=maximum?text:text[^maximum..];
        void ObserveInput(string phase,RoutedEventArgs? args=null,string? key=null,bool? handled=null)
        {
            var owner=view.XamlRoot is{} xamlRoot?FocusManager.GetFocusedElement(xamlRoot):null;
            var edit=FindInput(commandInput);
            inputTrace.Enqueue(new{milliseconds=watch.ElapsedMilliseconds,phase,key,handled,
                source=args?.OriginalSource?.GetType().FullName,
                focus=owner?.GetType().FullName,
                focusId=owner is DependencyObject focusOwner?AutomationProperties.GetAutomationId(focusOwner):null,
                canvasFocus=modelCanvas.FocusState.ToString(),
                focusPending=view.Console.IsCanvasFocusPending,
                text=Limited(view.Console.InputText,80),innerText=edit is null?null:Limited(edit.Text,80),
                selectionStart=edit?.SelectionStart,selectionLength=edit?.SelectionLength,
                history=Limited(view.Console.HistoryText,160)});
            while(inputTrace.Count>96)inputTrace.Dequeue();
        }
        KeyEventHandler keyDown=(_,e)=>ObserveInput("KeyDown",e,e.Key.ToString(),e.Handled);
        KeyEventHandler keyUp=(_,e)=>ObserveInput("KeyUp",e,e.Key.ToString(),e.Handled);
        TypedEventHandler<UIElement,CharacterReceivedRoutedEventArgs> character=(_,e)=>ObserveInput("CharacterReceived",e,e.Character.ToString(),e.Handled);
        PointerEventHandler pressed=(_,e)=>ObserveInput("CanvasPointerPressed",e,handled:e.Handled);
        void ObservePointer(string phase,PointerRoutedEventArgs args)
        {
            var point=args.GetCurrentPoint(view);var source=args.OriginalSource as DependencyObject;string? sourceId=null;
            for(var element=source;element is not null;element=VisualTreeHelper.GetParent(element))
                if(AutomationProperties.GetAutomationId(element) is{Length:>0} identity){sourceId=identity;break;}
            var owner=view.XamlRoot is{} xamlRoot?FocusManager.GetFocusedElement(xamlRoot):null;
            pointerTrace.Enqueue(new{milliseconds=watch.ElapsedMilliseconds,phase,pointer=args.Pointer.PointerId,
                source=args.OriginalSource?.GetType().FullName,sourceId,x=point.Position.X,y=point.Position.Y,
                left=point.Properties.IsLeftButtonPressed,right=point.Properties.IsRightButtonPressed,middle=point.Properties.IsMiddleButtonPressed,
                handled=args.Handled,focusId=owner is DependencyObject focused?AutomationProperties.GetAutomationId(focused):null});
            while(pointerTrace.Count>64)pointerTrace.Dequeue();
        }
        PointerEventHandler workspacePressed=(_,e)=>ObservePointer("Pressed",e);
        PointerEventHandler workspaceMoved=(_,e)=>ObservePointer("Moved",e);
        PointerEventHandler workspaceReleased=(_,e)=>ObservePointer("Released",e);
        RoutedEventHandler focused=(_,e)=>ObserveInput("GotFocus",e);
        RoutedEventHandler blurred=(_,e)=>ObserveInput("LostFocus",e);
        TypedEventHandler<AutoSuggestBox,AutoSuggestBoxTextChangedEventArgs> textChanged=(_,e)=>ObserveInput("CommandTextChanged",key:e.Reason.ToString());
        view.AddHandler(UIElement.KeyDownEvent,keyDown,true);
        view.AddHandler(UIElement.KeyUpEvent,keyUp,true);
        view.AddHandler(UIElement.CharacterReceivedEvent,character,true);
        modelCanvas.AddHandler(UIElement.PointerPressedEvent,pressed,true);
        view.AddHandler(UIElement.PointerPressedEvent,workspacePressed,true);
        view.AddHandler(UIElement.PointerMovedEvent,workspaceMoved,true);
        view.AddHandler(UIElement.PointerReleasedEvent,workspaceReleased,true);
        view.GotFocus+=focused;view.LostFocus+=blurred;commandInput.TextChanged+=textChanged;
        try
        {
            view.GraphEditor.Canvas.ObservePointerEvents=true;
            view.SetPreferences(new(DefinitionVisible:true,InspectorTab:"Layers"),false);controller.Scene.ShowGrid=true;controller.Scene.ShowIsocurves=true;
            view.Viewport.Canvas.RestoreFourViews();view.Viewport.Canvas.Fit();view.GraphEditor.Canvas.Fit();
            view.Console.ClearHistory();view.Console.WriteLine("NurbsScene — independent NURBS modeling and visual programming");
            await Task.Delay(250);
            // Each isolated campaign has a fixed hard lifetime, including the
            // expanded native camera and rendering-preference workflows on the
            // software GPU. Individual input waits still expire after 20 seconds;
            // the finish flag restores promptly and never renews this budget.
            while(watch.Elapsed<observationLifetime)
            {
                if(Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.nurbsSceneUiFinish === true ? 'yes' : 'no'")=="yes")break;
                var controls=new Dictionary<string,object>();double? sliderInspectorValue=null;
                // Canvas elements derive from FrameworkElement, not Control.
                // Read the root-local focus owner once for the complete sample;
                // do not infer focus from selection or dispatch Focus() here.
                // https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.focusmanager.getfocusedelement
                var focusedElement=view.XamlRoot is{} root?Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root):null;
                object Box(FrameworkElement element)
                {
                    var p=element.TransformToVisual(view).TransformPoint(new(0,0));
                    // Bounds include a TextBox's header. Observe the actual
                    // focused draft so input tests cannot silently type into a
                    // previous field after clicking a header or template border.
                    var identity=AutomationProperties.GetAutomationId(element);
                    var draft=element is TextBox textBox&&(identity.StartsWith("Slider",StringComparison.Ordinal)||identity.StartsWith("Typography",StringComparison.Ordinal)||identity.StartsWith("ViewportProperty",StringComparison.Ordinal)||identity.StartsWith("Rendering",StringComparison.Ordinal)||identity.StartsWith("Layer",StringComparison.Ordinal)||identity.StartsWith("InspectorColor",StringComparison.Ordinal))
                        ? textBox.Text : null;
                    if(draft?.Length>256)draft=draft[..256];
                    return new{x=p.X,y=p.Y,width=element.ActualWidth,height=element.ActualHeight,enabled=element is not Control c||c.IsEnabled,
                        focused=(element is Control input&&input.FocusState!=FocusState.Unfocused)||ReferenceEquals(element,focusedElement),text=draft};
                }
                void Visit(DependencyObject value)
                {
                    if(value is FrameworkElement e)
                    {
                        if(e.Visibility!=Visibility.Visible)return;
                        var id=AutomationProperties.GetAutomationId(e);
                        if(id=="SelectedNodeValue"&&e is NumberBox number&&double.IsFinite(number.Value))sliderInspectorValue=number.Value;
                        if(id.Length>0&&e.ActualWidth>0&&e.ActualHeight>0)controls[id]=Box(e);
                    }
                    for(var i=0;i<VisualTreeHelper.GetChildrenCount(value);i++)Visit(VisualTreeHelper.GetChild(value,i));
                }
                bool HasVisibleAncestors(DependencyObject value)
                {
                    for(DependencyObject? element=value;element is not null;element=VisualTreeHelper.GetParent(element))
                        if(element is UIElement visual&&visual.Visibility!=Visibility.Visible)return false;
                    return true;
                }
                Visit(view);
                if(view.GraphEditor.PanelTypographyEditorIsOpen&&view.GraphEditor.PanelTypographyEditor is{} typographyDialog)Visit(typographyDialog);
                if(view.GraphEditor.ValueEditorIsOpen&&view.GraphEditor.ValueEditor is{} valueDialog)Visit(valueDialog);
                if(view.GraphEditor.NumberSliderEditorIsOpen&&view.GraphEditor.NumberSliderEditor is{} sliderDialog)Visit(sliderDialog);
                if(view.RenderingSettingsEditorIsOpen&&view.RenderingSettingsEditor is{} renderingDialog)Visit(renderingDialog);
                if(view.Inspector.ColorEditorIsOpen&&view.Inspector.ColorEditor is{} layerColorDialog)Visit(layerColorDialog);
                if(view.GraphEditor.EditorHelpIsOpen&&view.GraphEditor.EditorHelp is{} editorHelp)Visit(editorHelp);
                // Native menu/flyout presenters live in the popup root rather
                // than under WorkspaceView. Retained menus can remain loaded
                // below a collapsed ancestor; loaded alone is not visibility.
                foreach(var item in view.GraphEditor.ActionElements.Concat(view.ActionElements))if(item.IsLoaded&&HasVisibleAncestors(item))Visit(item);
                if(view.GraphEditor.SearchIsOpen&&view.GraphEditor.SearchElement is{} search&&search.IsLoaded)Visit(search);
                var cad=view.Viewport.Canvas;var graph=view.GraphEditor.Canvas;
                controls["CadCanvas"]=Box(cad);controls["GraphCanvas"]=Box(graph);
                var cadOffset=cad.TransformToVisual(view).TransformPoint(new(0,0));
                var views=ViewportLayout.Create((int)cad.ActualWidth,(int)cad.ActualHeight,cad.Cameras,cad.Maximized)
                    .Select(v=>new{name=v.Name,x=cadOffset.X+v.X,y=cadOffset.Y+v.Y,width=v.Width,height=v.Height}).ToArray();
                var graphOffset=graph.TransformToVisual(view).TransformPoint(new(0,0));
                var nodes=drawing.Get(controller.Graph).Nodes.Select(n=>{
                    var right=graph.ScreenPoint(n.Node.Id,true,0);
                    var x=graphOffset.X+right.X-n.Bounds.Width*graph.Zoom;
                    var y=graphOffset.Y+right.Y-19*graph.Zoom;
                    object[] Ports(bool output)=> (output?n.Definition.Outputs:n.Definition.Inputs).Select((port,i)=>{
                        var p=graph.ScreenPoint(n.Node.Id,output,i);return(object)new{name=port.Name,x=graphOffset.X+p.X,y=graphOffset.Y+p.Y};
                    }).ToArray();
                    return new{id=n.Node.Id,name=n.Node.Name,component=n.Node.Component,modelX=n.Node.X,modelY=n.Node.Y,
                        value=n.Node.Parameters.GetValueOrDefault("value"),minimum=n.Node.Parameters.GetValueOrDefault("min"),maximum=n.Node.Parameters.GetValueOrDefault("max"),decimalPlaces=n.Node.Parameters.GetValueOrDefault("decimalPlaces"),enabled=n.Node.Enabled,preview=n.Node.Preview,matching=n.Node.Matching,x,y,width=n.Bounds.Width*graph.Zoom,height=n.Bounds.Height*graph.Zoom,
                        inputs=Ports(false),outputs=Ports(true)};
                }).ToArray();
                var last=controller.Document.Objects.LastOrDefault();
                var lastPoints=last?.Geometry is NurbsCurve curve?new[]{curve.PointAt(curve.Domain.Min).ToArray(),curve.PointAt(curve.Domain.Max).ToArray()}:null;
                var wires=controller.Graph.Wires.Select(w=>new{id=w.Id,from=w.From.Node,fromPort=w.From.Port,to=w.To.Node,toPort=w.To.Port,transform=w.Transform}).ToArray();
                var previewLines=new List<string>();
                void Preview(DependencyObject value)
                {
                    if(value is TextBlock text)previewLines.Add(text.Text);
                    for(var i=0;i<VisualTreeHelper.GetChildrenCount(value);i++)Preview(VisualTreeHelper.GetChild(value,i));
                }
                if(controls.ContainsKey("GraphResultPreview"))
                {
                    void FindPreview(DependencyObject value)
                    {
                        if(value is FrameworkElement e&&AutomationProperties.GetAutomationId(e)=="GraphResultPreview"){Preview(e);return;}
                        for(var i=0;i<VisualTreeHelper.GetChildrenCount(value);i++)FindPreview(VisualTreeHelper.GetChild(value,i));
                    }
                    FindPreview(view.GraphEditor);
                }
                var snapshot=new{status="ready",sample=++sample,controls,views,nodes,wires,previewLines,values=ObserveCanvasValues(view),lastGeometry=ObserveLastGeometry(view),typography=ObserveTypography(view),state=new{
                    objects=controller.Document.Objects.Count,layers=controller.Document.Layers.Count,activeLayer=controller.Document.ActiveLayer,
                    layerRows=controller.Document.Layers.Select(l=>new{id=l.Id,name=l.Name,visible=l.Visible,locked=l.Locked,color=new[]{l.Color.R,l.Color.G,l.Color.B,l.Color.A}}).ToArray(),
                    layerFilter=view.Inspector.FilterText,layerVisibleIds=view.Inspector.VisibleLayerIds,layerSelectedId=view.Inspector.SelectedLayerId,
                    layerColorEditorOpen=view.Inspector.ColorEditorIsOpen,layerColorEditorPending=view.Inspector.ColorEditor is not null,layerColorValidation=view.Inspector.ColorEditorValidation,
                    selectedLayers=controller.Document.Objects.Where(o=>controller.Document.Selection.Contains(o.Id)).Select(o=>o.LayerId).Distinct().ToArray(),
                    selected=controller.Document.Selection.Count,documentRevision=controller.Document.Revision,documentUndo=controller.Document.CanUndo,documentRedo=controller.Document.CanRedo,
                    nodeCount=controller.Graph.Nodes.Count,wireCount=controller.Graph.Wires.Count,graphRevision=controller.Graph.Revision,
                    graphFocus=graph.FocusState.ToString(),focusedElement=focusedElement?.GetType().FullName,
                    modelInputObservations=inputTrace.ToArray(),
                    workspacePointerObservations=pointerTrace.ToArray(),
                    pointerObservations=graph.PointerObservations,
                    graphSelected=graph.Selection.ToArray(),graphUndo=controller.GraphSession.CanUndo,graphRedo=controller.GraphSession.CanRedo,
                    graphErrors=controller.Graph.LastResult?.ErrorCount??0,graphLayoutBuilds=graph.LayoutBuilds,graphPathBuilds=graph.PathBuilds,
                    meshBuilds=controller.Scene.MeshBuildCount,layerStructureChanges=view.Inspector.LayerStructureChanges,
                    contextMenuOpen=view.GraphEditor.ContextMenuIsOpen,searchOpen=view.GraphEditor.SearchIsOpen,sliderEditorOpen=view.GraphEditor.NumberSliderEditorIsOpen,sliderValidation=view.GraphEditor.NumberSliderValidation,sliderInspectorValue,
                    shelfPageBuilds=view.GraphEditor.Shelf.PageBuildCount,shelfButtonBuilds=view.GraphEditor.Shelf.ButtonBuildCount,
                    paletteGroupOpen=view.GraphEditor.Shelf.IsGroupMenuOpen,paletteOpenedGroup=view.GraphEditor.Shelf.OpenedGroup,
                    componentPaletteVisible=view.GraphEditor.ComponentPaletteVisible,canvasToolbarVisible=view.GraphEditor.CanvasToolbarVisible,
                    graphGridVisible=graph.GridVisible,graphComponentIconsVisible=graph.ComponentIconsVisible,graphComponentNamesVisible=graph.ComponentNamesVisible,graphFancyWires=graph.FancyWires,
                    toolbar=view.Toolbar.Category,category=view.GraphEditor.Shelf.Category,inspector=view.Inspector.ActiveTab,
                    startupPreferences=preferences.Encode(),preferences=view.Preferences.Encode(),sidebarVisible=view.Preferences.SidebarVisible,inspectorWidth=view.Preferences.InspectorWidth,historyExpanded=view.Console.HistoryExpanded,commandInput=view.Console.InputText,commandHistoryCount=view.Console.HistoryCount,commandHistory=view.Console.HistoryText,
                    modelContextMenuOpen=view.ModelContextMenuIsOpen,viewportPropertyDraft=view.ViewportPropertyDraft,viewportPropertyValidation=view.ViewportPropertyValidation,
                    renderingSettings=RenderSettingsPreferences.Serialize(cad.RenderSettings),startupRenderingSettings=RenderSettingsPreferences.Serialize(renderingSettings),
                    renderingDialogOpen=view.RenderingSettingsEditorIsOpen,renderingValidation=view.RenderingSettingsValidation,renderFrames=cad.Frames,
                    geometryUploadBytes=cad.GeometryUploadBytes,geometryUploadCount=cad.GeometryUploadCount,
                    cameras=cad.Cameras.ToDictionary(p=>p.Key,p=>new{target=p.Value.Target.ToArray(),distance=p.Value.Distance,scale=p.Value.Scale,azimuth=p.Value.Azimuth,elevation=p.Value.Elevation,fov=p.Value.FieldOfView}),
                    dock=view.Preferences.Dock,definitionVisible=view.Preferences.DefinitionVisible,inspectorVisible=view.Preferences.InspectorVisible,
                    definitionFloating=view.Preferences.Dock=="Float",definitionFolded=view.GraphEditor.IsFolded,editorHelpOpen=view.GraphEditor.EditorHelpIsOpen,
                    activeViewport=cad.ActiveViewport, maximized=cad.Maximized,gridSnap=view.Aids.GridSnap,ortho=view.Aids.Ortho,endSnap=view.Aids.EndSnap,
                    pending=controller.PendingCommand,anchor=controller.CommandAnchor?.ToArray(),lastName=last?.Name,lastPoints,
                    gpuError=cad.LastError,workspaceWidth=view.ActualWidth,workspaceHeight=view.ActualHeight}};
                PublishUi(snapshot);await Task.Delay(120);
            }
            view.GraphEditor.Canvas.ObservePointerEvents=false;
            controller.Load(source);controller.Scene.ShowGrid=true;controller.Scene.ShowIsocurves=true;
            view.SetPreferences(preferences,false);view.Aids.GridSnap=false;view.Aids.Ortho=false;view.Aids.EndSnap=false;
            view.Viewport.Canvas.RenderSettings=renderingSettings;
            view.Viewport.Canvas.RestoreFourViews();view.Viewport.Canvas.Fit();view.GraphEditor.Canvas.Fit();
            if(watch.Elapsed>=observationLifetime)
                PublishUi(new{status="failed",error="The qualification observation lifetime expired before the test finished."});
            else PublishUi(new{status="restored"});
        }
        catch(Exception error){PublishUi(new{status="failed",error=error.ToString()});}
        finally
        {
            view.RemoveHandler(UIElement.KeyDownEvent,keyDown);
            view.RemoveHandler(UIElement.KeyUpEvent,keyUp);
            view.RemoveHandler(UIElement.CharacterReceivedEvent,character);
            modelCanvas.RemoveHandler(UIElement.PointerPressedEvent,pressed);
            view.RemoveHandler(UIElement.PointerPressedEvent,workspacePressed);
            view.RemoveHandler(UIElement.PointerMovedEvent,workspaceMoved);
            view.RemoveHandler(UIElement.PointerReleasedEvent,workspaceReleased);
            view.GotFocus-=focused;view.LostFocus-=blurred;commandInput.TextChanged-=textChanged;
        }
    }
    private static void PublishUi(object snapshot)
    {
        var json=JsonSerializer.Serialize(JsonSerializer.Serialize(snapshot));
        Uno.Foundation.WebAssemblyRuntime.InvokeJS($"globalThis.nurbsSceneUi=JSON.parse({json});");
    }
}
#endif
