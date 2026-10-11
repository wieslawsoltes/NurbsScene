using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Parametric;
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
        PointerWheelChanged+=(_,e)=>{var point=e.GetCurrentPoint(this);var world=World(point.Position);_zoom=Math.Clamp(_zoom*(float)Math.Exp(point.Properties.MouseWheelDelta*.001),.15f,3.5f);_panX=(float)point.Position.X-(float)world.X*_zoom;_panY=(float)point.Position.Y-(float)world.Y*_zoom;Invalidate();e.Handled=true;};
        DoubleTapped+=(_,e)=>{_worldLast=World(e.GetPosition(this));if(NodeAt(_worldLast) is null)SearchRequested?.Invoke();e.Handled=true;};
        KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Delete){DeleteSelected();e.Handled=true;}else if(e.Key==VirtualKey.Escape){CancelGesture();e.Handled=true;}else if(e.Key==VirtualKey.F){Fit();e.Handled=true;}};
    }
    private void Subscribe(){if(_subscribed||_session is null)return;_subscribed=true;_session.Replaced+=Replaced;_graph=_session.Graph;_graph.Changed+=GraphChanged;_graph.Solved+=Solved;}
    private void Unsubscribe(){if(!_subscribed)return;_subscribed=false;if(_session is not null)_session.Replaced-=Replaced;if(_graph is not null){_graph.Changed-=GraphChanged;_graph.Solved-=Solved;}_graph=null;}
    private void Replaced(){if(_graph is not null){_graph.Changed-=GraphChanged;_graph.Solved-=Solved;}_graph=_session!.Graph;_graph.Changed+=GraphChanged;_graph.Solved+=Solved;_selection.IntersectWith(_graph.Nodes.Select(n=>n.Id));SelectionChanged?.Invoke();Invalidate();}
    private void GraphChanged()=>Invalidate();
    private void Solved(GraphResult result)=>Invalidate();
    private Point World(Point point)=>new((point.X-_panX)/_zoom,(point.Y-_panY)/_zoom);
    private float Height(GraphNode node){var d=_session!.Graph.Registry.Get(node.Component);return 30+Math.Max(d.Inputs.Length,d.Outputs.Length)*22+(node.Component is "Number" or "Integer"?49:25);}
    private SKRect Rect(GraphNode node)=>new((float)node.X,(float)node.Y,(float)node.X+176,(float)node.Y+Height(node));
    private Point PortPoint(GraphNode node,bool output,int index)=>new(node.X+(output?176:0),node.Y+43+index*22);
    private Point PortPoint(PortHit port){var node=_session!.Graph.GetNode(port.Node);var definition=_session.Graph.Registry.Get(node.Component);var ports=port.Output?definition.Outputs:definition.Inputs;var index=Array.FindIndex(ports.ToArray(),p=>p.Name==port.Port);return PortPoint(node,port.Output,index);}
    private GraphNode? NodeAt(Point point)=>_session?.Graph.Nodes.Reverse().FirstOrDefault(n=>Rect(n).Contains((float)point.X,(float)point.Y));
    private PortHit? PortAt(Point point)
    {
        if(_session is null)return null;var tolerance=9/_zoom;
        foreach(var node in _session.Graph.Nodes.Reverse())
        {
            var definition=_session.Graph.Registry.Get(node.Component);
            for(var side=0;side<2;side++){var ports=side==1?definition.Outputs:definition.Inputs;for(var i=0;i<ports.Length;i++){var p=PortPoint(node,side==1,i);if(Math.Abs(p.X-point.X)<=tolerance&&Math.Abs(p.Y-point.Y)<=tolerance)return new(node.Id,ports[i].Name,side==1);}}
        }
        return null;
    }
    private static SKColor Category(string category)=>category switch{"Params"=>new(111,143,103),"Maths"=>new(134,132,90),"Sets"=>new(118,135,98),"Vector"=>new(94,136,144),"Curve"=>new(85,137,137),"Surface"=>new(134,121,93),"Transform"=>new(130,111,143),_=>new(113,133,111)};
    private void Fill(SKCanvas canvas,SKRect rect,SKColor color,float radius=0){_paint.Style=SKPaintStyle.Fill;_paint.Color=color;if(radius>0)canvas.DrawRoundRect(rect,radius,radius,_paint);else canvas.DrawRect(rect,_paint);}
    private void Stroke(SKCanvas canvas,SKRect rect,SKColor color,float width=1,float radius=0){_paint.Style=SKPaintStyle.Stroke;_paint.StrokeWidth=width;_paint.Color=color;if(radius>0)canvas.DrawRoundRect(rect,radius,radius,_paint);else canvas.DrawRect(rect,_paint);}
    private void Text(SKCanvas canvas,string text,float x,float y,SKColor color,float size=12){_font.Size=size;_paint.Style=SKPaintStyle.Fill;_paint.Color=color;canvas.DrawText(text,x,y,_font,_paint);}
    private void Wire(SKCanvas canvas,Point a,Point b,SKColor color,float thickness=2)
    {
        using var path=new SKPath();var distance=(float)Math.Max(35,Math.Abs(b.X-a.X)*.48);path.MoveTo((float)a.X,(float)a.Y);path.CubicTo((float)a.X+distance,(float)a.Y,(float)b.X-distance,(float)b.Y,(float)b.X,(float)b.Y);_paint.Style=SKPaintStyle.Stroke;_paint.StrokeWidth=thickness;_paint.Color=color;canvas.DrawPath(path,_paint);
    }
    protected override void RenderOverride(SKCanvas canvas,Size area)
    {
        canvas.Save();canvas.ClipRect(new(0,0,(float)area.Width,(float)area.Height));Fill(canvas,new(0,0,(float)area.Width,(float)area.Height),new(245,245,239));
        canvas.Translate(_panX,_panY);canvas.Scale(_zoom);
        var bounds=new SKRect(-_panX/_zoom,-_panY/_zoom,((float)area.Width-_panX)/_zoom,((float)area.Height-_panY)/_zoom);
        var grid=_zoom>.45f?20:100;_paint.Style=SKPaintStyle.Fill;_paint.Color=new(211,213,204);
        for(var x=MathF.Floor(bounds.Left/grid)*grid;x<bounds.Right;x+=grid)for(var y=MathF.Floor(bounds.Top/grid)*grid;y<bounds.Bottom;y+=grid)canvas.DrawCircle(x,y,.7f/_zoom,_paint);
        if(_session is not null)
        {
            var graph=_session.Graph;var nodes=graph.Nodes.ToDictionary(n=>n.Id);
            foreach(var wire in graph.Wires)
            {
                var a=PortPoint(new(wire.From.Node,wire.From.Port,true));var b=PortPoint(new(wire.To.Node,wire.To.Port,false));
                if(Math.Max(a.X,b.X)+70<bounds.Left||Math.Min(a.X,b.X)-70>bounds.Right||Math.Max(a.Y,b.Y)<bounds.Top||Math.Min(a.Y,b.Y)>bounds.Bottom)continue;
                var selected=_selection.Contains(wire.From.Node)||_selection.Contains(wire.To.Node);var count=graph.LastResult?.Nodes.GetValueOrDefault(wire.From.Node)?.Outputs.GetValueOrDefault(wire.From.Port)?.ItemCount??0;
                Wire(canvas,a,b,selected?new(89,146,72):new(131,137,120),count>1?3:1.6f);
            }
            foreach(var node in nodes.Values)
            {
                var rect=Rect(node);if(!rect.IntersectsWith(bounds))continue;var definition=graph.Registry.Get(node.Component);var selected=_selection.Contains(node.Id);var result=graph.LastResult?.Nodes.GetValueOrDefault(node.Id);var error=result?.Status is "error" or "blocked";
                Fill(canvas,new(rect.Left+2,rect.Top+3,rect.Right+2,rect.Bottom+3),new(0,0,0,25),6);
                Fill(canvas,rect,node.Enabled?new(242,242,228):new(219,220,217),6);Stroke(canvas,rect,error?new(190,80,50):selected?new(70,139,71):new(115,123,102),selected?2.5f:1,6);
                canvas.Save();canvas.ClipRect(rect);Fill(canvas,new(rect.Left,rect.Top,rect.Right,rect.Top+28),error?new(190,99,65):Category(definition.Category));
                Text(canvas,node.Name??node.Component,rect.Left+10,rect.Top+19,SKColors.White,12);
                if(_zoom>.35f)
                {
                    for(var side=0;side<2;side++){var ports=side==1?definition.Outputs:definition.Inputs;for(var i=0;i<ports.Length;i++){var p=PortPoint(node,side==1,i);_paint.Style=SKPaintStyle.Fill;_paint.Color=new(93,109,80);canvas.DrawCircle((float)p.X,(float)p.Y,4,_paint);var label=ports[i].Name;_font.Size=11;var x=side==1?(float)p.X-_font.MeasureText(label)-11:(float)p.X+11;Text(canvas,label,x,(float)p.Y+4,new(51,61,48),11);}}
                    if(node.Component is "Number" or "Integer")
                    {
                        var value=Parameter(node,"value",1);var min=Parameter(node,"min",0);var max=Parameter(node,"max",100);var fraction=max>min?Math.Clamp((value-min)/(max-min),0,1):0;var y=rect.Bottom-26;
                        Fill(canvas,new(rect.Left+13,y-2,rect.Right-13,y+2),new(190,199,175),2);Fill(canvas,new(rect.Left+13,y-2,rect.Left+13+(float)fraction*(rect.Width-26),y+2),new(91,140,77),2);
                        _paint.Style=SKPaintStyle.Fill;_paint.Color=new(58,106,55);canvas.DrawCircle(rect.Left+13+(float)fraction*(rect.Width-26),y,5,_paint);Text(canvas,value.ToString("G7",CultureInfo.InvariantCulture),rect.Left+13,rect.Bottom-7,new(51,61,48),11);
                    }
                    else Text(canvas,error?(result!.Messages.FirstOrDefault()??"Error")[..Math.Min(24,(result.Messages.FirstOrDefault()??"Error").Length)]:node.Component=="Text"?node.Parameters.GetValueOrDefault("text")?.ToString()??"":result is null?"Not solved":$"{result.Outputs.Values.Sum(t=>t.ItemCount)} items · {result.Milliseconds:F2} ms",rect.Left+11,rect.Bottom-8,error?new(160,55,40):new(102,112,93),10);
                }
                canvas.Restore();
            }
            if(_wire is{} pending){var anchor=PortPoint(pending);Wire(canvas,pending.Output?anchor:_worldLast,pending.Output?_worldLast:anchor,new(90,145,65),2.2f);}
            if(_gesture=="marquee"){var r=new SKRect((float)Math.Min(_worldStart.X,_worldLast.X),(float)Math.Min(_worldStart.Y,_worldLast.Y),(float)Math.Max(_worldStart.X,_worldLast.X),(float)Math.Max(_worldStart.Y,_worldLast.Y));Fill(canvas,r,new(89,146,72,25));Stroke(canvas,r,new(89,146,72),1/_zoom);}
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
        _zoom=Math.Clamp((float)Math.Min(Math.Max(1,ActualWidth-60)/(maxX-minX),Math.Max(1,ActualHeight-60)/(maxY-minY)),.15f,1.5f);_panX=(float)ActualWidth/2-(minX+maxX)*_zoom/2;_panY=(float)ActualHeight/2-(minY+maxY)*_zoom/2;Invalidate();
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
            if((node.Component is "Number" or "Integer")&&_worldStart.Y>Rect(node).Bottom-40){_gesture="slider";_slider=node.Id;Scrub(node,_worldStart.X);}
            else{_gesture="nodes";_starts.Clear();foreach(var n in _session.Graph.Nodes.Where(n=>_selection.Contains(n.Id)))_starts[n.Id]=(n.X,n.Y);}
        }
        else{if(!shift){_selection.Clear();SelectionChanged?.Invoke();}_gesture=shift?"marquee":"pan";}
        Invalidate();e.Handled=true;
    }
    private void Scrub(GraphNode node,double x)
    {
        var min=Parameter(node,"min",0);var max=Parameter(node,"max",100);if(!(max>min))return;var value=min+Math.Clamp((x-node.X-13)/150,0,1)*(max-min);if(node.Component=="Integer")value=Math.Round(value);_session!.Graph.Update(node with{Parameters=node.Parameters.SetItem("value",value)});
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        var point=e.GetCurrentPoint(this).Position;_worldLast=World(point);if(_gesture==""||_session is null)return;_moved|=Math.Abs(point.X-_start.X)+Math.Abs(point.Y-_start.Y)>2;
        if(_gesture=="pan"){_panX+=(float)(point.X-_last.X);_panY+=(float)(point.Y-_last.Y);}
        else if(_gesture=="nodes")foreach(var (id,at) in _starts){var node=_session.Graph.GetNode(id);_session.Graph.Update(node with{X=at.X+(point.X-_start.X)/_zoom,Y=at.Y+(point.Y-_start.Y)/_zoom});}
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
    public void Dispose(){Unsubscribe();_font.Dispose();_paint.Dispose();}
}
