using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NurbsScene.IO;
using NurbsScene.Rendering;
using NurbsScene.Workbench;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NurbsScene.Controls;

/// <summary>Reusable native Uno CAD/parametric workspace. The application entry project contains no modeling logic.</summary>
public sealed class WorkspaceView : UserControl,IDisposable
{
    public WorkspaceController Controller {get;}
    public ViewportControl Viewport {get;}=new();
    public GraphEditorControl GraphEditor {get;}=new();
    public CommandConsole Console {get;}=new();
    private readonly LayerInspector _inspector=new();
    private readonly TextBlock _status=new(){FontSize=11,Margin=new(7,4,7,4)};
    private readonly Grid _workspace=new(),_body=new();
    private readonly RowDefinition _viewportRow=new(){Height=new GridLength(1.2,GridUnitType.Star)},_graphRow=new(){Height=new GridLength(1,GridUnitType.Star)};
    private readonly ColumnDefinition _inspectorColumn=new(){Width=new GridLength(235)};
    private readonly Border _graphGrip=new(){Background=new SolidColorBrush(ColorHelper.FromArgb(255,168,173,173))};
    private long _savedDocument=-1,_savedGraph=-1;
    private bool _graphVisible=true;
    public WorkspaceView(WorkspaceController? controller=null)
    {
        Controller=controller??new();RequestedTheme=ElementTheme.Light;
        var root=new Grid{Background=new SolidColorBrush(ColorHelper.FromArgb(255,235,237,237))};
        foreach(var size in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new(){Height=size});
        var menus=CreateMenus();root.Children.Add(menus);root.Children.Add(Console);Grid.SetRow(Console,1);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=3,Padding=new(5,3,5,3)};
        foreach(var (name,command) in new[]{("Line","Line"),("Circle","Circle 0,0,0 10"),("Curve","Interpolate -20,0,0 -8,12,3 8,-8,6 20,0,0"),("Extrude","Extrude 0,0,12"),("Loft","Loft"),("Move","Move 5,0,0"),("Rotate","Rotate 30")})tools.Children.Add(Tool(name,()=>Run(command)));
        tools.Children.Add(Tool("Undo",()=>Run("Undo")));tools.Children.Add(Tool("Redo",()=>Run("Redo")));tools.Children.Add(Tool("Zoom extents",()=>Run("ZoomExtents")));tools.Children.Add(Tool("Definition",ToggleGraph));tools.Children.Add(Tool("Save",()=>_ =SafeAsync(SaveAsync)));
        var toolbar=new ScrollViewer{Content=tools,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(toolbar);Grid.SetRow(toolbar,2);
        _body.ColumnDefinitions.Add(new(){Width=new GridLength(37)});_body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});_body.ColumnDefinitions.Add(new(){Width=new GridLength(5)});_body.ColumnDefinitions.Add(_inspectorColumn);
        var palette=new StackPanel{Spacing=3,Padding=new(2)};
        foreach(var (name,command) in new[]{("↖","SelNone"),("╱","Line"),("○","Circle 0,0,0 10"),("∿","Interpolate -20,0,0 -10,10,0 10,-10,0 20,0,0"),("▱","Saddle"),("↟","Extrude 0,0,12"),("↔","Move 5,0,0"),("↻","Rotate 30"),("×","Delete")}){var button=Tool(name,()=>Run(command));button.Width=32;ToolTipService.SetToolTip(button,command);palette.Children.Add(button);}
        _body.Children.Add(palette);_workspace.RowDefinitions.Add(_viewportRow);_workspace.RowDefinitions.Add(new(){Height=new GridLength(5)});_workspace.RowDefinitions.Add(_graphRow);
        _workspace.Children.Add(Viewport);_workspace.Children.Add(_graphGrip);Grid.SetRow(_graphGrip,1);_workspace.Children.Add(GraphEditor);Grid.SetRow(GraphEditor,2);_body.Children.Add(_workspace);Grid.SetColumn(_workspace,1);
        var sideGrip=new Border{Background=new SolidColorBrush(ColorHelper.FromArgb(255,168,173,173))};_body.Children.Add(sideGrip);Grid.SetColumn(sideGrip,2);_body.Children.Add(_inspector);Grid.SetColumn(_inspector,3);
        InstallGrip(_graphGrip,true);InstallGrip(sideGrip,false);root.Children.Add(_body);Grid.SetRow(_body,3);root.Children.Add(_status);Grid.SetRow(_status,4);Content=root;
        AutomationProperties.SetAutomationId(this,"NurbsSceneWorkspace");AutomationProperties.SetAutomationId(_status,"WorkspaceStatus");
        Viewport.Canvas.Scene=Controller.Scene;GraphEditor.Session=Controller.GraphSession;_inspector.Document=Controller.Document;
        Controller.Changed+=Refresh;Controller.Message+=Console.WriteLine;Controller.FitRequested+=Viewport.Canvas.Fit;Controller.WorkspaceReplaced+=Replaced;
        Console.CommandSubmitted+=Run;GraphEditor.Message+=Console.WriteLine;_inspector.Message+=Console.WriteLine;
        GraphEditor.BakeRequested+=ids=>{try{Controller.Bake(ids);}catch(Exception error){Console.WriteLine(error.Message);}};
        Viewport.Canvas.ObjectPicked+=(result,add)=>{if(Controller.PendingCommand!="")return;if(result is null){if(!add)Controller.Document.Select([]);}else Controller.Document.Select([result.ObjectId],add);};
        Viewport.Canvas.ConstructionPointPicked+=p=>{try{Controller.AcceptPoint(p);}catch(Exception error){Console.WriteLine(error.Message);Controller.CancelCommand();}};
        Viewport.Canvas.DiagnosticsChanged+=text=>{Console.WriteLine(text);Refresh();};
        SizeChanged+=(_,_)=>{if(ActualWidth<900)_inspectorColumn.Width=new GridLength(0);else if(_inspectorColumn.Width.Value<180)_inspectorColumn.Width=new GridLength(235);};
        Loaded+=(_,_)=>{GraphEditor.Canvas.Fit();Viewport.Canvas.Fit();};
        Controller.LoadPavilion();Refresh();Console.WriteLine("NurbsScene source preview. Exact NURBS / parametric subset; Rhino/Grasshopper parity is not claimed.");
    }
    private bool IsDirty=>Controller.Document.Revision!=_savedDocument||Controller.Graph.Revision!=_savedGraph;
    private void Replaced(){_inspector.Document=Controller.Document;Refresh();}
    private void Refresh(){Viewport.Canvas.Invalidate();var graph=Controller.Graph;_status.Text=$"{Controller.Document.Objects.Count} objects  |  {graph.Nodes.Count} components · {graph.Wires.Count} wires  |  {graph.LastResult?.ErrorCount??0} component errors  |  {Controller.Document.Units}  |  {Controller.Scene.Mode}  |  {Viewport.Canvas.Backend}";}
    private void Run(string command){try{Controller.Execute(command);}catch(Exception error){Console.WriteLine($"Error: {error.Message}");}}
    private static Button Tool(string label,Action action){var button=new Button{Content=label,Padding=new(7,3,7,3),FontSize=11,MinHeight=27,MinWidth=0,CornerRadius=new(0)};button.Click+=(_,_)=>action();return button;}
    private void ToggleGraph(){_graphVisible=!_graphVisible;GraphEditor.Visibility=_graphVisible?Visibility.Visible:Visibility.Collapsed;_graphGrip.Visibility=GraphEditor.Visibility;_graphRow.Height=_graphVisible?new GridLength(1,GridUnitType.Star):new GridLength(0);_viewportRow.Height=new GridLength(1.2,GridUnitType.Star);}
    private void InstallGrip(Border grip,bool horizontal)
    {
        bool dragging=false;grip.PointerPressed+=(_,e)=>{dragging=grip.CapturePointer(e.Pointer);e.Handled=true;};
        grip.PointerMoved+=(_,e)=>{if(!dragging)return;if(horizontal){var y=e.GetCurrentPoint(_workspace).Position.Y;var height=_workspace.ActualHeight;if(height<300)return;y=Math.Clamp(y,130,height-140);_viewportRow.Height=new GridLength(y);_graphRow.Height=new GridLength(Math.Max(140,height-y-5));}else{var width=_body.ActualWidth-e.GetCurrentPoint(_body).Position.X;_inspectorColumn.Width=new GridLength(Math.Clamp(width,180,420));}e.Handled=true;};
        grip.PointerReleased+=(_,e)=>{dragging=false;grip.ReleasePointerCapture(e.Pointer);e.Handled=true;};grip.PointerCanceled+=(_,e)=>{dragging=false;grip.ReleasePointerCapture(e.Pointer);};
    }
    private MenuBar CreateMenus()
    {
        var bar=new MenuBar{FontSize=12,MinHeight=28};
        MenuBarItem Menu(string name){var menu=new MenuBarItem{Title=name};bar.Items.Add(menu);return menu;}
        void Item(MenuBarItem menu,string name,Action action){var item=new MenuFlyoutItem{Text=name};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        void Command(MenuBarItem menu,string name,string command)=>Item(menu,name,()=>Run(command));
        var file=Menu("File");Item(file,"New",()=>_ =SafeAsync(async()=>{if(await ConfirmDiscardAsync()){Controller.New();_savedDocument=Controller.Document.Revision;_savedGraph=Controller.Graph.Revision;}}));Item(file,"Open…",()=>_ =SafeAsync(OpenAsync));Item(file,"Save as…",()=>_ =SafeAsync(SaveAsync));file.Items.Add(new MenuFlyoutSeparator());Item(file,"Export display mesh (.obj)…",()=>_ =SafeAsync(()=>ExportAsync(false)));Item(file,"Export display mesh (.stl)…",()=>_ =SafeAsync(()=>ExportAsync(true)));Item(file,"Load pavilion sample",()=>_ =SafeAsync(async()=>{if(await ConfirmDiscardAsync())Controller.LoadPavilion();}));
        var edit=Menu("Edit");Command(edit,"Undo","Undo");Command(edit,"Redo","Redo");Command(edit,"Delete selected","Delete");Command(edit,"Select all","SelAll");Command(edit,"Select none","SelNone");
        var view=Menu("View");Command(view,"Zoom extents","ZoomExtents");foreach(var name in new[]{"Top","Perspective","Front","Right"})Item(view,name,()=>Viewport.Canvas.Maximize(name));view.Items.Add(new MenuFlyoutSeparator());Command(view,"Wireframe","Wireframe");Command(view,"Shaded","Shaded");Command(view,"Ghosted","Ghosted");Item(view,"Toggle grid",()=>{Controller.Scene.ShowGrid=!Controller.Scene.ShowGrid;Refresh();});Item(view,"Toggle isocurves",()=>{Controller.Scene.ShowIsocurves=!Controller.Scene.ShowIsocurves;Refresh();});
        var curve=Menu("Curve");Command(curve,"Line","Line");Item(curve,"Circle…",()=>Console.BeginCommand("Circle 0,0,0 10"));Item(curve,"Interpolate points…",()=>Console.BeginCommand("Interpolate -20,0,0 -10,10,0 10,-10,0 20,0,0"));Command(curve,"Reverse","Reverse");Item(curve,"Insert knot…",()=>Console.BeginCommand("InsertKnot 0.5"));Command(curve,"Elevate degree","ElevateDegree");
        var surface=Menu("Surface");Item(surface,"Extrude curve…",()=>Console.BeginCommand("Extrude 0,0,12"));Item(surface,"Revolve curve…",()=>Console.BeginCommand("Revolve 0,0,0 0,0,1 360"));Command(surface,"Loft selected curves","Loft");Command(surface,"Bicubic saddle","Saddle");
        var solid=Menu("Solid");solid.Items.Add(new MenuFlyoutItem{Text="B-rep modeling is not integrated in this build",IsEnabled=false});
        var transform=Menu("Transform");foreach(var (name,command) in new[]{("Move…","Move 5,0,0"),("Copy…","Copy 5,0,0"),("Scale…","Scale 2"),("Rotate…","Rotate 30")})Item(transform,name,()=>Console.BeginCommand(command));
        var tools=Menu("Tools");Item(tools,"Solve definition",()=>_ =SafeAsync(async()=>{var result=await Controller.SolveAsync();Console.WriteLine($"Solved {result.Evaluated} components; {result.Cached} cached; {result.ErrorCount} errors.");}));Item(tools,"Bake selected components",()=>{try{Controller.Bake(GraphEditor.Canvas.Selection);}catch(Exception error){Console.WriteLine(error.Message);}});
        var panels=Menu("Panels");Item(panels,"Parametric editor",ToggleGraph);Item(panels,"Properties / layers",()=>{_inspectorColumn.Width=_inspectorColumn.Width.Value==0?new GridLength(235):new GridLength(0);});
        var help=Menu("Help");Item(help,"About NurbsScene",()=>_ =SafeAsync(async()=>{await new ContentDialog{XamlRoot=XamlRoot,Title="NurbsScene — independent source preview",Content=new TextBlock{Text="Reusable Uno CAD and visual programming controls, exact positive-weight NURBS and typed incremental graphs.\n\nThis is not a complete Rhino or Grasshopper replacement. B-rep topology, solid booleans, rolling-ball fillets, SubD, proprietary formats/plugins, complete UI parity and production rendering qualification remain open.\n\nNo proprietary application code, icons or binaries are included.",TextWrapping=TextWrapping.Wrap},CloseButtonText="Close"}.ShowAsync();}));return bar;
    }
    private async Task SafeAsync(Func<Task> action){try{await action();}catch(Exception error){Console.WriteLine($"Error: {error.Message}");}}
    private async Task<bool> ConfirmDiscardAsync()
    {
        if(!IsDirty)return true;var result=await new ContentDialog{XamlRoot=XamlRoot,Title="Discard unsaved workspace changes?",Content="Save the current workspace before replacing it to retain its exact geometry and definition.",PrimaryButtonText="Discard",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close}.ShowAsync();return result==ContentDialogResult.Primary;
    }
    private async Task OpenAsync()
    {
        var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".nscene");var file=await picker.PickSingleFileAsync();if(file is null)return;var properties=await file.GetBasicPropertiesAsync();if(properties.Size>WorkspaceFormat.MaximumBytes)throw new InvalidOperationException("Workspace exceeds 64 MiB.");var text=await FileIO.ReadTextAsync(file);
        // Validate before offering to discard or modifying the current document.
        WorkspaceFormat.Deserialize(Controller.Kernel.Evaluator,Controller.Registry,text);if(!await ConfirmDiscardAsync())return;Controller.Load(text);_savedDocument=Controller.Document.Revision;_savedGraph=Controller.Graph.Revision;Viewport.Canvas.Fit();GraphEditor.Canvas.Fit();
    }
    private async Task SaveAsync()
    {
        var documentRevision=Controller.Document.Revision;var graphRevision=Controller.Graph.Revision;var text=Controller.Save();var picker=new FileSavePicker{SuggestedFileName=Controller.Document.Name};picker.FileTypeChoices.Add("NurbsScene exact managed workspace",new List<string>{".nscene"});var file=await picker.PickSaveFileAsync();if(file is null)return;await FileIO.WriteTextAsync(file,text);_savedDocument=documentRevision;_savedGraph=graphRevision;Console.WriteLine($"Saved {file.Name}.");
    }
    private async Task ExportAsync(bool stl)
    {
        var objects=Controller.Document.Selection.Count>0?Controller.Document.Objects.Where(o=>Controller.Document.Selection.Contains(o.Id)).ToArray():Controller.Document.Objects.ToArray();var picker=new FileSavePicker{SuggestedFileName=Controller.Document.Name};picker.FileTypeChoices.Add(stl?"Display mesh STL":"Display mesh OBJ",new List<string>{stl?".stl":".obj"});var file=await picker.PickSaveFileAsync();if(file is null)return;if(stl)await FileIO.WriteBytesAsync(file,WorkspaceFormat.ExportStl(objects));else await FileIO.WriteTextAsync(file,WorkspaceFormat.ExportObj(objects));Console.WriteLine("Exported display triangulation. This is not exact CAD interchange or a watertight-solid certificate.");
    }
    public void Dispose(){Controller.Changed-=Refresh;Controller.Message-=Console.WriteLine;Controller.FitRequested-=Viewport.Canvas.Fit;Controller.WorkspaceReplaced-=Replaced;_inspector.Detach();GraphEditor.Dispose();Controller.Dispose();}
}
