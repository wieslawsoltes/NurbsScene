using NurbsScene.Parametric;

namespace NurbsScene.Presentation;

/// <summary>Immutable binary64 slider range, independent of Uno. Automatic
/// precision preserves legacy continuous scrubbing. Explicit precision rounds
/// generated values (ties to even) before clamping; exact endpoints remain
/// reachable. Integer sliders retain their existing signed 32-bit output type.</summary>
public readonly record struct NumberSliderSettings
{
    public double Minimum { get; }
    public double Maximum { get; }
    public int? DecimalPlaces { get; }
    public bool Integer { get; }

    public NumberSliderSettings(double minimum, double maximum, int? decimalPlaces = null, bool integer = false)
    {
        Minimum = minimum; Maximum = maximum; DecimalPlaces = integer ? 0 : decimalPlaces; Integer = integer;
        if (Error(minimum, maximum, decimalPlaces, integer) is { } error) throw new ArgumentException(error);
    }
    private static string? Error(double minimum, double maximum, int? digits, bool integer)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum >= maximum)
            return "Minimum and maximum must be finite, with minimum below maximum.";
        if (digits is < 0 or > 12) return "Decimal places must be Automatic or an integer from 0 to 12.";
        if (integer && (minimum < int.MinValue || maximum > int.MaxValue ||
            minimum != Math.Truncate(minimum) || maximum != Math.Truncate(maximum)))
            return "Integer slider bounds must be whole signed 32-bit values.";
        return null;
    }
    public void Validate()
    {
        if (Error(Minimum, Maximum, DecimalPlaces, Integer) is { } error) throw new ArgumentException(error);
    }
    private static bool Number(GraphNode node, string key, double fallback, out double value)
    {
        if (!node.Parameters.TryGetValue(key, out var input)) { value = fallback; return true; }
        value = input switch { double d => d, int i => i, _ => double.NaN };
        return double.IsFinite(value);
    }
    /// <summary>Reads immutable parameters without allocation on valid input.
    /// Invalid external slider metadata is diagnosed rather than converted through
    /// arbitrary objects or allowed to throw from a rendering callback. Legacy
    /// finite values outside the display range are retained until an explicit edit.</summary>
    public static bool TryRead(GraphNode node, out NumberSliderSettings settings, out double value, out string? error)
    {
        ArgumentNullException.ThrowIfNull(node);
        settings = default; value = 0; error = null;
        if (node.Component is not ("Number" or "Integer")) { error = "Select a Number or Integer slider."; return false; }
        if (!Number(node, "min", 0, out var min) || !Number(node, "max", 100, out var max) ||
            !Number(node, "value", 1, out value))
        { error = "Slider value and bounds must be finite numbers."; return false; }
        int? digits = null;
        if (node.Parameters.ContainsKey("decimalPlaces"))
        {
            if (!Number(node, "decimalPlaces", 0, out var precision) || precision < 0 || precision > 12 || precision != Math.Truncate(precision))
            { error = "Decimal places must be Automatic or an integer from 0 to 12."; return false; }
            digits = (int)precision;
        }
        var integer = node.Component == "Integer";
        error = Error(min, max, digits, integer);
        if (error is not null) return false;
        if (integer && (value < int.MinValue || value > int.MaxValue || value != Math.Truncate(value)))
        { error = "Integer slider value must be a whole signed 32-bit value."; return false; }
        settings = new(min, max, digits, integer); return true;
    }
    public double Fraction(double value)
    {
        Validate();
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (value <= Minimum) return 0;
        if (value >= Maximum) return 1;
        var span = Maximum - Minimum;
        // An opposite-sign extreme range can have a finite midpoint and infinite
        // width. Halving first avoids that overflow; narrow spans use subtraction
        // before division so nearby large same-sign endpoints remain distinct.
        return Math.Clamp(double.IsFinite(span) ? (value - Minimum) / span :
            (value / 2 - Minimum / 2) / (Maximum / 2 - Minimum / 2), 0, 1);
    }
    public double ValueAt(double fraction)
    {
        Validate();
        if (!double.IsFinite(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction));
        if (fraction <= 0) return Minimum;
        if (fraction >= 1) return Maximum;
        var span = Maximum - Minimum;
        var value = double.IsFinite(span) ? Minimum + fraction * span :
            Minimum * (1 - fraction) + Maximum * fraction;
        return Normalize(Math.Clamp(value, Minimum, Maximum));
    }
    public double Normalize(double value)
    {
        Validate();
        if (!double.IsFinite(value) || value < Minimum || value > Maximum)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must lie inside the slider range.");
        // Preserve exact endpoints even when they have more digits than the chosen
        // interior rounding precision. Math.Round avoids scaling large values by
        // 10^digits and therefore does not overflow a representable large input.
        if (value == Minimum || value == Maximum) return value;
        return DecimalPlaces is { } digits ? Math.Clamp(Math.Round(value, digits, MidpointRounding.ToEven), Minimum, Maximum) : value;
    }
}

/// <summary>Atomic, stale-edit-guarded slider settings changes. Unrelated parameters,
/// component type, identities, wires and flags are preserved. Opening or cancelling
/// a UI editor does not call this API; one Apply becomes one history entry.</summary>
public static class NumberSliderEditing
{
    public static bool Apply(GraphSession session, Graph expectedGraph, GraphNode expectedNode,
        NumberSliderSettings settings, double value, string? name)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(expectedGraph);
        ArgumentNullException.ThrowIfNull(expectedNode); settings.Validate();
        if (!ReferenceEquals(session.Graph, expectedGraph) ||
            !ReferenceEquals(expectedGraph.GetNode(expectedNode.Id), expectedNode))
            throw new InvalidOperationException("The slider changed while its settings were being edited. Reopen the editor.");
        if (expectedNode.Component is not ("Number" or "Integer") || settings.Integer != (expectedNode.Component == "Integer"))
            throw new ArgumentException("Slider settings must retain the component's numeric type.");
        if (name is { Length: > 256 }) throw new ArgumentException("Slider names are limited to 256 characters.");
        value = settings.Normalize(value);
        if (NumberSliderSettings.TryRead(expectedNode, out var current, out var oldValue, out _) &&
            current == settings && oldValue == value && expectedNode.Name == name) return false;
        var parameters = expectedNode.Parameters.SetItem("min", settings.Minimum).SetItem("max", settings.Maximum).SetItem("value", value);
        parameters = settings.DecimalPlaces is { } places ? parameters.SetItem("decimalPlaces", places) : parameters.Remove("decimalPlaces");
        var updated = expectedNode with { Name = name, Parameters = parameters };
        session.Edit("Edit number slider", graph => graph.Update(updated));
        return true;
    }
}
