using System.Collections.Immutable;

namespace NurbsScene.Parametric;

public sealed class ComponentRegistry
{
    private readonly Dictionary<string,ComponentDefinition> _definitions=[];
    private IReadOnlyList<ComponentDefinition>? _view;
    public int Count=>_definitions.Count;
    /// <summary>Retained read-only snapshot in registration order. Older views
    /// remain stable after registration; callers cannot mutate the cached array.
    /// Registration follows the registry's existing owner-thread contract.</summary>
    public IReadOnlyList<ComponentDefinition> Definitions=>_view??=Array.AsReadOnly(_definitions.Values.ToArray());
    public void Register(ComponentDefinition definition){if(definition.Inputs.Select(p=>p.Name).Distinct().Count()!=definition.Inputs.Length||definition.Outputs.Select(p=>p.Name).Distinct().Count()!=definition.Outputs.Length)throw new ArgumentException("Duplicate port names.");_definitions.Add(definition.Id,definition with { Inputs=definition.Inputs.Select(p=>p with {Default=ParameterValues.FreezeValue(p.Default)}).ToImmutableArray() });_view=null;}
    public ComponentDefinition Get(string id)=>_definitions.TryGetValue(id,out var d)?d:throw new NotSupportedException($"Missing component implementation: {id}.");
}
