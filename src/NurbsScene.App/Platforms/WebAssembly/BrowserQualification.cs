#if NURBSSCENE_QUALIFICATION
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NurbsScene.Controls;
using NurbsScene.Geometry;
using NurbsScene.Rendering;

namespace NurbsScene.App;

// Opt-in build-time smoke hook for the REAL Uno application. It is never included
// in ordinary builds. No geometry/GPU replacement and no unconditional pass.
public partial class App
{
    partial void ConfigureQualification(WorkspaceView workspace)
    {
        if (Uno.Foundation.WebAssemblyRuntime.InvokeJS("new URLSearchParams(location.search).get('qualification') === '1' ? 'yes' : 'no'") == "yes")
            _ = RunQualificationAsync(workspace);
    }
    private static async Task RunQualificationAsync(WorkspaceView view)
    {
        var checks = new List<string>();
        var report = new Dictionary<string,object?> { ["application"]="Uno Platform", ["status"]="running", ["scope"]="startup, managed commands/graph/interchange and actual GL framebuffer smoke; not full UI/parity qualification" };
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
            await Wait(()=>canvas.Frames>0||canvas.LastError is not null,"Uno GPU canvas did not initialize.");
            report["gpuBackend"]=canvas.Backend;report["lastGpuError"]=canvas.LastError;
            Check(canvas.LastError is null&&canvas.Frames>0,"Uno created a GPU context and submitted a frame: "+canvas.LastError);
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
            canvas.Fit();controller.Scene.ShowGrid=false;controller.Scene.ShowIsocurves=false;
            var scene=controller.Scene;var items=scene.Items.ToArray();
            // Differential readback proves modeled surfaces, not only a grid, produced pixels.
            scene.SetItems([]);var empty=await canvas.CaptureRgbaAsync().WaitAsync(TimeSpan.FromSeconds(15));
            scene.SetItems(items);var filled=await canvas.CaptureRgbaAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var different=0;for(var i=0;i<filled.Length;i+=4)if(Math.Abs(filled[i]-empty[i])+Math.Abs(filled[i+1]-empty[i+1])+Math.Abs(filled[i+2]-empty[i+2])>12)different++;
            Check(different>100,"Actual GL surface pixels differ from empty scene");
            Check(canvas.Viewports.Length==4,"Four native Uno viewports");
            var before=SHA256.HashData(filled);canvas.Cameras["Perspective"].Orbit(60,25);
            var rotated=await canvas.CaptureRgbaAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Check(!before.SequenceEqual(SHA256.HashData(rotated)),"Camera motion changes the actual GPU image");
            Check(canvas.LastError is null,"No GPU submission error after interactions");
            report["gpuBackend"]=canvas.Backend;report["frames"]=canvas.Frames;report["surfacePixels"]=different;
            report["gpuReadbackSha256"]=Convert.ToHexString(SHA256.HashData(rotated));
            report["status"]="passed";
        }
        catch(Exception error){report["status"]="failed";report["error"]=error.ToString();}
        Publish();
    }
}
#endif
