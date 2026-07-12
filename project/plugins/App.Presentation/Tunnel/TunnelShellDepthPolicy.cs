using System;
using System.Collections.Generic;

namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelShellDepthBand(float NearZ, float FarZ, float Value);

internal static class TunnelShellDepthPolicy
{
    internal const int BandCount = 4;

    internal static IReadOnlyList<TunnelShellDepthBand> Plan(float mouthZ, float throatZ)
    {
        if (!float.IsFinite(mouthZ) || !float.IsFinite(throatZ) || throatZ >= mouthZ)
            return Array.Empty<TunnelShellDepthBand>();

        var bands = new TunnelShellDepthBand[BandCount];
        for (var index = 0; index < BandCount; index++)
        {
            var nearFraction = index / (float)BandCount;
            var farFraction = (index + 1f) / BandCount;
            var valueFraction = index / (float)(BandCount - 1);
            bands[index] = new TunnelShellDepthBand(
                NearZ: mouthZ + ((throatZ - mouthZ) * nearFraction),
                FarZ: mouthZ + ((throatZ - mouthZ) * farFraction),
                Value: 0.075f + ((0.018f - 0.075f) * valueFraction));
        }
        return bands;
    }
}
