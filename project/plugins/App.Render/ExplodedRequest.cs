using System;
using System.Text.Json.Nodes;

namespace FantaSim.App.Render;

/// <summary>
/// Godot-free payload parsing for the <c>render.exploded</c> command (M-B look-dev ingress for the
/// exploded solid-crust view). Mirrors <see cref="CutawayRequest"/>: the T4 seam owns the Godot side
/// (wiring the parsed factor to the binder); this type owns the pure-C# parsing so it is
/// unit-testable without Godot.
///
/// <para>Accepted payload: <c>{"factor":N}</c> where N is a number in [0, 1]. Factor 0 is the
/// ASSEMBLED solid crust (plates in place, thickness/side walls visible at the silhouette but not
/// translated apart); factor 1 is the maximum radial explode. Missing factor defaults to 0.0
/// (assembled). Factor is clamped to [0, 1].</para>
/// </summary>
public readonly record struct ExplodedRequest(double Factor)
{
    /// <summary>True when the crust is assembled (factor 0) — solids drawn in place, no translation.</summary>
    public bool IsAssembled => Factor <= 0.0;
}

public static class ExplodedRequestParser
{
    public static ExplodedRequest Parse(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new ExplodedRequest(0.0);

        var payload = JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new ArgumentException("render.exploded payload must be a JSON object.");

        double factor = 0.0;

        if (payload["factor"] is { } fNode)
            factor = ReadDouble(fNode, "factor");

        if (double.IsNaN(factor))
            throw new ArgumentException("render.exploded 'factor' must be a finite JSON number.");

        if (factor < 0.0) factor = 0.0;
        if (factor > 1.0) factor = 1.0;

        return new ExplodedRequest(factor);
    }

    private static double ReadDouble(JsonNode node, string fieldName)
    {
        try
        {
            return node.GetValue<double>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new ArgumentException(
                $"render.exploded '{fieldName}' must be a JSON number.", ex);
        }
    }
}
