using System.Collections.Immutable;
using System.Globalization;
using NurbsScene.Geometry;
using NurbsScene.IO;
using NurbsScene.Kernel;
using NurbsScene.Model;
using NurbsScene.Parametric;
using NurbsScene.Rendering;

namespace NurbsScene.Workbench;

/// <summary>UI-independent application engine. Requires the caller's UI synchronization context when bound to controls.</summary>
public sealed class WorkspaceController : IDisposable
{
    private CancellationTokenSource? _solveCancellation;
    private Graph _subscribedGraph;
    private bool _disposed;
    public GeometryFactory Kernel { get; }
    public ComponentRegistry Registry { get; }
    public SceneDocument Document { get; private set; }
    public GraphSession GraphSession { get; }
    public Graph Graph => GraphSession.Graph;
    public DisplayScene Scene { get; } = new();
    public event Action? Changed;
    public event Action<string>? Message;
    public event Action? FitRequested;
    public event Action? WorkspaceReplaced;
    public string PendingCommand { get; private set; } = "";
    private readonly List<Vec3> _commandPoints = [];
    public WorkspaceController(INurbsEvaluator? evaluator=null)
    {
        Kernel = new(evaluator??new ManagedNurbsEvaluator()); Registry=StandardComponents.Create(Kernel);
        Document=new();GraphSession=new(new Graph(Registry));_subscribedGraph=Graph;
        GraphSession.Replaced+=OnGraphReplaced;Document.Changed+=OnDocumentChanged;
        _subscribedGraph.Changed+=OnGraphChanged;_subscribedGraph.Solved+=OnSolved;
    }
    public void Report(string text)=>Message?.Invoke(text);
    private void OnDocumentChanged(DocumentChange change)=>Refresh();
    private void OnGraphChanged(){Changed?.Invoke();_ = ScheduleSolveAsync();}
    private void OnGraphReplaced()
    {
        _subscribedGraph.Changed-=OnGraphChanged;_subscribedGraph.Solved-=OnSolved;_subscribedGraph=Graph;
        _subscribedGraph.Changed+=OnGraphChanged;_subscribedGraph.Solved+=OnSolved;OnGraphChanged();
    }
    private void OnSolved(GraphResult result){if(!result.Stale){Refresh();if(result.ErrorCount>0)Report($"Definition contains {result.ErrorCount} error/blocked components.");}}
    private async Task ScheduleSolveAsync()
    {
        _solveCancellation?.Cancel();var cancellation=new CancellationTokenSource();_solveCancellation=cancellation;
        try{await Task.Delay(20,cancellation.Token);await Graph.SolveAsync(cancellation.Token);}
        catch(OperationCanceledException){}
        catch(Exception error){Report(error.Message);}
        finally{if(ReferenceEquals(_solveCancellation,cancellation))_solveCancellation=null;cancellation.Dispose();}
    }
    public Task<GraphResult> SolveAsync(CancellationToken cancellationToken=default)=>Graph.SolveAsync(cancellationToken);
    public void Refresh()
    {
        if(_disposed)return;
        try
        {
            var items=new List<DisplayItem>();
            foreach(var o in Document.VisibleObjects){var color=Document.ObjectColor(o);items.Add(new(o.Id,o.Geometry,new(color.R,color.G,color.B,color.A),Document.Selection.Contains(o.Id),o.Locked||Document.GetLayer(o.LayerId).Locked));}
            if(Graph.LastResult is{} result)
                foreach(var node in Graph.Nodes.Where(n=>n.Preview&&n.Enabled))
                    if(result.Nodes.TryGetValue(node.Id,out var value)&&value.Status=="ok")
                        foreach(var (port,tree) in value.Outputs)
                        {
                            var index=0;foreach(var geometry in tree.AllItems.OfType<IGeometry>())items.Add(new($"preview:{node.Id}:{port}:{index++}",geometry,new(.65f,.26f,.19f,.85f),false,true));
                        }
            Scene.SetItems(items);
        }
        catch(Exception error){Report($"Display preparation failed: {error.Message}");}
        Changed?.Invoke();
    }
    public void Load(string json)
    {
        var next=WorkspaceFormat.Deserialize(Kernel.Evaluator,Registry,json);
        _solveCancellation?.Cancel();Document.Changed-=OnDocumentChanged;Document=next.Document;Document.Changed+=OnDocumentChanged;
        GraphSession.Replace(next.Graph);CancelCommand();Refresh();WorkspaceReplaced?.Invoke();Report("Exact managed workspace loaded.");
    }
    public string Save()=>WorkspaceFormat.Serialize(Document,Graph);
    public void New()
    {
        _solveCancellation?.Cancel();Document.Changed-=OnDocumentChanged;Document=new();Document.Changed+=OnDocumentChanged;
        GraphSession.Replace(new(Registry));CancelCommand();Refresh();WorkspaceReplaced?.Invoke();
    }
    public void LoadPavilion()
    {
        New();Document.Name="Parametric pavilion";
        Document.Transaction("Create pavilion",()=>
        {
            var structure=Document.AddLayer("Structure",new(.47f,.58f,.56f),"structure");var curves=Document.AddLayer("Construction",new(.34f,.43f,.40f),"construction");
            foreach(var x in new[]{-24d,24d})foreach(var y in new[]{-17d,17d})Document.Add(Kernel.Extrude(Kernel.Circle(new(x,y,0),1.3),new(0,0,14)),"Column",structure.Id);
            Document.Add(Kernel.Polyline([new(-30,-21,0),new(30,-21,0),new(30,21,0),new(-30,21,0),new(-30,-21,0)]),"Footprint",curves.Id);
        });
        GraphSession.Edit("Create pavilion definition",graph=>
        {
            var width=graph.Add("Number",35,55,new Dictionary<string,object?>{{"value",60d},{"min",20d},{"max",100d}},"Width");
            var depth=graph.Add("Number",35,160,new Dictionary<string,object?>{{"value",42d},{"min",15d},{"max",80d}},"Depth");
            var height=graph.Add("Number",35,265,new Dictionary<string,object?>{{"value",12d},{"min",0d},{"max",30d}},"Rise");
            var surface=graph.Add("SaddleSurface",280,110,name:"Canopy");
            graph.Connect(new(width.Id,"N"),new(surface.Id,"Width"));graph.Connect(new(depth.Id,"N"),new(surface.Id,"Depth"));graph.Connect(new(height.Id,"N"),new(surface.Id,"Height"));
            var range=graph.Add("Range",285,310,new Dictionary<string,object?>{{"input:N",10d}});
            var iso=graph.Add("IsoCurve",540,140,name:"Canopy ribs");graph.Connect(new(surface.Id,"S"),new(iso.Id,"S"));graph.Connect(new(range.Id,"R"),new(iso.Id,"T"));
            graph.Add("Text",535,315,new Dictionary<string,object?>{{"text","Exact NURBS + reusable graph"}});
        });
        Refresh();FitRequested?.Invoke();
    }
    public int Bake(IEnumerable<string> selectedNodes)
    {
        var ids=selectedNodes.ToHashSet();var result=Graph.LastResult??throw new InvalidOperationException("Solve the definition before baking.");
        if(result.Stale||result.Revision!=Graph.Revision)throw new InvalidOperationException("The current definition has not finished solving.");
        var geometry=Graph.Nodes.Where(n=>ids.Contains(n.Id)).SelectMany(n=>result.Nodes[n.Id].Outputs.Values.SelectMany(t=>t.AllItems).OfType<IGeometry>().Select(g=>(Node:n,Geometry:g))).ToArray();
        Document.Transaction("Bake definition geometry",()=>{foreach(var (node,g) in geometry)Document.Add(g,node.Name??node.Component,source:node.Id);});Report($"Baked {geometry.Length} exact geometries.");return geometry.Length;
    }
    public void CancelCommand(){PendingCommand="";_commandPoints.Clear();}
    public void AcceptPoint(Vec3 point)
    {
        if(PendingCommand!="Line")return;_commandPoints.Add(point);
        if(_commandPoints.Count==1){Report("Line: pick the end point.");return;}
        var curve=Kernel.Line(_commandPoints[0],_commandPoints[1]);Document.Transaction("Line",()=>Document.Add(curve,"Line"));CancelCommand();
    }
    private static double Number(string value)=>double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:throw new ArgumentException($"Not a finite number: {value}.");
    private static Vec3 Point(string value){var a=value.Split(',').Select(Number).ToArray();return a.Length==3?new(a[0],a[1],a[2]):throw new ArgumentException("Use an x,y,z coordinate triple.");}
    private SceneObject[] Selected()=>Document.Selection.Select(Document.GetObject).ToArray();
    public void Execute(string input)
    {
        var tokens=input.Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries);if(tokens.Length==0)return;var command=tokens[0].TrimStart('_').ToLowerInvariant();var a=tokens.Skip(1).ToArray();
        Report($"> {input}");
        if(command=="undo"){Document.Undo();return;}if(command=="redo"){Document.Redo();return;}if(command=="cancel"){CancelCommand();return;}
        if(command=="selall"){Document.Select(Document.VisibleObjects.Where(Document.CanEdit).Select(o=>o.Id));return;}if(command=="selnone"){Document.Select([]);return;}
        if(command is "zoomextents" or "fit"){FitRequested?.Invoke();return;}
        if(command is "shaded" or "wireframe" or "ghosted"){Scene.Mode=command=="shaded"?DisplayMode.Shaded:command=="wireframe"?DisplayMode.Wireframe:DisplayMode.Ghosted;Changed?.Invoke();return;}
        if(command=="line"&&a.Length==0){PendingCommand="Line";_commandPoints.Clear();Report("Line: pick the start point in a viewport.");return;}
        Document.Transaction(tokens[0],()=>
        {
            switch(command)
            {
                case "line":if(a.Length!=2)throw new ArgumentException("Line x,y,z x,y,z");Document.Add(Kernel.Line(Point(a[0]),Point(a[1])),"Line");break;
                case "circle":if(a.Length!=2)throw new ArgumentException("Circle centerX,centerY,centerZ radius");Document.Add(Kernel.Circle(Point(a[0]),Number(a[1])),"Circle");break;
                case "interpolate":if(a.Length<2)throw new ArgumentException("Interpolate x,y,z x,y,z ...");Document.Add(Kernel.Interpolate(a.Select(Point).ToArray(),Math.Min(3,a.Length-1)),"Interpolated curve");break;
                case "delete":Document.Delete(Document.Selection);break;
                case "move":case "copy":if(a.Length!=1)throw new ArgumentException("Move/Copy dx,dy,dz");Document.Transform(Document.Selection,Transform3.Translation(Point(a[0])),command=="copy");break;
                case "scale":if(a.Length!=1)throw new ArgumentException("Scale factor");var factor=Number(a[0]);if(factor==0)throw new ArgumentException("Scale must be nonzero.");Document.Transform(Document.Selection,Transform3.Scale(new(factor,factor,factor)));break;
                case "rotate":if(a.Length!=1)throw new ArgumentException("Rotate degrees (world Z axis)");Document.Transform(Document.Selection,Transform3.Rotation(Vec3.UnitZ,Number(a[0])*Math.PI/180));break;
                case "extrude":if(a.Length!=1)throw new ArgumentException("Extrude dx,dy,dz");foreach(var selected in Selected()){if(selected.Geometry is not NurbsCurve c)throw new ArgumentException("Extrude requires selected curves.");Document.Add(Kernel.Extrude(c,Point(a[0])),"Extrusion",selected.LayerId);}break;
                case "revolve":if(a.Length!=3)throw new ArgumentException("Revolve axisOrigin axisVector sweepDegrees");foreach(var selected in Selected()){if(selected.Geometry is not NurbsCurve c)throw new ArgumentException("Revolve requires selected curves.");Document.Add(Kernel.Revolve(c,Point(a[0]),Point(a[1]),Number(a[2])*Math.PI/180),"Revolution",selected.LayerId);}break;
                case "loft":var curves=Selected().Select(o=>o.Geometry is NurbsCurve c?c:throw new ArgumentException("Loft requires selected curves.")).ToArray();Document.Add(Kernel.Loft(curves),"Loft");break;
                case "reverse":foreach(var selected in Selected()){if(selected.Geometry is not NurbsCurve c)throw new ArgumentException("Reverse requires selected curves.");Document.Update(selected with{Geometry=c.Reverse()});}break;
                case "insertknot":if(a.Length!=1)throw new ArgumentException("InsertKnot parameter");foreach(var selected in Selected()){if(selected.Geometry is not NurbsCurve c)throw new ArgumentException("Select curves.");Document.Update(selected with{Geometry=c.InsertKnot(Number(a[0]))});}break;
                case "elevatedegree":foreach(var selected in Selected()){if(selected.Geometry is not NurbsCurve c)throw new ArgumentException("Select curves.");Document.Update(selected with{Geometry=c.ElevateDegree(1)});}break;
                case "saddle":Document.Add(Kernel.Saddle(60,42,12),"Bicubic surface");break;
                default:throw new NotSupportedException($"Command {tokens[0]} is not implemented. No approximate replacement geometry has been created.");
            }
        });
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_solveCancellation?.Cancel();Document.Changed-=OnDocumentChanged;GraphSession.Replaced-=OnGraphReplaced;_subscribedGraph.Changed-=OnGraphChanged;_subscribedGraph.Solved-=OnSolved;}
}
