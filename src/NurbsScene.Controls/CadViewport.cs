using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Geometry;
using NurbsScene.Rendering;
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
        PointerPressed+=Pressed;PointerMoved+=Moved;PointerReleased+=Released;PointerCanceled+=(_,e)=>{_gesture="";ReleasePointerCapture(e.Pointer);};
        PointerWheelChanged+=Wheel;DoubleTapped+=(_,e)=>{var view=At(e.GetPosition(this));if(view is not null)Maximize(view.Name);e.Handled=true;};
        RegisterPropertyChangedCallback(IsGLInitializedProperty,(_,_)=>{if(IsLoaded&&IsGLInitialized==false&&LastError is null)Report("Uno could not initialize a GPU context. No substitute renderer is active.");});
    }
    public ViewportRectangle[] Viewports=>ViewportLayout.Create(_pixelWidth,_pixelHeight,Cameras,Maximized);
    public void Maximize(string name){Maximized=Maximized==name?null:name;LayoutChanged?.Invoke();Invalidate();}
    public void Fit(){foreach(var camera in Cameras.Values)camera.Fit(Scene.Bounds);if(!Scene.Bounds.IsEmpty)Scene.Rebase(Scene.Bounds.Center);Invalidate();}
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
        var p=e.GetCurrentPoint(this);_active=At(p.Position);if(_active is null)return;
        _start=_last=p.Position;_dragged=false;_gesture=p.Properties.IsRightButtonPressed||e.Pointer.PointerDeviceType.ToString()=="Touch"?"orbit":p.Properties.IsMiddleButtonPressed?"pan":"select";
        CapturePointer(e.Pointer);Focus(FocusState.Pointer);e.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        if(_active is null||_gesture=="")return;var p=e.GetCurrentPoint(this).Position;var dx=p.X-_last.X;var dy=p.Y-_last.Y;
        _dragged|=Math.Abs(p.X-_start.X)+Math.Abs(p.Y-_start.Y)>3;
        if(_gesture=="orbit"&&_active.Camera.Perspective)_active.Camera.Orbit(dx,dy);
        else if(_gesture is "pan" or "orbit")_active.Camera.Pan(dx,dy,_active.Height*Math.Max(1,ActualHeight)/_pixelHeight);
        _last=p;Invalidate();e.Handled=true;
    }
    private void Released(object sender,PointerRoutedEventArgs e)
    {
        if(_active is not null&&!_dragged&&_gesture=="select")
        {
            var p=Pixels(e.GetCurrentPoint(this).Position);var x=p.X-_active.X;var y=p.Y-_active.Y;
            ObjectPicked?.Invoke(MeshPicker.Pick(Scene,_active,x,y),(e.KeyModifiers&VirtualKeyModifiers.Shift)!=0);
            var ray=_active.Camera.Ray(x,y,_active.Width,_active.Height);var normal=_active.Name switch{"Front"=>Vec3.UnitY,"Right"=>Vec3.UnitX,_=>Vec3.UnitZ};var dot=Vec3.Dot(ray.Direction,normal);
            if(Math.Abs(dot)>1e-12){var distance=-Vec3.Dot(ray.Origin,normal)/dot;if(distance>=0)ConstructionPointPicked?.Invoke(ray.Origin+ray.Direction*distance);}
        }
        _gesture="";_active=null;ReleasePointerCapture(e.Pointer);e.Handled=true;
    }
    private void Wheel(object sender,PointerRoutedEventArgs e){var p=e.GetCurrentPoint(this);var view=At(p.Position);if(view is null)return;view.Camera.Zoom(-p.Properties.MouseWheelDelta);Invalidate();e.Handled=true;}
}

/// <summary>Four-view headers and visible context-failure diagnostics around the reusable GL canvas.</summary>
public sealed class ViewportControl : Grid
{
    public CadViewport Canvas {get;}=new();
    private readonly Canvas _headers=new();
    private readonly TextBlock _error=new(){TextWrapping=TextWrapping.Wrap,MaxWidth=540,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new(24),Visibility=Visibility.Collapsed};
    public ViewportControl()
    {
        Children.Add(Canvas);Children.Add(_headers);Children.Add(_error);Canvas.DiagnosticsChanged+=message=>{_error.Text=message;_error.Visibility=Canvas.LastError is null?Visibility.Collapsed:Visibility.Visible;};
        Canvas.LayoutChanged+=Headers;SizeChanged+=(_,_)=>Headers();Headers();
    }
    private void Headers()
    {
        _headers.Children.Clear();var layout=ViewportLayout.Create(Math.Max(2,(int)ActualWidth),Math.Max(2,(int)ActualHeight),Canvas.Cameras,Canvas.Maximized);
        foreach(var v in layout){var button=new Button{Content=v.Name+"  ▾",FontSize=11,Padding=new(7,2,7,2),MinHeight=22,MinWidth=80,CornerRadius=new(0)};button.Click+=(_,_)=>Canvas.Maximize(v.Name);Microsoft.UI.Xaml.Controls.Canvas.SetLeft(button,v.X+4);Microsoft.UI.Xaml.Controls.Canvas.SetTop(button,v.Y+4);_headers.Children.Add(button);}
    }
}
