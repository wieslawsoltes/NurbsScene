namespace NurbsScene.Parametric;

public sealed record GraphHistoryEntry(string Name,GraphSnapshot Before,GraphSnapshot After);
/// <summary>Bounded, synchronous graph editing transactions, including layout. Solves never become history mutations.</summary>
public sealed class GraphSession(Graph graph,int capacity=100)
{
    private readonly List<GraphHistoryEntry> _undo=[],_redo=[];
    public Graph Graph {get;private set;}=graph;
    public event Action? Replaced;
    public bool CanUndo=>_undo.Count>0;public bool CanRedo=>_redo.Count>0;
    public void Replace(Graph value){Graph=value;_undo.Clear();_redo.Clear();Replaced?.Invoke();}
    private void Restore(GraphSnapshot snapshot){Graph=Graph.FromSnapshot(Graph.Registry,snapshot);Replaced?.Invoke();}
    public void Record(string name,GraphSnapshot before){_undo.Add(new(name,new(before.Nodes.ToArray(),before.Wires.ToArray()),Graph.Snapshot()));if(_undo.Count>Math.Max(1,capacity))_undo.RemoveAt(0);_redo.Clear();}
    public void Edit(string name,Action<Graph> action)
    {
        if(action.Method.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute),false))throw new ArgumentException("Graph editing transactions must be synchronous.");var before=Graph.Snapshot();var revision=Graph.Revision;try{action(Graph);if(Graph.Revision!=revision)Record(name,before);}catch{Restore(before);throw;}
    }
    public void RestoreUncommitted(GraphSnapshot snapshot)=>Restore(snapshot);
    public bool Undo(){if(_undo.Count==0)return false;var entry=_undo[^1];_undo.RemoveAt(_undo.Count-1);_redo.Add(entry);Restore(entry.Before);return true;}
    public bool Redo(){if(_redo.Count==0)return false;var entry=_redo[^1];_redo.RemoveAt(_redo.Count-1);_undo.Add(entry);Restore(entry.After);return true;}
}
