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
        if(_session is null)return;ClearPanelResize();var point=e.GetCurrentPoint(this);_start=_last=point.Position;_worldStart=_worldLast=World(point.Position);_moved=false;_before=null;_slider=null;_wire=null;
        Focus(FocusState.Pointer);CapturePointer(e.Pointer);var node=NodeAt(_worldStart);var port=PortAt(_worldStart);var shift=(e.KeyModifiers&VirtualKeyModifiers.Shift)!=0;
        _rightPan=point.Properties.IsRightButtonPressed;
        if(point.Properties.IsMiddleButtonPressed||_rightPan)_gesture="pan";
        else if(port is not null){_wire=port;_gesture="wire";}
        else if(node is not null)
        {
            if(shift&&_selection.Remove(node.Id)){SelectionChanged?.Invoke();_gesture="";ReleasePointerCapture(e.Pointer);Invalidate();e.Handled=true;return;}
            if(!_selection.Contains(node.Id))Select(node.Id,shift);
            if(PanelLayout.CanResize(node)&&PanelLayout.IsHandle(Layout(node).Bounds,new(_worldStart.X,_worldStart.Y),Math.Min(128,10/_zoom)))
            {_gesture="resize";_panelResize=new(_session.Graph,node.Id);}
            else if((node.Component is "Number" or "Integer")&&_worldStart.Y>Rect(node).Bottom-22){_before=_session.Graph.Snapshot();_gesture="slider";_slider=node.Id;Scrub(node,_worldStart.X);}
            else{_gesture="nodes";_starts.Clear();foreach(var n in _session.Graph.Nodes.Where(n=>_selection.Contains(n.Id)))_starts[n.Id]=(n.X,n.Y);}
        }
        else{if(!shift){_selection.Clear();SelectionChanged?.Invoke();}_gesture="marquee";}
        Observe("Pressed",point.Position);Invalidate();e.Handled=true;
    }
    private void Scrub(GraphNode node,double x)
    {
        if(!NumberSliderSettings.TryRead(node,out var settings,out var previous,out var error))
        { Message?.Invoke(error!);_gesture="";_before=null;return; }
        var fraction=(x-node.X-13)/154;
        if(!double.IsFinite(fraction))return;
        var value=settings.ValueAt(fraction);
        if(value!=previous)_session!.Graph.Update(node with{Parameters=node.Parameters.SetItem("value",value)});
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {
        var point=e.GetCurrentPoint(this).Position;_worldLast=World(point);if(_gesture==""||_session is null)return;_moved|=Math.Abs(point.X-_start.X)+Math.Abs(point.Y-_start.Y)>2;
        if(_gesture=="pan"){_panX+=(float)(point.X-_last.X);_panY+=(float)(point.Y-_last.Y);}
        else if(_gesture=="resize"&&_moved)_panelResize?.Move((point.X-_start.X)/_zoom,(point.Y-_start.Y)/_zoom);
        else if(_gesture=="nodes"&&_moved){_before??=_session.Graph.Snapshot();_session.Graph.MoveNodes(_starts.Select(p=>new NodePosition(p.Key,p.Value.X+(point.X-_start.X)/_zoom,p.Value.Y+(point.Y-_start.Y)/_zoom)).ToArray());}
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
            if(_gesture=="resize"&&_panelResize is{} resize)
            {
                var dx=screen.X-_start.X;var dy=screen.Y-_start.Y;
                if(_moved||Math.Abs(dx)+Math.Abs(dy)>2){resize.Move(dx/_zoom,dy/_zoom);resize.Commit(_session);}
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
        finally{ClearPanelResize();_gesture="";_wire=null;_before=null;_slider=null;ReleasePointerCapture(e.Pointer);Invalidate();SelectionChanged?.Invoke();e.Handled=true;}
        if(context is{} point)ContextMenuRequested?.Invoke(point);
    }
    private void CancelGesture(){ClearPanelResize();var before=_before;_before=null;_gesture="";_wire=null;_slider=null;if(before is not null&&_session is not null)_session.RestoreUncommitted(before);Invalidate();}
}
