using System.Globalization;
using System.Text.Json;
using NurbsScene.Geometry;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.Rendering;
using NurbsScene.Workbench;

var tests=new List<(string,Action)>();var f=new GeometryFactory(new ManagedNurbsEvaluator());
void Test(string name,Action body)=>tests.Add((name,body));
void Check(bool ok,string message="Assertion failed"){if(!ok)throw new Exception(message);}
void Near(Vec3 a,Vec3 b)=>Check((a-b).Length<1e-8,$"{a} != {b}");
void Throws<T>(Action run)where T:Exception{try{run();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
Graph Graph()=>new(StandardComponents.Create(f));
Test("catalog has unique identities and actual command categories",()=>{
    Check(WorkspaceCommands.Tools.Length>=35);Check(WorkspaceCommands.Tools.Select(t=>t.Id).Distinct().Count()==WorkspaceCommands.Tools.Length);
    Check(WorkspaceCommands.Tools.All(t=>WorkspaceCommands.Categories.Contains(t.Category)&&t.Command.Length>0&&t.Icon.Length>0));
});
Test("toolbar model commands all route to implemented handlers",()=>{
    foreach(var tool in WorkspaceCommands.Tools.Where(t=>!t.Command.StartsWith('@'))){using var w=new WorkspaceController();try{w.Execute(tool.Command);}catch(NotSupportedException e){throw new Exception(tool.Id+": "+e.Message);}catch(ArgumentException){}catch(InvalidOperationException){}}
});
Test("suggestions rank prefixes and are case insensitive and bounded",()=>{
    Check(WorkspaceCommands.Search("_cir").Single().Id=="Circle");Check(WorkspaceCommands.Search("surface").Length>0);
    Check(WorkspaceCommands.Search("",3).Length==3);Check(WorkspaceCommands.Search(new string('a',257)).Length==0);
});
Test("recall preserves a draft after navigating older commands",()=>{
    var h=new CommandRecall();h.Submit("Line");h.Submit("Circle 0,0,0 5");
    Check(h.Previous("unfinished")=="Circle 0,0,0 5");Check(h.Previous("ignored")=="Line");Check(h.Next()=="Circle 0,0,0 5");Check(h.Next()=="unfinished");
});
Test("empty submission repeats without growing duplicate history",()=>{
    var h=new CommandRecall();Check(h.Submit("") is null);h.Submit("Line");Check(h.Submit("")=="Line"&&h.Count==1);
});
Test("command history has bounded storage and input length",()=>{
    var h=new CommandRecall(3);for(var i=0;i<20;i++)h.Submit("C"+i);Check(h.Count==3);Throws<ArgumentException>(()=>h.Submit(new string('x',16385)));h.ResetNavigation();Check(h.Previous("")=="C19");
});
Test("preference round trip is invariant across cultures",()=>{
    var prior=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=new("pl-PL");var p=new WorkspacePreferences("Bottom",false,true,.63);Check(WorkspacePreferences.TryParse(p.Encode(),out var r)&&r==p);}finally{CultureInfo.CurrentCulture=prior;}
});
Test("malformed preferences reset atomically, including NaN and bad versions",()=>{
    foreach(var text in new[]{"","2|Right|1|1|0.5","1|Other|1|1|0.5","1|Bottom|1|0|NaN","1|Right|1|2|.5","1|Right|1|1|99"})Check(!WorkspacePreferences.TryParse(text,out var p)&&p==new WorkspacePreferences());
    Throws<ArgumentException>(()=>new WorkspacePreferences(Split:double.NaN).Encode());
});
Test("grid snapping handles negative half steps consistently",()=>{
    var aids=new ConstructionAids{GridSnap=true,Spacing=2};Near(aids.Resolve(new(-3,5,0),"Top").Point,new(-4,6,0));
});
Test("ortho and grid preserve the exact off-grid anchor coordinate",()=>{
    var aids=new ConstructionAids{Ortho=true,GridSnap=true};Near(aids.Resolve(new(7.1,3,0),"Top",new(.2,.3,0)).Point,new(7,.3,0));
});
Test("front and right construction planes constrain their proper axes",()=>{
    var aids=new ConstructionAids{Ortho=true};Near(aids.Resolve(new(3,99,8),"Front",new(2,0,1)).Point,new(2,0,8));Near(aids.Resolve(new(99,7,3),"Right",new(0,2,.5)).Point,new(0,7,.5));
});
Test("invalid construction options fail before returning misleading points",()=>{
    var a=new ConstructionAids();Throws<ArgumentOutOfRangeException>(()=>a.Spacing=0);Throws<ArgumentException>(()=>a.Resolve(new(double.NaN,0,0),"Top"));Throws<ArgumentException>(()=>a.Resolve(Vec3.Zero,"Unknown"));
});
Test("endpoint snaps override grid/ortho and preserve exact model coordinates",()=>{
    var scene=new DisplayScene();var c=f.Line(new(.2,.3,2),new(3,0,0));scene.SetItems([new("line",c,new(1,0,0))]);
    var camera=new Camera("Top"){Target=Vec3.Zero,Scale=20,Distance=10};var view=new ViewportRectangle("Top",0,0,800,800,camera);var screen=camera.Project(c.PointAt(0),800,800);
    var a=new ConstructionAids{EndSnap=true,Ortho=true,GridSnap=true};var r=a.At(scene,view,screen.X+1,screen.Y,Vec3.Zero);Check(r?.Snap=="End");Near(r!.Point,c.PointAt(0));
});
Test("locked endpoints do not participate in construction snaps",()=>{
    var scene=new DisplayScene();scene.SetItems([new("line",f.Line(new(.2,.3,2),new(3,0,0)),new(1,0,0),Locked:true)]);
    var camera=new Camera("Top"){Target=Vec3.Zero,Scale=20,Distance=10};var v=new ViewportRectangle("Top",0,0,800,800,camera);var p=camera.Project(new(.2,.3,2),800,800);
    var r=new ConstructionAids{EndSnap=true}.At(scene,v,p.X,p.Y);Check(r?.Snap!="End");Near(r!.Point,new(.2,.3,0));
});
Test("clipped and far-plane endpoints are excluded",()=>{
    var scene=new DisplayScene{ClipPlane=(Vec3.UnitZ,-10)};scene.SetItems([new("line",f.Line(new(.2,.3,2),new(3,0,0)),new(1,0,0))]);var camera=new Camera("Top"){Target=Vec3.Zero,Scale=20,Distance=10};var v=new ViewportRectangle("Top",0,0,800,800,camera);var p=camera.Project(new(.2,.3,2),800,800);Check(new ConstructionAids{EndSnap=true}.At(scene,v,p.X,p.Y)?.Snap!="End");
});
Test("graph layout aligns compact sliders and exposes all ports",()=>{
    var g=Graph();var number=g.Add("Number");var add=g.Add("Add",200,30);g.Connect(new(number.Id,"N"),new(add.Id,"A"));var d=new GraphPresentation().Get(g);
    Check(d.Nodes[0].Bounds.Height==38&&d.Nodes[0].Bounds.Width==180);Check(d.Wires[0].Start==d.Nodes[0].Port(true,0));Check(d.Wires[0].End==d.Nodes[1].Port(false,0));
});
Test("warm graph presentation avoids snapshots and managed allocations",()=>{
    var g=Graph();for(var i=0;i<1000;i++)g.Add("Number",i*20,0);var cache=new GraphPresentation();var first=cache.Get(g);var before=GC.GetAllocatedBytesForCurrentThread();
    for(var i=0;i<10000;i++)_ =cache.Get(g);var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
    Check(allocated==0&&cache.BuildCount==1&&ReferenceEquals(first,cache.Get(g)), $"Allocated {allocated}");Console.WriteLine($"METRIC unchanged-presentation-10000-calls={allocated} B");
});
Test("selection-free solve results do not invalidate drawing layout",()=>{
    var g=Graph();g.Add("Number");var cache=new GraphPresentation();var first=cache.Get(g);g.SolveAsync().GetAwaiter().GetResult();Check(ReferenceEquals(first,cache.Get(g)));
});
Test("presentation invalidates on graph replacement and preserves prior snapshots",()=>{
    var a=Graph();var node=a.Add("Number");var cache=new GraphPresentation();var before=cache.Get(a);a.Update(node with{X=500});var next=cache.Get(a);Check(next.Nodes[0].Bounds.X==500&&before.Nodes[0].Bounds.X==0);var b=Graph();b.Add("Text");Check(cache.Get(b).Nodes[0].Node.Component=="Text");
});
Test("backward wire bounds include cubic control points for culling",()=>{
    var g=Graph();var a=g.Add("Number",300,100);var b=g.Add("Add",0,400);g.Connect(new(a.Id,"N"),new(b.Id,"A"));var w=new GraphPresentation().Get(g).Wires.Single();Check(w.Bounds.X<0&&w.Bounds.X+w.Bounds.Width>w.Start.X);Check(w.Bounds.Intersects(new(w.Start.X,0,100,600)));
});
Test("multi-node dragging publishes once without dirtying the solve",()=>{
    var g=Graph();var a=g.Add("Number");var b=g.Add("Add");g.Connect(new(a.Id,"N"),new(b.Id,"A"));var before=g.SolveAsync().GetAwaiter().GetResult();var revision=g.Revision;var notifications=0;g.Changed+=()=>notifications++;
    g.MoveNodes([new(a.Id,17,23),new(b.Id,250,300)]);Check(g.Revision==revision+2&&notifications==1);var after=g.SolveAsync().GetAwaiter().GetResult();Check(after.Evaluated==0&&ReferenceEquals(before.Nodes,after.Nodes));
});
Test("multi-node move validation is atomic and no-op moves stay silent",()=>{
    var g=Graph();var a=g.Add("Number");var b=g.Add("Number");var revision=g.Revision;
    Throws<ArgumentException>(()=>g.MoveNodes([new(a.Id,100,100),new(b.Id,double.NaN,0)]));Check(g.GetNode(a.Id).X==0&&g.Revision==revision);
    Throws<ArgumentException>(()=>g.MoveNodes([new(a.Id,100,100),new(a.Id,200,100)]));g.MoveNodes([new(a.Id,0,0)]);Check(g.Revision==revision);
});
Test("interactive command anchor rejects invalid points and clears on cancel",()=>{
    using var w=new WorkspaceController();var notifications=0;w.Changed+=()=>notifications++;w.Execute("Line");Check(notifications==1&&w.CommandAnchor is null);
    Throws<ArgumentException>(()=>w.AcceptPoint(new(double.NaN,0,0)));Check(w.CommandAnchor is null);w.AcceptPoint(new(2,3,0));Check(w.CommandAnchor==new Vec3(2,3,0));w.CancelCommand();Check(w.CommandAnchor is null&&w.PendingCommand=="");
});
Test("aspect-aware zoom extents contains every control-hull corner",()=>{
    var bounds=new Bounds3(new(-30,-20,-10),new(30,20,45));
    foreach(var name in new[]{"Perspective","Top","Front","Right"})foreach(var size in new[]{(160,900),(1000,150),(500,500)})
    {
        var camera=new Camera(name);ViewportNavigation.Fit(camera,bounds,size.Item1,size.Item2);
        for(var i=0;i<8;i++)
        {
            var p=new Vec3((i&1)==0?bounds.Min.X:bounds.Max.X,(i&2)==0?bounds.Min.Y:bounds.Max.Y,(i&4)==0?bounds.Min.Z:bounds.Max.Z);
            var projected=camera.Project(p,size.Item1,size.Item2);
            Check(projected.X>=0&&projected.X<=size.Item1&&projected.Y>=0&&projected.Y<=size.Item2&&projected.Depth>camera.NearClip&&projected.Depth<camera.FarClip,$"{name} {size}: {projected}");
        }
    }
});
Test("invalid camera fitting is atomic and empty bounds are a no-op",()=>{
    var camera=new Camera();var target=camera.Target;var distance=camera.Distance;var scale=camera.Scale;
    Throws<ArgumentOutOfRangeException>(()=>ViewportNavigation.Fit(camera,new(Vec3.Zero,Vec3.UnitX),0,200));
    Throws<ArgumentException>(()=>ViewportNavigation.Fit(camera,new(Vec3.Zero,new(double.PositiveInfinity,1,1)),200,200));
    ViewportNavigation.Fit(camera,Bounds3.Empty,200,200);
    Check(camera.Target==target&&camera.Distance==distance&&camera.Scale==scale);
});
int failed=0;foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}Console.WriteLine(JsonSerializer.Serialize(new{suite="presentation",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
