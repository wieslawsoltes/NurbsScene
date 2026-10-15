using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Geometry;
using NurbsScene.Rendering;
using NurbsScene.Presentation;
using Silk.NET.OpenGL;
using Uno.WinUI.Graphics3DGL;
using Windows.Foundation;
using Windows.System;

namespace NurbsScene.Controls;

/// <summary>Reusable depth-tested 3D Uno canvas. There is no CPU placeholder on GPU failure.</summary>
public sealed class CadViewport : GLCanvasElement
{
    private GlRenderer? _renderer;
    private bool _dragged;
    private Point _start,_last;
    private string _gesture="";
    private uint? _pointerId;
    private int _pointerButton;
    private ViewportGesture? _navigation;
    private double _originX,_originY;
    private long _pressedAt;
    private ViewportRectangle? _active;
    private int _pixelWidth=1,_pixelHeight=1;
    private TaskCompletionSource<byte[]>? _capture;
    public DisplayScene Scene {get;set;}=new();
    public IReadOnlyDictionary<string,Camera> Cameras {get;}=new[]{"Top","Perspective","Front","Right"}.ToDictionary(n=>n,n=>new Camera(n));
    public string? Maximized {get;private set;}
    public string ActiveViewport {get;private set;}="Perspective";
    public ConstructionAids Aids {get;set;}=new();
    public Func<Vec3?>? ConstructionAnchor {get;set;}
    public event Action<ConstructionHit?>? CursorMoved;
    public event Action? ViewChanged;
    public ConstructionHit? CursorHit {get;private set;}
    public string CursorViewport {get;private set;}="Perspective";
    public string? LastError {get;private set;}
    public long Frames=>_renderer?.Frames??0;
    public string Backend=>_renderer?.Backend??"GPU not initialized";
    public event Action<string>? DiagnosticsChanged;
    public event Action<PickResult?,bool>? ObjectPicked;
    public event Action<Vec3>? ConstructionPointPicked;
    public event Action? LayoutChanged;
    public event Action? RepeatRequested;
    public event Action<Point>? ContextMenuRequested;
    public event Action<PickResult?,VirtualKeyModifiers>? ObjectSelectionRequested;
    public CadViewport():base(null!)
    {
        IsTabStop=true;SizeChanged+=(_,_)=>{CancelNavigation();Invalidate();};Unloaded+=(_,_)=>CancelNavigation();
        PointerPressed+=Pressed;PointerMoved+=Moved;PointerReleased+=Released;
        PointerCanceled+=(_,e)=>{if(_pointerId==e.Pointer.PointerId)CancelNavigation();};
        PointerCaptureLost+=(_,e)=>{if(_pointerId==e.Pointer.PointerId)CancelNavigation();};
        PointerExited+=(_,_)=>{if(_gesture==""){CursorHit=null;CursorMoved?.Invoke(null);}};
        PointerWheelChanged+=Wheel;
        RegisterPropertyChangedCallback(IsGLInitializedProperty,(_,_)=>{if(IsLoaded&&IsGLInitialized==false&&LastError is null)Report("Uno could not initialize a GPU context. No substitute renderer is active.");});
    }
    public ViewportRectangle[] Viewports=>ViewportLayout.Create(_pixelWidth,_pixelHeight,Cameras,Maximized);
    public void Activate(string name)
    {
        if(!Cameras.ContainsKey(name))throw new ArgumentException("Unknown viewport.");
        if(ActiveViewport==name&&(Maximized is null||Maximized==name))return;
        CancelNavigation();ActiveViewport=name;if(Maximized is not null)Maximized=name;LayoutChanged?.Invoke();ViewChanged?.Invoke();Invalidate();
    }
    public void Maximize(string name){if(!Cameras.ContainsKey(name))throw new ArgumentException("Unknown viewport.");CancelNavigation();ActiveViewport=name;Maximized=Maximized==name?null:name;LayoutChanged?.Invoke();ViewChanged?.Invoke();Invalidate();}
    public void RestoreFourViews(){CancelNavigation();Maximized=null;LayoutChanged?.Invoke();ViewChanged?.Invoke();Invalidate();}
    public void Fit()
    {
        foreach(var view in ViewportLayout.Create(Math.Max(1,(int)ActualWidth),Math.Max(1,(int)ActualHeight),Cameras,Maximized))
            if(view.Width>0&&view.Height>0)ViewportNavigation.Fit(view.Camera,Scene.Bounds,view.Width,view.Height);
        if(!Scene.Bounds.IsEmpty)Scene.Rebase(Scene.Bounds.Center);ViewChanged?.Invoke();Invalidate();
    }
    private void Report(string text){LastError=text;System.Console.Error.WriteLine("NURBSSCENE_GL: "+text);DiagnosticsChanged?.Invoke(text);}
    protected override void Init(GL gl)
    {
        try{UnoGlReadback.Configure(this);_renderer=new(gl);LastError=null;DiagnosticsChanged?.Invoke(_renderer.Backend);}
        catch(Exception error){Report(error.Message);throw;}
    }
    protected override unsafe void RenderOverride(GL gl)
    {
        if(_renderer is null)return;
        try
        {
            var viewport=stackalloc int[4];gl.GetInteger(GetPName.Viewport,viewport);_pixelWidth=viewport[2];_pixelHeight=viewport[3];
            _renderer.Render(Scene,Viewports,_pixelWidth,_pixelHeight);
            if(_capture is{} capture){_capture=null;capture.TrySetResult(_renderer.CaptureRgba());}
            if(_renderer.Frames==1)DiagnosticsChanged?.Invoke($"{Backend}; GPU frame submitted");
        }
        catch(Exception error){_capture?.TrySetException(error);_capture=null;Report(error.Message);}
    }
    protected override void OnDestroy(GL gl){_renderer?.Dispose();_renderer=null;_capture?.TrySetException(new InvalidOperationException("GPU context was destroyed."));_capture=null;}
    public Task<byte[]> CaptureRgbaAsync()
    {
        if(_renderer is null)throw new InvalidOperationException(LastError??"GPU is not initialized.");
        if(_capture is not null)throw new InvalidOperationException("A capture is already pending.");
        _capture=new(TaskCreationOptions.RunContinuationsAsynchronously);Invalidate();return _capture.Task;
    }
    private Point Pixels(Point p)=>new(p.X*_pixelWidth/Math.Max(1,ActualWidth),p.Y*_pixelHeight/Math.Max(1,ActualHeight));
    private ViewportRectangle? At(Point p){p=Pixels(p);return Viewports.FirstOrDefault(v=>p.X>=v.X&&p.Y>=v.Y&&p.X<v.X+v.Width&&p.Y<v.Y+v.Height);}
    public bool CancelNavigation()
    {
        if(_pointerId is null)return false;
        var navigation=_navigation;_pointerId=null;_navigation=null;_gesture="";_active=null;
        navigation?.Restore();ReleasePointerCaptures();NotifyViewChanged();return true;
    }
    public void NotifyViewChanged(){ViewChanged?.Invoke();Invalidate();}
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(_pointerId is not null){e.Handled=true;return;}
        var p=e.GetCurrentPoint(this);var active=At(p.Position);if(active is null)return;
        var touch=e.Pointer.PointerDeviceType.ToString()=="Touch";
        if(!touch&&!p.Properties.IsLeftButtonPressed&&!p.Properties.IsRightButtonPressed&&!p.Properties.IsMiddleButtonPressed)return;
        Activate(active.Name);Focus(FocusState.Pointer);if(!CapturePointer(e.Pointer))return;
        _active=active;_pointerId=e.Pointer.PointerId;_pointerButton=p.Properties.IsRightButtonPressed?3:p.Properties.IsMiddleButtonPressed?2:1;
        _start=_last=p.Position;_dragged=false;_pressedAt=Environment.TickCount64;
        _gesture=_pointerButton!=1?"navigate":"select";
        if(_gesture=="navigate"||touch)
        {
            var sx=ActualWidth/_pixelWidth;var sy=ActualHeight/_pixelHeight;
            _originX=active.X*sx;_originY=active.Y*sy;
            var mode=ViewportGesture.Resolve(active.Camera.Perspective,(e.KeyModifiers&VirtualKeyModifiers.Shift)!=0,
                (e.KeyModifiers&VirtualKeyModifiers.Control)!=0,_pointerButton==2);
            _navigation=new(active.Camera,mode,p.Position.X-_originX,p.Position.Y-_originY,active.Width*sx,active.Height*sy);
        }
        e.Handled=true;
    }
    private bool StillPressed(bool left,bool middle,bool right)=>_pointerButton switch{3=>right,2=>middle,_=>left};
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        var point=e.GetCurrentPoint(this);var p=point.Position;
        if(_pointerId is not null&&_pointerId!=e.Pointer.PointerId)return;
        try{UpdateCursor(p);}catch(Exception error){DiagnosticsChanged?.Invoke(error.Message);}
        if(_pointerId is null||_active is null)return;
        if(e.Pointer.PointerDeviceType.ToString()=="Mouse"&&!StillPressed(point.Properties.IsLeftButtonPressed,point.Properties.IsMiddleButtonPressed,point.Properties.IsRightButtonPressed)){CancelNavigation();return;}
        _dragged|=Math.Abs(p.X-_start.X)+Math.Abs(p.Y-_start.Y)>3;
        if(_dragged&&_navigation is not null)
            try{_navigation.Move(p.X-_originX,p.Y-_originY);NotifyViewChanged();}
            catch(Exception error){CancelNavigation();DiagnosticsChanged?.Invoke(error.Message);}
        _last=p;e.Handled=true;
    }
    private void Released(object sender,PointerRoutedEventArgs e)
    {
        var release=e.GetCurrentPoint(this);
        if(_pointerId!=e.Pointer.PointerId||StillPressed(release.Properties.IsLeftButtonPressed,release.Properties.IsMiddleButtonPressed,release.Properties.IsRightButtonPressed))return;
        var active=_active;var gesture=_gesture;var button=_pointerButton;
        var p=e.GetCurrentPoint(this).Position;
        _pointerId=null;_navigation=null;_gesture="";_active=null;ReleasePointerCapture(e.Pointer);
        if(active is not null&&!_dragged&&gesture=="select")
        {
            var pixel=Pixels(p);var x=pixel.X-active.X;var y=pixel.Y-active.Y;
            try
            {
                var hit=Aids.At(Scene,active,x,y,ConstructionAnchor?.Invoke(),8*_pixelWidth/Math.Max(1,ActualWidth));
                var picked=MeshPicker.Pick(Scene,active,x,y,out _,7*_pixelWidth/Math.Max(1,ActualWidth));
                ObjectPicked?.Invoke(picked,(e.KeyModifiers&VirtualKeyModifiers.Shift)!=0);
                ObjectSelectionRequested?.Invoke(picked,e.KeyModifiers);
                if(hit is not null)ConstructionPointPicked?.Invoke(hit.Point);
            }
            catch(Exception error){DiagnosticsChanged?.Invoke(error.Message);}
        }
        else if(!_dragged&&button==3)
        {
            if(Environment.TickCount64-_pressedAt>=500)ContextMenuRequested?.Invoke(p);
            else RepeatRequested?.Invoke();
        }
        e.Handled=true;
    }
    private void UpdateCursor(Point position)
    {
        var view=At(position);if(view is null){CursorHit=null;CursorMoved?.Invoke(null);return;}
        var p=Pixels(position);CursorViewport=view.Name;CursorHit=Aids.At(Scene,view,p.X-view.X,p.Y-view.Y,ConstructionAnchor?.Invoke(),8*_pixelWidth/Math.Max(1,ActualWidth));CursorMoved?.Invoke(CursorHit);
    }
    private void Wheel(object sender,PointerRoutedEventArgs e)
    {
        if(_pointerId is not null){e.Handled=true;return;}
        var p=e.GetCurrentPoint(this);var view=At(p.Position);if(view is null)return;
        var pixel=Pixels(p.Position);Activate(view.Name);
        ViewportGesture.ZoomAt(view.Camera,-p.Properties.MouseWheelDelta,pixel.X-view.X,pixel.Y-view.Y,view.Width,view.Height);
        NotifyViewChanged();e.Handled=true;
    }
}

/// <summary>Viewport titles activate on click and maximize on double-click.
/// Reused title controls preserve double-click state and focus during activation.</summary>
public sealed class ViewportControl : Grid
{
    public CadViewport Canvas {get;}=new();
    private readonly Microsoft.UI.Xaml.Controls.Canvas _headers=new();
    private readonly Dictionary<string,Button> _titles=[];
    private readonly Dictionary<string,StackPanel> _titleGroups=[];
    private readonly ViewportOverlay _overlay;
    private readonly TextBlock _error=new(){TextWrapping=TextWrapping.Wrap,MaxWidth=540,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new(24),Visibility=Visibility.Collapsed};
    public IReadOnlyDictionary<string,Button> Titles=>_titles;
    public ViewportControl()
    {
        _overlay=new(Canvas){IsHitTestVisible=false};Children.Add(Canvas);Children.Add(_overlay);Children.Add(_headers);Children.Add(_error);
        Canvas.DiagnosticsChanged+=message=>{_error.Text=message;_error.Visibility=Canvas.LastError is null?Visibility.Collapsed:Visibility.Visible;};
        foreach(var name in new[]{"Top","Perspective","Front","Right"})
        {
            var title=ClassicChrome.Button(name,()=>Canvas.Activate(name),"Viewport_"+name);title.Height=22;title.BorderThickness=new(0);title.Padding=new(5,0,5,0);
            title.DoubleTapped+=(_,e)=>{Canvas.Maximize(name);e.Handled=true;};_titles[name]=title;
            var drop=ClassicChrome.IconButton("Viewport options","Dropdown",()=>{},"ViewportOptions_"+name);drop.Width=17;drop.Height=22;
            var menu=new MenuFlyout();
            void Item(string text,Action action){var item=new MenuFlyoutItem{Text=text};item.Click+=(_,_)=>action();menu.Items.Add(item);}
            Item("Maximize / restore",()=>Canvas.Maximize(name));Item("Zoom extents",()=>{
                var view=ViewportLayout.Create(Math.Max(1,(int)ActualWidth),Math.Max(1,(int)ActualHeight),Canvas.Cameras,Canvas.Maximized).Single(v=>v.Name==name);
                if(view.Width>0&&view.Height>0)ViewportNavigation.Fit(view.Camera,Canvas.Scene.Bounds,view.Width,view.Height);
                Canvas.NotifyViewChanged();
            });
            foreach(var mode in Enum.GetValues<DisplayMode>())Item(mode.ToString(),()=>{Canvas.Scene.Mode=mode;Canvas.NotifyViewChanged();});
            drop.Flyout=menu;title.ContextFlyout=menu;var group=new StackPanel{Orientation=Orientation.Horizontal,Background=ClassicChrome.Background};group.Children.Add(title);group.Children.Add(drop);_titleGroups[name]=group;_headers.Children.Add(group);
        }
        Canvas.LayoutChanged+=Headers;Canvas.ViewChanged+=()=>_overlay.Invalidate();Canvas.CursorMoved+=_=>_overlay.Invalidate();SizeChanged+=(_,_)=>Headers();Headers();
    }
    private void Headers()
    {
        var layout=ViewportLayout.Create(Math.Max(0,(int)ActualWidth),Math.Max(0,(int)ActualHeight),Canvas.Cameras,Canvas.Maximized);
        var visible=layout.Where(v=>v.Width>=40&&v.Height>=25).ToDictionary(v=>v.Name);
        foreach(var (name,group) in _titleGroups)
        {
            group.Visibility=visible.ContainsKey(name)?Visibility.Visible:Visibility.Collapsed;
            if(!visible.TryGetValue(name,out var v))continue;
            _titles[name].Background=Canvas.ActiveViewport==name?ClassicChrome.Selected:ClassicChrome.Background;
            Microsoft.UI.Xaml.Controls.Canvas.SetLeft(group,v.X+3);Microsoft.UI.Xaml.Controls.Canvas.SetTop(group,v.Y+3);
        }
        _overlay.Invalidate();
    }
}
