namespace NurbsScene.Parametric;

public sealed partial class Graph
{
    /// <summary>Changes to connections or their transform metadata. Unlike
    /// TopologyRevision, this also advances when existing wire transforms change.</summary>
    public long WireRevision {get;private set;}

    /// <summary>Detached incoming-wire snapshot in evaluation/fan-in order.
    /// Uses the maintained input index, not a scan of every wire in the graph.</summary>
    public IReadOnlyList<Wire> Incoming(PortAddress input)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock(_gate)
        {
            var definition=Registry.Get(GetNode(input.Node).Component);
            var found=false;
            foreach(var port in definition.Inputs)if(port.Name==input.Port){found=true;break;}
            if(!found)throw new ArgumentException("Input port is missing.",nameof(input));
            return _inputWires.TryGetValue(input,out var wires)?wires.ToArray():Array.Empty<Wire>();
        }
    }

    /// <summary>Atomically changes existing wires without reconnecting, changing
    /// their identities/order, or sorting the graph again. The captured execution
    /// plan of an in-flight solve is never modified. Only destinations and their
    /// descendants become dirty. No-op edits do not advance revision/history.</summary>
    public void SetWireTransforms(IReadOnlyCollection<string> wireIds,string transform)
    {
        ArgumentNullException.ThrowIfNull(wireIds);
        if(transform is not("none" or "flatten" or "graft" or "simplify"))
            throw new ArgumentException("Unknown wire transform.",nameof(transform));
        if(wireIds.Count>MaximumWires)throw new ArgumentException("Wire edit budget exceeded.",nameof(wireIds));
        lock(_gate)
        {
            var changes=new List<Wire>();var seen=new HashSet<string>();var visits=0;
            foreach(var id in wireIds)
            {
                if(++visits>MaximumWires)throw new ArgumentException("Wire enumeration exceeds its budget.",nameof(wireIds));
                if(id is null||!seen.Add(id))throw new ArgumentException("Distinct wire identities required.",nameof(wireIds));
                if(!_wires.TryGetValue(id,out var wire))throw new KeyNotFoundException(id);
                if(wire.Transform!=transform)changes.Add(wire with{Transform=transform});
            }
            if(changes.Count==0)return;
            var targets=new HashSet<PortAddress>();var replacements=changes.ToDictionary(w=>w.Id);
            foreach(var wire in changes){_wires[wire.Id]=wire;targets.Add(wire.To);}
            // Scan each affected fan-in once. Repeated FindIndex per changed
            // wire would be quadratic for a heavily connected input.
            foreach(var target in targets)
            {
                var input=_inputWires[target];
                for(var i=0;i<input.Count;i++)if(replacements.TryGetValue(input[i].Id,out var updated))input[i]=updated;
            }
            if(_plan is{} plan)
            {
                var inputs=new Dictionary<PortAddress,Wire[]>(plan.Inputs);
                foreach(var target in targets)inputs[target]=_inputWires[target].ToArray();
                // Transform-only edits cannot change which input ports are connected.
                // Retain the ordered immutable connection snapshots as well.
                _plan=plan with{Inputs=inputs};
            }
            // One traversal for the entire batch: shared downstream components
            // must not be revisited once for every transformed destination.
            var pending=new Queue<string>(targets.Select(t=>t.Node));
            var visited=new HashSet<string>();
            while(pending.TryDequeue(out var node))
            {
                if(!visited.Add(node))continue;
                _dirty.Add(node);
                if(_outgoing.TryGetValue(node,out var children))
                    foreach(var child in children.Keys)pending.Enqueue(child);
            }
            WireRevision++;Revision++;
        }
        NotifyChanged();
    }
}
