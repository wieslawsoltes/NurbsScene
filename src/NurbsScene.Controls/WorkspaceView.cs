using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NurbsScene.IO;
using NurbsScene.Rendering;
using NurbsScene.Presentation;
using NurbsScene.Workbench;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace NurbsScene.Controls;

/// <summary>Reusable compact CAD/definition workspace with authored vector icons,
/// real modeling toggles and versioned local layout preferences.</summary>
public sealed class WorkspaceView : UserControl,IDisposable
{
    public WorkspaceController Controller {get;}
    public ViewportControl Viewport {get;}=new();
    public GraphEditorControl GraphEditor {get;}=new();
    public CommandConsole Console {get;}=new();
    public ModelToolbar Toolbar {get;}=new();
    public LayerInspector Inspector {get;}=new();
    public WorkspacePreferences Preferences {get;private set;}=new();
    public ConstructionAids Aids {get;}=new();
    public Button SnapButton {get;}
    public Button OrthoButton {get;}
    public Button EndButton {get;}
    private readonly TextBlock _status=ClassicChrome.Label("Ready",10),_title=ClassicChrome.Label("NurbsScene",11),_coordinates=ClassicChrome.Label("World   x 0.000   y 0.000   z 0.000",10);
    private readonly Grid _workspace=new(),_body=new(),_modelPanel=new();
    private readonly ColumnDefinition _inspectorColumn=new(){Width=new GridLength(250)};
    private readonly Border _graphGrip=new(){Background=ClassicChrome.Brush(165,168,169)};
    private readonly Dictionary<string,Button> _viewportTabs=[];
    private bool _disposed,_graphVisible=true,_ownsController,_fitAfterLayout;
    private bool? _effectiveRight;
    private long _savedDocument=-1,_savedGraph=-1;
    private const string PreferenceKey="NurbsScene.Workspace.Layout.v1";
    public WorkspaceView(WorkspaceController? controller=null)
    {
        _ownsController=controller is null;Controller=controller??new();RequestedTheme=ElementTheme.Light;
        try{if(ApplicationData.Current.LocalSettings.Values.TryGetValue(PreferenceKey,out var stored)&&WorkspacePreferences.TryParse(stored as string,out var parsed))Preferences=parsed;}catch(Exception error){Console.WriteLine("Layout preferences: "+error.Message);}
        var root=new Grid{Background=ClassicChrome.Background};
        foreach(var height in new[]{new GridLength(21),new GridLength(27),GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),new GridLength(25)})root.RowDefinitions.Add(new(){Height=height});
        var titleRow=new Grid{Background=ClassicChrome.Brush(248,248,248)};titleRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});titleRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        _title.Margin=new(8,0,0,0);_title.TextTrimming=TextTrimming.CharacterEllipsis;titleRow.Children.Add(_title);
        var branding=ClassicChrome.Label("NURBS + VISUAL PROGRAMMING",9);branding.Margin=new(0,0,8,0);branding.Foreground=ClassicChrome.Brush(112,112,112);titleRow.Children.Add(branding);Grid.SetColumn(branding,1);root.Children.Add(titleRow);
        var menus=CreateMenus();root.Children.Add(menus);Grid.SetRow(menus,1);root.Children.Add(Console);Grid.SetRow(Console,2);root.Children.Add(Toolbar);Grid.SetRow(Toolbar,3);Toolbar.Invoked+=InvokeTool;
        _body.ColumnDefinitions.Add(new(){Width=new GridLength(57)});_body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});_body.ColumnDefinitions.Add(new(){Width=new GridLength(4)});_body.ColumnDefinitions.Add(_inspectorColumn);
        var palette=new Grid{Margin=new(1,2,1,2)};palette.ColumnDefinitions.Add(new(){Width=new GridLength(27)});palette.ColumnDefinitions.Add(new(){Width=new GridLength(27)});
        var toolIds=new[]{"SelectAll","SelectNone","Line","Circle","Interpolate","InsertKnot","Extrude","Revolve","Loft","Saddle","Move","Copy","Scale","Rotate","Mirror","Project","Area","Centroid","SplitCurve","Delete"};
        for(var i=0;i<(toolIds.Length+1)/2;i++)palette.RowDefinitions.Add(new(){Height=new GridLength(29)});
        for(var i=0;i<toolIds.Length;i++){var tool=WorkspaceCommands.Tools.Single(t=>t.Id==toolIds[i]);var button=ClassicChrome.IconButton(tool.Label,tool.Icon,()=>InvokeTool(tool),"Side_"+tool.Id);Grid.SetColumn(button,i%2);Grid.SetRow(button,i/2);palette.Children.Add(button);}
        var leftScroll=new ScrollViewer{Content=palette,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};_body.Children.Add(leftScroll);
        for(var i=0;i<3;i++){_workspace.RowDefinitions.Add(new(){Height=new GridLength(i==0?1:0,GridUnitType.Star)});_workspace.ColumnDefinitions.Add(new(){Width=new GridLength(i==0?1:0,GridUnitType.Star)});}
        _modelPanel.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});_modelPanel.RowDefinitions.Add(new(){Height=new GridLength(25)});_modelPanel.RowDefinitions.Add(new(){Height=new GridLength(26)});_modelPanel.Children.Add(Viewport);
        var tabs=new StackPanel{Orientation=Orientation.Horizontal,Spacing=1};tabs.Children.Add(ClassicChrome.IconButton("Four viewports","FourViews",()=>Viewport.Canvas.RestoreFourViews(),"FourViewButton"));
        foreach(var name in new[]{"Perspective","Top","Front","Right"}){var b=ClassicChrome.Button(name,()=>Viewport.Canvas.Activate(name),"ViewTab_"+name);b.DoubleTapped+=(_,e)=>{Viewport.Canvas.Maximize(name);e.Handled=true;};_viewportTabs[name]=b;tabs.Children.Add(b);}
        var tabScroll=ClassicChrome.Horizontal(tabs);_modelPanel.Children.Add(tabScroll);Grid.SetRow(tabScroll,1);
        var snaps=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4,Padding=new(5,0,0,0)};snaps.Children.Add(ClassicChrome.Label("Object snap",10));
        EndButton=ClassicChrome.Button("End",()=>{Aids.EndSnap=!Aids.EndSnap;UpdateAids();},"EndSnap");snaps.Children.Add(EndButton);snaps.Children.Add(ClassicChrome.Button("Grid size…",()=>_ =SafeAsync(GridOptionsAsync),"GridOptions"));
        var help=ClassicChrome.Label("F8 Ortho  ·  F9 Grid Snap",9);help.Margin=new(4,0,0,0);snaps.Children.Add(help);var snapScroll=ClassicChrome.Horizontal(snaps);_modelPanel.Children.Add(snapScroll);Grid.SetRow(snapScroll,2);
        _workspace.Children.Add(_modelPanel);_workspace.Children.Add(_graphGrip);_workspace.Children.Add(GraphEditor);_body.Children.Add(_workspace);Grid.SetColumn(_workspace,1);
        var sideGrip=new Border{Background=ClassicChrome.Border};_body.Children.Add(sideGrip);Grid.SetColumn(sideGrip,2);_body.Children.Add(Inspector);Grid.SetColumn(Inspector,3);InstallGrip(_graphGrip,true);InstallGrip(sideGrip,false);
        root.Children.Add(_body);Grid.SetRow(_body,4);
        var statusRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};_coordinates.Width=275;_coordinates.TextTrimming=TextTrimming.CharacterEllipsis;_coordinates.Margin=new(7,0,0,0);statusRow.Children.Add(_coordinates);
        SnapButton=ClassicChrome.Button("Grid Snap",()=>{Aids.GridSnap=!Aids.GridSnap;UpdateAids();},"GridSnap");OrthoButton=ClassicChrome.Button("Ortho",()=>{Aids.Ortho=!Aids.Ortho;UpdateAids();},"Ortho");statusRow.Children.Add(SnapButton);statusRow.Children.Add(OrthoButton);
        statusRow.Children.Add(ClassicChrome.Button("Panels",()=>SetPreferences(Preferences with{InspectorVisible=!Preferences.InspectorVisible}),"ToggleInspector"));statusRow.Children.Add(_status);
        var statusScroll=ClassicChrome.Horizontal(statusRow);root.Children.Add(statusScroll);Grid.SetRow(statusScroll,5);Content=root;
        AutomationProperties.SetAutomationId(this,"NurbsSceneWorkspace");AutomationProperties.SetAutomationId(_status,"WorkspaceStatus");
        Viewport.Canvas.Scene=Controller.Scene;Viewport.Canvas.Aids=Aids;Viewport.Canvas.ConstructionAnchor=()=>Controller.CommandAnchor;GraphEditor.Session=Controller.GraphSession;Inspector.Document=Controller.Document;
        Controller.Changed+=Refresh;Controller.Message+=Console.WriteLine;Controller.FitRequested+=Viewport.Canvas.Fit;Controller.WorkspaceReplaced+=Replaced;
        Console.CommandSubmitted+=Run;GraphEditor.Message+=Console.WriteLine;Inspector.Message+=Console.WriteLine;
        GraphEditor.DockRequested+=dock=>{if(dock=="Hide")SetPreferences(Preferences with{DefinitionVisible=false});else SetPreferences(Preferences with{Dock=dock,DefinitionVisible=true});};
        GraphEditor.BakeRequested+=ids=>{try{Controller.Bake(ids);}catch(Exception error){Console.WriteLine(error.Message);}};
        Viewport.Canvas.ObjectPicked+=(result,add)=>{if(Controller.PendingCommand!="")return;if(result is null){if(!add)Controller.Document.Select([]);}else Controller.Document.Select([result.ObjectId],add);};
        Viewport.Canvas.ConstructionPointPicked+=p=>{try{Controller.AcceptPoint(p);}catch(Exception error){Console.WriteLine(error.Message);Controller.CancelCommand();}};
        Viewport.Canvas.CursorMoved+=hit=>_coordinates.Text=hit is null?"World":FormattableString.Invariant($"World  x {hit.Point.X:0.000}  y {hit.Point.Y:0.000}  z {hit.Point.Z:0.000}  {hit.Snap}");
        Viewport.Canvas.DiagnosticsChanged+=text=>{Console.WriteLine(text);Refresh();};Viewport.Canvas.LayoutChanged+=Refresh;
        GraphEditor.Canvas.SizeChanged+=(_,_)=>{if(_fitAfterLayout&&GraphEditor.Canvas.ActualWidth>120&&GraphEditor.Canvas.ActualHeight>80){_fitAfterLayout=false;GraphEditor.Canvas.Fit();}};
        SizeChanged+=(_,_)=>ApplyLayout();Loaded+=(_,_)=>{ApplyLayout();GraphEditor.Canvas.Fit();Viewport.Canvas.Fit();};
        var save=new KeyboardAccelerator{Key=VirtualKey.S,Modifiers=VirtualKeyModifiers.Control};save.Invoked+=(sender,e)=>{_ =SafeAsync(SaveAsync);e.Handled=true;};KeyboardAccelerators.Add(save);
        foreach(var key in new[]{VirtualKey.F2,VirtualKey.F7,VirtualKey.F8,VirtualKey.F9}){var accelerator=new KeyboardAccelerator{Key=key};accelerator.Invoked+=(_,e)=>{if(key==VirtualKey.F2)Console.BeginCommand("");else if(key==VirtualKey.F7){Controller.Scene.ShowGrid=!Controller.Scene.ShowGrid;Refresh();}else if(key==VirtualKey.F8){Aids.Ortho=!Aids.Ortho;UpdateAids();}else{Aids.GridSnap=!Aids.GridSnap;UpdateAids();}e.Handled=true;};KeyboardAccelerators.Add(accelerator);}
        if(_ownsController)Controller.LoadPavilion();ApplyLayout();Refresh();UpdateAids();Console.WriteLine("NurbsScene — independent CAD alpha. Type a command, or double-click the definition canvas to search.");
    }
    public void SetPreferences(WorkspacePreferences preferences,bool persist=true)
    {
        var encoded=preferences.Encode();Preferences=preferences;ApplyLayout();
        if(persist)try{ApplicationData.Current.LocalSettings.Values[PreferenceKey]=encoded;}catch(Exception error){Console.WriteLine("Could not save layout: "+error.Message);}
    }
    private void ApplyLayout()
    {
        _graphVisible=Preferences.DefinitionVisible;GraphEditor.Visibility=_graphVisible?Visibility.Visible:Visibility.Collapsed;_graphGrip.Visibility=GraphEditor.Visibility;
        var showInspector=Preferences.InspectorVisible&&ActualWidth>=1050;
        _inspectorColumn.Width=showInspector?new GridLength(250):new GridLength(0);
        Inspector.Visibility=showInspector?Visibility.Visible:Visibility.Collapsed;
        var right=Preferences.Dock=="Right"&&ActualWidth>=1100;
        if(_effectiveRight!=right){_effectiveRight=right;_fitAfterLayout=true;}
        for(var i=0;i<3;i++){_workspace.ColumnDefinitions[i].Width=new GridLength(i==0?1:0,GridUnitType.Star);_workspace.RowDefinitions[i].Height=new GridLength(i==0?1:0,GridUnitType.Star);}
        Grid.SetRow(_graphGrip,right?0:1);Grid.SetColumn(_graphGrip,right?1:0);Grid.SetRow(GraphEditor,right?0:2);Grid.SetColumn(GraphEditor,right?2:0);
        if(_graphVisible)
        {
            if(right){_workspace.ColumnDefinitions[0].Width=new GridLength(Preferences.Split,GridUnitType.Star);_workspace.ColumnDefinitions[1].Width=new GridLength(5);_workspace.ColumnDefinitions[2].Width=new GridLength(1-Preferences.Split,GridUnitType.Star);}
            else{_workspace.RowDefinitions[0].Height=new GridLength(Preferences.Split,GridUnitType.Star);_workspace.RowDefinitions[1].Height=new GridLength(5);_workspace.RowDefinitions[2].Height=new GridLength(1-Preferences.Split,GridUnitType.Star);}
        }
    }
    private void InstallGrip(Border grip,bool definition)
    {
        bool dragging=false;grip.PointerPressed+=(_,e)=>{dragging=grip.CapturePointer(e.Pointer);e.Handled=true;};
        grip.PointerMoved+=(_,e)=>{if(!dragging)return;
            if(definition){var point=e.GetCurrentPoint(_workspace).Position;var right=Preferences.Dock=="Right"&&ActualWidth>=1100;var fraction=right?point.X/Math.Max(1,_workspace.ActualWidth):point.Y/Math.Max(1,_workspace.ActualHeight);SetPreferences(Preferences with{Split=Math.Clamp(fraction,.2,.8)},false);}
            else{var width=_body.ActualWidth-e.GetCurrentPoint(_body).Position.X;_inspectorColumn.Width=new GridLength(Math.Clamp(width,200,360));}e.Handled=true;};
        grip.PointerReleased+=(_,e)=>{if(dragging&&definition)SetPreferences(Preferences);dragging=false;grip.ReleasePointerCapture(e.Pointer);e.Handled=true;};grip.PointerCanceled+=(_,e)=>{dragging=false;grip.ReleasePointerCapture(e.Pointer);};
    }
    private void UpdateAids(){SnapButton.Background=Aids.GridSnap?ClassicChrome.Selected:ClassicChrome.Background;OrthoButton.Background=Aids.Ortho?ClassicChrome.Selected:ClassicChrome.Background;EndButton.Background=Aids.EndSnap?ClassicChrome.Selected:ClassicChrome.Background;}
    private bool IsDirty=>Controller.Document.Revision!=_savedDocument||Controller.Graph.Revision!=_savedGraph;
    private void Replaced(){Inspector.Document=Controller.Document;Refresh();}
    private void Refresh()
    {
        if(_disposed)return;Viewport.Canvas.Invalidate();_title.Text=$"{Controller.Document.Name}{(IsDirty?" *":"")} — NurbsScene";Console.SetPrompt(Controller.PendingCommand);
        _status.Text=$"{Controller.Document.Units}  |  {Controller.Document.GetLayer(Controller.Document.ActiveLayer).Name}  |  {Controller.Document.Selection.Count} selected  |  {Controller.Scene.Mode}";Toolbar.SetUndo(Controller.Document.CanUndo,Controller.Document.CanRedo);
        foreach(var (name,button) in _viewportTabs)button.Background=name==Viewport.Canvas.ActiveViewport?ClassicChrome.Brush(255,255,255):ClassicChrome.Background;
    }
    private void Run(string command){try{Controller.Execute(command);}catch(Exception error){Console.WriteLine($"Error: {error.Message}");}}
    private void InvokeTool(WorkspaceTool tool)
    {
        switch(tool.Command)
        {
            case "@New":_ =SafeAsync(NewAsync);break;case "@Open":_ =SafeAsync(OpenAsync);break;case "@Save":_ =SafeAsync(SaveAsync);break;
            case "@FourViews":Viewport.Canvas.RestoreFourViews();break;case "@Grid":Controller.Scene.ShowGrid=!Controller.Scene.ShowGrid;Refresh();break;
            case "@Definition":SetPreferences(Preferences with{DefinitionVisible=!Preferences.DefinitionVisible});break;
            default:if(tool.Prompt)Console.BeginCommand(tool.Command);else Run(tool.Command);break;
        }
    }
    private async Task NewAsync(){if(await ConfirmDiscardAsync()){Controller.New();_savedDocument=Controller.Document.Revision;_savedGraph=Controller.Graph.Revision;Refresh();}}
    private async Task GridOptionsAsync()
    {
        var box=new NumberBox{Header="World-unit snap spacing",Value=Aids.Spacing,Minimum=.000001,Maximum=1e9};
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="Construction grid",Content=box,PrimaryButtonText="Apply",CloseButtonText="Cancel"};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary)Aids.Spacing=box.Value;
    }
    private MenuBar CreateMenus()
    {
        var bar=new MenuBar{FontSize=11,MinHeight=24};
        MenuBarItem Menu(string name){var menu=new MenuBarItem{Title=name};bar.Items.Add(menu);return menu;}
        void Item(MenuBarItem menu,string name,Action action){var item=new MenuFlyoutItem{Text=name};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        var file=Menu("File");Item(file,"New workspace",()=>_ =SafeAsync(NewAsync));Item(file,"Open…",()=>_ =SafeAsync(OpenAsync));Item(file,"Save as…",()=>_ =SafeAsync(SaveAsync));Item(file,"Export display mesh (.obj)…",()=>_ =SafeAsync(()=>ExportAsync(false)));Item(file,"Export display mesh (.stl)…",()=>_ =SafeAsync(()=>ExportAsync(true)));Item(file,"Load pavilion sample",()=>_ =SafeAsync(async()=>{if(await ConfirmDiscardAsync())Controller.LoadPavilion();}));
        var edit=Menu("Edit");foreach(var id in new[]{"Undo","Redo","Delete","SelectAll","SelectNone"}){var tool=WorkspaceCommands.Tools.Single(t=>t.Id==id);Item(edit,tool.Label,()=>InvokeTool(tool));}
        foreach(var category in WorkspaceCommands.Categories.Where(c=>c!="Standard"))
        {
            var menu=Menu(category);foreach(var tool in WorkspaceCommands.Category(category))Item(menu,tool.Label,()=>InvokeTool(tool));
        }
        var tools=Menu("Tools");Item(tools,"Construction grid…",()=>_ =SafeAsync(GridOptionsAsync));Item(tools,"Solve definition",()=>_ =SafeAsync(async()=>{await Controller.SolveAsync();}));Item(tools,"Bake selected components",()=>{try{Controller.Bake(GraphEditor.Canvas.Selection);}catch(Exception e){Console.WriteLine(e.Message);}});
        var panels=Menu("Panels");foreach(var name in new[]{"Layers","Properties","Objects"})Item(panels,name,()=>{SetPreferences(Preferences with{InspectorVisible=true});Inspector.ShowTab(name);});Item(panels,"Definition: dock right",()=>SetPreferences(Preferences with{Dock="Right",DefinitionVisible=true}));Item(panels,"Definition: dock bottom",()=>SetPreferences(Preferences with{Dock="Bottom",DefinitionVisible=true}));Item(panels,"Reset layout",()=>SetPreferences(new()));
        var help=Menu("Help");Item(help,"About NurbsScene",()=>_ =SafeAsync(async()=>{await new ContentDialog{XamlRoot=XamlRoot,Title="NurbsScene — independent CAD alpha",Content=new TextBlock{Text="Exact positive-weight NURBS, modular modeling libraries and typed visual programming.\n\nThe compact interface follows Rhino/Grasshopper conventions but is not identical. B-rep/solid operations, arbitrary trims, SubD, proprietary formats/plugins, complete command coverage and direct Uno WebGPU remain unfinished.\n\nOnly implemented commands are shown. Construction aids currently provide world-aligned grid, 90-degree ortho and curve-end snapping.\n\nNo proprietary application icons, code or binaries are included.",TextWrapping=TextWrapping.Wrap},CloseButtonText="Close"}.ShowAsync();}));return bar;
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
        var documentRevision=Controller.Document.Revision;var graphRevision=Controller.Graph.Revision;var text=Controller.Save();var picker=new FileSavePicker{SuggestedFileName=Controller.Document.Name};picker.FileTypeChoices.Add("NurbsScene exact managed workspace",new List<string>{".nscene"});var file=await picker.PickSaveFileAsync();if(file is null)return;await FileIO.WriteTextAsync(file,text);_savedDocument=documentRevision;_savedGraph=graphRevision;Console.WriteLine($"Saved {file.Name}.");Refresh();
    }
    private async Task ExportAsync(bool stl)
    {
        var objects=Controller.Document.Selection.Count>0?Controller.Document.Objects.Where(o=>Controller.Document.Selection.Contains(o.Id)).ToArray():Controller.Document.Objects.ToArray();var picker=new FileSavePicker{SuggestedFileName=Controller.Document.Name};picker.FileTypeChoices.Add(stl?"Display mesh STL":"Display mesh OBJ",new List<string>{stl?".stl":".obj"});var file=await picker.PickSaveFileAsync();if(file is null)return;if(stl)await FileIO.WriteBytesAsync(file,WorkspaceFormat.ExportStl(objects));else await FileIO.WriteTextAsync(file,WorkspaceFormat.ExportObj(objects));Console.WriteLine("Exported display triangulation. This is not exact CAD interchange or a watertight-solid certificate.");
    }
    public void Dispose(){if(_disposed)return;_disposed=true;Controller.Changed-=Refresh;Controller.Message-=Console.WriteLine;Controller.FitRequested-=Viewport.Canvas.Fit;Controller.WorkspaceReplaced-=Replaced;Inspector.Detach();GraphEditor.Dispose();if(_ownsController)Controller.Dispose();}
}
