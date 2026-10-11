using NurbsScene.Presentation;
using Windows.Foundation;

namespace NurbsScene.Controls;

/// <summary>Opt-in bounded observation of actual routed pointer events. Disabled
/// by default; does not dispatch input or alter the gesture state machine.</summary>
public sealed record GraphPointerObservation(string Event,string Gesture,CanvasPoint Screen,
    CanvasPoint Start,CanvasPoint Current,bool Moved,int Selected);

public sealed partial class GraphCanvas
{
    private readonly Queue<GraphPointerObservation> _pointerObservations=[];
    public bool ObservePointerEvents {get;set;}
    public IReadOnlyList<GraphPointerObservation> PointerObservations=>_pointerObservations.ToArray();
    private void Observe(string name,Point point)
    {
        if(!ObservePointerEvents)return;
        _pointerObservations.Enqueue(new(name,_gesture,new(point.X,point.Y),new(_worldStart.X,_worldStart.Y),
            new(_worldLast.X,_worldLast.Y),_moved,_selection.Count));
        while(_pointerObservations.Count>64)_pointerObservations.Dequeue();
    }
}
