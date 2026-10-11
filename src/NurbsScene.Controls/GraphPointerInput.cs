using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using Windows.Foundation;
using Windows.System;

namespace NurbsScene.Controls;

public sealed partial class GraphCanvas
{
    private void InitializePointerInput()
    {
        PointerPressed+=Pressed;PointerMoved+=Moved;PointerReleased+=Released;
        PointerCanceled+=(_,e)=>{Observe("Cancelled",_last);CancelGesture();ReleasePointerCapture(e.Pointer);};
        PointerCaptureLost+=(_,_)=>{Observe("CaptureLost",_last);if(_gesture!="")CancelGesture();};
    }
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(_session is null)return;var point=e.GetCurrentPoint(this);_start=_last=point.Position;_worldStart=_worldLast=World(point.Position);_moved=false;_before=null;_slider=null;_wire=null;
        Focus(FocusState.Pointer);CapturePointer(e.Pointer);var node=NodeAt(_worldStart);var port=PortAt(_worldStart);var shift=(e.KeyModifiers&VirtualKeyModifiers.Shift)!=0;
        _rightPan=point.Properties.IsRightButtonPressed;
        if(point.Properties.IsMiddleButtonPressed||_rightPan)_gesture="pan";
        else if(port is not null){_wire=port;_gesture="wire";}
        else if(node is not null)
        {
            if(shift&&_selection.Remove(node.Id)){SelectionChanged?.Invoke();_gesture="";ReleasePointerCapture(e.Pointer);Invalidate();e.Handled=true;return;}
            if(!_selection.Contains(node.Id))Select(node.Id,shift);_before=_session.Graph.Snapshot();
            if((node.Component is "Number" or "Integer")&&_worldStart.Y>Rect(node).Bottom-22){_gesture="slider";_slider=node.Id;Scrub(node,_worldStart.X);}
            else{_gesture="nodes";_starts.Clear();foreach(var n in _session.Graph.Nodes.Where(n=>_selection.Contains(n.Id)))_starts[n.Id]=(n.X,n.Y);}
        }
        else{if(!shift){_selection.Clear();SelectionChanged?.Invoke();}_gesture="marquee";}
        Observe("Pressed",point.Position);Invalidate();e.Handled=true;
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
        _last=point;Observe("Moved",point);Invalidate();e.Handled=true;
    }
    private void Released(object sender,PointerRoutedEventArgs e)
    {
        if(_session is null)return;
        Point? context=null;
        try
        {
            var screen=e.GetCurrentPoint(this).Position;
            _worldLast=World(screen);Observe("Released",screen);
            if(_gesture=="pan"&&_rightPan&&!_moved)
            {
                var node=NodeAt(_worldLast);
                if(node is null)SelectNodes([]);else if(!_selection.Contains(node.Id))Select(node.Id,false);
                context=screen;
            }
            if(_wire is{} source&&PortAt(_worldLast) is{} target&&source.Output!=target.Output)
            {
                var output=source.Output?source:target;var input=source.Output?target:source;
                _session.Edit("Connect ports",graph=>graph.Connect(new(output.Node,output.Port),new(input.Node,input.Port),replace:(e.KeyModifiers&VirtualKeyModifiers.Shift)==0));
            }
            if(_before is not null&&(_moved||_gesture=="slider"))_session.Record(_gesture=="slider"?"Change number":"Move components",_before);
            if(_gesture=="marquee"&&_moved)
            {
                _selection.UnionWith(GraphSelection.InRectangle(Drawing(),new(_worldStart.X,_worldStart.Y),new(_worldLast.X,_worldLast.Y)));
                Observe("SelectionCommitted",screen);SelectionChanged?.Invoke();
            }
        }
        catch(Exception error){Message?.Invoke(error.Message);}
        finally{_gesture="";_wire=null;_before=null;_slider=null;ReleasePointerCapture(e.Pointer);Invalidate();SelectionChanged?.Invoke();e.Handled=true;}
        if(context is{} point)ContextMenuRequested?.Invoke(point);
    }
    private void CancelGesture(){var before=_before;_before=null;_gesture="";_wire=null;_slider=null;if(before is not null&&_session is not null)_session.RestoreUncommitted(before);Invalidate();}
}
