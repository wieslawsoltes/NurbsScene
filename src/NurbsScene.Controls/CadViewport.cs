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
    public CadViewport():base(null!)
    {
        IsTabStop=true;SizeChanged+=(_,_)=>Invalidate();
        PointerPressed+=Pressed;PointerMoved+=Moved;PointerReleased+=Released;PointerCanceled+=(_,e)=>{_gesture="";_active=null;ReleasePointerCapture(e.Pointer);};
        PointerExited+=(_,_)=>{if(_gesture==""){CursorHit=null;CursorMoved?.Invoke(null);}};
        PointerWheelChanged+=Wheel;DoubleTapped+=(_,e)=>{var view=At(e.GetPosition(this));if(view is not null)Maximize(view.Name);e.Handled=true;};
        RegisterPropertyChangedCallback(IsGLInitializedProperty,(_,_)=>{if(IsLoaded&&IsGLInitialized==false&&LastError is null)Report("Uno could not initialize a GPU context. No substitute renderer is active.");});
    }
    public ViewportRectangle[] Viewports=>ViewportLayout.Create(_pixelWidth,_pixelHeight,Cameras,Maximized);
    public void Activate(string name)
    {
        if(!Cameras.ContainsKey(name))throw new ArgumentException("Unknown viewport.");
        if(ActiveViewport==name&&(Maximized is null||Maximized==name))return;
        ActiveViewport=name;if(Maximized is not null)Maximized=name;LayoutChanged?.Invoke();ViewChanged?.Invoke();Invalidate();
    }
    public void Maximize(string name){if(!Cameras.ContainsKey(name))throw new ArgumentException("Unknown viewport.");ActiveViewport=name;Maximized=Maximized==name?null:name;LayoutChanged?.Invoke();ViewChanged?.Invoke();Invalidate();}
    public void RestoreFourViews(){Maximized=null;LayoutChanged?.Invoke();ViewChanged?.Invoke();Invalidate();}
    public void Fit(){foreach(var camera in Cameras.Values)camera.Fit(Scene.Bounds);if(!Scene.Bounds.IsEmpty)Scene.Rebase(Scene.Bounds.Center);ViewChanged?.Invoke();Invalidate();}
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
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        var p=e.GetCurrentPoint(this);_active=At(p.Position);if(_active is null)return;Activate(_active.Name);
        _start=_last=p.Position;_dragged=false;_gesture=p.Properties.IsRightButtonPressed||e.Pointer.PointerDeviceType.ToString()=="Touch"?"orbit":p.Properties.IsMiddleButtonPressed?"pan":"select";
        CapturePointer(e.Pointer);Focus(FocusState.Pointer);e.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        var p=e.GetCurrentPoint(this).Position;
        try{UpdateCursor(p);}catch(Exception error){DiagnosticsChanged?.Invoke(error.Message);}
        if(_active is null||_gesture=="")return;var dx=p.X-_last.X;var dy=p.Y-_last.Y;
        _dragged|=Math.Abs(p.X-_start.X)+Math.Abs(p.Y-_start.Y)>3;
        if(_gesture=="orbit"&&_active.Camera.Perspective)_active.Camera.Orbit(dx,dy);
        else if(_gesture is "pan" or "orbit")_active.Camera.Pan(dx,dy,_active.Height*Math.Max(1,ActualHeight)/_pixelHeight);
        _last=p;ViewChanged?.Invoke();Invalidate();e.Handled=true;
    }
    private void Released(object sender,PointerRoutedEventArgs e)
    {
        if(_active is not null&&!_dragged&&_gesture=="select")
        {
            var p=Pixels(e.GetCurrentPoint(this).Position);var x=p.X-_active.X;var y=p.Y-_active.Y;
            try
            {
                var hit=Aids.At(Scene,_active,x,y,ConstructionAnchor?.Invoke(),8*_pixelWidth/Math.Max(1,ActualWidth));
                ObjectPicked?.Invoke(MeshPicker.Pick(Scene,_active,x,y,out _,7*_pixelWidth/Math.Max(1,ActualWidth)),(e.KeyModifiers&VirtualKeyModifiers.Shift)!=0);
                if(hit is not null)ConstructionPointPicked?.Invoke(hit.Point);
            }
            catch(Exception error){DiagnosticsChanged?.Invoke(error.Message);}
        }
        _gesture="";_active=null;ReleasePointerCapture(e.Pointer);e.Handled=true;
    }
    private void UpdateCursor(Point position)
    {
        var view=At(position);if(view is null){CursorHit=null;CursorMoved?.Invoke(null);return;}
        var p=Pixels(position);CursorViewport=view.Name;CursorHit=Aids.At(Scene,view,p.X-view.X,p.Y-view.Y,ConstructionAnchor?.Invoke(),8*_pixelWidth/Math.Max(1,ActualWidth));CursorMoved?.Invoke(CursorHit);
    }
    private void Wheel(object sender,PointerRoutedEventArgs e){var p=e.GetCurrentPoint(this);var view=At(p.Position);if(view is null)return;view.Camera.Zoom(-p.Properties.MouseWheelDelta);ViewChanged?.Invoke();Invalidate();e.Handled=true;}
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
            Item("Maximize / restore",()=>Canvas.Maximize(name));Item("Zoom extents",()=>{Canvas.Cameras[name].Fit(Canvas.Scene.Bounds);Canvas.Invalidate();_overlay.Invalidate();});
            foreach(var mode in Enum.GetValues<DisplayMode>())Item(mode.ToString(),()=>{Canvas.Scene.Mode=mode;Canvas.Invalidate();});
            drop.Flyout=menu;var group=new StackPanel{Orientation=Orientation.Horizontal,Background=ClassicChrome.Background};group.Children.Add(title);group.Children.Add(drop);_titleGroups[name]=group;_headers.Children.Add(group);
        }
        Canvas.LayoutChanged+=Headers;Canvas.ViewChanged+=()=>_overlay.Invalidate();Canvas.CursorMoved+=_=>_overlay.Invalidate();SizeChanged+=(_,_)=>Headers();Headers();
    }
    private void Headers()
    {
        foreach(var group in _titleGroups.Values)group.Visibility=Visibility.Collapsed;
        var layout=ViewportLayout.Create(Math.Max(0,(int)ActualWidth),Math.Max(0,(int)ActualHeight),Canvas.Cameras,Canvas.Maximized);
        foreach(var v in layout)
        {
            if(v.Width<40||v.Height<25)continue;
            var group=_titleGroups[v.Name];group.Visibility=Visibility.Visible;_titles[v.Name].Background=Canvas.ActiveViewport==v.Name?ClassicChrome.Selected:ClassicChrome.Background;
            Microsoft.UI.Xaml.Controls.Canvas.SetLeft(group,v.X+3);Microsoft.UI.Xaml.Controls.Canvas.SetTop(group,v.Y+3);
        }
        _overlay.Invalidate();
    }
}
