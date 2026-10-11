namespace NurbsScene.Parametric;

public sealed record GraphHistoryEntry(string Name,GraphSnapshot Before,GraphSnapshot After);
/// <summary>Owner-thread synchronous edits. Observers see only committed batches
/// or one restored graph, never intermediate edits. Solves are not history.</summary>
public sealed class GraphSession(Graph graph,int capacity=100)
{
    private readonly List<GraphHistoryEntry> _undo=[],_redo=[];
    private bool _editing;
    public Graph Graph {get;private set;}=graph??throw new ArgumentNullException(nameof(graph));
    public event Action? Replaced;
    public bool CanUndo=>_undo.Count>0;
    public bool CanRedo=>_redo.Count>0;
    private void Guard(){if(_editing)throw new InvalidOperationException("Cannot alter graph history during an edit.");}
    public void Replace(Graph value)
    {
        Guard();ArgumentNullException.ThrowIfNull(value);Graph=value;_undo.Clear();_redo.Clear();Replaced?.Invoke();
    }
    private void Restore(GraphSnapshot snapshot){Graph=Graph.FromSnapshot(Graph.Registry,snapshot);Replaced?.Invoke();}
    private void RecordCore(string name,GraphSnapshot before)
    {
        _undo.Add(new(name,new(before.Nodes.ToArray(),before.Wires.ToArray()),Graph.Snapshot()));
        if(_undo.Count>Math.Max(1,capacity))_undo.RemoveAt(0);_redo.Clear();
    }
    public void Record(string name,GraphSnapshot before){Guard();RecordCore(name,before);}
    public void Edit(string name,Action<Graph> action)
    {
        Guard();ArgumentNullException.ThrowIfNull(action);
        if(action.Method.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute),false))
            throw new ArgumentException("Graph editing transactions must be synchronous.");
        var before=Graph.Snapshot();var current=Graph;var revision=current.Revision;
        current.BeginEdit();_editing=true;
        try{action(current);if(current.Revision!=revision)RecordCore(name,before);}
        catch
        {
            current.EndEdit(false);_editing=false;Restore(before);throw;
        }
        finally{_editing=false;}
        // Subscriber exceptions happen after commit and cannot undo a successful
        // edit or make history disagree with the published graph.
        current.EndEdit(true);
    }
    public void RestoreUncommitted(GraphSnapshot snapshot){Guard();Restore(snapshot);}
    public bool Undo(){Guard();if(_undo.Count==0)return false;var entry=_undo[^1];_undo.RemoveAt(_undo.Count-1);_redo.Add(entry);Restore(entry.Before);return true;}
    public bool Redo(){Guard();if(_redo.Count==0)return false;var entry=_redo[^1];_redo.RemoveAt(_redo.Count-1);_undo.Add(entry);Restore(entry.After);return true;}
}
