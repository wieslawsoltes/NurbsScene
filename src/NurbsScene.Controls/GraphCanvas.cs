using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using PortHit = NurbsScene.Presentation.GraphPortHit;
using NurbsScene.Controls.Drawing;
using Windows.Foundation;
using Windows.System;

namespace NurbsScene.Controls;

/// <summary>Reusable vector-drawn visual editor with typed port wiring, graph history, numeric scrubbing, culling and zoom LOD.</summary>
public sealed partial class GraphCanvas : WebGpuVectorCanvasElement,IDisposable
{
    private GraphSession? _session;private Graph? _graph;private bool _subscribed;
    private readonly HashSet<string> _selection=[];
    private readonly VectorFont _font=new(VectorTypeface.Default,12);
    private readonly VectorPaint _paint=new(){IsAntialias=true};
    private float _zoom=.8f,_panX=20,_panY=20;
    private Point _start,_last,_worldStart,_worldLast;
    private string _gesture="";private string? _slider;
    private PortHit? _wire;
    private GraphSnapshot? _before;
    private bool _moved,_rightPan;
    private readonly GraphPresentation _presentation=new();
    private GraphDrawing? _drawing;
    private readonly Dictionary<string,PresentedNode> _layouts=[];
    private readonly List<VectorPath> _paths=[];
    private readonly Dictionary<string,(PresentedWire Wire,VectorPath Path)> _wirePaths=[];
    private readonly VectorPathEffect _treeDash=VectorPathEffect.CreateDash([5,4],0);
    public long LayoutBuilds=>_presentation.BuildCount;
    public long PathBuilds {get;private set;}
    public long WireCachePasses {get;private set;}
    public float Zoom=>_zoom;
    public event Action? ViewChanged;
    public void CancelInteraction(){CancelGesture();ReleasePointerCaptures();}
    public void SetZoom(float zoom)
    {
        if(!float.IsFinite(zoom))throw new ArgumentException("Finite zoom required.");
        CancelGesture();ReleasePointerCaptures();
        ApplyNavigation(GraphCanvasNavigation.ZoomAt(NavigationView,new(ActualWidth/2,ActualHeight/2),zoom));
    }
    public CanvasPoint ScreenPoint(string nodeId,bool output,int index)
    {
        Drawing();var n=_layouts[nodeId];var p=CanvasPort(n,output,index);return new(p.X*_zoom+_panX,p.Y*_zoom+_panY);
    }
    private GraphDrawing Drawing()
    {
        var next=_presentation.Get(_session!.Graph);
        if(!ReferenceEquals(next,_drawing))
        {
            _layouts.Clear();
            foreach(var node in next.Nodes)_layouts[node.Node.Id]=node;
            // Parameter-only changes retain the complete immutable wire array.
            // Skip the entire native-cache reconciliation, not just path creation.
            if(_drawing is null||next.Wires!=_drawing.Wires)
            {
                WireCachePasses++;_paths.Clear();
                var live=new HashSet<string>();
                foreach(var wire in next.Wires)
                {
                    live.Add(wire.Wire.Id);
                    if(!_wirePaths.TryGetValue(wire.Wire.Id,out var cached)||(cached.Wire.Start!=wire.Start||cached.Wire.End!=wire.End))
                    {
                        cached.Path?.Dispose();
                        cached=(wire,MakePath(new(wire.Start.X,wire.Start.Y),new(wire.End.X,wire.End.Y)));
                        PathBuilds++;
                    }
                    _wirePaths[wire.Wire.Id]=(wire,cached.Path);_paths.Add(cached.Path);
                }
                foreach(var id in _wirePaths.Keys.Where(id=>!live.Contains(id)).ToArray())
                {_wirePaths[id].Path.Dispose();_wirePaths.Remove(id);}
            }
            if(_panelDisplayLayouts.Count>0)
                foreach(var id in _panelDisplayLayouts.Keys.Where(id=>!_layouts.TryGetValue(id,out var live)||live.Node.Component is not ("Panel" or "Text")).ToArray())
                {ForgetPanelPresentation(id);}
            _drawing=next;
        }
        return next;
    }
    public GraphSession? Session
    {
        get=>_session;
        set{ObjectDisposedException.ThrowIf(_canvasDisposed,this);CancelGesture();ReleasePointerCaptures();Unsubscribe();ClearPanelPresentation();_session=value;Subscribe();_selection.Clear();SelectionChanged?.Invoke();Invalidate();}
    }
    public IReadOnlySet<string> Selection=>new HashSet<string>(_selection);
    public int SelectionCount=>_selection.Count;
    public bool IsInteracting=>_gesture!="";
    public bool IsSelected(string id)=>_selection.Contains(id);
    public event Action? SelectionChanged;
    public event Action<string>? Message;
    public event Action? SearchRequested;
    public event Action<Point>? ContextMenuRequested;
    public CanvasPoint InsertionPoint=>_worldLast==default?ViewCenter:new(_worldLast.X,_worldLast.Y);
    public CanvasPoint ViewCenter {get{var p=World(new(ActualWidth/2,ActualHeight/2));return new(p.X,p.Y);}}
    public void SelectNodes(IEnumerable<string> ids)
    {
        if(_session is null)return;
        var selected=ids.Take(10_001).ToArray();if(selected.Length>10_000)throw new ArgumentException("Selection budget exceeded.");
        foreach(var id in selected)_session.Graph.GetNode(id);
        _selection.Clear();_selection.UnionWith(selected);SelectionChanged?.Invoke();Invalidate();
    }
    public GraphCanvas()
    {
        IsTabStop=true;
        Loaded+=(_,_)=>Subscribe();Unloaded+=(_,_)=>{CancelGesture();ReleasePointerCaptures();Unsubscribe();ClearPanelPresentation();};SizeChanged+=(_,_)=>Invalidate();
        InitializePointerInput();
        RightTapped+=(_,e)=>e.Handled=true;
        PointerWheelChanged+=(_,e)=>
        {
            e.Handled=true;if(_activePointerId is not null||TryPanelWheel(e))return;
            var point=e.GetCurrentPoint(this);
            try { ApplyNavigation(GraphCanvasNavigation.ZoomBy(NavigationView,new(point.Position.X,point.Position.Y),point.Properties.MouseWheelDelta*.001)); }
            catch(ArgumentOutOfRangeException error) { Message?.Invoke(error.Message); }
        };
        DoubleTapped+=OnCanvasDoubleTapped;
        KeyDown+=(_,e)=>{if(TryPanelScrollKey(e.Key)){e.Handled=true;}else if(e.Key==VirtualKey.Delete){DeleteSelected();e.Handled=true;}else if(e.Key==VirtualKey.Escape){CancelGesture();ReleasePointerCaptures();e.Handled=true;}else if(e.Key==VirtualKey.F){Fit();e.Handled=true;}else if(e.Key==VirtualKey.Space){SearchRequested?.Invoke();e.Handled=true;}};
    }
    private void Subscribe(){if(_canvasDisposed||_subscribed||_session is null)return;_subscribed=true;_session.Replaced+=Replaced;_graph=_session.Graph;_graph.Changed+=GraphChanged;_graph.Solved+=Solved;}
    private void Unsubscribe(){if(!_subscribed)return;_subscribed=false;if(_session is not null)_session.Replaced-=Replaced;if(_graph is not null){_graph.Changed-=GraphChanged;_graph.Solved-=Solved;}_graph=null;}
    private void Replaced(){ClearPanelPresentation();ClearPanelResize();_before=null;_gesture="";_wire=null;_slider=null;ClearPointerDrafts();ReleasePointerCaptures();if(_graph is not null){_graph.Changed-=GraphChanged;_graph.Solved-=Solved;}_graph=_session!.Graph;_graph.Changed+=GraphChanged;_graph.Solved+=Solved;_selection.IntersectWith(_graph.Nodes.Select(n=>n.Id));SelectionChanged?.Invoke();Invalidate();}
    private void GraphChanged()
    {
        CancelStalePanelScroll();
        if ((_gesture is "nodes" or "slider") && !_applyingPointerEdit)
            AbandonStalePointerEdit();
        // Dropping a stale resize draft never rolls back independent host edits.
        if(_session is not null&&_panelResize is{} resize&&!resize.IsCurrent(_session.Graph))
        {ClearPanelResize();ClearPointerDrafts();_gesture="";ReleasePointerCaptures();}
        // A deleted wire source must not be dereferenced by the next paint.
        if(_session is not null&&_wire is{} pending&&!_session.Graph.TryGetNode(pending.Node,out _))
        {CancelGesture();ReleasePointerCaptures();}
        if(_session is not null&&_selection.Count>0){var count=_selection.Count;_selection.IntersectWith(Drawing().Nodes.Select(n=>n.Node.Id));if(count!=_selection.Count)SelectionChanged?.Invoke();}
        Invalidate();
    }
    private void Solved(GraphResult result){CancelStalePanelScroll();Invalidate();}
    private Point World(Point point)=>new((point.X-_panX)/_zoom,(point.Y-_panY)/_zoom);
    private PresentedNode Layout(GraphNode node){Drawing();return _layouts[node.Id];}
    private VectorRect Rect(GraphNode node){var b=Layout(node).Bounds;return new((float)b.X,(float)b.Y,(float)(b.X+b.Width),(float)(b.Y+b.Height));}
    private Point PortPoint(GraphNode node,bool output,int index){var p=Layout(node).Port(output,index);return new(p.X,p.Y);}
    private Point PortPoint(PortHit port)
    {
        Drawing();var n=_layouts[port.Node];var ports=port.Output?n.Definition.Outputs:n.Definition.Inputs;
        for(var i=0;i<ports.Length;i++)if(ports[i].Name==port.Port){var p=n.Port(port.Output,i);return new(p.X,p.Y);}
        throw new ArgumentException("Missing port.");
    }
    private GraphNode? NodeAt(Point point)
    {
        if(_session is null)return null;var nodes=Drawing().Nodes;
        for(var i=nodes.Length-1;i>=0;i--)if(nodes[i].Bounds.Contains(new(point.X,point.Y)))return nodes[i].Node;return null;
    }
    private PortHit? PortAt(Point point)
        =>_session is null?null:GraphHitTesting.PortAt(Drawing(),new(point.X,point.Y),9/_zoom);
    private void Fill(VectorCanvas canvas,VectorRect rect,VectorColor color,float radius=0){_paint.Style=VectorPaintStyle.Fill;_paint.Color=color;if(radius>0)canvas.DrawRoundRect(rect,radius,radius,_paint);else canvas.DrawRect(rect,_paint);}
    private void Stroke(VectorCanvas canvas,VectorRect rect,VectorColor color,float width=1,float radius=0){_paint.Style=VectorPaintStyle.Stroke;_paint.StrokeWidth=width;_paint.Color=color;if(radius>0)canvas.DrawRoundRect(rect,radius,radius,_paint);else canvas.DrawRect(rect,_paint);}
    private void Text(VectorCanvas canvas,string text,float x,float y,VectorColor color,float size=12){_font.Size=size;_paint.Style=VectorPaintStyle.Fill;_paint.Color=color;canvas.DrawText(text,x,y,_font,_paint);}
    private static VectorPath MakePath(Point a,Point b)
    {
        var path=new VectorPath();var distance=(float)Math.Max(35,Math.Abs(b.X-a.X)*.48);path.MoveTo((float)a.X,(float)a.Y);path.CubicTo((float)a.X+distance,(float)a.Y,(float)b.X-distance,(float)b.Y,(float)b.X,(float)b.Y);return path;
    }
    private void Wire(VectorCanvas canvas,Point a,Point b,VectorColor color,float thickness=2)
    {
        using var path=MakePath(a,b);_paint.Style=VectorPaintStyle.Stroke;_paint.StrokeWidth=thickness;_paint.Color=color;canvas.DrawPath(path,_paint);
    }
    protected override void Draw(VectorCanvas canvas,Size area)
    {
        canvas.Save();canvas.ClipRect(new(0,0,(float)area.Width,(float)area.Height));Fill(canvas,new(0,0,(float)area.Width,(float)area.Height),CanvasPaper);
        // Draw in screen coordinates using bounded integer indices. Adding a
        // 25-unit step to a float around 1e9 does not advance that float at all.
        if(GridVisible&&CanvasGridLayout.TryCreate(area.Width,area.Height,_zoom,_panX,_panY,out var grid))
        {
            _paint.Style=VectorPaintStyle.Stroke;_paint.StrokeWidth=.5f;_paint.Color=new(215,212,203);
            for(var i=0;i<grid.Columns;i++){var x=(float)(grid.OffsetX+i*grid.Spacing);canvas.DrawLine(x,0,x,(float)area.Height,_paint);}
            for(var i=0;i<grid.Rows;i++){var y=(float)(grid.OffsetY+i*grid.Spacing);canvas.DrawLine(0,y,(float)area.Width,y,_paint);}
        }
        canvas.Translate(_panX,_panY);canvas.Scale(_zoom);
        var bounds=new VectorRect(-_panX/_zoom,-_panY/_zoom,((float)area.Width-_panX)/_zoom,((float)area.Height-_panY)/_zoom);
        var view=new CanvasBox(bounds.Left,bounds.Top,bounds.Width,bounds.Height);
        if(_session is not null)
        {
            var graph=_session.Graph;var drawing=Drawing();
            for(var i=0;i<drawing.Wires.Length;i++)
            {
                var w=drawing.Wires[i];if(!WireCanvasBounds(w).Intersects(view))continue;
                var wire=w.Wire;var selected=_selection.Contains(wire.From.Node)||_selection.Contains(wire.To.Node);
                var tree=graph.LastResult?.Nodes.GetValueOrDefault(wire.From.Node)?.Outputs.GetValueOrDefault(wire.From.Port);
                var list=FancyWires&&tree?.ItemCount>1;var branches=FancyWires&&tree?.Branches.Length>1;
                _paint.Style=VectorPaintStyle.Stroke;_paint.StrokeWidth=list?3.2f:1.25f;_paint.Color=selected?new(101,153,39):new(91,91,82);
                _paint.PathEffect=branches?_treeDash:null;DrawWirePath(canvas,w,_paths[i]);_paint.PathEffect=null;
                if(list&&!branches)
                {
                    _paint.StrokeWidth=1.25f;_paint.Color=selected?new(193,218,153):CanvasPaper;
                    DrawWirePath(canvas,w,_paths[i]);
                }
                if(_zoom>.35f&&wire.Transform!="none")
                {
                    var tag=wire.Transform switch{"flatten"=>"F","graft"=>"G","simplify"=>"S",_=>"?"};
                    var endpoint=WireEnd(w);var x=(float)endpoint.X-24;var y=(float)endpoint.Y;
                    Fill(canvas,new(x-5,y-7,x+5,y+6),new(249,239,186),2);
                    Text(canvas,tag,x-3,y+3,new(68,65,46),9);
                }
            }
            foreach(var n in drawing.Nodes)
            {
                var b=CanvasBounds(n);if(!b.Intersects(view))continue;
                var node=n.Node;var rect=new VectorRect((float)b.X,(float)b.Y,(float)(b.X+b.Width),(float)(b.Y+b.Height));
                var selected=_selection.Contains(node.Id);var result=graph.LastResult?.Nodes.GetValueOrDefault(node.Id);
                DrawCapsule(canvas,n,rect,result,selected);
                if(_zoom>.35f||PanelLayout.CanResize(node))
                {
                    canvas.Save();canvas.ClipRect(new(rect.Left+5,rect.Top+2,rect.Right-5,rect.Bottom-2));
                    if(n.Slider)
                    {
                        DrawNumberSlider(canvas,node,rect);
                    }
                    else if(node.Component=="Boolean")DrawBooleanToggle(canvas,node,rect);
                    else if(node.Component is "Text" or "Panel")DrawDataPanel(canvas,node,rect,result);
                    else DrawComponentBody(canvas,n,rect,selected);
                    canvas.Restore();
                    if(PanelLayout.CanResize(node))DrawResizeHandles(canvas,rect,selected);
                    for(var side=0;side<2;side++){var ports=side==1?n.Definition.Outputs:n.Definition.Inputs;for(var i=0;i<ports.Length;i++){var p=CanvasPort(n,side==1,i);_paint.Style=VectorPaintStyle.Fill;_paint.Color=selected?new(203,228,156):new(240,240,235);canvas.DrawCircle((float)p.X,(float)p.Y,3,_paint);_paint.Style=VectorPaintStyle.Stroke;_paint.Color=selected?new(67,103,29):new(79,81,73);_paint.StrokeWidth=1;canvas.DrawCircle((float)p.X,(float)p.Y,3,_paint);}}
                }
            }
            if(_wire is{} pending){var anchor=PortPoint(pending);Wire(canvas,pending.Output?anchor:_worldLast,pending.Output?_worldLast:anchor,new(110,155,35),2.2f);}
            DrawSelectionFeedback(canvas);
        }
        canvas.Restore();
    }
    private static double Parameter(GraphNode node,string name,double fallback)=>node.Parameters.TryGetValue(name,out var value)?Convert.ToDouble(value,CultureInfo.InvariantCulture):fallback;
    private void Select(string id,bool add){if(!add)_selection.Clear();_selection.Add(id);SelectionChanged?.Invoke();Invalidate();}
    public void SelectNode(string id)=>Select(id,false);
    public void AddComponent(string component)
    {
        if(_session is null)return;var at=InsertionPoint;
        _session.Edit("Add component",graph=>{var node=graph.Add(component,at.X,at.Y);Select(node.Id,false);});
    }
    public void DeleteSelected(){if(_session is null||_selection.Count==0)return;CancelGesture();ReleasePointerCaptures();try{var ids=_selection.ToArray();_session.Edit("Delete components",graph=>{foreach(var id in ids)graph.Remove(id);});_selection.Clear();SelectionChanged?.Invoke();}catch(Exception error){Message?.Invoke(error.Message);}}
    public void Fit()
    {
        CancelGesture();ReleasePointerCaptures();
        if(_session is null||_session.Graph.Nodes.Count==0)return;var rects=_session.Graph.Nodes.Select(Rect).ToArray();var minX=rects.Min(r=>r.Left);var minY=rects.Min(r=>r.Top);var maxX=rects.Max(r=>r.Right);var maxY=rects.Max(r=>r.Bottom);
        var zoom=Math.Clamp((float)Math.Min(Math.Max(1,ActualWidth-60)/(maxX-minX),Math.Max(1,ActualHeight-60)/(maxY-minY)),.15f,1.5f);
        var panX=(float)ActualWidth/2-(minX/2+maxX/2)*zoom;var panY=(float)ActualHeight/2-(minY/2+maxY/2)*zoom;
        if(!float.IsFinite(zoom)||!float.IsFinite(panX)||!float.IsFinite(panY)){Message?.Invoke("Definition coordinates exceed the canvas rendering range. The previous view was retained.");return;}
        _zoom=zoom;_panX=panX;_panY=panY;ViewChanged?.Invoke();Invalidate();
    }
    private bool _canvasDisposed;
    public new void Dispose(){if(_canvasDisposed)return;_canvasDisposed=true;CancelGesture();ReleasePointerCaptures();ClearPanelPresentation();_panelTypography.Dispose();ClearPanelResize();Unsubscribe();foreach(var path in _wirePaths.Values)path.Path.Dispose();_wirePaths.Clear();_paths.Clear();_treeDash.Dispose();_font.Dispose();_paint.Dispose();ReleaseDrawingResources();base.Dispose();}
}
