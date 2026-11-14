using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace NurbsScene.Controls;

public sealed partial class GraphEditorControl
{
    private readonly Grid _titleDragHandle=new(){Background=ClassicChrome.Brush(224,227,219)};
    private readonly Grid _editorBody=new(),_menuHost=new();
    private readonly TextBlock _heading=ClassicChrome.Label("Grasshopper · Untitled",11);
    private readonly TextBlock _documentLabel=ClassicChrome.Label("Untitled",10);
    private readonly TextBlock _solutionState=ClassicChrome.Label("Not solved",10);
    private readonly ScrollViewer _toolbarStrip;
    private readonly Border _statusFrame;
    private readonly List<(ToggleMenuFlyoutItem Item,Func<bool> Value)> _appearanceChecks=[];
    private readonly List<FrameworkElement> _fileMenuElements=[];
    private Button? _documentMenuButton,_recomputeButton,_foldButton;
    private MenuFlyout? _documentMenu;
    private ContentDialog? _helpDialog;
    private CancellationTokenSource? _recomputeCancellation;
    private bool _componentPaletteVisible=true,_canvasToolbarVisible=true,_isFolded,_solving,_chromeDisposed;
    private string _documentName="Untitled";

    public event Action<string>? WorkspaceActionRequested;
    public FrameworkElement TitleDragHandle=>_titleDragHandle;
    public FrameworkElement? EditorHelp=>_helpDialog;
    public bool EditorHelpIsOpen=>_helpDialog?.IsLoaded==true;
    public bool IsFolded=>_isFolded;
    public string DocumentName
    {
        get=>_documentName;
        set
        {
            _documentName=string.IsNullOrWhiteSpace(value)?"Untitled":value;
            _heading.Text="Grasshopper · "+_documentName;_documentLabel.Text=_documentName;
            if(_documentMenuButton is not null)ToolTipService.SetToolTip(_documentMenuButton,_documentName+" · Workspace files");
        }
    }
    public bool ComponentPaletteVisible
    {
        get=>_componentPaletteVisible;
        set{if(_componentPaletteVisible==value)return;_componentPaletteVisible=value;ApplyEditorVisibility();}
    }
    public bool CanvasToolbarVisible
    {
        get=>_canvasToolbarVisible;
        set{if(_canvasToolbarVisible==value)return;_canvasToolbarVisible=value;ApplyEditorVisibility();}
    }
    public void SetFolded(bool folded)
    {
        ObjectDisposedException.ThrowIf(_chromeDisposed,this);
        if(_isFolded==folded)return;
        Canvas.CancelInteraction();CloseActionPopups();_isFolded=folded;ApplyEditorVisibility();
        if(_foldButton is not null)
        {
            var label=folded?"Unfold definition":"Fold definition";
            AutomationProperties.SetName(_foldButton,label);ToolTipService.SetToolTip(_foldButton,label);
        }
    }
    private void ApplyEditorVisibility()
    {
        _menuHost.Visibility=_isFolded?Visibility.Collapsed:Visibility.Visible;
        Shelf.Visibility=!_isFolded&&_componentPaletteVisible?Visibility.Visible:Visibility.Collapsed;
        _toolbarStrip.Visibility=!_isFolded&&_canvasToolbarVisible?Visibility.Visible:Visibility.Collapsed;
        _editorBody.Visibility=_statusFrame.Visibility=_isFolded?Visibility.Collapsed:Visibility.Visible;
        RowDefinitions[1].Height=new GridLength(_isFolded?0:26);
        RowDefinitions[2].Height=!_isFolded&&_componentPaletteVisible?GridLength.Auto:new GridLength(0);
        RowDefinitions[3].Height=new GridLength(!_isFolded&&_canvasToolbarVisible?31:0);
        RowDefinitions[4].Height=_isFolded?new GridLength(0):new GridLength(1,GridUnitType.Star);
        RowDefinitions[5].Height=new GridLength(_isFolded?0:23);
        if(!ComponentPaletteVisible)Shelf.CloseGroupMenu();
        UpdateAppearanceChecks();
    }
    private Grid CreateDefinitionHeader()
    {
        var title=new Grid{Background=ClassicChrome.Brush(224,227,219)};
        title.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});title.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        _titleDragHandle.ColumnDefinitions.Add(new(){Width=new GridLength(27)});_titleDragHandle.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        _titleDragHandle.Children.Add(new WorkbenchIcon("Graph"){Width=19,Height=19,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
        _heading.TextTrimming=TextTrimming.CharacterEllipsis;_heading.Margin=new(1,0,5,0);_titleDragHandle.Children.Add(_heading);SetColumn(_heading,1);
        AutomationProperties.SetAutomationId(_titleDragHandle,"GraphTitleDragHandle");
        ToolTipService.SetToolTip(_titleDragHandle,"In a floating window, drag to move or double-click to fold.");title.Children.Add(_titleDragHandle);
        var docking=new StackPanel{Orientation=Orientation.Horizontal};
        Button Dock(string name,string icon,string action,string id)
        {
            var button=ClassicChrome.IconButton(name,icon,()=>DockRequested?.Invoke(action),id);button.Width=25;button.Height=25;
            button.Background=ClassicChrome.Brush(224,227,219);docking.Children.Add(button);return button;
        }
        Dock("Dock definition right","Dock","Right","GraphDockRight");
        Dock("Dock definition below viewports","FourViews","Bottom","GraphDockBottom");
        Dock("Float definition","Float","Float","GraphFloat");
        _foldButton=Dock("Fold definition","Fold","Fold","GraphFold");
        Dock("Hide definition","Close","Hide","GraphHide");
        title.Children.Add(docking);SetColumn(docking,1);return title;
    }
    private StackPanel CreateCanvasToolbar()
    {
        var tools=new StackPanel{Orientation=Orientation.Horizontal,Spacing=1,Padding=new(3,1,3,1),Background=ClassicChrome.Brush(244,245,241)};
        tools.Children.Add(ClassicChrome.IconButton("Open workspace","Open",()=>WorkspaceActionRequested?.Invoke("Open"),"GraphOpen"));
        tools.Children.Add(ClassicChrome.IconButton("Save workspace","Save",()=>WorkspaceActionRequested?.Invoke("Save"),"GraphSave"));
        tools.Children.Add(ClassicChrome.Separator());tools.Children.Add(UndoButton);tools.Children.Add(RedoButton);
        tools.Children.Add(ClassicChrome.Separator());
        tools.Children.Add(ClassicChrome.IconButton("Zoom extents","Fit",()=>Canvas.Fit(),"GraphFit"));
        tools.Children.Add(ClassicChrome.IconButton("Zoom out","ZoomOut",()=>Canvas.SetZoom(Canvas.Zoom/1.2f),"GraphZoomOut"));
        var zoom=ClassicChrome.Button("Zoom presets",()=>{},"GraphZoomPresets");zoom.Content=_zoom;zoom.Width=49;zoom.Padding=new(3,1,3,1);zoom.BorderThickness=new(0);
        ToolTipService.SetToolTip(zoom,"Canvas zoom · click for presets");zoom.Click+=(_,_)=>ShowZoomMenu(zoom);tools.Children.Add(zoom);
        tools.Children.Add(ClassicChrome.IconButton("Zoom in","ZoomIn",()=>Canvas.SetZoom(Canvas.Zoom*1.2f),"GraphZoomIn"));
        tools.Children.Add(ClassicChrome.Separator());
        tools.Children.Add(ClassicChrome.IconButton("Bake selected geometry","Bake",()=>BakeRequested?.Invoke(Canvas.Selection),"GraphBake"));
        tools.Children.Add(ClassicChrome.IconButton("Delete selected components","Delete",()=>Canvas.DeleteSelected(),"GraphDelete"));
        tools.Children.Add(ClassicChrome.IconButton("Component properties","Properties",()=>{_inspectorVisible=!_inspectorVisible;SizeInspector();},"GraphProperties"));
        tools.Children.Add(_search);tools.Children.Add(ClassicChrome.Separator());
        _recomputeButton=ClassicChrome.IconButton("Recompute the current definition","Recompute",()=>_ =RecomputeAsync(),"GraphRecompute");tools.Children.Add(_recomputeButton);
        tools.Children.Add(ClassicChrome.IconButton("Enable selected previews","PreviewOn",()=>_ =RunActionAsync("PreviewOn"),"GraphPreviewOn"));
        tools.Children.Add(ClassicChrome.IconButton("Disable selected previews","PreviewOff",()=>_ =RunActionAsync("PreviewOff"),"GraphPreviewOff"));
        tools.Children.Add(ClassicChrome.IconButton("Grasshopper visual programming help","Help",()=>_ =ShowEditorHelpAsync(),"GraphHelp"));return tools;
    }
    private Border CreateDefinitionStatus()
    {
        var bar=new Grid{Background=ClassicChrome.Brush(232,234,227)};bar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});bar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        _status.Margin=new(7,0,7,0);_status.TextTrimming=TextTrimming.CharacterEllipsis;bar.Children.Add(_status);
        // Reserve the floating host's lower-right resize grip without covering
        // the solution text; the same status geometry is retained when docked.
        _solutionState.Margin=new(8,0,24,0);bar.Children.Add(_solutionState);SetColumn(_solutionState,1);
        AutomationProperties.SetAutomationId(_status,"GraphStatus");AutomationProperties.SetAutomationId(_solutionState,"GraphSolutionStatus");
        return new Border{BorderBrush=ClassicChrome.Brush(187,190,181),BorderThickness=new(0,1,0,0),Child=bar};
    }
    private Button CreateDocumentMenuButton()
    {
        var button=ClassicChrome.Button("Workspace files",()=>{},"GraphDocumentMenu");_documentMenuButton=button;
        var caption=new StackPanel{Orientation=Orientation.Horizontal,Spacing=5};_documentLabel.MaxWidth=155;_documentLabel.TextTrimming=TextTrimming.CharacterEllipsis;
        caption.Children.Add(_documentLabel);caption.Children.Add(ClassicChrome.Label("▾",10));button.Content=caption;
        button.BorderThickness=new(1,0,0,0);button.BorderBrush=ClassicChrome.Border;button.Height=25;button.Padding=new(8,0,6,0);
        button.Click+=(_,_)=>
        {
            _documentMenu?.Hide();_fileMenuElements.Clear();var menu=new MenuFlyout();_documentMenu=menu;
            foreach(var(id,text)in new[]{("OpenWorkspace","Open workspace…"),("SaveWorkspace","Save workspace"),("SaveWorkspaceAs","Save workspace as…")})
                menu.Items.Add(ActionItem(id,text,_fileMenuElements));
            menu.ShowAt(button);
        };
        ToolTipService.SetToolTip(button,_documentName+" · Workspace files");return button;
    }
    private void ShowZoomMenu(FrameworkElement anchor)
    {
        _documentMenu?.Hide();_fileMenuElements.Clear();var menu=new MenuFlyout();_documentMenu=menu;
        foreach(var percent in new[]{25,50,75,100,150,200})
        {
            var value=percent;var item=new MenuFlyoutItem{Text=$"{value}%",FontSize=11,MinHeight=25,Padding=new(8,2,8,2)};
            AutomationProperties.SetAutomationId(item,"GraphZoom_"+value);_fileMenuElements.Add(item);
            item.Click+=(_,_)=>Canvas.SetZoom(value/100f);menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());menu.Items.Add(ActionItem("Fit","Zoom extents",_fileMenuElements));menu.ShowAt(anchor);
    }
    private ToggleMenuFlyoutItem AppearanceItem(string id,string label,Func<bool> value,Action<bool> update)
    {
        var item=new ToggleMenuFlyoutItem{Text=label,IsChecked=value(),FontSize=11,MinHeight=25,Padding=new(8,2,8,2)};
        AutomationProperties.SetAutomationId(item,id);_menuElements.Add(item);_appearanceChecks.Add((item,value));
        item.Click+=(_,_)=>{update(item.IsChecked);UpdateAppearanceChecks();};return item;
    }
    private void UpdateAppearanceChecks(){foreach(var(item,value)in _appearanceChecks)item.IsChecked=value();}
    private async Task RecomputeAsync()
    {
        if(_chromeDisposed||_solving||Session is not{} session||Canvas.IsInteracting)return;
        _solving=true;var cancellation=new CancellationTokenSource();_recomputeCancellation=cancellation;UpdateStatus();
        try{await session.Graph.SolveAsync(cancellation.Token);}
        catch(OperationCanceledException){} // Editing/replacing a graph may supersede this solve.
        catch(Exception error){if(!_chromeDisposed&&ReferenceEquals(Session,session))Report(error.Message);}
        finally
        {
            cancellation.Dispose();if(ReferenceEquals(_recomputeCancellation,cancellation))_recomputeCancellation=null;
            _solving=false;if(!_chromeDisposed)UpdateStatus();
        }
    }
    private async Task ShowEditorHelpAsync()
    {
        if(_chromeDisposed||XamlRoot is null||_helpDialog is not null||_textPanelDialog is not null||_numberSliderDialog is not null||_panelTypographyDialog is not null)return;
        CloseActionPopups();
        var text=new StackPanel{Spacing=10,MaxWidth=500};
        text.Children.Add(new TextBlock{Text="Build a Grasshopper-style definition by placing components and connecting their typed ports. Component palettes and search expose the same executable catalog. Slider edits solve the graph and update the model viewport preview; Bake creates model objects from selected geometry outputs.",FontSize=12,TextWrapping=TextWrapping.Wrap});
        text.Children.Add(ClassicChrome.SectionHeader("Canvas navigation"));
        text.Children.Add(new TextBlock{Text="Right-drag / middle-drag     Pan\nMouse wheel                       Zoom around the pointer\nCtrl + right-drag                 Continuous zoom\nF                                           Zoom extents\nDouble-click empty canvas  Find or create a component",FontSize=12,TextWrapping=TextWrapping.Wrap});
        text.Children.Add(ClassicChrome.SectionHeader("Editing"));
        text.Children.Add(new TextBlock{Text="Drag ports to connect. Shift retains existing input wires.\nDrag component bodies to move; Shift constrains an axis.\nDouble-click a number label to edit its range and precision.\nDrag its rail to scrub. Double-click a panel to edit its text.\nCtrl + double-click a panel opens typography.\nEscape cancels a drag; Ctrl+Z / Ctrl+Y undo or redo graph edits.",FontSize=12,TextWrapping=TextWrapping.Wrap});
        text.Children.Add(ClassicChrome.SectionHeader("Preview and display"));
        text.Children.Add(new TextBlock{Text="Green capsules are selected; orange means an upstream component is blocked; red indicates an evaluation error. Right-click for component options and data matching. Display settings change the canvas without changing your definition. Bake creates exact model objects from selected outputs.",FontSize=12,TextWrapping=TextWrapping.Wrap});
        AutomationProperties.SetAutomationId(text,"GraphHelpContent");
        var dialog=new ContentDialog{Title="Grasshopper · Visual programming",Content=new ScrollViewer{Content=text,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=520},CloseButtonText="Close",XamlRoot=XamlRoot};_helpDialog=dialog;
        try{await dialog.ShowAsync();}catch(Exception error){if(!_chromeDisposed)Report(error.Message);}finally{_helpDialog=null;}
    }
    private void DisposeChrome(){_chromeDisposed=true;_recomputeCancellation?.Cancel();_helpDialog?.Hide();Shelf.CloseGroupMenu();_documentMenu?.Hide();}
}
