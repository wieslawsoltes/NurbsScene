using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.Kernel;
using NurbsScene.IO;

var tests = new List<(string Name, Action Run)>();
var factory = new GeometryFactory(new ManagedNurbsEvaluator()); var registry = StandardComponents.Create(factory);
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool ok) { if (!ok) throw new Exception("Assertion failed."); }
void Near(double a, double b) => Check(Math.Abs(a - b) < 1e-9);
void Throws<T>(Action a) where T : Exception { try { a(); } catch(T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Graph New() => new(registry);
var custom = new PanelTextStyle("Courier New", 18, true, true, PanelHorizontalAlignment.Right, PanelVerticalAlignment.Bottom);
var preview = new PanelPreview(["WWW", "iii", ""], "3 items");
var layout = PanelDisplayLayout.Create(new(0,0,300,220), 1);
var metrics = new PanelFontMetrics(-8,2);
double Measure(string s) => s == "WWW" ? 42 : s == "iii" ? 9 : s.Length * 6;
PanelTextPlacement Place(PanelTextStyle style, double width=300, double height=220, PanelFontMetrics? fm=null) =>
    new PanelTextPlacementCache().Get(preview, style, width, height, layout, fm ?? metrics, 1, Measure);

Test("legacy defaults are absent and identical after workspace round trip", () => {
    var g=New();var p=g.Add("Panel");var t=g.Add("Text");
    Check(PanelTextStyle.For(p)==new PanelTextStyle() && PanelTextStyle.For(t)==PanelTextStyle.Default);
    var json=WorkspaceFormat.Serialize(new(),g);Check(!json.Contains("panelStyle"));
    var restored=WorkspaceFormat.Deserialize(factory.Evaluator,registry,json).Graph;
    Check(restored.GetNode(p.Id).PanelStyle is null && restored.GetNode(t.Id).PanelStyle is null);
});
Test("typography edits are display-only, atomic and preserve solved trees", () => {
    var g=New();var p=g.Add("Panel",parameters:new Dictionary<string,object?>{{"text","seed"}});var target=g.Add("ListLength");g.Connect(new(p.Id,"T"),new(target.Id,"L"));
    var before=g.SolveAsync().GetAwaiter().GetResult();var s=new GraphSession(g);var events=0;g.Changed+=()=>events++;
    var rev=g.Revision;var topology=g.TopologyRevision;var plans=g.ExecutionPlanBuilds;
    Check(PanelTypographyEditing.Apply(s,g,p,custom));var after=g.SolveAsync().GetAwaiter().GetResult();
    Check(events==1 && g.Revision==rev+1 && g.TopologyRevision==topology && g.ExecutionPlanBuilds==plans && after.Evaluated==0 && ReferenceEquals(before.Nodes,after.Nodes));
    Check(ReferenceEquals(g.GetNode(p.Id).Parameters,p.Parameters));
});
Test("one undo and redo restore all style properties together", () => {
    var g=New();var p=g.Add("Panel");var s=new GraphSession(g);PanelTypographyEditing.Apply(s,g,p,custom);
    Check(s.Undo() && s.Graph.GetNode(p.Id).PanelStyle is null && !s.CanUndo);
    Check(s.Redo() && s.Graph.GetNode(p.Id).PanelStyle==custom);
});
Test("no-op preserves redo and restoring defaults removes optional metadata", () => {
    var g=New();var p=g.Add("Panel");var s=new GraphSession(g);PanelTypographyEditing.Apply(s,g,p,custom);s.Undo();g=s.Graph;p=g.GetNode(p.Id);
    var rev=g.Revision;Check(!PanelTypographyEditing.Apply(s,g,p,new()) && s.CanRedo && rev==g.Revision);
    s.Redo();g=s.Graph;PanelTypographyEditing.Apply(s,g,g.GetNode(p.Id),new());Check(g.GetNode(p.Id).PanelStyle is null);
});
Test("both panel kinds persist typography and independent display options", () => {
    foreach(var kind in new[]{"Panel","Text"}){
        var g=New();var p=g.Add(kind);g.Update(p with{PanelStyle=custom,PanelDisplay=new(false,true,false),CanvasSize=new(420,300)});
        var loaded=WorkspaceFormat.Deserialize(factory.Evaluator,registry,WorkspaceFormat.Serialize(new(),g)).Graph.GetNode(p.Id);
        Check(loaded.PanelStyle==custom && loaded.PanelDisplay==new PanelDisplayOptions(false,true,false) && loaded.CanvasSize==new NodeCanvasSize(420,300));
    }
});
Test("exact copy paste and duplicate preserve style under new identities", () => {
    var g=New();var p=g.Add("Panel");g.Update(p with{PanelStyle=custom});
    var fragment=GraphFragmentFormat.Deserialize(factory.Evaluator,registry,GraphFragmentFormat.Serialize(GraphFragment.Capture(g,[p.Id]),registry));
    var s=new GraphSession(g);var id=fragment.Paste(s).Single();Check(id!=p.Id && g.GetNode(id).PanelStyle==custom && s.Undo() && s.Graph.NodeCount==1);
});
Test("stale deleted replaced and nonpanel targets are rejected without history edits", () => {
    var g=New();var p=g.Add("Panel");var s=new GraphSession(g);g.Update(p with{Name="external"});
    Throws<InvalidOperationException>(()=>PanelTypographyEditing.Apply(s,g,p,custom));Check(!s.CanUndo);
    p=g.GetNode(p.Id);g.Remove(p.Id);Throws<InvalidOperationException>(()=>PanelTypographyEditing.Apply(s,g,p,custom));
    g=New();p=g.Add("Panel");s.Replace(g);s.Replace(New());Throws<InvalidOperationException>(()=>PanelTypographyEditing.Apply(s,g,p,custom));
    g=New();p=g.Add("Number");Throws<ArgumentException>(()=>PanelTypographyEditing.Apply(new(g),g,p,custom));
});
Test("independent changes survive the edit and its undo", () => {
    var g=New();var p=g.Add("Panel");var s=new GraphSession(g);var other=g.Add("Number");
    PanelTypographyEditing.Apply(s,g,p,custom);s.Undo();Check(s.Graph.GetNode(other.Id).Component=="Number" && s.Graph.GetNode(p.Id).PanelStyle is null);
});
Test("invalid sizes and enums cannot mutate a graph", () => {
    var g=New();var p=g.Add("Panel");var revision=g.Revision;
    foreach(var size in new[]{0d,5.9,72.1,double.NaN,double.PositiveInfinity,double.NegativeInfinity}) Throws<ArgumentException>(()=>g.Update(p with{PanelStyle=custom with{Size=size}}));
    Throws<ArgumentException>(()=>g.Update(p with{PanelStyle=custom with{Horizontal=(PanelHorizontalAlignment)99}}));
    Throws<ArgumentException>(()=>g.Update(p with{PanelStyle=custom with{Vertical=(PanelVerticalAlignment)(-1)}}));
    Check(g.Revision==revision && ReferenceEquals(g.GetNode(p.Id),p));
});
Test("font names are bounded and sanitized at import and update boundaries", () => {
    foreach(var name in new[]{" padded","padded ","line\nbreak","nul\0","bad\ud800","https://font","font#name","font/file",@"font\file",new string('a',129)}) Throws<ArgumentException>(()=>(custom with{FontFamily=name}).Validate());
    Throws<ArgumentException>(()=>(custom with{FontFamily=null!}).Validate());
    foreach(var name in new[]{"","Noto Sans","日本語","a-b 123"})(custom with{FontFamily=name}).Validate();
});
Test("bad serialized style rejects the whole workspace", () => {
    var g=New();var p=g.Add("Panel");var raw=JsonNode.Parse(WorkspaceFormat.Serialize(new(),g))!;
    raw["graph"]!["nodes"]![0]!["panelStyle"]=new JsonObject{["size"]=-10};
    Throws<ArgumentException>(()=>WorkspaceFormat.Deserialize(factory.Evaluator,registry,raw.ToJsonString()));Check(g.GetNode(p.Id).PanelStyle is null);
});
Test("unsupported component cannot carry panel typography", () => {
    var g=New();var n=g.Add("Number");var rev=g.Revision;
    Throws<ArgumentException>(()=>g.Update(n with{PanelStyle=custom}));Check(g.Revision==rev);
});
Test("metadata changes preserve native drawing topology and panel bounds", () => {
    var g=New();var p=g.Add("Panel");var n=g.Add("ListLength");g.Connect(new(p.Id,"T"),new(n.Id,"L"));
    var drawing=new GraphPresentation();var before=drawing.Get(g);var wires=drawing.WireBuilds;
    PanelTypographyEditing.Apply(new(g),g,p,custom);var next=drawing.Get(g);
    Check(before.Wires==next.Wires && drawing.WireBuilds==wires && before.Nodes[0].Bounds==next.Nodes[0].Bounds);
});
Test("font size layout keeps historical default and low zoom readability", () => {
    var b=new CanvasBox(0,0,640,420);Check(PanelDisplayLayout.Create(b,.3)==PanelDisplayLayout.Create(b,.3,10));
    Near(PanelDisplayLayout.Create(b,.3,6).FontSize*.3,8);
    Check(PanelDisplayLayout.Create(b,1,20).Rows<PanelDisplayLayout.Create(b,1,10).Rows);
    Check(PanelDisplayLayout.Create(b,1,20).Columns<PanelDisplayLayout.Create(b,1,10).Columns);
    Throws<ArgumentException>(()=>PanelDisplayLayout.Create(b,1,double.NaN));
});
foreach(var h in Enum.GetValues<PanelHorizontalAlignment>())foreach(var v in Enum.GetValues<PanelVerticalAlignment>())
Test($"measured placement honors {h}/{v} without estimating character width", () => {
    var place=Place(new(Horizontal:h,Vertical:v));Check(place.Rows.Length==3);
    for(var i=0;i<3;i++){var r=place.Rows[i];Near(r.X,place.Clip.X+(place.Clip.Width-r.Advance)*(int)h/2);Near(r.Advance,Measure(r.Text));}
    var inkTop=place.Rows[0].Baseline+metrics.Ascent;var inkBottom=place.Rows[^1].Baseline+metrics.Descent;
    if(v==PanelVerticalAlignment.Top)Near(inkTop,place.Clip.Y);
    if(v==PanelVerticalAlignment.Bottom)Near(inkBottom,place.Clip.Y+place.Clip.Height);
    if(v==PanelVerticalAlignment.Center)Near((inkTop+inkBottom)/2,place.Clip.Y+place.Clip.Height/2);
});
Test("oversized rows clip from the left and never have negative placement", () => {
    var p=Place(new(Horizontal:PanelHorizontalAlignment.Right),30);Check(p.Clipped);Near(p.Rows[0].X,p.Clip.X);
});
Test("oversized font metrics fit fewer rows without overlapping the footer", () => {
    var fm=new PanelFontMetrics(-30,10);var p=Place(new(),300,100,fm);
    Check(p.Clipped && p.Rows.Length>0);foreach(var row in p.Rows)Check(row.Baseline+fm.Descent<=p.Clip.Y+p.Clip.Height+1e-9);
});
Test("tiny body safely publishes zero rows", () => {
    var p=Place(new(),10,5);Check(p.Rows.IsEmpty && p.Clipped && p.Clip.Width==0);
});
Test("alignment changes reuse measurements but rebuild positions", () => {
    var cache=new PanelTextPlacementCache();var a=cache.Get(preview,new(),300,220,layout,metrics,1,Measure);
    var b=cache.Get(preview,new(Horizontal:PanelHorizontalAlignment.Right),300,220,layout,metrics,1,_=>throw new Exception("remeasured"));
    Check(cache.MeasurementBuilds==1 && cache.LayoutBuilds==2 && cache.MeasuredRows==3 && a!=b);
});
Test("retained preview movement and warm reads allocate zero", () => {
    var c=new PanelTextPlacementCache();var style=new PanelTextStyle();Func<string,double> measure=Measure;
    var first=c.Get(preview,style,300,220,layout,metrics,1,measure);
    for(var i=0;i<100;i++)c.Get(preview,style,300,220,layout,metrics,1,measure);
    var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<10000;i++)c.Get(preview,style,300,220,layout,metrics,1,measure);
    var bytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(bytes==0 && c.MeasurementBuilds==1 && c.LayoutBuilds==1 && ReferenceEquals(first,c.Current));
    Console.WriteLine($"METRIC typography-placement-10000={bytes} B");
});
Test("changed font identity size or text invalidates measurements", () => {
    var c=new PanelTextPlacementCache();c.Get(preview,new(),300,220,layout,metrics,1,Measure);
    c.Get(preview,new(),300,220,layout,metrics,2,Measure);
    c.Get(preview,new(),300,220,layout with{FontSize=20},metrics,2,Measure);
    c.Get(new(["other"],""),new(),300,220,layout with{FontSize=20},metrics,2,Measure);Check(c.MeasurementBuilds==4);
});
Test("changed footer with identical immutable lines does not rebuild rows", () => {
    var c=new PanelTextPlacementCache();var first=c.Get(preview,new(),300,220,layout,metrics,1,Measure);
    Check(ReferenceEquals(first,c.Get(preview with{Footer="new"},new(),300,220,layout,metrics,1,Measure)));
});
Test("failed measurement leaves previous cache intact and next call can recover", () => {
    var c=new PanelTextPlacementCache();var first=c.Get(preview,new(),300,220,layout,metrics,1,Measure);var next=new PanelPreview(["bad"],"");
    foreach(var value in new[]{-1d,double.NaN,double.PositiveInfinity})Throws<ArgumentException>(()=>c.Get(next,new(),300,220,layout,metrics,1,_=>value));
    Throws<InvalidOperationException>(()=>c.Get(next,new(),300,220,layout,metrics,1,_=>throw new InvalidOperationException()));
    Check(ReferenceEquals(first,c.Current) && c.MeasurementBuilds==1);
    Check(c.Get(next,new(),300,220,layout,metrics,1,Measure).Rows[0].Text=="bad");Check(first.Rows[0].Text=="WWW");
});
Test("invalid bounds metrics layouts and row budgets fail explicitly", () => {
    var c=new PanelTextPlacementCache();
    foreach(var fm in new[]{new PanelFontMetrics(1,2),new(-2,-1),new(0,0),new(double.NaN,2)})Throws<ArgumentException>(()=>c.Get(preview,new(),300,220,layout,fm,1,Measure));
    foreach(var width in new[]{0d,-1,double.NaN,double.PositiveInfinity})Throws<ArgumentException>(()=>c.Get(preview,new(),width,220,layout,metrics,1,Measure));
    Throws<ArgumentException>(()=>c.Get(new(Enumerable.Repeat("a",65).ToImmutableArray(),""),new(),300,220,layout,metrics,1,Measure));
    Throws<ArgumentException>(()=>c.Get(preview,new(),300,220,layout with{LineHeight=double.NaN},metrics,1,Measure));
});
Test("row placement retains complete Unicode strings", () => {
    var strings=ImmutableArray.Create("👩‍💻 e\u0301", "🇵🇱", "日本語");var c=new PanelTextPlacementCache();var p=c.Get(new(strings,""),new(),300,220,layout,metrics,1,_=>30);
    Check(p.Rows.Select(r=>r.Text).SequenceEqual(strings));
});
Test("randomized alignment stays inside measured body intervals", () => {
    var random=new Random(13412);for(var i=0;i<1000;i++){
        var w=100+random.NextDouble()*400;var h=100+random.NextDouble()*300;
        var style=new PanelTextStyle(Horizontal:(PanelHorizontalAlignment)random.Next(3),Vertical:(PanelVerticalAlignment)random.Next(3));
        var p=Place(style,w,h);foreach(var r in p.Rows){Check(r.X>=p.Clip.X-1e-9 && r.X+r.Advance<=p.Clip.X+p.Clip.Width+1e-9);Check(r.Baseline+metrics.Ascent>=p.Clip.Y-1e-9 && r.Baseline+metrics.Descent<=p.Clip.Y+p.Clip.Height+1e-9);}
    }
});
Test("invalid public layout offsets and budgets cannot extend the panel body", () => {
    var c=new PanelTextPlacementCache();var good=c.Get(preview,new(),300,220,layout,metrics,1,Measure);
    foreach(var invalid in new[]{layout with{FooterInset=-100},layout with{FirstBaseline=-1},layout with{Rows=65},layout with{Columns=129},layout with{FontSize=1025}})
        Throws<ArgumentException>(()=>c.Get(preview,new(),300,220,invalid,metrics,1,Measure));
    Check(ReferenceEquals(good,c.Current)&&c.MeasurementBuilds==1);
});
TypographyReviewContracts.Register(tests, registry);
var failed=0;foreach(var(name,run)in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+"\n"+e);}
Console.WriteLine(JsonSerializer.Serialize(new{suite="typography",total=tests.Count,passed=tests.Count-failed,failed}));return failed==0?0:1;
