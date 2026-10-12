#if NURBSSCENE_QUALIFICATION
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
        var watch=Stopwatch.StartNew();var drawing=new GraphPresentation();long sample=0;
        try
        {
            view.GraphEditor.Canvas.ObservePointerEvents=true;
            view.SetPreferences(new(),false);controller.Scene.ShowGrid=true;controller.Scene.ShowIsocurves=true;
            view.Viewport.Canvas.RestoreFourViews();view.Viewport.Canvas.Fit();view.GraphEditor.Canvas.Fit();
            view.Console.ClearHistory();view.Console.WriteLine("NurbsScene — independent NURBS modeling and visual programming");
            await Task.Delay(250);
            // The expanded suite contains three complete input campaigns. Keep a
            // bounded observer lifetime without restoring the document mid-campaign.
            while(watch.Elapsed<TimeSpan.FromMinutes(10))
            {
                if(Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.nurbsSceneUiFinish === true ? 'yes' : 'no'")=="yes")break;
                var controls=new Dictionary<string,object>();double? sliderInspectorValue=null;
                object Box(FrameworkElement element)
                {
                    var p=element.TransformToVisual(view).TransformPoint(new(0,0));
                    return new{x=p.X,y=p.Y,width=element.ActualWidth,height=element.ActualHeight,enabled=element is not Control c||c.IsEnabled};
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
                Visit(view);
                if(view.GraphEditor.ValueEditorIsOpen&&view.GraphEditor.ValueEditor is{} valueDialog)Visit(valueDialog);
                if(view.GraphEditor.NumberSliderEditorIsOpen&&view.GraphEditor.NumberSliderEditor is{} sliderDialog)Visit(sliderDialog);
                // Native menu/flyout presenters live in the popup root rather
                // than under WorkspaceView. Observe only loaded controls.
                foreach(var item in view.GraphEditor.ActionElements)if(item.IsLoaded)Visit(item);
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
                var snapshot=new{status="ready",sample=++sample,controls,views,nodes,wires,previewLines,values=ObserveCanvasValues(view),state=new{
                    objects=controller.Document.Objects.Count,layers=controller.Document.Layers.Count,
                    selected=controller.Document.Selection.Count,documentRevision=controller.Document.Revision,
                    nodeCount=controller.Graph.Nodes.Count,wireCount=controller.Graph.Wires.Count,graphRevision=controller.Graph.Revision,
                    graphFocus=graph.FocusState.ToString(),focusedElement=view.XamlRoot is{} root?Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root)?.GetType().FullName:null,
                    pointerObservations=graph.PointerObservations,
                    graphSelected=graph.Selection.ToArray(),graphUndo=controller.GraphSession.CanUndo,graphRedo=controller.GraphSession.CanRedo,
                    graphErrors=controller.Graph.LastResult?.ErrorCount??0,graphLayoutBuilds=graph.LayoutBuilds,graphPathBuilds=graph.PathBuilds,
                    meshBuilds=controller.Scene.MeshBuildCount,layerStructureChanges=view.Inspector.LayerStructureChanges,
                    contextMenuOpen=view.GraphEditor.ContextMenuIsOpen,searchOpen=view.GraphEditor.SearchIsOpen,sliderEditorOpen=view.GraphEditor.NumberSliderEditorIsOpen,sliderValidation=view.GraphEditor.NumberSliderValidation,sliderInspectorValue,
                    shelfPageBuilds=view.GraphEditor.Shelf.PageBuildCount,shelfButtonBuilds=view.GraphEditor.Shelf.ButtonBuildCount,
                    toolbar=view.Toolbar.Category,category=view.GraphEditor.Shelf.Category,inspector=view.Inspector.ActiveTab,
                    dock=view.Preferences.Dock,definitionVisible=view.Preferences.DefinitionVisible,inspectorVisible=view.Preferences.InspectorVisible,
                    activeViewport=cad.ActiveViewport,maximized=cad.Maximized,gridSnap=view.Aids.GridSnap,ortho=view.Aids.Ortho,endSnap=view.Aids.EndSnap,
                    pending=controller.PendingCommand,anchor=controller.CommandAnchor?.ToArray(),lastName=last?.Name,lastPoints,
                    gpuError=cad.LastError,workspaceWidth=view.ActualWidth,workspaceHeight=view.ActualHeight}};
                PublishUi(snapshot);await Task.Delay(120);
            }
            view.GraphEditor.Canvas.ObservePointerEvents=false;
            controller.Load(source);controller.Scene.ShowGrid=true;controller.Scene.ShowIsocurves=true;
            view.SetPreferences(preferences,false);view.Aids.GridSnap=false;view.Aids.Ortho=false;view.Aids.EndSnap=false;
            view.Viewport.Canvas.RestoreFourViews();view.Viewport.Canvas.Fit();view.GraphEditor.Canvas.Fit();
            if(watch.Elapsed>=TimeSpan.FromMinutes(10))
                PublishUi(new{status="failed",error="The qualification observation lifetime expired before the test finished."});
            else PublishUi(new{status="restored"});
        }
        catch(Exception error){PublishUi(new{status="failed",error=error.ToString()});}
    }
    private static void PublishUi(object snapshot)
    {
        var json=JsonSerializer.Serialize(JsonSerializer.Serialize(snapshot));
        Uno.Foundation.WebAssemblyRuntime.InvokeJS($"globalThis.nurbsSceneUi=JSON.parse({json});");
    }
}
#endif
