using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Topography;
using FantaSim.Geosphere.Crust;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Pins <see cref="ConvergentPolarity.Derive"/>: subduction polarity from the crust pipeline's Trench
/// feature (down-going side), the ContinentalFraction fallback (denser/oceanic subducts), the
/// continent–continent collision case, and the tie-break.
/// </summary>
public sealed class ConvergentPolarityTests
{
    private const double Near = 0.5;

    private static PlateBoundaryArc ConvArc(int a, int b, GlobeVec3 p0, GlobeVec3 p1) =>
        new(a, b, PlateBoundaryKind.Convergent, new[] { p0, p1 });

    private static GlobeCell Cell(int id, int plate, GlobeVec3 pos) =>
        new(id, plate, pos, pos, pos);

    private static readonly GlobeVec3 Origin = new(1, 0, 0);

    // ── Trench feature marks the down-going side ──────────────────────────────────────────────

    [Fact]
    public void Trench_features_on_plate_b_mark_b_as_subducting()
    {
        var arcs = new[] { ConvArc(0, 1, Origin, Origin) };
        var cells = new[]
        {
            Cell(0, plate: 0, Origin),
            Cell(1, plate: 1, Origin),
        };
        var features = new Dictionary<int, CrustFeature>
        {
            [1] = new CrustFeature(1, CrustFeatureKind.Trench, 1.0),
        };
        var state = new Dictionary<int, CellCrustState>
        {
            [0] = MakeState(0.0),
            [1] = MakeState(0.0),
        };

        var pol = ConvergentPolarity.Derive(arcs, cells, features, state, nearRadiusRad: Near);

        Assert.True(pol.TryGetValue((0, 1), out var p));
        Assert.Equal(1, p.SubductingPlateId);
        Assert.Equal(0, p.OverridingPlateId);
        Assert.False(p.IsCollision);
    }

    // ── Composition fallback: lower ContinentalFraction subducts ──────────────────────────────

    [Fact]
    public void Fallback_lower_continental_fraction_subducts()
    {
        // No Trench features. Plate 0 is continental (cf 0.9), plate 1 is oceanic (cf 0.1) ⇒ plate 1 subducts.
        var arcs = new[] { ConvArc(0, 1, Origin, Origin) };
        var cells = new[]
        {
            Cell(0, plate: 0, Origin),
            Cell(1, plate: 1, Origin),
        };
        var state = new Dictionary<int, CellCrustState>
        {
            [0] = MakeState(0.9),
            [1] = MakeState(0.1),
        };

        var pol = ConvergentPolarity.Derive(arcs, cells, features: null, state, nearRadiusRad: Near);

        Assert.True(pol.TryGetValue((0, 1), out var p));
        Assert.Equal(1, p.SubductingPlateId);
        Assert.False(p.IsCollision);
    }

    // ── Both continental ⇒ collision ──────────────────────────────────────────────────────────

    [Fact]
    public void Both_continental_is_collision()
    {
        var arcs = new[] { ConvArc(0, 1, Origin, Origin) };
        var cells = new[]
        {
            Cell(0, plate: 0, Origin),
            Cell(1, plate: 1, Origin),
        };
        var state = new Dictionary<int, CellCrustState>
        {
            [0] = MakeState(0.8),
            [1] = MakeState(0.9),
        };

        var pol = ConvergentPolarity.Derive(arcs, cells, features: null, state, nearRadiusRad: Near);

        Assert.True(pol.TryGetValue((0, 1), out var p));
        Assert.True(p.IsCollision);
    }

    // ── Tie-break: equal ContinentalFraction ⇒ lower plate id subducts ─────────────────────────

    [Fact]
    public void Tie_break_lower_plate_id_subducts()
    {
        // Both oceanic with identical ContinentalFraction ⇒ lower id subducts.
        var arcs = new[] { ConvArc(0, 1, Origin, Origin) };
        var cells = new[]
        {
            Cell(0, plate: 0, Origin),
            Cell(1, plate: 1, Origin),
        };
        var state = new Dictionary<int, CellCrustState>
        {
            [0] = MakeState(0.2),
            [1] = MakeState(0.2),
        };

        var pol = ConvergentPolarity.Derive(arcs, cells, features: null, state, nearRadiusRad: Near);

        Assert.True(pol.TryGetValue((0, 1), out var p));
        Assert.Equal(0, p.SubductingPlateId);
        Assert.Equal(1, p.OverridingPlateId);
        Assert.False(p.IsCollision);
    }

    // ── A side with no state counts as oceanic (mean 0) and subducts ──────────────────────────

    [Fact]
    public void No_state_for_one_side_treats_it_as_oceanic_and_subducting()
    {
        // Plate 0 has continental state; plate 1 has NO state entry ⇒ mean 0 ⇒ plate 1 subducts.
        var arcs = new[] { ConvArc(0, 1, Origin, Origin) };
        var cells = new[]
        {
            Cell(0, plate: 0, Origin),
            Cell(1, plate: 1, Origin),
        };
        var state = new Dictionary<int, CellCrustState>
        {
            [0] = MakeState(0.7),
        };

        var pol = ConvergentPolarity.Derive(arcs, cells, features: null, state, nearRadiusRad: Near);

        Assert.True(pol.TryGetValue((0, 1), out var p));
        Assert.Equal(1, p.SubductingPlateId);
    }

    // ── Non-convergent arcs are ignored ───────────────────────────────────────────────────────

    [Fact]
    public void Divergent_and_transform_arcs_are_not_polaritised()
    {
        var arcs = new[]
        {
            new PlateBoundaryArc(0, 1, PlateBoundaryKind.Divergent, new[] { Origin, Origin }),
            new PlateBoundaryArc(0, 1, PlateBoundaryKind.Transform, new[] { Origin, Origin }),
        };
        var cells = System.Array.Empty<GlobeCell>();
        var state = new Dictionary<int, CellCrustState>();

        var pol = ConvergentPolarity.Derive(arcs, cells, features: null, state, nearRadiusRad: Near);

        Assert.Empty(pol);
    }

    private static CellCrustState MakeState(double continentalFraction)
        => new(0, ContinentalFraction: continentalFraction, OrogenicPressure: 0.0, VolcanicActivity: 0.0, CrustAgeTicks: 0.0);
}
