using System.Collections.Immutable;
using System.Globalization;
using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

public sealed record CanvasCreation(string Component, ImmutableDictionary<string, object?> Parameters, string Label)
{
    public string Apply(GraphSession session, CanvasPoint position)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y)) throw new ArgumentException("Finite insertion point required.");
        session.Graph.Registry.Get(Component);
        string? id = null;
        session.Edit("Create " + Component, g => id = g.Add(Component, position.X, position.Y, Parameters).Id);
        return id!;
    }
}

/// <summary>Bounded literal initialization, never an expression evaluator. These
/// familiar Grasshopper shortcuts create actual registry components in one edit.</summary>
public static class CanvasCreationParser
{
    public const int MaximumQueryLength = 8192;
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["+"]="Add", ["-"]="Subtract", ["*"]="Multiply", ["/"]="Divide", ["^"]="Power",
        ["Number Slider"]="Number", ["Boolean Toggle"]="Boolean", ["Text Panel"]="Panel"
    };
    public static bool TryParse(string query, ComponentRegistry registry, out CanvasCreation? creation, out string? error)
    {
        ArgumentNullException.ThrowIfNull(query); ArgumentNullException.ThrowIfNull(registry);
        creation = null; error = null;
        if (query.Length > MaximumQueryLength) { error = "Creation input exceeds 8,192 characters."; return false; }
        var text = query.Trim(); var literal = query.TrimStart();
        var parameters = ImmutableDictionary<string, object?>.Empty;
        string? component = null;
        if (literal.StartsWith("//", StringComparison.Ordinal) || literal.StartsWith('"'))
        {
            var content = literal.StartsWith("//", StringComparison.Ordinal) ? literal[2..] : literal[1..];
            if (literal.StartsWith('"') && content.EndsWith('"')) content = content[..^1];
            component = "Panel"; parameters = parameters.Add("text", content);
        }
        else if (bool.TryParse(text, out var flag)) { component = "Boolean"; parameters = parameters.Add("value", flag); }
        else if (Aliases.TryGetValue(text, out var alias)) component = alias;
        else if (text.Contains('<') || text.Length > 0 && (char.IsAsciiDigit(text[0]) || text[0] is '.' or '+' or '-'))
        {
            var parts = text.Split('<', StringSplitOptions.TrimEntries);
            if (parts.Length is not (1 or 3)) { error = "Use a number or minimum < value < maximum."; return false; }
            var numbers = new double[parts.Length];
            for (var i = 0; i < parts.Length; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !double.IsFinite(numbers[i]))
                { error = "Slider values must be finite numeric literals, using a dot decimal separator."; return false; }
            var value = numbers[parts.Length == 3 ? 1 : 0];
            var min = parts.Length == 3 ? numbers[0] : Math.Min(0, value);
            var max = parts.Length == 3 ? numbers[2] : Math.Max(100, value);
            if (min >= max || value < min || value > max) { error = "Slider range must increase and contain the value."; return false; }
            var digits = parts.Max(Precision);
            // More precision than the editor supports is retained as continuous,
            // never silently rounded away by creation shorthand.
            int? places = digits <= 12 ? digits : null;
            _ = new NumberSliderSettings(min, max, places);
            component = "Number"; parameters = parameters.Add("value", value).Add("min", min).Add("max", max);
            if (places is { } p) parameters = parameters.Add("decimalPlaces", p);
        }
        else component = registry.Definitions.FirstOrDefault(d => d.Id.Equals(text, StringComparison.OrdinalIgnoreCase))?.Id;
        if (component is null) { error = "No executable component or supported literal matches this input."; return false; }
        if (!registry.Definitions.Any(d => d.Id == component)) { error = "The required component is not registered: " + component; return false; }
        creation = new(component, parameters, component + (parameters.Count > 0 ? " (initialized)" : "")); return true;
    }
    private static int Precision(string literal)
    {
        var e = literal.IndexOfAny(['e', 'E']); var mantissa = e < 0 ? literal : literal[..e];
        var dot = mantissa.IndexOf('.'); var digits = dot < 0 ? 0 : mantissa.Length - dot - 1;
        if (e < 0) return digits;
        return int.TryParse(literal.AsSpan(e + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var exponent)
            ? (int)Math.Clamp((long)digits - exponent, 0, 13) : 13;
    }
}

/// <summary>Search result reuse for repeated native TextChanged notifications.
/// Registration invalidates cached results through the registry's snapshot identity.</summary>
public sealed class CanvasSearchIndex
{
    private IReadOnlyList<ComponentDefinition>? _definitions;
    private string? _query;
    private ImmutableArray<string> _results = [];
    public long BuildCount { get; private set; }
    public ImmutableArray<string> Search(ComponentRegistry registry, string query)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(query);
        var definitions = registry.Definitions;
        if (ReferenceEquals(definitions, _definitions) && query == _query) return _results;
        _definitions = definitions; _query = query; BuildCount++;
        if (query.Length > CanvasCreationParser.MaximumQueryLength || string.IsNullOrWhiteSpace(query)) return _results = [];
        var text = query.Trim(); var matches = ImmutableArray.CreateBuilder<string>();
        if (CanvasCreationParser.TryParse(text, registry, out var creation, out _) && creation!.Parameters.Count > 0) matches.Add(query);
        foreach (var d in definitions)
        {
            if (d.Id.Contains(text, StringComparison.OrdinalIgnoreCase) || d.Category.Contains(text, StringComparison.OrdinalIgnoreCase)) matches.Add(d.Id);
            if (matches.Count == 20) break;
        }
        if (matches.Count == 0 && CanvasCreationParser.TryParse(text, registry, out var alias, out _)) matches.Add(alias!.Component);
        return _results = matches.ToImmutable();
    }
}
