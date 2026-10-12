using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.IO;

var tests = new List<(string Name, Action Run)>();
var factory = new GeometryFactory(new ManagedNurbsEvaluator());
void Test(string name, Action run) => tests.Add((name,run));
void Check(bool ok, string reason = "Assertion failed") { if(!ok)throw new Exception(reason); }
void Near(double a, double b, double tolerance=1e-12) => Check(double.IsFinite(a)&&Math.Abs(a-b)<=tolerance,$"{a:G17} != {b:G17}");
void Throws<T>(Action run) where T:Exception { try {run();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name); }
Graph Graph()=>new(StandardComponents.Create(factory));
Test("legacy sliders preserve automatic precision without mutating metadata",()=>{
    var g=Graph();var n=g.Add("Number");var revision=g.Revision;
    Check(NumberSliderSettings.TryRead(n,out var s,out var v,out _)&&s.DecimalPlaces is null&&v==1);
    Near(s.ValueAt(.123456789),12.3456789,1e-10);Check(g.Revision==revision&&n.Parameters.IsEmpty);
});
Test("extreme opposite-sign ranges produce finite midpoints and monotone values",()=>{
    var s=new NumberSliderSettings(-double.MaxValue,double.MaxValue);
    Near(s.ValueAt(.5),0);Near(s.Fraction(0),.5);
    var previous=s.Minimum;
    for(var i=0;i<=1000;i++){var v=s.ValueAt(i/1000d);Check(double.IsFinite(v)&&v>=previous&&v>=s.Minimum&&v<=s.Maximum);Near(s.Fraction(v),i/1000d,1e-15);previous=v;}
    Check(s.ValueAt(0)==s.Minimum&&s.ValueAt(1)==s.Maximum);
});
Test("narrow large ranges and subnormal endpoints remain usable",()=>{
    var min=1e300;var max=Math.BitIncrement(min);var s=new NumberSliderSettings(min,max);
    Check(s.Fraction(min)==0&&s.Fraction(max)==1&&s.ValueAt(0)==min&&s.ValueAt(1)==max);
    s=new(-double.Epsilon,double.Epsilon);Check(s.ValueAt(.5)==0&&s.Fraction(0)==.5);
});
Test("explicit decimal precision rounds generated values and preserves endpoints",()=>{
    var s=new NumberSliderSettings(-1,1,2);Near(s.ValueAt(.562),.12);Near(s.Normalize(.125),.12);Near(s.Normalize(.135),.14);
    var narrow=new NumberSliderSettings(.123,.124,2);Check(narrow.ValueAt(0)==.123&&narrow.ValueAt(1)==.124);
    Check(narrow.ValueAt(.5)>=narrow.Minimum&&narrow.ValueAt(.5)<=narrow.Maximum);
});
Test("large finite values do not overflow decimal quantization",()=>{
    var s=new NumberSliderSettings(1e300,1.1e300,12);Check(double.IsFinite(s.ValueAt(.7))&&s.Normalize(1.02e300)==1.02e300);
});
Test("integer settings enforce signed 32-bit bounds and whole generated values",()=>{
    var s=new NumberSliderSettings(int.MinValue,int.MaxValue,integer:true);
    for(var i=0;i<100;i++){var v=s.ValueAt(i/99d);Check(v==Math.Truncate(v)&&v>=int.MinValue&&v<=int.MaxValue);}
    Throws<ArgumentException>(()=>new NumberSliderSettings(-.5,3,integer:true));
    Throws<ArgumentException>(()=>new NumberSliderSettings(int.MinValue,(double)int.MaxValue+1,integer:true));
});
Test("invalid settings and nonfinite interaction values fail explicitly",()=>{
    foreach(var pair in new[]{(1d,1d),(2d,1d),(double.NaN,10d),(0d,double.PositiveInfinity)})Throws<ArgumentException>(()=>new NumberSliderSettings(pair.Item1,pair.Item2));
    Throws<ArgumentException>(()=>new NumberSliderSettings(0,1,13));Throws<ArgumentException>(()=>default(NumberSliderSettings).ValueAt(.5));
    var s=new NumberSliderSettings(0,1);Throws<ArgumentOutOfRangeException>(()=>s.ValueAt(double.NaN));Throws<ArgumentOutOfRangeException>(()=>s.Normalize(2));
    Check(s.ValueAt(-1)==0&&s.ValueAt(2)==1);
});
Test("malformed immutable slider metadata is a rendering diagnostic, not an exception",()=>{
    var g=Graph();
    foreach(var values in new[]{new Dictionary<string,object?>{{"min","bad"}},new(){{"value",null}},new(){{"decimalPlaces",.5}},new(){{"min",3d},{"max",2d}}})
    {var n=g.Add("Number",parameters:values);Check(!NumberSliderSettings.TryRead(n,out _,out _,out var error)&&!string.IsNullOrEmpty(error));}
    Check(!NumberSliderSettings.TryRead(g.Add("Add"),out _,out _,out _));
});
Test("legacy values outside range are displayed without being silently clamped",()=>{
    var n=Graph().Add("Number",parameters:new Dictionary<string,object?>{{"value",200d}});
    Check(NumberSliderSettings.TryRead(n,out var s,out var v,out _)&&v==200&&s.Fraction(v)==1);
    Throws<ArgumentOutOfRangeException>(()=>s.Normalize(v));
});
Test("settings apply is one undoable notification and preserves wires and parameters",()=>{
    var g=Graph();var n=g.Add("Number",parameters:new Dictionary<string,object?>{{"other","retained"}});var add=g.Add("Add");g.Connect(new(n.Id,"N"),new(add.Id,"A"));
    var session=new GraphSession(g);var topology=g.TopologyRevision;var changed=0;g.Changed+=()=>changed++;
    Check(NumberSliderEditing.Apply(session,g,n,new(-20,30,2),2.345,"Edited"));
    var result=g.GetNode(n.Id);Check((double)result.Parameters["value"]! ==2.35&&result.Name=="Edited"&&(string)result.Parameters["other"]! =="retained");
    Check(g.TopologyRevision==topology&&g.WireCount==1&&changed==1&&session.CanUndo);
    Check(session.Undo()&&session.Graph.GetNode(n.Id).Name is null&&!session.CanUndo);
    Check(session.Redo()&&session.Graph.GetNode(n.Id).Name=="Edited");
});
Test("no-op apply preserves redo and parameter absence",()=>{
    var g=Graph();var n=g.Add("Number");var session=new GraphSession(g);session.Edit("Move",x=>x.Update(n with{X=15}));session.Undo();
    g=session.Graph;n=g.GetNode(n.Id);var rev=g.Revision;
    Check(!NumberSliderEditing.Apply(session,g,n,new(0,100),1,null));Check(g.Revision==rev&&session.CanRedo&&n.Parameters.IsEmpty);
});
Test("stale slider edits reject replaced graphs and changed nodes",()=>{
    var g=Graph();var n=g.Add("Number");var session=new GraphSession(g);g.Update(n with{Name="New"});
    Throws<InvalidOperationException>(()=>NumberSliderEditing.Apply(session,g,n,new(0,100),2,null));Check(!session.CanUndo);
    n=g.GetNode(n.Id);session.Replace(Graph());Throws<InvalidOperationException>(()=>NumberSliderEditing.Apply(session,g,n,new(0,100),2,null));Check(session.Graph.NodeCount==0);
});
Test("invalid apply remains atomic and cannot change the component numeric type",()=>{
    var g=Graph();var n=g.Add("Integer");var session=new GraphSession(g);var revision=g.Revision;
    Throws<ArgumentException>(()=>NumberSliderEditing.Apply(session,g,n,new(0,100),1,null));
    Throws<ArgumentOutOfRangeException>(()=>NumberSliderEditing.Apply(session,g,n,new(0,10,integer:true),20,null));
    Throws<ArgumentException>(()=>NumberSliderEditing.Apply(session,g,n,new(0,100,integer:true),1,new string('a',257)));
    Check(g.Revision==revision&&!session.CanUndo);
});
Test("settings and rounded values survive exact workspace round trip",()=>{
    var g=Graph();var n=g.Add("Number");NumberSliderEditing.Apply(new(g),g,n,new(-10,20,3),1.23456,"Scale");
    var loaded=WorkspaceFormat.Deserialize(factory.Evaluator,g.Registry,WorkspaceFormat.Serialize(new(),g)).Graph;
    Check(NumberSliderSettings.TryRead(loaded.GetNode(n.Id),out var s,out var v,out _)&&s.DecimalPlaces==3&&v==1.235);
    var solution=loaded.SolveAsync().GetAwaiter().GetResult();Check(solution.ErrorCount==0&&(double)solution.Nodes[n.Id].Outputs["N"].AllItems.Single()! ==v);
});
Test("slider edits retain every existing wire drawing record",()=>{
    var g=Graph();var n=g.Add("Number");var a=g.Add("Add");g.Connect(new(n.Id,"N"),new(a.Id,"A"));var p=new GraphPresentation();var before=p.Get(g);var wb=p.WireBuilds;
    NumberSliderEditing.Apply(new(g),g,n,new(-10,20,3),1.25,"Slider");var after=p.Get(g);
    Check(before.Wires==after.Wires&&p.WireBuilds==wb);
});
Test("valid settings read and mapping avoid allocations after warm-up",()=>{
    var n=Graph().Add("Number");double sum=0;
    for(var i=0;i<100;i++){NumberSliderSettings.TryRead(n,out var s,out var v,out _);sum+=s.Fraction(v)+s.ValueAt(.3);}
    var start=GC.GetAllocatedBytesForCurrentThread();
    for(var i=0;i<10000;i++){NumberSliderSettings.TryRead(n,out var s,out var v,out _);sum+=s.Fraction(v)+s.ValueAt(.3);}
    var bytes=GC.GetAllocatedBytesForCurrentThread()-start;Check(bytes==0&&sum>0);Console.WriteLine($"METRIC slider-read-map-10000={bytes} B");
});
var failed=0;foreach(var(name,run)in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+"\n"+error);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="slider-contracts",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
