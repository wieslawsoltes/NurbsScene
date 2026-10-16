using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Geometry;
using NurbsScene.Presentation;
using NurbsScene.Rendering;

namespace NurbsScene.Controls;

/// <summary>Native camera editor used when the object selection is empty.
/// Drafts belong to one viewport and are committed atomically by Apply.</summary>
public sealed class ViewportPropertiesPanel : StackPanel,IDisposable
{
    private readonly CadViewport _viewport;
    private readonly Func<bool> _isActive;
    private readonly TextBlock _heading=ClassicChrome.Label("Viewport properties",13);
    private readonly TextBlock _projection=ClassicChrome.Label("",10),_size=ClassicChrome.Label("",10);
    private readonly TextBlock _error=new(){FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=ClassicChrome.Brush(150,40,35)};
    private readonly Dictionary<string,TextBox> _fields=[];
    private readonly Dictionary<TextBox,string> _presentedText=[];
    private readonly ComboBox _display=new(){Header="Display mode (all viewports)",FontSize=11,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly DispatcherTimer _refreshTimer=new(){Interval=TimeSpan.FromMilliseconds(50)};
    private string _camera="";
    private bool _draft,_refreshing,_disposed;
    private ViewportCameraSettings? _snapshot;
    public bool IsPanelActive=>_isActive();
    public bool HasDraft=>_draft;
    public string Validation=>_error.Text;
    public ViewportPropertiesPanel(CadViewport viewport,Func<bool>? isActive=null)
    {
        ArgumentNullException.ThrowIfNull(viewport);_viewport=viewport;_isActive=isActive??(()=>true);Spacing=7;
        AutomationProperties.SetAutomationId(this,"ViewportProperties");
        Children.Add(_heading);Children.Add(_projection);Children.Add(_size);
        _display.ItemsSource=Enum.GetNames<DisplayMode>();
        AutomationProperties.SetAutomationId(_display,"ViewportDisplayMode");Children.Add(_display);
        _display.SelectionChanged+=(_,_)=>{
            if(_refreshing||_display.SelectedItem is not string name||!Enum.TryParse<DisplayMode>(name,out var mode))return;
            _viewport.Scene.Mode=mode;_viewport.NotifyViewChanged();
        };
        foreach(var (key,label) in new[]{("X","Target X"),("Y","Target Y"),("Z","Target Z"),("Distance","Camera distance"),("Scale","Parallel view height"),("Azimuth","Azimuth (degrees)"),("Elevation","Elevation (degrees)"),("Fov","Field of view (degrees)")})
        {
            var box=new TextBox{Header=label,FontSize=11,MinHeight=26,Padding=new(5,2,5,2),HorizontalAlignment=HorizontalAlignment.Stretch};
            AutomationProperties.SetAutomationId(box,"ViewportProperty"+key);AutomationProperties.SetName(box,label);
            box.TextChanged+=(_,_)=>{
                if(_refreshing||(_presentedText.TryGetValue(box,out var presented)&&box.Text==presented))return;
                _draft=true;_error.Text="Unapplied camera changes";
            };
            _fields.Add(key,box);Children.Add(box);
        }
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};
        actions.Children.Add(ClassicChrome.Button("Apply",Apply,"ApplyViewportProperties"));
        actions.Children.Add(ClassicChrome.Button("Revert",()=>{_draft=false;_snapshot=null;Refresh();},"RevertViewportProperties"));Children.Add(actions);Children.Add(_error);
        Children.Add(new TextBlock{Text="Camera changes do not alter geometry or document undo. Orbit angles apply to Perspective; parallel views retain their named axes.",FontSize=10,TextWrapping=TextWrapping.Wrap});
        _refreshTimer.Tick+=(_,_)=>{_refreshTimer.Stop();Refresh();};
        _viewport.ViewChanged+=QueueRefresh;_viewport.SizeChanged+=OnSizeChanged;Refresh();
    }
    private void OnSizeChanged(object sender,SizeChangedEventArgs e)=>QueueRefresh();
    private void QueueRefresh()
    {
        if(_disposed||!IsPanelActive)return;
        if(_camera!=_viewport.ActiveViewport){Refresh();return;}
        if(!_refreshTimer.IsEnabled)_refreshTimer.Start();
    }
    public void Refresh()
    {
        if(_disposed||!IsPanelActive)return;var camera=_viewport.Cameras[_viewport.ActiveViewport];
        _heading.Text=camera.Name+" viewport";_projection.Text=camera.Perspective?"Perspective projection":"Parallel projection";
        var rectangle=ViewportLayout.Create(Math.Max(1,(int)_viewport.ActualWidth),Math.Max(1,(int)_viewport.ActualHeight),_viewport.Cameras,_viewport.Maximized).FirstOrDefault(v=>v.Name==camera.Name);
        _size.Text=rectangle is null?"Not currently visible":$"Size: {rectangle.Width} × {rectangle.Height} DIP";
        if(_camera!=camera.Name){_camera=camera.Name;_draft=false;_snapshot=null;}
        _refreshing=true;
        try
        {
            _display.SelectedItem=_viewport.Scene.Mode.ToString();
            if(_draft)return;
            var settings=ViewportCameraSettings.Read(camera);if(settings==_snapshot)return;_snapshot=settings;_error.Text="";
            var values=new Dictionary<string,double>{{"X",settings.Target.X},{"Y",settings.Target.Y},{"Z",settings.Target.Z},{"Distance",settings.Distance},{"Scale",settings.Scale},{"Azimuth",settings.Azimuth*180/Math.PI},{"Elevation",settings.Elevation*180/Math.PI},{"Fov",settings.FieldOfView*180/Math.PI}};
            foreach(var (name,box) in _fields)
            {
                var text=values[name].ToString("G17",CultureInfo.InvariantCulture);
                _presentedText[box]=text;box.Text=text;
            }
            _fields["Scale"].IsEnabled=!camera.Perspective;foreach(var name in new[]{"Azimuth","Elevation","Fov"})_fields[name].IsEnabled=camera.Perspective;
        }
        finally{_refreshing=false;}
    }
    private void Apply()
    {
        if(_camera!=_viewport.ActiveViewport){_draft=false;_snapshot=null;Refresh();return;}
        try
        {
            double Number(string name)=>double.TryParse(_fields[name].Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:throw new ArgumentException($"{_fields[name].Header}: enter a finite number using a decimal point.");
            var camera=_viewport.Cameras[_camera];
            var draft=new ViewportCameraSettings(new Vec3(Number("X"),Number("Y"),Number("Z")),Number("Distance"),Number("Scale"),Number("Azimuth")*Math.PI/180,Number("Elevation")*Math.PI/180,Number("Fov")*Math.PI/180);
            _viewport.CancelNavigation();draft.Apply(camera);_draft=false;_snapshot=null;_viewport.NotifyViewChanged();Refresh();
        }
        catch(Exception error){_error.Text=error.Message;}
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_refreshTimer.Stop();_viewport.ViewChanged-=QueueRefresh;_viewport.SizeChanged-=OnSizeChanged;}
}
