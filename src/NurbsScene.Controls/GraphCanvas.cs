using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
using Windows.System;

namespace NurbsScene.Controls;

/// <summary>Reusable vector-drawn visual editor with typed port wiring, graph history, numeric scrubbing, culling and zoom LOD.</summary>
public sealed class GraphCanvas : SKCanvasElement,IDisposable
{
    private GraphSession? _session;private Graph? _graph;private bool _subscribed;
    private readonly HashSet<string> _selection=[];
    private readonly SKFont _font=new(SKTypeface.Default,12);
    private readonly SKPaint _paint=new(){IsAntialias=true};
    private float _zoom=.8f,_panX=20,_panY=20;
    private Point _start,_last,_worldStart,_worldLast;
    private string _gesture="";private string? _slider;
    private PortHit? _wire;
    private GraphSnapshot? _before;
    private readonly Dictionary<string,(double X,double Y)> _starts=[];
    private bool _moved;
    private readonly GraphPresentation _presentation=new();
    private GraphDrawing? _drawing;
    private readonly Dictionary<string,PresentedNode> _layouts=[];
    private readonly List<SKPath> _paths=[];
    private readonly SKPathEffect _treeDash=SKPathEffect.CreateDash([5,4],0);
    public long LayoutBuilds=>_presentation.BuildCount;
    public long PathBuilds {get;private set;}
    public float Zoom=>_zoom;
    public event Action? ViewChanged;
    public void SetZoom(float zoom)
    {
        if(!float.IsFinite(zoom))throw new ArgumentException("Finite zoom required.");
        var center=new Point(ActualWidth/2,ActualHeight/2);var world=World(center);_zoom=Math.Clamp(zoom,.15f,3.5f);
        _panX=(float)(center.X-world.X*_zoom);_panY=(float)(center.Y-world.Y*_zoom);ViewChanged?.Invoke();Invalidate();
    }
    public CanvasPoint ScreenPoint(string nodeId,bool output,int index)
    {
        var n=Drawing().Nodes.Single(n=>n.Node.Id==nodeId);var p=n.Port(output,index);return new(p.X*_zoom+_panX,p.Y*_zoom+_panY);
    }
    private GraphDrawing Drawing()
    {
        var next=_presentation.Get(_session!.Graph);
        if(!ReferenceEquals(next,_drawing))
        {
            foreach(var path in _paths)path.Dispose();_paths.Clear();_layouts.Clear();
            foreach(var node in next.Nodes)_layouts[node.Node.Id]=node;
            foreach(var wire in next.Wires){_paths.Add(MakePath(new(wire.Start.X,wire.Start.Y),new(wire.End.X,wire.End.Y)));PathBuilds++;}
            _drawing=next;
        }
        return next;
    }
    public GraphSession? Session
    {
        get=>_session;
        set{Unsubscribe();_session=value;Subscribe();_selection.Clear();SelectionChanged?.Invoke();Invalidate();}
    }
    public IReadOnlySet<string> Selection=>new HashSet<string>(_selection);
    public event Action? SelectionChanged;
    public event Action<string>? Message;
    public event Action? SearchRequested;
    private sealed record PortHit(string Node,string Port,bool Output);
    public GraphCanvas()
    {
        IsTabStop=true;
        Loaded+=(_,_)=>Subscribe();Unloaded+=(_,_)=>Unsubscribe();SizeChanged+=(_,_)=>Invalidate();
        PointerPressed+=Pressed;PointerMoved+=Moved;PointerReleased+=Released;
        PointerCanceled+=(_,e)=>{CancelGesture();ReleasePointerCapture(e.Pointer);};
        PointerWheelChanged+=(_,e)=>{var point=e.GetCurrentPoint(this);var world=World(point.Position);_zoom=Math.Clamp(_zoom*(float)Math.Exp(point.Properties.MouseWheelDelta*.001),.15f,3.5f);_panX=(float)point.Position.X-(float)world.X*_zoom;_panY=(float)point.Position.Y-(float)world.Y*_zoom;ViewChanged?.Invoke();Invalidate();e.Handled=true;};
        DoubleTapped+=(_,e)=>{_worldLast=World(e.GetPosition(this));if(NodeAt(_worldLast) is null)SearchRequested?.Invoke();e.Handled=true;};
        KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Delete){DeleteSelected();e.Handled=true;}else if(e.Key==VirtualKey.Escape){CancelGesture();e.Handled=true;}else if(e.Key==VirtualKey.F){Fit();e.Handled=true;}else if(e.Key==VirtualKey.Space){SearchRequested?.Invoke();e.Handled=true;}};
    }
    private void Subscribe(){if(_subscribed||_session is null)return;_subscribed=true;_session.Replaced+=Replaced;_graph=_session.Graph;_graph.Changed+=GraphChanged;_graph.Solved+=Solved;}
    private void Unsubscribe(){if(!_subscribed)return;_subscribed=false;if(_session is not null)_session.Replaced-=Replaced;if(_graph is not null){_graph.Changed-=GraphChanged;_graph.Solved-=Solved;}_graph=null;}
    private void Replaced(){if(_graph is not null){_graph.Changed-=GraphChanged;_graph.Solved-=Solved;}_graph=_session!.Graph;_graph.Changed+=GraphChanged;_graph.Solved+=Solved;_selection.IntersectWith(_graph.Nodes.Select(n=>n.Id));SelectionChanged?.Invoke();Invalidate();}
    private void GraphChanged()
    {
        if(_session is not null&&_selection.Count>0){var count=_selection.Count;_selection.IntersectWith(Drawing().Nodes.Select(n=>n.Node.Id));if(count!=_selection.Count)SelectionChanged?.Invoke();}
        Invalidate();
    }
    private void Solved(GraphResult result)=>Invalidate();
    private Point World(Point point)=>new((point.X-_panX)/_zoom,(point.Y-_panY)/_zoom);
    private PresentedNode Layout(GraphNode node){Drawing();return _layouts[node.Id];}
    private SKRect Rect(GraphNode node){var b=Layout(node).Bounds;return new((float)b.X,(float)b.Y,(float)(b.X+b.Width),(float)(b.Y+b.Height));}
    private Point PortPoint(GraphNode node,bool output,int index){var p=Layout(node).Port(output,index);return new(p.X,p.Y);}
    private Point PortPoint(PortHit port)
    {
        var n=Drawing().Nodes.First(n=>n.Node.Id==port.Node);var ports=port.Output?n.Definition.Outputs:n.Definition.Inputs;
        for(var i=0;i<ports.Length;i++)if(ports[i].Name==port.Port){var p=n.Port(port.Output,i);return new(p.X,p.Y);}
        throw new ArgumentException("Missing port.");
    }
    private GraphNode? NodeAt(Point point)
    {
        if(_session is null)return null;var nodes=Drawing().Nodes;
        for(var i=nodes.Length-1;i>=0;i--)if(nodes[i].Bounds.Contains(new(point.X,point.Y)))return nodes[i].Node;return null;
    }
    private PortHit? PortAt(Point point)
    {
        if(_session is null)return null;var tolerance=9/_zoom;var nodes=Drawing().Nodes;
        for(var n=nodes.Length-1;n>=0;n--)
        {
            var node=nodes[n];
            for(var side=0;side<2;side++){var ports=side==1?node.Definition.Outputs:node.Definition.Inputs;for(var i=0;i<ports.Length;i++){var p=node.Port(side==1,i);if(Math.Abs(p.X-point.X)<=tolerance&&Math.Abs(p.Y-point.Y)<=tolerance)return new(node.Node.Id,ports[i].Name,side==1);}}
        }
        return null;
    }
    private void Fill(SKCanvas canvas,SKRect rect,SKColor color,float radius=0){_paint.Style=SKPaintStyle.Fill;_paint.Color=color;if(radius>0)canvas.DrawRoundRect(rect,radius,radius,_paint);else canvas.DrawRect(rect,_paint);}
    private void Stroke(SKCanvas canvas,SKRect rect,SKColor color,float width=1,float radius=0){_paint.Style=SKPaintStyle.Stroke;_paint.StrokeWidth=width;_paint.Color=color;if(radius>0)canvas.DrawRoundRect(rect,radius,radius,_paint);else canvas.DrawRect(rect,_paint);}
    private void Text(SKCanvas canvas,string text,float x,float y,SKColor color,float size=12){_font.Size=size;_paint.Style=SKPaintStyle.Fill;_paint.Color=color;canvas.DrawText(text,x,y,_font,_paint);}
    private static SKPath MakePath(Point a,Point b)
    {
        var path=new SKPath();var distance=(float)Math.Max(35,Math.Abs(b.X-a.X)*.48);path.MoveTo((float)a.X,(float)a.Y);path.CubicTo((float)a.X+distance,(float)a.Y,(float)b.X-distance,(float)b.Y,(float)b.X,(float)b.Y);return path;
    }
    private void Wire(SKCanvas canvas,Point a,Point b,SKColor color,float thickness=2)
    {
        using var path=MakePath(a,b);_paint.Style=SKPaintStyle.Stroke;_paint.StrokeWidth=thickness;_paint.Color=color;canvas.DrawPath(path,_paint);
    }
    protected override void RenderOverride(SKCanvas canvas,Size area)
    {
        canvas.Save();canvas.ClipRect(new(0,0,(float)area.Width,(float)area.Height));Fill(canvas,new(0,0,(float)area.Width,(float)area.Height),new(214,211,203));
        canvas.Translate(_panX,_panY);canvas.Scale(_zoom);
        var bounds=new SKRect(-_panX/_zoom,-_panY/_zoom,((float)area.Width-_panX)/_zoom,((float)area.Height-_panY)/_zoom);
        var view=new CanvasBox(bounds.Left,bounds.Top,bounds.Width,bounds.Height);
        var grid=_zoom>.45f?25:100;_paint.Style=SKPaintStyle.Stroke;_paint.StrokeWidth=.6f/_zoom;_paint.Color=new(194,192,185);
        // Line grid is O(width+height), rather than one draw per grid intersection.
        for(var x=MathF.Floor(bounds.Left/grid)*grid;x<bounds.Right;x+=grid)canvas.DrawLine(x,bounds.Top,x,bounds.Bottom,_paint);
        for(var y=MathF.Floor(bounds.Top/grid)*grid;y<bounds.Bottom;y+=grid)canvas.DrawLine(bounds.Left,y,bounds.Right,y,_paint);
        if(_session is not null)
        {
            var graph=_session.Graph;var drawing=Drawing();
            for(var i=0;i<drawing.Wires.Length;i++)
            {
                var w=drawing.Wires[i];if(!w.Bounds.Intersects(view))continue;
                var wire=w.Wire;var selected=_selection.Contains(wire.From.Node)||_selection.Contains(wire.To.Node);
                var tree=graph.LastResult?.Nodes.GetValueOrDefault(wire.From.Node)?.Outputs.GetValueOrDefault(wire.From.Port);
                _paint.Style=SKPaintStyle.Stroke;_paint.StrokeWidth=tree?.ItemCount>1?2.8f:1.3f;_paint.Color=selected?new(120,171,47):new(93,91,86);
                _paint.PathEffect=tree?.Branches.Length>1?_treeDash:null;canvas.DrawPath(_paths[i],_paint);_paint.PathEffect=null;
            }
            foreach(var n in drawing.Nodes)
            {
                if(!n.Bounds.Intersects(view))continue;
                var node=n.Node;var b=n.Bounds;var rect=new SKRect((float)b.X,(float)b.Y,(float)(b.X+b.Width),(float)(b.Y+b.Height));
                var selected=_selection.Contains(node.Id);var result=graph.LastResult?.Nodes.GetValueOrDefault(node.Id);var error=result?.Status is "error" or "blocked";
                var body=!node.Enabled?new SKColor(184,184,182):error?new(241,163,59):selected?new(188,222,105):node.Component=="Text"?new(249,245,163):new(220,220,226);
                Fill(canvas,new(rect.Left+2,rect.Top+3,rect.Right+2,rect.Bottom+3),new(0,0,0,32),7);
                Fill(canvas,rect,body,n.Slider?12:6);Stroke(canvas,rect,selected?new(64,95,19):new(83,83,88),selected?2:1,n.Slider?12:6);
                if(_zoom>.35f)
                {
                    canvas.Save();canvas.ClipRect(new(rect.Left+5,rect.Top+2,rect.Right-5,rect.Bottom-2));
                    if(n.Slider)
                    {
                        var value=Parameter(node,"value",1);var min=Parameter(node,"min",0);var max=Parameter(node,"max",100);var fraction=max>min?Math.Clamp((value-min)/(max-min),0,1):0;
                        Text(canvas,node.Name??node.Component,rect.Left+9,rect.Top+14,new(50,50,54),10);
                        _font.Size=10;var label=value.ToString("G7",CultureInfo.InvariantCulture);Text(canvas,label,rect.Right-10-_font.MeasureText(label),rect.Top+14,new(40,40,43),10);
                        Fill(canvas,new(rect.Left+13,rect.Bottom-12,rect.Right-13,rect.Bottom-8),new(157,157,161),2);
                        var x=rect.Left+13+(float)fraction*(rect.Width-26);Fill(canvas,new(x-3,rect.Bottom-16,x+3,rect.Bottom-4),new(230,231,223),2);Stroke(canvas,new(x-3,rect.Bottom-16,x+3,rect.Bottom-4),new(78,78,79),1,2);
                    }
                    else if(node.Component=="Text")
                    {
                        Text(canvas,"Panel",rect.Left+8,rect.Top+16,new(94,92,54),10);
                        var text=node.Parameters.GetValueOrDefault("text")?.ToString()??"";Text(canvas,text,rect.Left+8,rect.Top+37,new(68,66,47),11);
                    }
                    else
                    {
                        canvas.Save();canvas.Translate(rect.MidX-10,rect.MidY-14);canvas.Scale(.85f);IconArtwork.Draw(canvas,n.Definition.Category);canvas.Restore();
                        for(var side=0;side<2;side++){var ports=side==1?n.Definition.Outputs:n.Definition.Inputs;for(var i=0;i<ports.Length;i++){var p=n.Port(side==1,i);var label=ports[i].Name;_font.Size=10;var x=side==1?(float)p.X-_font.MeasureText(label)-9:(float)p.X+9;Text(canvas,label,x,(float)p.Y+3,new(55,55,61),10);}}
                        var name=node.Name??node.Component;_font.Size=9;Text(canvas,name,rect.MidX-_font.MeasureText(name)/2,rect.Bottom-5,new(59,59,68),9);
                    }
                    canvas.Restore();
                    for(var side=0;side<2;side++){var ports=side==1?n.Definition.Outputs:n.Definition.Inputs;for(var i=0;i<ports.Length;i++){var p=n.Port(side==1,i);_paint.Style=SKPaintStyle.Fill;_paint.Color=new(220,220,225);canvas.DrawCircle((float)p.X,(float)p.Y,3,_paint);_paint.Style=SKPaintStyle.Stroke;_paint.Color=new(65,65,70);_paint.StrokeWidth=1;canvas.DrawCircle((float)p.X,(float)p.Y,3,_paint);}}
                }
            }
            if(_wire is{} pending){var anchor=PortPoint(pending);Wire(canvas,pending.Output?anchor:_worldLast,pending.Output?_worldLast:anchor,new(110,155,35),2.2f);}
            if(_gesture=="marquee"){var r=new SKRect((float)Math.Min(_worldStart.X,_worldLast.X),(float)Math.Min(_worldStart.Y,_worldLast.Y),(float)Math.Max(_worldStart.X,_worldLast.X),(float)Math.Max(_worldStart.Y,_worldLast.Y));Fill(canvas,r,new(100,155,35,30));Stroke(canvas,r,new(100,155,35),1/_zoom);}
        }
        canvas.Restore();
    }
    private static double Parameter(GraphNode node,string name,double fallback)=>node.Parameters.TryGetValue(name,out var value)?Convert.ToDouble(value,CultureInfo.InvariantCulture):fallback;
    private void Select(string id,bool add){if(!add)_selection.Clear();_selection.Add(id);SelectionChanged?.Invoke();Invalidate();}
    public void SelectNode(string id)=>Select(id,false);
    public void AddComponent(string component)
    {
        if(_session is null)return;var at=_worldLast==default?World(new(ActualWidth/2,ActualHeight/2)):_worldLast;
        _session.Edit("Add component",graph=>{var node=graph.Add(component,at.X,at.Y);Select(node.Id,false);});
    }
    public void DeleteSelected(){if(_session is null||_selection.Count==0)return;try{var ids=_selection.ToArray();_session.Edit("Delete components",graph=>{foreach(var id in ids)graph.Remove(id);});_selection.Clear();SelectionChanged?.Invoke();}catch(Exception error){Message?.Invoke(error.Message);}}
    public void Fit()
    {
        if(_session is null||_session.Graph.Nodes.Count==0)return;var rects=_session.Graph.Nodes.Select(Rect).ToArray();var minX=rects.Min(r=>r.Left);var minY=rects.Min(r=>r.Top);var maxX=rects.Max(r=>r.Right);var maxY=rects.Max(r=>r.Bottom);
        _zoom=Math.Clamp((float)Math.Min(Math.Max(1,ActualWidth-60)/(maxX-minX),Math.Max(1,ActualHeight-60)/(maxY-minY)),.15f,1.5f);_panX=(float)ActualWidth/2-(minX+maxX)*_zoom/2;_panY=(float)ActualHeight/2-(minY+maxY)*_zoom/2;ViewChanged?.Invoke();Invalidate();
    }
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(_session is null)return;var point=e.GetCurrentPoint(this);_start=_last=point.Position;_worldStart=_worldLast=World(point.Position);_moved=false;_before=null;_slider=null;_wire=null;
        Focus(FocusState.Pointer);CapturePointer(e.Pointer);var node=NodeAt(_worldStart);var port=PortAt(_worldStart);var shift=(e.KeyModifiers&VirtualKeyModifiers.Shift)!=0;
        if(point.Properties.IsMiddleButtonPressed||point.Properties.IsRightButtonPressed)_gesture="pan";
        else if(port is not null){_wire=port;_gesture="wire";}
        else if(node is not null)
        {
            if(!_selection.Contains(node.Id)||shift)Select(node.Id,shift);_before=_session.Graph.Snapshot();
            if((node.Component is "Number" or "Integer")&&_worldStart.Y>Rect(node).Bottom-22){_gesture="slider";_slider=node.Id;Scrub(node,_worldStart.X);}
            else{_gesture="nodes";_starts.Clear();foreach(var n in _session.Graph.Nodes.Where(n=>_selection.Contains(n.Id)))_starts[n.Id]=(n.X,n.Y);}
        }
        else{if(!shift){_selection.Clear();SelectionChanged?.Invoke();}_gesture=shift?"marquee":"pan";}
        Invalidate();e.Handled=true;
    }
    private void Scrub(GraphNode node,double x)
    {
        var min=Parameter(node,"min",0);var max=Parameter(node,"max",100);if(!(max>min))return;var value=min+Math.Clamp((x-node.X-13)/154,0,1)*(max-min);if(node.Component=="Integer")value=Math.Round(value);_session!.Graph.Update(node with{Parameters=node.Parameters.SetItem("value",value)});
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        var point=e.GetCurrentPoint(this).Position;_worldLast=World(point);if(_gesture==""||_session is null)return;_moved|=Math.Abs(point.X-_start.X)+Math.Abs(point.Y-_start.Y)>2;
        if(_gesture=="pan"){_panX+=(float)(point.X-_last.X);_panY+=(float)(point.Y-_last.Y);}
        else if(_gesture=="nodes")_session.Graph.MoveNodes(_starts.Select(p=>new NodePosition(p.Key,p.Value.X+(point.X-_start.X)/_zoom,p.Value.Y+(point.Y-_start.Y)/_zoom)).ToArray());
        else if(_gesture=="slider"&&_slider is not null)Scrub(_session.Graph.GetNode(_slider),_worldLast.X);
        _last=point;Invalidate();e.Handled=true;
    }
    private void Released(object sender,PointerRoutedEventArgs e)
    {
        if(_session is null)return;
        try
        {
            _worldLast=World(e.GetCurrentPoint(this).Position);
            if(_wire is{} source&&PortAt(_worldLast) is{} target&&source.Output!=target.Output)
            {
                var output=source.Output?source:target;var input=source.Output?target:source;
                _session.Edit("Connect ports",graph=>graph.Connect(new(output.Node,output.Port),new(input.Node,input.Port),replace:(e.KeyModifiers&VirtualKeyModifiers.Shift)==0));
            }
            if(_before is not null&&(_moved||_gesture=="slider"))_session.Record(_gesture=="slider"?"Change number":"Move components",_before);
            if(_gesture=="marquee")
            {
                var r=new SKRect((float)Math.Min(_worldStart.X,_worldLast.X),(float)Math.Min(_worldStart.Y,_worldLast.Y),(float)Math.Max(_worldStart.X,_worldLast.X),(float)Math.Max(_worldStart.Y,_worldLast.Y));foreach(var node in _session.Graph.Nodes)if(r.IntersectsWith(Rect(node)))_selection.Add(node.Id);SelectionChanged?.Invoke();
            }
        }
        catch(Exception error){Message?.Invoke(error.Message);}
        finally{_gesture="";_wire=null;_before=null;_slider=null;ReleasePointerCapture(e.Pointer);Invalidate();SelectionChanged?.Invoke();e.Handled=true;}
    }
    private void CancelGesture(){if(_before is not null&&_session is not null)_session.RestoreUncommitted(_before);_before=null;_gesture="";_wire=null;_slider=null;Invalidate();}
    public void Dispose(){Unsubscribe();foreach(var path in _paths)path.Dispose();_paths.Clear();_treeDash.Dispose();_font.Dispose();_paint.Dispose();}
}
