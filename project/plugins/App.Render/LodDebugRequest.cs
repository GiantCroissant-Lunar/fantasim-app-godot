using System;
using System.Text.Json.Nodes;

namespace FantaSim.App.Render;

/// <summary>
/// Godot-free payload parsing for the <c>render.lod</c> debug toggle command
/// (directive 4, slice 1). Mirrors <see cref="MantleRequest"/>: the T4 seam owns the
/// Godot side (wiring the parsed mode to the binder); this type owns the pure-C# parsing
/// so it is unit-testable without Godot.
///
/// <para>Accepted payload: <c>{"mode":"off"|"wireframe"|"density"}</c>. An empty payload
/// or a missing <c>mode</c> key defaults to <c>"off"</c> (clears the debug overlay). An
/// unrecognized string throws <see cref="ArgumentException"/> (reported as <c>ok:false</c>).</para>
/// </summary>
public readonly record struct LodDebugRequest(LodDebugMode Mode)
{
    public bool IsInactive => Mode == LodDebugMode.Off;
}

public enum LodDebugMode
{
    Off = 0,
    Wireframe = 1,
    Density = 2,
}

public static class LodDebugRequestParser
{
    public static LodDebugRequest Parse(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new LodDebugRequest(LodDebugMode.Off);

        var payload = JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new ArgumentException("render.lod payload must be a JSON object.");

        if (payload["mode"] is not { } node)
            return new LodDebugRequest(LodDebugMode.Off);

        string modeStr;
        try
        {
            modeStr = node.GetValue<string>();
        }
        catch (InvalidOperationException ex)
        {
            throw new ArgumentException("render.lod 'mode' must be a JSON string.", ex);
        }

        return modeStr.ToLowerInvariant() switch
        {
            "off" => new LodDebugRequest(LodDebugMode.Off),
            "wireframe" => new LodDebugRequest(LodDebugMode.Wireframe),
            "density" => new LodDebugRequest(LodDebugMode.Density),
            _ => throw new ArgumentException(
                $"render.lod 'mode' must be one of: off, wireframe, density. Got '{modeStr}'."),
        };
    }
}