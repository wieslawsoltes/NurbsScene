#if NURBSSCENE_QUALIFICATION
using NurbsScene.Controls;
using NurbsScene.Geometry;

namespace NurbsScene.App;

public partial class App
{
    // Read-only, constant-size samples of the last authoritative object. The
    // browser test creates/edits objects solely through native mouse/keyboard.
    private static object? ObserveLastGeometry(WorkspaceView view)
    {
        var geometry = view.Controller.Document.Objects.LastOrDefault()?.Geometry;
        if (geometry is NurbsCurve c)
            return new { kind="curve", degreeU=c.Degree, degreeV=0, countU=c.Count, countV=1,
                samples=Enumerable.Range(0,9).Select(i=>c.PointAt(c.Domain.At(i/8d)).ToArray()).ToArray() };
        if (geometry is NurbsSurface s)
            return new { kind="surface", degreeU=s.DegreeU, degreeV=s.DegreeV, countU=s.CountU, countV=s.CountV,
                samples=Enumerable.Range(0,9).Select(i=>s.PointAt(s.DomainU.At((i%3)/2d),s.DomainV.At((i/3)/2d)).ToArray()).ToArray() };
        return null;
    }
}
#endif
