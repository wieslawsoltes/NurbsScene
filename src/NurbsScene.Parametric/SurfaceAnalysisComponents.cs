using NurbsScene.Geometry;
using NurbsScene.Kernel;

namespace NurbsScene.Parametric;

public static class SurfaceAnalysisComponents
{
    public static void Register(ComponentRegistry registry)
    {
        foreach (var centroid in new[] { false, true })
        {
            var id = centroid ? "SurfaceCentroid" : "SurfaceArea";
            registry.Register(new(id, "Surface", centroid
                ? "Area centroid of an untrimmed NURBS surface, with a numerical integration error estimate. Zero-area surfaces have no centroid."
                : "Unsigned area from analytic NURBS derivatives, not display triangles. Self-overlap is counted with multiplicity.",
                [new("S",ValueKind.Surface), new("Tolerance",ValueKind.Number,Default:1e-8)],
                centroid ? [new("C",ValueKind.Point),new("Error",ValueKind.Vector)] : [new("A",ValueKind.Number),new("Error",ValueKind.Number)],
                c => {
                    var result=c.Get<NurbsSurface>("S").GetAreaProperties(new(AbsoluteTolerance:c.Number("Tolerance")),c.CancellationToken);
                    if(!result.Converged)throw new ArithmeticException($"Surface integration did not converge: {result.Termination}.");
                    IReadOnlyDictionary<string,object?> output;
                    if(centroid)
                    {
                        if(result.Centroid is not{} point||result.EstimatedCentroidError is not{} error)
                            throw new ArithmeticException("Surface has no resolved nonzero-area centroid.");
                        output=new Dictionary<string,object?>{{"C",point},{"Error",error}};
                    }
                    else output=new Dictionary<string,object?>{{"A",result.Area},{"Error",result.EstimatedAreaError}};
                    return ValueTask.FromResult(output);
                }));
        }
    }
}
