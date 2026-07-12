using System;
using System.Collections.Generic;

namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelCorridorDepthBand(
    float NearZ,
    float FarZ,
    float DepthFraction);

internal readonly record struct TunnelCorridorWallTone(
    float Brightness,
    bool EmissionEnabled);

/// <summary>
/// Keeps time-bearing corridor walls on the canonical current-to-future interval. The physical
/// shell alone spans back toward the mouth; a wall may not cross behind the current plane into the
/// camera's near field, where it would project as a flat radial wedge.
/// </summary>
internal static class TunnelCorridorDepthPolicy
{
    internal const int BandCount = 4;

    internal static IReadOnlyList<TunnelCorridorDepthBand> Plan(
        float currentPlaneZ,
        float throatZ)
    {
        if (!float.IsFinite(currentPlaneZ)
            || !float.IsFinite(throatZ)
            || throatZ >= currentPlaneZ)
        {
            return Array.Empty<TunnelCorridorDepthBand>();
        }

        var bands = new TunnelCorridorDepthBand[BandCount];
        for (var index = 0; index < BandCount; index++)
        {
            var nearFraction = index / (float)BandCount;
            var farFraction = (index + 1f) / BandCount;
            bands[index] = new TunnelCorridorDepthBand(
                NearZ: currentPlaneZ + ((throatZ - currentPlaneZ) * nearFraction),
                FarZ: currentPlaneZ + ((throatZ - currentPlaneZ) * farFraction),
                DepthFraction: nearFraction);
        }

        return bands;
    }

    internal static TunnelCorridorWallTone ToneAt(float depthFraction)
    {
        var clamped = Math.Clamp(depthFraction, 0f, 1f);
        return new TunnelCorridorWallTone(
            Brightness: 0.58f + ((0.18f - 0.58f) * clamped),
            EmissionEnabled: false);
    }
}
