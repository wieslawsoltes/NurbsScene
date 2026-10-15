using NurbsScene.Geometry;
using NurbsScene.Rendering;

namespace NurbsScene.Presentation;

/// <summary>Atomic camera-property edit. Validation is performed on a separate
/// camera so an invalid draft never partially updates the live viewport.</summary>
public sealed record ViewportCameraSettings(Vec3 Target,double Distance,double Scale,
    double Azimuth,double Elevation,double FieldOfView)
{
    public static ViewportCameraSettings Read(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return new(camera.Target,camera.Distance,camera.Scale,camera.Azimuth,camera.Elevation,camera.FieldOfView);
    }
    public void Apply(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if(!Target.IsFinite||!double.IsFinite(Distance)||Distance<.001||Distance>1e12
            ||!double.IsFinite(Scale)||Scale<.001||Scale>1e12
            ||!double.IsFinite(Azimuth)||!double.IsFinite(Elevation)||Math.Abs(Elevation)>1.55
            ||!double.IsFinite(FieldOfView)||FieldOfView<Math.PI/180||FieldOfView>179*Math.PI/180)
            throw new ArgumentException("Use finite coordinates, distance/height from 0.001 to 1e12, elevation within ±88.8°, and field of view from 1° to 179°.");
        var candidate=new Camera(camera.Name){Target=Target,Distance=Distance,Scale=Scale,Azimuth=Azimuth,Elevation=Elevation,FieldOfView=FieldOfView};
        candidate.Basis();
        camera.Target=Target;camera.Distance=Distance;camera.Scale=Scale;
        camera.Azimuth=Azimuth;camera.Elevation=Elevation;camera.FieldOfView=FieldOfView;
    }
}
