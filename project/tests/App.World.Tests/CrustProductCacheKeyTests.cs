using FantaSim.App.World.Services;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// TDD regression guard for the crust-product cache-key completion
/// (vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md §1.2): the key must carry every
/// world-identity dimension the render options + generation graph expose -- Seed and
/// SpinRateRadiansPerMegaAnnum (mirroring the sibling globe-reconstructor cache key,
/// Service.cs:_globeReconstructorKey) plus GraphRevision (mirroring CrustGenerationTriggerKey,
/// CrustGenerationTriggerPolicy.cs). Before the fix the key carried only (Frequency, SnapshotTick),
/// so two worlds differing ONLY by Seed, SpinRate, or GraphRevision aliased onto the same cache
/// slot -- these tests reproduce that defect at the key-equality level (the key is now `internal`
/// specifically so this project, covered by App.World's InternalsVisibleTo, can assert on it
/// directly rather than only indirectly through Service behavior).
/// </summary>
public sealed class CrustProductCacheKeyTests
{
    private static Service.CrustProductCacheKey Key(
        int seed = 7,
        int frequency = 4,
        double spinRate = 0.02,
        int graphRevision = 1,
        long snapshotTick = 105_000_000L)
        => new(seed, frequency, spinRate, graphRevision, snapshotTick);

    [Fact]
    public void Keys_WithIdenticalFields_AreEqual()
    {
        Assert.Equal(Key(), Key());
    }

    [Fact]
    public void Keys_WithDifferentSeed_AreNotEqual()
    {
        Assert.NotEqual(Key(seed: 7), Key(seed: 8));
    }

    [Fact]
    public void Keys_WithDifferentSpinRate_AreNotEqual()
    {
        Assert.NotEqual(Key(spinRate: 0.02), Key(spinRate: 0.05));
    }

    [Fact]
    public void Keys_WithDifferentGraphRevision_AreNotEqual()
    {
        Assert.NotEqual(Key(graphRevision: 1), Key(graphRevision: 2));
    }

    [Fact]
    public void Keys_WithDifferentFrequency_AreNotEqual()
    {
        Assert.NotEqual(Key(frequency: 4), Key(frequency: 3));
    }

    [Fact]
    public void Keys_WithDifferentSnapshotTick_AreNotEqual()
    {
        Assert.NotEqual(Key(snapshotTick: 105_000_000L), Key(snapshotTick: 110_000_000L));
    }
}
