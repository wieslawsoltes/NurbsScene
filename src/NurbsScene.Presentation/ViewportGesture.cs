using NurbsScene.Geometry;
using NurbsScene.Rendering;

namespace NurbsScene.Presentation;

public enum ModelNavigation { Orbit, Pan, Zoom }

/// <summary>A reversible model-view gesture. All movement is evaluated from the
/// press snapshot, so event coalescing and changing frame rate cannot accumulate
/// drift. Zoom keeps the press point on the camera target plane under the cursor.
/// The geometry document and its undo stack are never touched.</summary>
public sealed class ViewportGesture
{
    private readonly Camera _camera;
    private readonly Vec3 _target;
    private readonly double _distance,_scale,_azimuth,_elevation,_width,_height,_x,_y;
    private readonly ModelNavigation _mode;
    public ViewportGesture(Camera camera,ModelNavigation mode,double x,double y,double width,double height)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if(!Enum.IsDefined(mode)||!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)
            throw new ArgumentException("Finite positive viewport dimensions and a supported navigation mode are required.");
        camera.Basis();
        _camera=camera;_mode=mode;_x=x;_y=y;_width=width;_height=height;
        _target=camera.Target;_distance=camera.Distance;_scale=camera.Scale;_azimuth=camera.Azimuth;_elevation=camera.Elevation;
    }
    public void Restore()
    {
        _camera.Target=_target;_camera.Distance=_distance;_camera.Scale=_scale;
        _camera.Azimuth=_azimuth;_camera.Elevation=_elevation;
    }
    public void Move(double x,double y)
    {
        if(!double.IsFinite(x)||!double.IsFinite(y))throw new ArgumentException("Finite pointer coordinates required.");
        Restore();
        try
        {
            if(_mode==ModelNavigation.Zoom)ZoomAt(_camera,(y-_y)*4,_x,_y,_width,_height);
            else if(_mode==ModelNavigation.Pan)_camera.Pan(x-_x,y-_y,_height);
            else _camera.Orbit(x-_x,y-_y);
            _camera.Basis();
        }
        catch{Restore();throw;}
    }
    public static ModelNavigation Resolve(bool perspective,bool shift,bool control,bool middle=false)=>
        middle||shift?ModelNavigation.Pan:control?ModelNavigation.Zoom:perspective?ModelNavigation.Orbit:ModelNavigation.Pan;
    public static void ZoomAt(Camera camera,double delta,double x,double y,double width,double height)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if(!double.IsFinite(delta)||!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)
            throw new ArgumentException("Finite zoom and positive viewport dimensions required.");
        var (_,_,right,up)=camera.Basis();var original=camera.Target;var distance=camera.Distance;var scale=camera.Scale;
        var oldHeight=camera.Perspective?2*distance*Math.Tan(camera.FieldOfView/2):scale;
        camera.Zoom(delta);
        var newHeight=camera.Perspective?2*camera.Distance*Math.Tan(camera.FieldOfView/2):camera.Scale;
        var target=original+right*((x/width-.5)*(oldHeight-newHeight)*width/height)+up*((.5-y/height)*(oldHeight-newHeight));
        if(!target.IsFinite){camera.Distance=distance;camera.Scale=scale;throw new ArithmeticException("Zoom exceeds camera coordinate range.");}
        camera.Target=target;
    }
}
