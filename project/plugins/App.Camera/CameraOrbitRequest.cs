using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FantaSim.App.Camera;

public static class CameraOrbitLimits
{
    public const double MinPitchDeg = -85.0;
    public const double MaxPitchDeg = 85.0;
    public const double MinDistance = 1.5;
    public const double MaxDistance = 8.0;

    public static double ClampPitch(double pitchDeg)
        => Math.Clamp(RequireFinite(pitchDeg, "pitchDeg"), MinPitchDeg, MaxPitchDeg);

    public static double ClampDistance(double distance)
        => Math.Clamp(RequireFinite(distance, "distance"), MinDistance, MaxDistance);

    public static double RequireFinite(double value, string fieldName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException($"camera.orbit '{fieldName}' must be a finite JSON number.");

        return value;
    }
}

/// <summary>
/// Godot-free payload parsing for the <c>camera.orbit</c> command. The T4 seam owns the
/// PhantomCamera application; this type owns the pure JSON parsing so ingress behavior is
/// unit-testable without Godot.
///
/// <para>Accepted payload: <c>{"yawDeg":N,"pitchDeg":N,"distance":N}</c>. All fields are optional:
/// absent means keep the current orbit value. Pitch is clamped to +/-85 degrees; distance is
/// clamped to the default globe orbit spring bounds [1.5, 8.0].</para>
/// </summary>
public readonly record struct CameraOrbitRequest(double? YawDeg, double? PitchDeg, double? Distance)
{
    public bool HasChanges => YawDeg.HasValue || PitchDeg.HasValue || Distance.HasValue;
}

public static class CameraOrbitRequestParser
{
    public static CameraOrbitRequest Parse(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new CameraOrbitRequest(null, null, null);

        JsonObject payload;
        try
        {
            payload = JsonNode.Parse(payloadJson) as JsonObject
                ?? throw new ArgumentException("camera.orbit payload must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("camera.orbit payload must be valid JSON.", ex);
        }

        var yawDeg = ReadOptionalDouble(payload, "yawDeg");
        var pitchDeg = ReadOptionalDouble(payload, "pitchDeg");
        var distance = ReadOptionalDouble(payload, "distance");

        if (yawDeg.HasValue)
            yawDeg = CameraOrbitLimits.RequireFinite(yawDeg.Value, "yawDeg");
        if (pitchDeg.HasValue)
            pitchDeg = CameraOrbitLimits.ClampPitch(pitchDeg.Value);
        if (distance.HasValue)
            distance = CameraOrbitLimits.ClampDistance(distance.Value);

        return new CameraOrbitRequest(yawDeg, pitchDeg, distance);
    }

    private static double? ReadOptionalDouble(JsonObject payload, string fieldName)
    {
        if (payload[fieldName] is not { } node)
            return null;

        try
        {
            return node.GetValue<double>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new ArgumentException(
                $"camera.orbit '{fieldName}' must be a JSON number.", ex);
        }
    }
}
