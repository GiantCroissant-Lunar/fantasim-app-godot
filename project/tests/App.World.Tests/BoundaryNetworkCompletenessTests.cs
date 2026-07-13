using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Contracts.Units;
using TimeDete.Time.Primitives;
using UnifyCell;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Characterization / regression tests for the plate-boundary NETWORK completeness at the
/// mobile-plate regime. The app builds its onset roster via
/// <see cref="OnsetRoster.Build"/> with the default render-options seed/frequency and calls
/// <see cref="GlobeReconstructor.BuildBoundaryArcsAt"/> at the onset tick. Every plate must be
/// enclosed by closed, typed boundary polylines: every plate id appears in at least one arc,
/// every topology boundary yields at least one arc, and every arc carries >= 2 ordered points.
/// </summary>
public sealed class BoundaryNetworkCompletenessTests
{
    // The app's default render options (WorldGenerationRenderOptions.Default): seed 7, freq 3.
    private const int AppSeed = 7;
    private const int AppFrequency = 3;

    private static GlobeReconstructor BuildAppReconstructor(out long onsetTick)
    {
        onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var roster = OnsetRoster.Build(AppSeed, onsetTick, AppFrequency);
        var schedule = SphereRegimeScheduleDefaults.GeosphereFor(onsetTick);
        return GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, AppFrequency);
    }

    [Fact]
    public void EveryPlateIdAppearsInAtLeastOneArc()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        var snapshot = model.BuildGlobeAt(onsetTick);
        var arcs = model.BuildBoundaryArcsAt(onsetTick);

        var platesInArcs = new HashSet<int>();
        foreach (var arc in arcs)
        {
            platesInArcs.Add(arc.PlateA);
            platesInArcs.Add(arc.PlateB);
        }

        var allPlates = snapshot.Plates.Select(p => p.PlateId).ToHashSet();
        Assert.NotEmpty(allPlates);
        Assert.Subset(platesInArcs, allPlates);
    }

    [Fact]
    public void EveryTopologyBoundaryYieldsAnArc()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        var arcs = model.BuildBoundaryArcsAt(onsetTick);

        // Re-derive the topology truth the same way BuildBoundaryArcsAt does, to compare.
        var tess = new GeodesicSphereTessellation(AppFrequency);
        var roster = OnsetRoster.Build(AppSeed, onsetTick, AppFrequency);
        var plates = roster.SeedPlatesAt(onsetTick);
        var topology = PlateTopologyBuilder.Build(tess, plates);
        var boundaries = PlateTopologyBuilder.ClassifyBoundariesAt(
            tess, plates, topology, new CanonicalTick(onsetTick));

        var arcPairs = arcs.Select(a => (a.PlateA, a.PlateB)).ToHashSet();
        foreach (var b in boundaries)
        {
            Assert.Contains((b.PlateA, b.PlateB), arcPairs);
        }
    }

    [Fact]
    public void EveryArcHasAtLeastTwoPoints()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        var arcs = model.BuildBoundaryArcsAt(onsetTick);

        Assert.NotEmpty(arcs);
        Assert.All(arcs, a => Assert.True(a.Points.Count >= 2, $"arc {a.PlateA}|{a.PlateB} has {a.Points.Count} points"));
    }

    [Fact]
    public void ArcKindsCoverActiveTypes()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        // Check past onset: at the onset tick the rotation is identity (the reference frame), so the
        // classification reflects the seed geometry. The full motion vocabulary (Convergent +
        // Divergent + Transform) emerges once plates have drifted. With the 2026-07-07 rate
        // calibration (default drift 0.02 -> 0.0035 rad per anchor unit, ~5.7x slower to match the
        // real-plate median; see tools/rates/2026-07-07-rate-calibration-report.md) the same
        // relative displacement needs a ~5.7x longer window: the old 8 Ma gate scales to ~46 Ma,
        // and 100 Ma gives margin (measured kinds at +100 Ma: Convergent 6, Divergent 3, Transform 15).
        long tick = onsetTick + 100 * UnitConverter.TicksPerMegaAnnum;
        var arcs = model.BuildBoundaryArcsAt(tick);

        var kinds = arcs.Select(a => a.Kind).Distinct().ToArray();
        // The network must include real motion types (not only Inactive).
        Assert.Contains(PlateBoundaryKind.Convergent, kinds);
        Assert.Contains(PlateBoundaryKind.Divergent, kinds);
        Assert.Contains(PlateBoundaryKind.Transform, kinds);
    }

    [Fact]
    public void ArcPlatePairsMatchTopologyBoundaryPairs()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        var arcs = model.BuildBoundaryArcsAt(onsetTick);

        var tess = new GeodesicSphereTessellation(AppFrequency);
        var roster = OnsetRoster.Build(AppSeed, onsetTick, AppFrequency);
        var plates = roster.SeedPlatesAt(onsetTick);
        var topology = PlateTopologyBuilder.Build(tess, plates);
        var boundaries = PlateTopologyBuilder.ClassifyBoundariesAt(
            tess, plates, topology, new CanonicalTick(onsetTick));

        var boundaryPairs = boundaries.Select(boundary => (boundary.PlateA, boundary.PlateB)).ToHashSet();
        var arcPairs = arcs.Select(arc => (arc.PlateA, arc.PlateB)).ToHashSet();
        Assert.Equal(boundaryPairs, arcPairs);
    }

    // Regression: the specific failure mode — boundaries whose topology truth carries a single
    // sample point (one shared cell edge) were dropped by the old >= 2 sample guard. The seed is
    // pinned to one that PRODUCES single-sample boundaries: after the 2026-07-07 rate calibration
    // re-fractured the default world (drift feeds ConvectionFieldConfig, so upwelling positions at
    // the onset tick moved), the app default seed 7 no longer yields any. Seed 2 at frequency 3
    // yields exactly two — pairs (5,7) and (8,9) — keeping this recovery path exercised. Both must
    // produce a real arc with >= 2 unit-length points, so the network is closed around every plate.
    [Fact]
    public void SingleSampleBoundariesAreRecoveredNotDropped()
    {
        const int SingleSampleSeed = 2;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var schedule = SphereRegimeScheduleDefaults.GeosphereFor(onsetTick);
        var recoverRoster = OnsetRoster.Build(SingleSampleSeed, onsetTick, AppFrequency);
        var model = GlobeReconstructor.FromOnsetRoster(recoverRoster, onsetTick, schedule, AppFrequency);

        var tess = new GeodesicSphereTessellation(AppFrequency);
        var roster = OnsetRoster.Build(SingleSampleSeed, onsetTick, AppFrequency);
        var plates = roster.SeedPlatesAt(onsetTick);
        var topology = PlateTopologyBuilder.Build(tess, plates);
        var boundaries = PlateTopologyBuilder.ClassifyBoundariesAt(
            tess, plates, topology, new CanonicalTick(onsetTick));

        var singleSamplePairs = boundaries
            .Where(b => b.SamplePoints.Count < 2)
            .Select(b => (b.PlateA, b.PlateB))
            .ToArray();
        Assert.NotEmpty(singleSamplePairs);

        var arcs = model.BuildBoundaryArcsAt(onsetTick);
        foreach (var pair in singleSamplePairs)
        {
            var matchingArcs = arcs
                .Where(arc => (arc.PlateA, arc.PlateB) == pair)
                .ToArray();
            Assert.NotEmpty(matchingArcs);
            foreach (var arc in matchingArcs)
            {
                Assert.True(arc.Points.Count >= 2,
                    $"recovered arc {pair} has {arc.Points.Count} points");
                foreach (var p in arc.Points)
                    AssertUnitLength(p);
            }
        }
    }

    private static void AssertUnitLength(GlobeVec3 v)
    {
        double len = System.Math.Sqrt((double)v.X * v.X + (double)v.Y * v.Y + (double)v.Z * v.Z);
        Assert.InRange(len, 1.0 - 1e-4, 1.0 + 1e-4);
    }
}
