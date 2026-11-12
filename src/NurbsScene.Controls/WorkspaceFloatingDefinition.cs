using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Presentation;
using Windows.Foundation;
using Windows.System;

namespace NurbsScene.Controls;

public sealed partial class WorkspaceView
{
    private readonly Microsoft.UI.Xaml.Controls.Canvas _floatingHost=new();
    private readonly ContentControl _floatingFrame=new(){IsTabStop=true,Padding=new(0),BorderThickness=new(0),
        HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly Grid _floatingContent=new();
    private ContentControl? _floatingResize;
    private UIElement? _floatingPointerOwner;
    private uint? _floatingPointer;
    private Point _floatingStart;
    private FloatingPanePlacement _floatingBefore;
    private FloatingPaneBounds _floatingStartBounds;
    private bool _floatingResizing,_floatingMoved,_floatingFoldPending;
    private string? _effectiveDefinitionDock;
    public bool DefinitionFloating=>Preferences.Dock=="Float";
    public bool DefinitionFolded=>DefinitionFloating&&Preferences.FloatingFolded;
    public FloatingPaneBounds DefinitionFloatingBounds {get;private set;}

    private void InitializeFloatingDefinition()
    {
        AutomationProperties.SetAutomationId(_floatingFrame,"GraphFloatFrame");
        AutomationProperties.SetName(_floatingFrame,"Floating definition window");
        AutomationProperties.SetHelpText(_floatingFrame,"Move with the title bar, resize from the lower-right corner. Escape cancels a drag.");
        AutomationProperties.SetAutomationId(GraphEditor.TitleDragHandle,"GraphTitleDragHandle");
        _floatingFrame.Content=_floatingContent;_floatingHost.Children.Add(_floatingFrame);
        // A ButtonBase owns and releases pointer capture during focus/release.
        // Use a neutral focusable grip so this gesture is its only owner.
        _floatingResize=new ContentControl{Width=17,Height=17,Padding=new(0),IsTabStop=true,
            HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,
            HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,
            Content=new Border{Background=ClassicChrome.PanelBackground,
                Child=new WorkbenchIcon("Resize"){Width=14,Height=14,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}}};
        AutomationProperties.SetAutomationId(_floatingResize,"GraphFloatResize");
        AutomationProperties.SetName(_floatingResize,"Resize definition window");
        AutomationProperties.SetHelpText(_floatingResize,"Drag or use the arrow keys to resize. Escape cancels a drag.");
        ToolTipService.SetToolTip(_floatingResize,"Resize definition window");_floatingContent.Children.Add(_floatingResize);
        HookFloatingPointer(GraphEditor.TitleDragHandle,false);HookFloatingPointer(_floatingResize,true);
        GraphEditor.TitleDragHandle.DoubleTapped+=(_,e)=>{
            if(!DefinitionFloating)return;
            if(_floatingPointer is not null){if(!_floatingMoved)_floatingFoldPending=true;}
            else ToggleDefinitionFold();
            e.Handled=true;
        };
        _floatingFrame.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler((_,e)=>{
            if(e.Key==VirtualKey.Escape&&CancelFloatingGesture()){e.Handled=true;return;}
            if(_floatingPointer is not null||!DefinitionFloating||XamlRoot is null)return;
            var focused=FocusManager.GetFocusedElement(XamlRoot);var resize=ReferenceEquals(focused,_floatingResize);
            if(!resize&&!ReferenceEquals(focused,_floatingFrame))return;
            var step=(e.Key is VirtualKey.Left or VirtualKey.Up)?-8:(e.Key is VirtualKey.Right or VirtualKey.Down)?8:0;
            if(step==0)return;
            var p=Preferences.Placement;var bounds=ExpandedFloatingBounds(p);
            bounds=resize?(e.Key is VirtualKey.Left or VirtualKey.Right?bounds with{Width=Math.Max(1,bounds.Width+step)}:bounds with{Height=Math.Max(1,bounds.Height+step)})
                :(e.Key is VirtualKey.Left or VirtualKey.Right?bounds with{X=bounds.X+step}:bounds with{Y=bounds.Y+step});
            if(_floatingHost.ActualWidth<=0||_floatingHost.ActualHeight<=0)return;
            SetPreferences(Preferences.WithPlacement(FloatingPanePlacement.FromBounds(bounds,_floatingHost.ActualWidth,_floatingHost.ActualHeight,p.Folded)));
            e.Handled=true;
        }),true);
        _floatingHost.SizeChanged+=(_,_)=>ApplyFloatingBounds();
        _floatingFrame.Unloaded+=(_,_)=>CancelFloatingGesture();
    }
    private void HookFloatingPointer(UIElement handle,bool resize)
    {
        handle.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=>{
            if(!DefinitionFloating||_floatingPointer is not null)return;
            var point=e.GetCurrentPoint(_floatingHost);
            if(!point.Properties.IsLeftButtonPressed&&e.Pointer.PointerDeviceType.ToString()!="Touch")return;
            if(!handle.CapturePointer(e.Pointer))return;
            _floatingPointer=e.Pointer.PointerId;_floatingPointerOwner=handle;_floatingStart=point.Position;
            _floatingBefore=Preferences.Placement;_floatingStartBounds=ExpandedFloatingBounds(_floatingBefore);
            _floatingResizing=resize;_floatingMoved=false;_floatingFoldPending=false;
            (resize?_floatingResize:_floatingFrame)?.Focus(FocusState.Pointer);e.Handled=true;
        }),true);
        handle.AddHandler(UIElement.PointerMovedEvent,new PointerEventHandler((_,e)=>{
            if(_floatingPointer!=e.Pointer.PointerId)return;
            var point=e.GetCurrentPoint(_floatingHost);
            if(e.Pointer.PointerDeviceType.ToString()=="Mouse"&&!point.Properties.IsLeftButtonPressed){CancelFloatingGesture();return;}
            UpdateFloatingGesture(point.Position);e.Handled=true;
        }),true);
        handle.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,e)=>{
            if(_floatingPointer!=e.Pointer.PointerId||e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)return;
            UpdateFloatingGesture(e.GetCurrentPoint(_floatingHost).Position);
            var fold=_floatingFoldPending&&!_floatingMoved;var moved=_floatingMoved;
            _floatingPointer=null;_floatingPointerOwner=null;_floatingFoldPending=false;
            handle.ReleasePointerCapture(e.Pointer);
            if(moved)SetPreferences(Preferences);
            if(fold)ToggleDefinitionFold();
            e.Handled=true;
        }),true);
        handle.PointerCanceled+=(_,e)=>{if(_floatingPointer==e.Pointer.PointerId)CancelFloatingGesture();};
        handle.PointerCaptureLost+=(_,e)=>{if(_floatingPointer==e.Pointer.PointerId)CancelFloatingGesture();};
    }
    private void UpdateFloatingGesture(Point point)
    {
        var dx=point.X-_floatingStart.X;var dy=point.Y-_floatingStart.Y;
        _floatingMoved|=Math.Abs(dx)+Math.Abs(dy)>2;
        if(!_floatingMoved)return;
        _floatingFoldPending=false;
        var bounds=_floatingResizing?_floatingStartBounds with{Width=Math.Max(1,_floatingStartBounds.Width+dx),Height=Math.Max(1,_floatingStartBounds.Height+dy)}
            :_floatingStartBounds with{X=_floatingStartBounds.X+dx,Y=_floatingStartBounds.Y+dy};
        if(_floatingHost.ActualWidth<=0||_floatingHost.ActualHeight<=0)return;
        var placement=FloatingPanePlacement.FromBounds(bounds,_floatingHost.ActualWidth,_floatingHost.ActualHeight,_floatingBefore.Folded);
        SetPreferences(Preferences.WithPlacement(placement),false);
    }
    private bool CancelFloatingGesture()
    {
        if(_floatingPointer is null)return false;
        var owner=_floatingPointerOwner;_floatingPointer=null;_floatingPointerOwner=null;_floatingFoldPending=false;
        if(!_disposed)SetPreferences(Preferences.WithPlacement(_floatingBefore),false);
        owner?.ReleasePointerCaptures();return true;
    }
    private void HandleDefinitionDock(string dock)
    {
        CancelFloatingGesture();
        if(dock=="Hide"){SetPreferences(Preferences with{DefinitionVisible=false});return;}
        if(dock=="Fold"){ToggleDefinitionFold();return;}
        if(dock is not("Right" or "Bottom" or "Float"))return;
        SetPreferences(Preferences.WithPlacement(Preferences.Placement.WithFolded(false)) with{Dock=dock,DefinitionVisible=true});
    }
    private void ToggleDefinitionFold()
    {
        CancelFloatingGesture();
        var folded=!DefinitionFloating||!Preferences.FloatingFolded;
        SetPreferences(Preferences.WithPlacement(Preferences.Placement.WithFolded(folded)) with{Dock="Float",DefinitionVisible=true});
    }
    private void AttachDefinitionContainer(bool floating)
    {
        var target=floating?_floatingContent:_workspace;
        if(ReferenceEquals(GraphEditor.Parent,target))return;
        if(GraphEditor.Parent is Panel old)old.Children.Remove(GraphEditor);
        Grid.SetRow(GraphEditor,0);Grid.SetColumn(GraphEditor,0);
        target.Children.Insert(0,GraphEditor);_fitAfterLayout=true;
    }
    private void ApplyFloatingBounds()
    {
        if(_disposed||!DefinitionFloating)return;
        var bounds=Preferences.Placement.Resolve(_floatingHost.ActualWidth,_floatingHost.ActualHeight);
        DefinitionFloatingBounds=bounds;
        _floatingFrame.Width=bounds.Width;_floatingFrame.Height=bounds.Height;
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(_floatingFrame,bounds.X);Microsoft.UI.Xaml.Controls.Canvas.SetTop(_floatingFrame,bounds.Y);
        GraphEditor.SetFolded(Preferences.FloatingFolded);
        if(_floatingResize is not null)_floatingResize.Visibility=Preferences.FloatingFolded?Visibility.Collapsed:Visibility.Visible;
    }
    private FloatingPaneBounds ExpandedFloatingBounds(FloatingPanePlacement placement)
    {
        var expanded=placement.WithFolded(false).Resolve(_floatingHost.ActualWidth,_floatingHost.ActualHeight);
        var visible=placement.Resolve(_floatingHost.ActualWidth,_floatingHost.ActualHeight);
        return expanded with{X=visible.X,Y=visible.Y};
    }
}
