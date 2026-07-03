using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace FantaSim.App.Render;

/// <summary>
/// Godot-free payload parsing for the <c>render.cutaway</c> command (look-dev ingress for the
/// cutaway wedge interaction, §5c W3a). Mirrors <see cref="ScreenshotRequest"/>: the T4 seam owns
/// the Godot side (wiring the parsed values to the binder); this type owns the pure-C# parsing
/// so it is unit-testable without Godot.
///
/// <para>Accepted payload: <c>{"azimuthDeg":N,"widthDeg":N}</c> where both are numbers. Width 0
/// (or missing) clears the cutaway (inactive). Width is clamped to [0, 360]; azimuth is normalized
/// to [0, 360). Missing azimuth defaults to 0.</para>
/// </summary>
public readonly record struct CutawayRequest(double AzimuthDeg, double WidthDeg)
{
    public bool IsInactive => WidthDeg <= 0.0;
}

public static class CutawayRequestParser
{
    public static CutawayRequest Parse(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new CutawayRequest(0.0, 0.0);

        var payload = JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new ArgumentException("render.cutaway payload must be a JSON object.");

        double azimuth = 0.0;
        double width = 0.0;

        if (payload["azimuthDeg"] is { } azNode)
        {
            azimuth = ReadDouble(azNode, "azimuthDeg");
        }

        if (payload["widthDeg"] is { } wNode)
        {
            width = ReadDouble(wNode, "widthDeg");
        }

        if (width < 0.0) width = 0.0;
        if (width > 360.0) width = 360.0;

        azimuth = NormalizeAzimuth(azimuth);

        return new CutawayRequest(azimuth, width);
    }

    private static double NormalizeAzimuth(double deg)
    {
        deg %= 360.0;
        if (deg < 0.0) deg += 360.0;
        return deg;
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
                $"render.cutaway '{fieldName}' must be a JSON number.", ex);
        }
    }
}