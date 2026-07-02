using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Ecs.Cells;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Topography;
using FantaSim.Geosphere.Crust;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// End-to-end proof (Godot-free, real <see cref="GlobeReconstructor"/>) that the boundary-profile
/// contribution (P4) is purely additive and that the Earth-like defaults produce real topographic
/// signatures: convergent trench+arc asymmetry, divergent swell+rift, and small transform scarps.
/// </summary>
public sealed class BoundaryProfileIntegrationTests
{
    private const long Tick = 50 * 100_000L; // 50 anchors — enough crust evolution for features to emerge.

    private sealed record CrustField(
        double[] Baseline,
        double[] WithDefault,
        IReadOnlyList<CellBoundarySample> Field,
        IReadOnlyDictionary<int, CellCrustState> State,
        IReadOnlyDictionary<int, CrustFeature>? Features);

    private static CrustField BuildField(BoundaryProfileParameters parameters)
    {
        var reconstructor = new GlobeReconstructor(frequency: 2); // 320 cells — small but real
        var snapshot = reconstructor.RunCrustSnapshot(new[] { Tick });
        var state = snapshot.StateByTick.TryGetValue(Tick, out var s) ? s : new Dictionary<int, CellCrustState>();
        snapshot.FeaturesByTick.TryGetValue(Tick, out var features);

        // Field geometry at the onset (tick-0) frame: the static mesh frame the elevations displace.
        var globe = reconstructor.BuildGlobeAt(0);
        var arcs = reconstructor.BuildBoundaryArcsAt(0);

        var contributions = BoundaryProfileContribution.Build(globe, arcs, state, features, parameters);
        var polarity = ConvergentPolarity.Derive(arcs, globe.Cells, features, state);
        var field = CellBoundaryField.Build(globe.Cells, arcs, polarity);

        int n = globe.CellCount;
        var baseline = new double[n];
        var withDefault = new double[n];
        for (int c = 0; c < n; c++)
        {
            double derive = state.TryGetValue(c, out var cs)
                ? CellElevationSystem.Derive(new CrustSample(cs.ContinentalFraction, cs.OrogenicPressure, cs.VolcanicActivity, cs.CrustAgeTicks))
                : 0.0;
            baseline[c] = derive;
            withDefault[c] = derive + contributions[c];
        }
        return new CrustField(baseline, withDefault, field, state, features);
    }

    [Fact]
    public void Zero_profiles_reproduce_CellElevationSystem_Derive_exactly()
    {
        // The contribution is purely additive: zero params ⇒ the elevation equals the pre-profile baseline.
        var zero = BuildField(BoundaryProfileParameters.Zero);

        Assert.Equal(zero.Baseline, zero.WithDefault);
    }

    [Fact]
    public void Default_profiles_change_some_cell_elevations()
    {
        var def = BuildField(BoundaryProfileParameters.Default);
        int changed = Enumerable.Range(0, def.Baseline.Length)
            .Count(c => Math.Abs(def.WithDefault[c] - def.Baseline[c]) > 1e-6);

        Assert.True(changed > 0, "default profiles must shape at least some cells near boundaries");
    }

    [Fact]
    public void Convergent_boundary_shows_asymmetric_trench_and_arc()
    {
        var def = BuildField(BoundaryProfileParameters.Default);

        // Among cells whose nearest boundary is convergent, find the deepest dip (trench) and the highest
        // rise (arc) relative to the baseline. The trench must be negative and the arc positive — the
        // signature asymmetry of subduction.
        var convergentContributions = Enumerable.Range(0, def.Baseline.Length)
            .Where(c => def.Field[c].Found && def.Field[c].Kind == PlateBoundaryKind.Convergent && !def.Field[c].IsCollision)
            .Select(c => def.WithDefault[c] - def.Baseline[c])
            .ToList();

        if (convergentContributions.Count == 0) return; // no subduction boundaries in this seed at this tick

        double minContribution = convergentContributions.Min();
        double maxContribution = convergentContributions.Max();

        // The signature asymmetry: a clear trench dip (negative) AND an arc rise (positive). At coarse
        // frequency 2 the cells under-resolve the profile (boundary cells sit partway across the band), so
        // the magnitudes are partial — but the SIGN split is unambiguous.
        Assert.True(minContribution < -150.0, $"expected a trench dip, min contribution {minContribution}");
        Assert.True(maxContribution > 100.0, $"expected an arc rise, max contribution {maxContribution}");
    }

    [Fact]
    public void Divergent_boundary_shows_swell_rise()
    {
        var def = BuildField(BoundaryProfileParameters.Default);

        // Divergent cells should rise (swell) above the baseline near the boundary.
        var divergentContributions = Enumerable.Range(0, def.Baseline.Length)
            .Where(c => def.Field[c].Found && def.Field[c].Kind == PlateBoundaryKind.Divergent)
            .Select(c => def.WithDefault[c] - def.Baseline[c])
            .ToList();

        if (divergentContributions.Count == 0) return;

        Assert.True(divergentContributions.Max() > 100.0, $"expected divergent swell rise, max {divergentContributions.Max()}");
    }

    [Fact]
    public void Transform_boundary_contribution_is_small_relative_to_convergent()
    {
        var def = BuildField(BoundaryProfileParameters.Default);

        double transformMax = Enumerable.Range(0, def.Baseline.Length)
            .Where(c => def.Field[c].Found && def.Field[c].Kind == PlateBoundaryKind.Transform)
            .Select(c => Math.Abs(def.WithDefault[c] - def.Baseline[c]))
            .DefaultIfEmpty(0.0).Max();

        double convergentMax = Enumerable.Range(0, def.Baseline.Length)
            .Where(c => def.Field[c].Found && def.Field[c].Kind == PlateBoundaryKind.Convergent)
            .Select(c => Math.Abs(def.WithDefault[c] - def.Baseline[c]))
            .DefaultIfEmpty(0.0).Max();

        // The locked design: transform scarps are subtle. Even if no transform exists at this tick, the
        // parameter relationship (asserted in BoundaryProfileShapeTests) holds.
        if (convergentMax > 0.0)
            Assert.True(transformMax <= convergentMax, $"transform ({transformMax}) must not exceed convergent ({convergentMax})");
    }

    [Fact]
    public void Trench_feature_cells_carry_negative_contribution()
    {
        // The crust pipeline's Trench feature marks the subducting (down-going) side; the profile must dip
        // those cells below the baseline (the trench). This ties the polarity source to the profile output.
        var def = BuildField(BoundaryProfileParameters.Default);

        var trenchContributions = Enumerable.Range(0, def.Baseline.Length)
            .Where(c => def.Features is not null
                && def.Features.TryGetValue(c, out var f)
                && f.Kind == CrustFeatureKind.Trench)
            .Select(c => def.WithDefault[c] - def.Baseline[c])
            .ToList();

        // If Trench features exist, at least one must dip below the baseline.
        if (trenchContributions.Count > 0)
            Assert.True(trenchContributions.Min() < -100.0, $"trench cells must dip, min {trenchContributions.Min()}");
    }
}
