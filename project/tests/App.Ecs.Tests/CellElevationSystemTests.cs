using Arch.Core;
using FantaSim.App.Ecs.Cells;
using FantaSim.App.Ecs.Systems;
using Xunit;

namespace FantaSim.App.Ecs.Tests;

/// <summary>
/// Ordering proof for <see cref="CellElevationSystem"/> (sub-project B's core). The derivation is one
/// tunable step; what is pinned here is the resulting tier ORDERING the relief render depends on:
/// <c>collision &gt; continental-interior &gt; 0 &gt; young-ocean &gt; old-ocean</c>. The system runs over a
/// raw Arch world (same pattern as the field-reduction tests), and the pure <see cref="CellElevationSystem.Derive"/>
/// is checked directly so the formula has a single asserted source of truth.
/// </summary>
public sealed class CellElevationSystemTests
{
    // Crafted crust samples spanning the four tiers (magnitudes within the documented run envelope:
    // OrogenicPressure up to ~100, VolcanicActivity up to ~50, CrustAge up to ~1e7 ticks).
    private static readonly CrustSample Collision =
        new(ContinentalFraction: 1.0, OrogenicPressure: 100.0, VolcanicActivity: 0.0, CrustAgeTicks: 0.0);
    private static readonly CrustSample ContinentalInterior =
        new(ContinentalFraction: 1.0, OrogenicPressure: 0.0, VolcanicActivity: 0.0, CrustAgeTicks: 0.0);
    private static readonly CrustSample YoungOcean =
        new(ContinentalFraction: 0.0, OrogenicPressure: 0.0, VolcanicActivity: 0.0, CrustAgeTicks: 0.0);
    private static readonly CrustSample OldOcean =
        new(ContinentalFraction: 0.0, OrogenicPressure: 0.0, VolcanicActivity: 0.0, CrustAgeTicks: 1.0e7);

    [Fact]
    public void Derive_orders_collision_above_interior_above_zero_above_young_ocean_above_old_ocean()
    {
        double collision = CellElevationSystem.Derive(Collision);
        double interior = CellElevationSystem.Derive(ContinentalInterior);
        double youngOcean = CellElevationSystem.Derive(YoungOcean);
        double oldOcean = CellElevationSystem.Derive(OldOcean);

        // collision (continent–continent) sits highest; continental interior is up but below collision;
        // both land tiers are above sea level (0); ocean tiers below 0; old abyssal ocean lowest.
        Assert.True(collision > interior, $"collision {collision} should exceed interior {interior}");
        Assert.True(interior > 0, $"continental interior {interior} should be above sea level (0)");
        Assert.True(0 > youngOcean, $"young ocean {youngOcean} should be below sea level (0)");
        Assert.True(youngOcean > oldOcean, $"young ocean {youngOcean} should sit above old ocean {oldOcean}");
    }

    [Fact]
    public void Execute_writes_the_same_ordering_into_elevation_components_on_a_real_arch_world()
    {
        var world = Arch.Core.World.Create();
        try
        {
            var collision = world.Create(new CellId(0), Collision, default(Elevation));
            var interior = world.Create(new CellId(1), ContinentalInterior, default(Elevation));
            var youngOcean = world.Create(new CellId(2), YoungOcean, default(Elevation));
            var oldOcean = world.Create(new CellId(3), OldOcean, default(Elevation));

            new CellElevationSystem().Execute(world, deltaTime: 0f);

            double eCollision = world.Get<Elevation>(collision).Value;
            double eInterior = world.Get<Elevation>(interior).Value;
            double eYoung = world.Get<Elevation>(youngOcean).Value;
            double eOld = world.Get<Elevation>(oldOcean).Value;

            Assert.True(eCollision > eInterior, $"{eCollision} !> {eInterior}");
            Assert.True(eInterior > 0, $"{eInterior} !> 0");
            Assert.True(0 > eYoung, $"0 !> {eYoung}");
            Assert.True(eYoung > eOld, $"{eYoung} !> {eOld}");
        }
        finally
        {
            Arch.Core.World.Destroy(world);
        }
    }

    [Fact]
    public void Execute_only_deepens_oceanic_cells_with_age_continental_age_is_ignored()
    {
        // A continental cell (cf >= sea level) and an oceanic cell, both "old": only the oceanic cell
        // should drop with age — proving the oceanDepth term is gated on cf < SeaLevel.
        var oldContinent = new CrustSample(1.0, 0.0, 0.0, 1.0e7);
        var youngContinent = new CrustSample(1.0, 0.0, 0.0, 0.0);

        Assert.Equal(CellElevationSystem.Derive(youngContinent), CellElevationSystem.Derive(oldContinent), 6);
        Assert.True(
            CellElevationSystem.Derive(YoungOcean) > CellElevationSystem.Derive(OldOcean),
            "oceanic cells must deepen with age");
    }
}
