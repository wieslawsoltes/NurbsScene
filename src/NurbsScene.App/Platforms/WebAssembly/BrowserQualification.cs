#if NURBSSCENE_QUALIFICATION
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NurbsScene.Controls;
using NurbsScene.Geometry;
using NurbsScene.Rendering;
using NurbsScene.Parametric;
using NurbsScene.IO;
using NurbsScene.Model;

namespace NurbsScene.App;

// Opt-in build-time smoke hook for the REAL Uno application. It is never included
// in ordinary builds. No geometry/GPU replacement and no unconditional pass.
public partial class App
{
    private sealed record FrameSubmissionMetrics(long Frames,long GeometryUploadBytes,long GeometryUploadCount,
        int DrawCalls,int TriangleCount,int LineCount,double CpuSubmissionMilliseconds);
    private static FrameSubmissionMetrics ReadSubmissionMetrics(CadViewport canvas)=>new(canvas.Frames,
        canvas.GeometryUploadBytes,canvas.GeometryUploadCount,canvas.DrawCalls,canvas.TriangleCount,
        canvas.LineCount,canvas.LastSubmissionMilliseconds);
    partial void ConfigureQualification(WorkspaceView workspace)
    {
        if (Uno.Foundation.WebAssemblyRuntime.InvokeJS("new URLSearchParams(location.search).get('qualification') === '1' ? 'yes' : 'no'") == "yes")
            _ = RunQualificationAsync(workspace);
    }
    private static async Task RunQualificationAsync(WorkspaceView view)
    {
        var checks = new List<string>();
        var report = new Dictionary<string,object?> { ["application"]="Uno Platform", ["status"]="running", ["scope"]="startup, managed commands/graph/interchange and actual WebGPU framebuffer smoke; not full UI/parity qualification" };
        void Check(bool value,string name) { if(!value)throw new InvalidOperationException(name);checks.Add(name); }
        async Task Wait(Func<bool> predicate,string message)
        {
            var watch=Stopwatch.StartNew();while(!predicate()){if(watch.Elapsed>TimeSpan.FromSeconds(40))throw new TimeoutException(message);await Task.Delay(50);}
        }
        void Publish()
        {
            report["checks"]=checks;
            var encoded=JsonSerializer.Serialize(JsonSerializer.Serialize(report));
            Uno.Foundation.WebAssemblyRuntime.InvokeJS($"globalThis.nurbsSceneQualification = JSON.parse({encoded});");
        }
        Publish();
        try
        {
            var controller=view.Controller;var canvas=view.Viewport.Canvas;
            const int captureBudgetMilliseconds=60_000,captureOuterBudgetMilliseconds=65_000;
            var captures=new List<Dictionary<string,object?>>();
            report["captureMeasurements"]=captures;
            report["captureScope"]="Explicit browser qualification budget for cold software GPU execution; end-to-end capture duration is not GPU timing or a hardware latency claim.";
            async Task<byte[]> Capture(string name)
            {
                var sample=new Dictionary<string,object?>
                {
                    ["name"]=name,["status"]="running",["phase"]="requesting capture",
                    ["budgetMilliseconds"]=captureBudgetMilliseconds,["outerBudgetMilliseconds"]=captureOuterBudgetMilliseconds
                };
                captures.Add(sample);
                var elapsed=Stopwatch.StartNew();
                try
                {
                    var bytes=await canvas.CaptureRgbaAsync(TimeSpan.FromMilliseconds(captureBudgetMilliseconds)).WaitAsync(TimeSpan.FromMilliseconds(captureOuterBudgetMilliseconds));
                    sample["status"]="passed";sample["phase"]=canvas.LastCapturePhase;sample["byteCount"]=bytes.Length;
                    return bytes;
                }
                catch(Exception error)
                {
                    sample["status"]="failed";sample["phase"]=canvas.LastCapturePhase;sample["error"]=error.Message;
                    throw;
                }
                finally {sample["elapsedMilliseconds"]=elapsed.Elapsed.TotalMilliseconds;}
            }
            Check(controller.Document.Objects.Count==0&&controller.Graph.Nodes.Count==0,"Ordinary startup is an empty modeling document, not an auto-loaded sample");
            if(Uno.Foundation.WebAssemblyRuntime.InvokeJS("new URLSearchParams(location.search).get('qualificationStartup') === '1' ? 'yes' : 'no'")=="yes")
            {
                await Wait(()=>view.IsLoaded&&view.Console.IsLoaded&&(canvas.Frames>0||canvas.LastError is not null),"Ordinary Uno startup did not submit its first frame.");
                report["startup"]=await ObserveOrdinaryStartupAsync(view);
            }
            controller.LoadPavilion(); // Explicit qualification fixture; ordinary startup stays empty.
            await Wait(()=>canvas.Frames>0||canvas.LastError is not null,"Uno GPU canvas did not initialize.");
            report["gpuBackend"]=canvas.Backend;report["gpuApi"]="WebGPU";report["lastGpuError"]=canvas.LastError;
            Check(canvas.LastError is null&&canvas.Frames>0&&canvas.Backend.StartsWith("WebGPU",StringComparison.Ordinal),"Uno created its WebGPU device and submitted a frame: "+canvas.LastError);
            await Wait(()=>controller.Graph.LastResult is {Stale:false} r&&r.Revision==controller.Graph.Revision,"Parametric solution did not settle.");
            Check(controller.Graph.LastResult!.ErrorCount==0,"Managed pavilion graph solved without errors");
            var count=controller.Document.Objects.Count;
            controller.Execute("Circle 0,0,0 4");Check(controller.Document.Objects.Count==count+1,"Managed command creates exact curve");
            controller.Execute("Undo");Check(controller.Document.Objects.Count==count,"Document undo");
            controller.Execute("Redo");Check(controller.Document.Objects.Count==count+1,"Document redo");controller.Execute("Undo");
            var canopy=controller.Graph.Nodes.Single(n=>n.Component=="SaddleSurface");
            var old=(NurbsSurface)controller.Graph.LastResult!.Nodes[canopy.Id].Outputs["S"].AllItems.Single()!;
            var height=controller.Graph.Nodes.Single(n=>n.Name=="Rise");
            controller.GraphSession.Edit("Qualification parameter edit",g=>g.Update(height with{Parameters=height.Parameters.SetItem("value",19d)}));
            await Wait(()=>controller.Graph.LastResult is {Stale:false} r&&r.Revision==controller.Graph.Revision,"Changed definition did not settle.");
            var changed=(NurbsSurface)controller.Graph.LastResult!.Nodes[canopy.Id].Outputs["S"].AllItems.Single()!;
            Check((old.PointAt(.5,.5)-changed.PointAt(.5,.5)).Length>1e-6,"Parameter edit changes exact surface");
            Check(controller.Bake([canopy.Id])==1,"Exact surface baking");controller.Execute("Undo");
            var json=controller.Save();controller.Load(json);
            Check(controller.Document.Objects.Count==count,"Exact managed workspace round trip");
            await Wait(()=>controller.Graph.LastResult is {Stale:false} r&&r.Revision==controller.Graph.Revision,"Reloaded definition did not settle.");
            // Exercise typed path editing and persistent tree/empty branch handling
            // on the real managed WebAssembly runtime, independently of native tests.
            var paths=new Graph(controller.Registry);
            var tree=new DataTree([new(DataPath.Parse("{2;3}"),[1d,null,2d]),new(DataPath.Parse("{9}"),[])]);
            var clean=paths.Add("CleanTree",parameters:new Dictionary<string,object?>{{"input:T",tree}});
            var pick=paths.Add("TreeBranch",parameters:new Dictionary<string,object?>{{"input:P",DataPath.Parse("{2;3}")}});
            paths.Connect(new(clean.Id,"T"),new(pick.Id,"T"));
            var pathResult=await paths.SolveAsync();
            Check(pathResult.ErrorCount==0&&pathResult.Nodes[pick.Id].Outputs["T"].ItemCount==2,"Typed path components preserve sparse branches and clean explicit null items");
            var loadedPaths=WorkspaceFormat.Deserialize(controller.Kernel.Evaluator,controller.Registry,WorkspaceFormat.Serialize(new SceneDocument(),paths)).Graph;
            Check(loadedPaths.GetNode(pick.Id).Parameters["input:P"] is DataPath persisted&&persisted.Equals(DataPath.Parse("{2;3}")),"Typed path workspace round trip on WebAssembly");
            Check((await loadedPaths.SolveAsync()).ErrorCount==0,"Reloaded tree definition solves on WebAssembly");
            await VerifySectionsAsync(view,Check);
            await VerifySurfaceAnalysisAsync(view,Check);
            canvas.Fit();controller.Scene.ShowGrid=false;controller.Scene.ShowIsocurves=false;
            var scene=controller.Scene;var items=scene.Items.ToArray();
            // Differential readback proves modeled surfaces, not only a grid, produced pixels.
            scene.SetItems([]);var empty=await Capture("empty");
            scene.SetItems(items);var filled=await Capture("filled");
            var different=0;for(var i=0;i<filled.Length;i+=4)if(Math.Abs(filled[i]-empty[i])+Math.Abs(filled[i+1]-empty[i+1])+Math.Abs(filled[i+2]-empty[i+2])>12)different++;
            Check(different>100,"Actual WebGPU surface pixels differ from empty scene");
            Check(canvas.Viewports.Length==4,"Four native Uno viewports");
            var beforeMetrics=ReadSubmissionMetrics(canvas);
            var beforeScene=(scene.Revision,scene.MeshRevision,scene.Origin,scene.ShowGrid,scene.ShowIsocurves);
            var before=SHA256.HashData(filled);canvas.Cameras["Perspective"].Orbit(60,25);
            var rotated=await Capture("rotated");
            var afterMetrics=ReadSubmissionMetrics(canvas);
            var sceneUnchanged=beforeScene==(scene.Revision,scene.MeshRevision,scene.Origin,scene.ShowGrid,scene.ShowIsocurves);
            var geometryReused=beforeMetrics.GeometryUploadBytes==afterMetrics.GeometryUploadBytes&&
                beforeMetrics.GeometryUploadCount==afterMetrics.GeometryUploadCount;
            report["renderingPerformance"]=new
            {
                schema=1,sceneUnchanged,geometryReused,before=beforeMetrics,after=afterMetrics,
                scope="Camera-only resident geometry reuse. CPU submission time excludes GPU execution and readback; no hardware FPS claim."
            };
            Check(!before.SequenceEqual(SHA256.HashData(rotated)),"Camera motion changes the actual GPU image");
            Check(beforeMetrics.GeometryUploadBytes>0&&beforeMetrics.GeometryUploadCount>0&&
                beforeMetrics.DrawCalls>0&&beforeMetrics.TriangleCount>0&&
                afterMetrics.Frames>beforeMetrics.Frames&&afterMetrics.DrawCalls>0&&afterMetrics.TriangleCount>0,
                "Actual WebGPU camera samples submit resident mesh geometry");
            Check(sceneUnchanged,"Camera-only qualification preserves scene geometry, origin and grid state");
            Check(geometryReused,"Camera-only motion reuses resident WebGPU vertex, index and line buffers");
            Check(canvas.LastError is null,"No GPU submission error after interactions");
            report["gpuBackend"]=canvas.Backend;report["frames"]=canvas.Frames;report["surfacePixels"]=different;
            report["gpuReadbackSha256"]=Convert.ToHexString(SHA256.HashData(rotated));
            report["status"]="passed";
        }
        catch(Exception error)
        {
            report["status"]="failed";report["error"]=error.ToString();
            var canvas=view.Viewport.Canvas;
            var graph=view.GraphEditor.Canvas;
            // Read-only failure state distinguishes missing layout, unloaded
            // canvases and a compositor failure from shader/device validation.
            report["renderingState"]=new
            {
                workspace=new{view.IsLoaded,view.ActualWidth,view.ActualHeight},
                viewport=new{canvas.IsLoaded,canvas.ActualWidth,canvas.ActualHeight,canvas.IsWebGpuInitialized,canvas.Frames,canvas.Backend,canvas.LastError,canvas.LastCapturePhase},
                graph=new{graph.IsLoaded,graph.ActualWidth,graph.ActualHeight,graph.IsWebGpuInitialized,graph.DrawingFrames,graph.LastDrawingError}
            };
        }
        Publish();
        if(report["status"] as string=="passed")await ObserveWorkspaceUiAsync(view);
    }
}
#endif
