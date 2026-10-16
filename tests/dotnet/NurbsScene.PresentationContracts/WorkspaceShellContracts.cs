using NurbsScene.Geometry;
using NurbsScene.Presentation;
using NurbsScene.Rendering;

internal static class WorkspaceShellContracts
{
    public static void Register(List<(string,Action)> tests)
    {
        void Test(string name,Action body)=>tests.Add((name,body));
        void Check(bool condition){if(!condition)throw new Exception("Workspace shell assertion failed.");}
        void Near(double a,double b)=>Check(Math.Abs(a-b)<1e-8);
        void Throws(Action body){try{body();}catch(ArgumentException){return;}throw new Exception("Expected argument rejection.");}
        Test("modeling layout is the fresh default, not a half-width definition",()=>{
            var p=new WorkspacePreferences();Check(!p.DefinitionVisible&&p.InspectorVisible&&p.SidebarVisible&&p.InspectorTab=="Properties");
        });
        Test("version one migrates visible panels and old layer tab without loss",()=>{
            Check(WorkspacePreferences.TryParse("1|Bottom|1|0|0.7",out var p));
            Check(p.Dock=="Bottom"&&p.DefinitionVisible&&!p.InspectorVisible&&p.Split==.7&&p.InspectorTab=="Layers");
            Check(WorkspacePreferences.TryParse(p.Encode(),out var again)&&again==p);
        });
        Test("all workspace chrome preferences survive exact version two round trip",()=>{
            foreach(var dock in new[]{"Right","Bottom"})foreach(var tab in new[]{"Properties","Layers","Objects"})
            foreach(var width in new[]{200d,317.25,480}){
                var p=new WorkspacePreferences(dock,true,false,.63,width,false,true,tab);
                Check(WorkspacePreferences.TryParse(p.Encode(),out var parsed)&&parsed==p);
            }
        });
        Test("malformed chrome preferences fall back as one record",()=>{
            foreach(var input in new[]{"2|Right|1|1|.5|NaN|1|0|Layers","2|Right|1|1|.5|199|1|0|Layers",
                "2|Right|1|1|.5|481|1|0|Layers","2|Right|1|1|.5|250|2|0|Layers",
                "2|Right|1|1|.5|250|1|0|Script","3|Right|1|1|.5|250|1|0|Layers",
                "2|Right|1|1|.5|250|1|0|Layers|extra"})
                Check(!WorkspacePreferences.TryParse(input,out var p)&&p==new WorkspacePreferences());
            Throws(()=>new WorkspacePreferences(InspectorWidth:double.NaN).Encode());
            Throws(()=>new WorkspacePreferences(InspectorTab:"Other").Encode());
        });
        Test("Rhino right mouse modifiers choose pan zoom and perspective orbit",()=>{
            Check(ViewportGesture.Resolve(true,false,false)==ModelNavigation.Orbit);
            Check(ViewportGesture.Resolve(false,false,false)==ModelNavigation.Pan);
            Check(ViewportGesture.Resolve(true,true,false)==ModelNavigation.Pan);
            Check(ViewportGesture.Resolve(true,false,true)==ModelNavigation.Zoom);
            Check(ViewportGesture.Resolve(false,false,true)==ModelNavigation.Zoom);
            Check(ViewportGesture.Resolve(true,false,true,true)==ModelNavigation.Pan);
        });
        foreach(var name in new[]{"Top","Perspective","Front","Right"})
        {
            Test(name+" anchored zoom preserves the target-plane screen point",()=>{
                var camera=new Camera(name);var (_,_,right,up)=camera.Basis();
                var height=camera.Perspective?2*camera.Distance*Math.Tan(camera.FieldOfView/2):camera.Scale;
                var point=camera.Target+right*((.23-.5)*height*800/600)+up*((.5-.67)*height);
                ViewportGesture.ZoomAt(camera,-260,800*.23,600*.67,800,600);
                var p=camera.Project(point,800,600);Near(p.X,800*.23);Near(p.Y,600*.67);
            });
            Test(name+" pan draft restores the complete camera exactly",()=>{
                var c=new Camera(name);var start=(c.Target,c.Distance,c.Scale,c.Azimuth,c.Elevation);
                var g=new ViewportGesture(c,ModelNavigation.Pan,100,200,800,600);g.Move(220,250);Check(c.Target!=start.Target);
                g.Restore();Check((c.Target,c.Distance,c.Scale,c.Azimuth,c.Elevation)==start);
            });
            Test(name+" navigation endpoint is independent of event coalescing",()=>{
                foreach(var mode in Enum.GetValues<ModelNavigation>()){
                    var a=new Camera(name);var b=new Camera(name);
                    var first=new ViewportGesture(a,mode,110,130,800,600);var second=new ViewportGesture(b,mode,110,130,800,600);
                    for(var i=0;i<40;i++)first.Move(110+i,130+i*.3);first.Move(300,220);second.Move(300,220);
                    Check((a.Target,a.Distance,a.Scale,a.Azimuth,a.Elevation)==(b.Target,b.Distance,b.Scale,b.Azimuth,b.Elevation));
                }
            });
        }
        Test("camera properties apply all fields atomically",()=>{
            var camera=new Camera();var next=new ViewportCameraSettings(new Vec3(12,34,56),240,170,.5,.8,Math.PI/3);
            next.Apply(camera);Check(ViewportCameraSettings.Read(camera)==next);
            foreach(var invalid in new[]{next with{Distance=0},next with{Scale=double.NaN},next with{Target=new Vec3(double.PositiveInfinity,0,0)},next with{Elevation=2},next with{FieldOfView=Math.PI}}){
                Throws(()=>invalid.Apply(camera));Check(ViewportCameraSettings.Read(camera)==next);
            }
        });
        Test("shell command search is bounded and distinct from modeling prototypes",()=>{
            Check(WorkspaceShellCommands.Search("_comm").SequenceEqual(new[]{"CommandHistory"}));
            Check(WorkspaceShellCommands.Search("").Count()==0&&WorkspaceShellCommands.Search(new string('x',257)).Count()==0);
            Check(WorkspaceShellCommands.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count()==WorkspaceShellCommands.Names.Count);
        });
        Test("bad navigation inputs reject atomically",()=>{
            var c=new Camera();var state=(c.Target,c.Distance,c.Scale,c.Azimuth,c.Elevation);
            Throws(()=>new ViewportGesture(c,ModelNavigation.Pan,0,0,0,200));
            Throws(()=>ViewportGesture.ZoomAt(c,double.NaN,0,0,800,600));
            var g=new ViewportGesture(c,ModelNavigation.Zoom,0,0,800,600);Throws(()=>g.Move(10,double.PositiveInfinity));
            Check((c.Target,c.Distance,c.Scale,c.Azimuth,c.Elevation)==state);
        });
    }
}
