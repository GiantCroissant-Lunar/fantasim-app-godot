using System;
using System.Text.Json.Nodes;

namespace FantaSim.App.Render;

/// <summary>
/// Godot-free payload parsing for the <c>render.mantle</c> command (M-A mantle x-ray ingress).
/// Mirrors <see cref="CutawayRequest"/>: the T4 seam owns the Godot side (wiring the parsed flag to
/// the binder); this type owns the pure-C# parsing so it is unit-testable without Godot.
///
/// <para>Accepted payload: <c>{"enabled":true|false}</c>. An empty payload or a missing
/// <c>enabled</c> key activates the view (the common case is "turn it on"); a non-boolean
/// <c>enabled</c> throws <see cref="ArgumentException"/> (reported as <c>ok:false</c>).</para>
/// </summary>
public readonly record struct MantleRequest(bool Enabled);

public static class MantleRequestParser
{
    public static MantleRequest Parse(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new MantleRequest(Enabled: true);

        var payload = JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new ArgumentException("render.mantle payload must be a JSON object.");

        if (payload["enabled"] is not { } node)
            return new MantleRequest(Enabled: true);

        try
        {
            return new MantleRequest(node.GetValue<bool>());
        }
        catch (InvalidOperationException ex)
        {
            throw new ArgumentException("render.mantle 'enabled' must be a JSON boolean.", ex);
        }
    }
}
