using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Tests that WorldGenerationGraphFamilySource.SetTick does NOT recompose the
/// effective graph or raise Changed when the set of active graph-scoped overrides
/// at the new tick is identical to the set active at the last composed tick.
/// Only an override-range boundary crossing (a genuine effective-graph change)
/// triggers recompose + Changed.
/// </summary>
public sealed class RecomposeGatingTests
{
    [Fact]
    public void SetTick_NoActiveOverrides_FiresChangedAtMostOnce_NotOncePerTick()
    {
        // Default family: GraphOverrides is empty, so no override is ever active.
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "recompose-gating",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var changedCount = 0;
        source.Changed += () => changedCount++;

        // Tick through several distinct ticks inside the same regime.
        source.SetTick(1);
        source.SetTick(5);
        source.SetTick(10);
        source.SetTick(50);
        source.SetTick(100);

        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void SetTick_CrossingOverrideBoundary_RaisesChanged_TickingWithinRangeDoesNot()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily() with
        {
            GraphOverrides = new[]
            {
                new WorldGenerationGraphScopedOverride(
                    OverrideId: "fast_geosphere",
                    GraphId: WorldGenerationGraphDefaults.GeosphereGraphId,
                    Label: "Fast geosphere",
                    Range: new WorldGenerationTickRange(10, 20),
                    StrengthOrder: 0,
                    Edits: new[]
                    {
                        new WorldGenerationGraphEdit(
                            Kind: "set-param",
                            NodeId: "options",
                            ParamKey: "frequency",
                            ParamValue: "5"),
                    }),
            },
        };

        // Start BEFORE the override range.
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "recompose-gating",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 5,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var changedCount = 0;
        source.Changed += () => changedCount++;

        // Ticking inside the no-override zone: no boundary crossed.
        source.SetTick(6);
        source.SetTick(9);
        Assert.Equal(0, changedCount);

        // Cross INTO the override range [10, 20]: effective graph changes.
        source.SetTick(10);
        Assert.Equal(1, changedCount);

        // Ticking WITHIN the range: same override active, no boundary.
        source.SetTick(15);
        source.SetTick(20);
        Assert.Equal(1, changedCount);

        // Cross OUT of the range: effective graph changes back.
        source.SetTick(21);
        Assert.Equal(2, changedCount);
    }
}
