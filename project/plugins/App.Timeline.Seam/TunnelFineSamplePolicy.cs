using System;

namespace FantaSim.App.Timeline.Seam;

internal readonly record struct TunnelFineSample(
    long BaseTick,
    long SampleTick,
    long Bucket,
    bool TextureChanged);

/// <summary>Maps presentation-only fractional fine movement to an honest canonical render sample.
/// The visible readout remains fractional; textures change only after a whole tick/cadence bucket.</summary>
internal static class TunnelFineSamplePolicy
{
    internal static TunnelFineSample Map(
        long baseTick,
        long maxTick,
        double rawTickQuantity,
        long? cadenceTicks,
        long? previousBucket)
    {
        maxTick = Math.Max(0L, maxTick);
        baseTick = Math.Clamp(baseTick, 0L, maxTick);
        var sampleTick = baseTick;
        if (double.IsFinite(rawTickQuantity))
        {
            if (rawTickQuantity <= -baseTick)
                sampleTick = 0L;
            else if (rawTickQuantity >= maxTick - baseTick)
                sampleTick = maxTick;
            else
                sampleTick = baseTick + (long)Math.Truncate(rawTickQuantity);
        }

        var bucket = cadenceTicks is > 0 ? sampleTick / cadenceTicks.Value : sampleTick;
        return new TunnelFineSample(baseTick, sampleTick, bucket, previousBucket != bucket);
    }
}
