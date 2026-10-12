using System.Collections.ObjectModel;
using NurbsScene.Geometry;

namespace NurbsScene.Model;

public readonly record struct ColorRgba(float R,float G,float B,float A=1)
{
    public bool IsValid=>float.IsFinite(R)&&float.IsFinite(G)&&float.IsFinite(B)&&float.IsFinite(A)&&R>=0&&R<=1&&G>=0&&G<=1&&B>=0&&B<=1&&A>=0&&A<=1;
    public static ColorRgba Default=>new(.62f,.72f,.71f);
}
public sealed record Layer(string Id,string Name,ColorRgba Color,bool Visible=true,bool Locked=false);
public sealed record SceneObject(string Id,string Name,IGeometry Geometry,string LayerId,ColorRgba? Color=null,bool Visible=true,bool Locked=false,string? Source=null);
public sealed record DocumentChange(string Kind,long Revision);
public sealed record DocumentSnapshot(string Name,string Units,double AbsoluteTolerance,double AngleTolerance,string ActiveLayer,Layer[] Layers,SceneObject[] Objects,string[] Selection);
public sealed record DocumentHistoryEntry(string Name,DocumentSnapshot Before,DocumentSnapshot After);

/// <summary>UI-free authoritative document. Exact geometries are immutable and shared by undo snapshots.</summary>
public sealed partial class SceneDocument
{
    private Dictionary<string,SceneObject> _objects=[];private Dictionary<string,Layer> _layers=[];private readonly HashSet<string> _selection=[];private bool _inTransaction;
    private readonly Stack<DocumentHistoryEntry> _undo=new(),_redo=new();
    public event Action<DocumentChange>? Changed;
    private string _name="Untitled",_activeLayer="default";
    public string Name { get=>_name; set {ArgumentNullException.ThrowIfNull(value);if(_name==value)return;_name=value;Notify("metadata");} }
    public string Units { get; init; }="mm";
    public double AbsoluteTolerance { get; init; }=1e-6;
    public double AngleTolerance { get; init; }=1e-5;
    public string ActiveLayer { get=>_activeLayer; set {GetLayer(value);if(_activeLayer==value)return;_activeLayer=value;Notify("metadata");} }
    public long Revision { get; private set; }
    private IReadOnlyList<SceneObject>? _objectView;
    private IReadOnlyList<Layer>? _layerView;
    private IReadOnlySet<string>? _selectionView;
    // Snapshot views are detached from mutable storage and cached until a write.
    // Repeated Contains calls during rendering must not copy the whole selection.
    public IReadOnlyList<SceneObject> Objects=>_objectView??=OrderedValues(_objectOrder,_objects);
    public IReadOnlyList<Layer> Layers=>_layerView??=OrderedValues(_layerOrder,_layers);
    public IReadOnlySet<string> Selection=>_selectionView??=new ReadOnlySet<string>(new HashSet<string>(_selection));
    public bool CanUndo=>_undo.Count>0;public bool CanRedo=>_redo.Count>0;
    public SceneDocument()=>_layers.Add("default",new("default","Default",ColorRgba.Default));
    public SceneObject GetObject(string id)=>_objects.TryGetValue(id,out var o)?o:throw new KeyNotFoundException($"Unknown object {id}.");
    public Layer GetLayer(string id)=>_layers.TryGetValue(id,out var l)?l:throw new KeyNotFoundException($"Unknown layer {id}.");
    private void Notify(string kind){_objectView=null;_layerView=null;_selectionView=null;_selectionOrderView=null;Revision++;if(!_inTransaction)Changed?.Invoke(new(kind,Revision));}
    public Layer AddLayer(string name,ColorRgba color,string? id=null)
    {
        if(!color.IsValid)throw new ArgumentException("Invalid layer color.");var layer=new Layer(id??Guid.NewGuid().ToString("N"),name,color);_layers.Add(layer.Id,layer);_layerOrder.Add(layer.Id);Notify("layer");return layer;
    }
    public void UpdateLayer(Layer layer){GetLayer(layer.Id);if(!layer.Color.IsValid)throw new ArgumentException("Invalid color.");_layers[layer.Id]=layer;if(!layer.Visible||layer.Locked){_selection.RemoveWhere(id=>GetObject(id).LayerId==layer.Id);_selectionOrder.RemoveAll(id=>!_selection.Contains(id));}Notify("layer");}
    public void RemoveLayer(string id)
    {
        if(id=="default")throw new InvalidOperationException("Default layer cannot be removed.");GetLayer(id);foreach(var o in Objects.Where(o=>o.LayerId==id))_objects[o.Id]=o with{LayerId="default"};_layers.Remove(id);_layerOrder.Remove(id);if(ActiveLayer==id)ActiveLayer="default";Notify("layer");
    }
    public SceneObject Add(IGeometry geometry,string name="",string? layerId=null,string? id=null,ColorRgba? color=null,string? source=null)
    {
        ArgumentNullException.ThrowIfNull(geometry);layerId??=ActiveLayer;GetLayer(layerId);if(color is{} c&&!c.IsValid)throw new ArgumentException("Invalid object color.");if(_objects.Count>=100000)throw new InvalidOperationException("Object budget exceeded.");var o=new SceneObject(id??Guid.NewGuid().ToString("N"),name,geometry,layerId,color,Source:source);_objects.Add(o.Id,o);_objectOrder.Add(o.Id);Notify("add");return o;
    }
    public void Update(SceneObject value){var previous=GetObject(value.Id);if(previous.Locked||GetLayer(previous.LayerId).Locked)throw new InvalidOperationException("Locked objects cannot be edited.");GetLayer(value.LayerId);if(GetLayer(value.LayerId).Locked)throw new InvalidOperationException("Cannot move an object to a locked layer.");ArgumentNullException.ThrowIfNull(value.Geometry);if(value.Color is{} c&&!c.IsValid)throw new ArgumentException("Invalid color.");_objects[value.Id]=value;if(!CanEdit(value)&&_selection.Remove(value.Id))_selectionOrder.Remove(value.Id);Notify("update");}
    public bool CanEdit(SceneObject o)=>o.Visible&&!o.Locked&&GetLayer(o.LayerId) is{Visible:true,Locked:false};
    public void Delete(IEnumerable<string> ids)
    {
        var objects=ids.Distinct().Select(GetObject).ToArray();if(objects.Length==0)return;if(objects.Any(o=>o.Locked||GetLayer(o.LayerId).Locked))throw new InvalidOperationException("Locked objects cannot be deleted.");foreach(var o in objects){_objects.Remove(o.Id);_selection.Remove(o.Id);}_objectOrder.RemoveAll(id=>!_objects.ContainsKey(id));_selectionOrder.RemoveAll(id=>!_selection.Contains(id));Notify("delete");
    }
    public void Select(IEnumerable<string> ids,bool add=false){var objects=ids.Select(GetObject).ToArray();if(!add){_selection.Clear();_selectionOrder.Clear();}foreach(var o in objects.Where(CanEdit))if(_selection.Add(o.Id))_selectionOrder.Add(o.Id);_selectionView=null;_selectionOrderView=null;if(!_inTransaction)Changed?.Invoke(new("selection",Revision));}
    public SceneObject[] Transform(IEnumerable<string> ids,Transform3 transform,bool copy=false)
    {
        var objects=ids.Distinct().Select(GetObject).ToArray();if(copy&&(long)_objects.Count+objects.Length>100000)throw new InvalidOperationException("Object budget exceeded.");if(objects.Any(o=>!CanEdit(o)))throw new InvalidOperationException("Some selected objects are locked or hidden.");var prepared=objects.Select(o=>(Object:o,Geometry:o.Geometry.Transform(transform))).ToArray();var result=new List<SceneObject>();foreach(var(o,geometry)in prepared){var next=o with{Geometry=geometry};if(copy)next=Add(geometry,o.Name,o.LayerId,color:o.Color,source:o.Source);else Update(next);result.Add(next);}return result.ToArray();
    }
    public IEnumerable<SceneObject> VisibleObjects=>Objects.Where(o=>o.Visible&&GetLayer(o.LayerId).Visible);
    public ColorRgba ObjectColor(SceneObject value)=>value.Color??GetLayer(value.LayerId).Color;
    public DocumentSnapshot Snapshot()=>new(Name,Units,AbsoluteTolerance,AngleTolerance,ActiveLayer,Layers.ToArray(),Objects.ToArray(),_selectionOrder.ToArray());
    private void Restore(DocumentSnapshot snapshot){_objects=snapshot.Objects.ToDictionary(o=>o.Id);_layers=snapshot.Layers.ToDictionary(l=>l.Id);_objectOrder=snapshot.Objects.Select(o=>o.Id).ToList();_layerOrder=snapshot.Layers.Select(l=>l.Id).ToList();_selection.Clear();_selection.UnionWith(snapshot.Selection);_selectionOrder=snapshot.Selection.Distinct().ToList();_name=snapshot.Name;_activeLayer=snapshot.ActiveLayer;Notify("restore");}
    private static bool IsAsyncResult(Type type)=>typeof(Task).IsAssignableFrom(type)||type==typeof(ValueTask)
        ||type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(ValueTask<>);
    public T Transaction<T>(string name,Func<T> action)
    {
        if(IsAsyncResult(typeof(T))||action.Method.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute),false))throw new ArgumentException("Document transactions must be synchronous.");if(_inTransaction)throw new InvalidOperationException("Nested transactions are not supported.");var before=Snapshot();var revision=Revision;_inTransaction=true;try{var value=action();if(value is not null&&IsAsyncResult(value.GetType()))throw new InvalidOperationException("Document transactions must be synchronous.");if(Revision!=revision){_undo.Push(new(name,before,Snapshot()));if(_undo.Count>100){var keep=_undo.Take(100).Reverse().ToArray();_undo.Clear();foreach(var entry in keep)_undo.Push(entry);}_redo.Clear();}return value;}catch{Restore(before);throw;}finally{_inTransaction=false;Changed?.Invoke(new("transaction",Revision));}
    }
    public void Transaction(string name,Action action){if(action.Method.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute),false))throw new ArgumentException("Document transactions must be synchronous.");Transaction(name,()=>{action();return true;});}
    public bool Undo(){if(_inTransaction)throw new InvalidOperationException("Undo is not allowed inside a transaction.");if(!_undo.TryPop(out var entry))return false;_redo.Push(entry);Restore(entry.Before);return true;}
    public bool Redo(){if(_inTransaction)throw new InvalidOperationException("Redo is not allowed inside a transaction.");if(!_redo.TryPop(out var entry))return false;_undo.Push(entry);Restore(entry.After);return true;}
    public static SceneDocument FromSnapshot(DocumentSnapshot value)
    {
        if(value.Objects.Length>100000||value.Layers.Length>10000||!(value.AbsoluteTolerance>0)||!double.IsFinite(value.AbsoluteTolerance)||!(value.AngleTolerance>0)||!double.IsFinite(value.AngleTolerance))throw new ArgumentException("Invalid document snapshot.");
        var doc=new SceneDocument{Name=value.Name,Units=value.Units,AbsoluteTolerance=value.AbsoluteTolerance,AngleTolerance=value.AngleTolerance};doc.Restore(value);doc.GetLayer("default");doc.GetLayer(doc.ActiveLayer);foreach(var o in doc.Objects){doc.GetLayer(o.LayerId);if(o.Geometry is null||o.Color is{} color&&!color.IsValid)throw new ArgumentException("Invalid object.");}foreach(var layer in doc.Layers)if(!layer.Color.IsValid)throw new ArgumentException("Invalid layer color.");foreach(var id in doc.Selection)doc.GetObject(id);return doc;
    }
}
