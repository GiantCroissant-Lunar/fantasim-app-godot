using FantaSim.App.Timeline.Seam;
using Xunit;

namespace FantaSim.App.Timeline.Tests;

public sealed class TunnelFineSamplePolicyTests
{
    [Theory]
    [InlineData(2.9, 102L)]
    [InlineData(-2.9, 98L)]
    [InlineData(0.99, 100L)]
    [InlineData(-0.99, 100L)]
    public void Map_truncates_toward_zero_and_holds_sub_tick(double raw, long expected)
        => Assert.Equal(expected, TunnelFineSamplePolicy.Map(100, 200, raw, null, null).SampleTick);

    [Theory]
    [InlineData(-1000.0, 0L)]
    [InlineData(1000.0, 200L)]
    public void Map_clamps_to_canonical_bounds(double raw, long expected)
        => Assert.Equal(expected, TunnelFineSamplePolicy.Map(100, 200, raw, null, null).SampleTick);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Map_nonfinite_quantity_holds_base_tick(double raw)
        => Assert.Equal(100L, TunnelFineSamplePolicy.Map(100, 200, raw, null, null).SampleTick);

    [Fact]
    public void Positive_cadence_uses_sample_bucket_and_suppresses_same_bucket_texture()
    {
        var sample = TunnelFineSamplePolicy.Map(100, 200, 29.9, cadenceTicks: 10, previousBucket: 12);

        Assert.Equal(129L, sample.SampleTick);
        Assert.Equal(12L, sample.Bucket);
        Assert.False(sample.TextureChanged);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void Missing_or_nonpositive_cadence_uses_sample_tick(long? cadence)
    {
        var sample = TunnelFineSamplePolicy.Map(100, 200, 3.8, cadence, previousBucket: 100);

        Assert.Equal(103L, sample.Bucket);
        Assert.True(sample.TextureChanged);
    }

    [Fact]
    public void Resetting_previous_bucket_on_base_time_change_forces_refresh()
    {
        var sample = TunnelFineSamplePolicy.Map(120, 200, 0.0, cadenceTicks: 10, previousBucket: null);

        Assert.Equal(120L, sample.BaseTick);
        Assert.Equal(12L, sample.Bucket);
        Assert.True(sample.TextureChanged);
    }

    [Fact]
    public void Invalid_base_and_max_are_normalized_before_sampling()
    {
        var sample = TunnelFineSamplePolicy.Map(baseTick: 50, maxTick: -1, 10, null, null);

        Assert.Equal(0L, sample.BaseTick);
        Assert.Equal(0L, sample.SampleTick);
    }
}
