using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Globe.Core;
using FantaSim.Cartography.Shared;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Directive 3b ("the flat plate is not what we want") — TDD proofs for formed relief on slab tops.
/// The slab/mantle-layer presentation must marry the SAME ingredients that drive the World view
/// (CellElevations broad relief + <see cref="TectonicDetailSampler"/> boundary-conditioned detail +
/// the banded ramp) onto slab TOPS, with a DECLARED slab-view exaggeration that composes with
/// <see cref="RadialSectionProfile"/> ratio-locked (no double-scaling — the historical slab x4 lesson).
///
/// <para>These tests are Godot-free and exercise the pure seam (<see cref="SlabTopReliefProfile"/> +
/// <see cref="SlabTopReliefComposer"/>) that lives in the rendering contract tier alongside
/// <see cref="TectonicDetailSampler"/> and <see cref="PlateSolidBuilder"/>. The plan's TDD order is
/// the work order: (1) sampler conditioned on the cell feature bundle; (2) determinism + ridged-vs-
/// quiet character; (3) ratio-locked composition with <see cref="RadialSectionProfile"/>.</para>
///
/// <para><b>Terrain-diffusion constraints (locked in the plan):</b> detail is CONDITIONED ON the
/// per-cell causal feature bundle (boundary kind + magnitude), never position-only noise — asserted
/// in step 1; every sampled product is a PURE FUNCTION of (cell/position, tick, seed, declared params)
/// — asserted in step 2.</para>
/// </summary>
public sealed class SlabTopReliefTests
{
    // ───────────────────────────────────────────────────────────────────────────────────────────
    //  TDD step 1 — the slab-top relief composer invokes the detail sampler WITH the cell's feature
    //  bundle. The conditioning schema: each sampled position resolves to its NEAREST cell, and that
    //  cell's <see cref="CellCrustFeature"/> (Kind + Magnitude) shapes the ridged/quiet profile. This
    //  is "formed by mantle convection made visible" — never position-only noise.
    // ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildDetailSampler_is_conditioned_on_the_cell_feature_bundle()
    {
        // A mountain-bearing cell must produce a sampler whose resolved profile CARRIES that cell's
        // feature kind + a non-zero weight — proving the feature bundle flowed into the sampler. The
        // slab composer wires the sampler the SAME way the World path does (TectonicDetailSampler over
        // the snapshot's nearest-cell lookup), reusing the sampler type, not forking it.
        var snapshot = SingleCellSnapshot();
        var features = new[] { new CellCrustFeature(Kind: 1, Magnitude: 10_000.0) }; // Mountain
        var profile = SlabTopReliefProfile.Default;

        var sampler = SlabTopReliefComposer.BuildDetailSampler(snapshot, features, profile);
        var resolved = sampler.ResolveProfile(CellCenter(snapshot));

        Assert.Equal((byte)1, resolved.FeatureKind);
        Assert.True(resolved.FeatureWeight > 0.0, $"mountain feature must yield non-zero weight; got {resolved.FeatureWeight}");
    }

    [Fact]
    public void BuildDetailSampler_for_a_featureless_cell_yields_the_quiet_interior_profile()
    {
        // The conditioning schema's other half: a cell with NO typed feature (Kind 0) resolves to the
        // quiet interior profile (zero feature weight), so interiors stay calm and belts read. This is
        // the contrast that makes "formed by boundary processes" legible.
        var snapshot = SingleCellSnapshot();
        var features = new[] { default(CellCrustFeature) };
        var profile = SlabTopReliefProfile.Default;

        var sampler = SlabTopReliefComposer.BuildDetailSampler(snapshot, features, profile);
        var resolved = sampler.ResolveProfile(CellCenter(snapshot));

        Assert.Equal((byte)0, resolved.FeatureKind);
        Assert.Equal(0.0, resolved.FeatureWeight);
    }

    [Fact]
    public void BuildCaps_produces_non_flat_relief_when_features_and_elevation_are_present()
    {
        // Integration: the composed caps actually CARRY formed relief (non-flat radii) when the truth
        // inputs are non-empty. A flat-zero result would mean the sampler never fed the surface.
        var snapshot = SingleCellSnapshot();
        var features = new[] { new CellCrustFeature(Kind: 1, Magnitude: 10_000.0) };
        var elevations = new double[] { 500.0 };
        var radial = RadialSectionProfile.Default;
        var profile = SlabTopReliefProfile.Default;

        var caps = SlabTopReliefComposer.BuildCaps(snapshot, features, elevations, radial, profile);

        var cap = Assert.Single(caps);
        Assert.True(cap.Surface.Positions.Length > 0);
        double minR = cap.Surface.Positions.Min(Radius);
        double maxR = cap.Surface.Positions.Max(Radius);
        Assert.True(maxR - minR > 1e-9,
            $"slab-top caps must carry non-flat formed relief; radii span was {maxR - minR:G5}");
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────
    //  TDD step 2 — determinism (pure function of cell/position, tick, seed, params) AND distinct
    //  relief character for ridged (convergent boundary) vs quiet (interior) conditions.
    // ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildCaps_is_deterministic_identical_inputs_yield_bit_identical_positions()
    {
        // Pure-function identity: two composed cap sets from identical (snapshot [tick], features,
        // elevations, seed-in-profile, params) produce BIT-IDENTICAL positions. No query-order or
        // camera dependence. This is the terrain-diffusion determinism constraint, asserted at the
        // slab seam.
        var snapshot = SingleCellSnapshot();
        var features = new[] { new CellCrustFeature(Kind: 1, Magnitude: 10_000.0) };
        var elevations = new double[] { 500.0 };
        var radial = RadialSectionProfile.Default;
        var profile = SlabTopReliefProfile.Default;

        var caps1 = SlabTopReliefComposer.BuildCaps(snapshot, features, elevations, radial, profile);
        var caps2 = SlabTopReliefComposer.BuildCaps(snapshot, features, elevations, radial, profile);

        Assert.Equal(caps1.Count, caps2.Count);
        for (int p = 0; p < caps1.Count; p++)
        {
            Assert.Equal(caps1[p].PlateId, caps2[p].PlateId);
            Assert.Equal(caps1[p].Surface.Positions, caps2[p].Surface.Positions);
            Assert.Equal(caps1[p].Surface.Triangles, caps2[p].Surface.Triangles);
        }
    }

    [Fact]
    public void BuildDetailSampler_ridges_mountains_but_keeps_interiors_quiet()
    {
        // The conditioning schema produces DISTINCT relief character per boundary kind: a Mountain
        // cell resolves to a RIDGED profile (thin ridged belts — mountain chains), while a featureless
        // interior resolves to a NON-ridged, lower-amplitude profile (calm dry crust). This is the
        // "ridged vs quiet" contrast the plan's step 2 demands.
        var snapshot = SingleCellSnapshot();
        var mountainFeatures = new[] { new CellCrustFeature(Kind: 1, Magnitude: 10_000.0) };
        var interiorFeatures = new[] { default(CellCrustFeature) };
        var profile = SlabTopReliefProfile.Default;
        var center = CellCenter(snapshot);

        var mountain = SlabTopReliefComposer.BuildDetailSampler(snapshot, mountainFeatures, profile)
            .ResolveProfile(center);
        var interior = SlabTopReliefComposer.BuildDetailSampler(snapshot, interiorFeatures, profile)
            .ResolveProfile(center);

        Assert.True(mountain.Noise.Ridged, "mountain boundary must produce ridged relief character");
        Assert.False(interior.Noise.Ridged, "featureless interior must stay quiet (non-ridged)");
        Assert.True(mountain.Noise.Amplitude > interior.Noise.Amplitude,
            $"belt amplitude ({mountain.Noise.Amplitude:G5}) must exceed interior ({interior.Noise.Amplitude:G5})");
    }

    [Fact]
    public void BuildCaps_relief_character_differs_between_ridged_and_quiet_conditions()
    {
        // Integration of the conditioning schema onto actual cap geometry: a mountain-bearing cell and
        // a featureless cell, everything else identical, must produce DIFFERENT top positions. If they
        // were equal the sampler would be position-only noise (the forbidden conditioning failure).
        var snapshot = SingleCellSnapshot();
        var elevations = new double[] { 500.0 };
        var radial = RadialSectionProfile.Default;
        var profile = SlabTopReliefProfile.Default;

        var mountainCaps = SlabTopReliefComposer.BuildCaps(
            snapshot, new[] { new CellCrustFeature(Kind: 1, Magnitude: 10_000.0) }, elevations, radial, profile);
        var interiorCaps = SlabTopReliefComposer.BuildCaps(
            snapshot, new[] { default(CellCrustFeature) }, elevations, radial, profile);

        var mountain = Assert.Single(mountainCaps);
        var interior = Assert.Single(interiorCaps);
        Assert.False(mountain.Surface.Positions.SequenceEqual(interior.Surface.Positions),
            "mountain-bearing and featureless cells must produce distinct slab-top relief (conditioning must matter)");
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────
    //  TDD step 3 — the slab relief scale COMPOSES with RadialSectionProfile, ratio-locked. The
    //  declared <see cref="SlabTopReliefProfile.ReliefThicknessScaleMultiple"/> ties the relief
    //  metres-to-unit-radius scale to the SAME thickness scale the radial profile declares, so the
    //  two move together and cannot double-scale. The historical slab x4 double-scale bug is guarded
    //  by the top-position-immutability proof through <see cref="PlateSolidBuilder"/>.
    // ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MetresToUnitRadius_is_the_ratio_locked_thickness_scale_multiple()
    {
        // The ratio-lock identity: the slab relief metres-to-unit-radius scale is EXACTLY the declared
        // multiple of RadialSectionProfile.ThicknessDepthScale(). One declared knob, composed — not an
        // independent lens that could drift or double up.
        var radial = RadialSectionProfile.Default;
        var profile = SlabTopReliefProfile.Default;

        double expected = profile.ReliefThicknessScaleMultiple * radial.ThicknessDepthScale();
        double actual = profile.MetresToUnitRadius(radial);

        Assert.Equal(expected, actual, 12);
    }

    [Fact]
    public void Doubling_crust_thickness_exaggeration_doubles_the_relief_scale()
    {
        // Ratio-lock consequence: moving the radial profile's CrustThicknessExaggeration moves BOTH the
        // crust thickness fraction AND the slab relief scale by the same factor. They cannot drift
        // apart — the whole point of composing with RadialSectionProfile.
        var radial = RadialSectionProfile.Default;
        var profile = SlabTopReliefProfile.Default;
        double baseline = profile.MetresToUnitRadius(radial);

        var amplified = radial with { CrustThicknessExaggeration = radial.CrustThicknessExaggeration * 2.0 };
        Assert.Equal(baseline * 2.0, profile.MetresToUnitRadius(amplified), 12);
    }

    [Fact]
    public void Slab_relief_exceeds_the_world_silhouette_budget_at_the_envelope_magnitude()
    {
        // The World view clamps relief to a 0.005R silhouette budget (north-star §1). Reusing that
        // clamp on the slab path is the "reads as smooth" bug: at the slab's exaggerated thickness
        // (0.0377R) a 0.005R relief vanishes. The slab profile declares its OWN scale (no World clamp),
        // so a representative envelope elevation produces displacement ABOVE the World budget. If a
        // future change re-introduces the World clamp here, this fails and forces a conscious decision.
        var radial = RadialSectionProfile.Default;
        var profile = SlabTopReliefProfile.Default;
        const double WorldSilhouetteBudgetUnitRadius = 0.005;
        const double envelopeElevationMetres = 500.0;

        double slabDisplacement = profile.MetresToUnitRadius(radial) * envelopeElevationMetres;

        Assert.True(slabDisplacement > WorldSilhouetteBudgetUnitRadius,
            $"slab relief ({slabDisplacement:G5}R) must exceed the World silhouette budget ({WorldSilhouetteBudgetUnitRadius}R) " +
            "or the slab tops render smooth again");
    }

    [Fact]
    public void PlateSolidBuilder_does_not_mutate_slab_top_positions()
    {
        // THE DOUBLE-SCALE GUARD. The relief exaggeration acts on the slab TOP cap radii (inside
        // GlobePlateSurfaces); the thickness exaggeration acts ONLY on the BOTTOM inset (inside
        // PlateSolidBuilder). Feeding slab-relief caps through PlateSolidBuilder must leave the TOP
        // positions BIT-EQUAL to the cap positions — proving the two exaggerations never compound on
        // the same vertex. This is the test that would have caught the historical slab x4 double-scale
        // bug class.
        var snapshot = TwoCellSinglePlateSnapshot();
        var features = new[] { new CellCrustFeature(Kind: 1, Magnitude: 10_000.0), default };
        var elevations = new double[] { 500.0, 0.0 };
        var radial = RadialSectionProfile.Default;
        var profile = SlabTopReliefProfile.Default;

        var caps = SlabTopReliefComposer.BuildCaps(snapshot, features, elevations, radial, profile);
        var cap = Assert.Single(caps);
        var thickness = new double[] { radial.DefaultCrustThicknessMetres, radial.DefaultCrustThicknessMetres };

        var solids = PlateSolidBuilder.Build(caps, thickness, radial.ThicknessDepthScale());
        var solid = Assert.Single(solids);

        int n = cap.Surface.VertexCount;
        Assert.Equal(n * 2, solid.VertexCount);
        for (int v = 0; v < n; v++)
        {
            Assert.Equal(cap.Surface.Positions[v], solid.Positions[v]);
        }
    }

    [Fact]
    public void Slab_relief_and_thickness_compose_without_double_scaling_on_relief_vertices()
    {
        // End-to-end composition proof: under the slab profile, the bottom inset depth equals
        // thickness × ThicknessDepthScale (UN-contaminated by the relief multiple), AND the top relief
        // equals the cap relief (UN-contaminated by the thickness exaggeration). The two scales act on
        // disjoint vertex sets — the ratio-locked composition, verified at the geometry level.
        var snapshot = TwoCellSinglePlateSnapshot();
        var radial = RadialSectionProfile.Default;
        var profile = SlabTopReliefProfile.Default;
        var features = new[] { new CellCrustFeature(Kind: 1, Magnitude: 10_000.0), default };
        var elevations = new double[] { 0.0, 0.0 }; // zero envelope so relief == sampler detail only
        var thickness = new double[] { radial.DefaultCrustThicknessMetres, radial.DefaultCrustThicknessMetres };

        var caps = SlabTopReliefComposer.BuildCaps(snapshot, features, elevations, radial, profile);
        var solids = PlateSolidBuilder.Build(caps, thickness, radial.ThicknessDepthScale());
        var cap = Assert.Single(caps);
        var solid = Assert.Single(solids);

        int n = cap.Surface.VertexCount;
        // The bottom inset depth (radial) is exactly thickness × ThicknessDepthScale, independent of
        // the relief multiple. Measured along each top vertex's radial direction.
        double expectedDepth = radial.DefaultCrustThicknessMetres * radial.ThicknessDepthScale();
        for (int v = 0; v < n; v++)
        {
            var top = solid.Positions[v];
            var bottom = solid.Positions[n + v];
            // (top - bottom) projected onto the top's radial direction == the pure thickness depth.
            double topR = Radius(top);
            double dx = top.X - bottom.X;
            double dy = top.Y - bottom.Y;
            double dz = top.Z - bottom.Z;
            double radialDepth = topR > 0.0
                ? (dx * top.X + dy * top.Y + dz * top.Z) / topR
                : 0.0;
            Assert.Equal(expectedDepth, radialDepth, 6);
        }
    }

    // === fixtures ===============================================================================

    private static double Radius(CartesianPoint3 p)
        => Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z));

    private static CartesianPoint3 CellCenter(WorldGlobeSnapshot snapshot)
    {
        var cell = snapshot.Cells[0];
        var p = new CartesianPoint3(
            cell.C0.X + cell.C1.X + cell.C2.X,
            cell.C0.Y + cell.C1.Y + cell.C2.Y,
            cell.C0.Z + cell.C1.Z + cell.C2.Z);
        double len = Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z));
        return len > 1e-12
            ? new CartesianPoint3(p.X / len, p.Y / len, p.Z / len)
            : new CartesianPoint3(cell.C0.X, cell.C0.Y, cell.C0.Z);
    }

    // Single triangular cell on plate 0 — the minimal fixture for conditioning/determinism proofs.
    private static WorldGlobeSnapshot SingleCellSnapshot()
    {
        var v0 = new GlobeVec3(0f, 0f, 1f);
        var v1 = new GlobeVec3(1f, 0f, 0f);
        var v2 = new GlobeVec3(0f, 1f, 0f);
        return new WorldGlobeSnapshot(
            Frequency: 0,
            CellCount: 1,
            PlateCount: 1,
            TicksPerAnchor: 100_000L,
            Cells: new[] { new GlobeCell(0, 0, v0, v1, v2) },
            Plates: new[] { new GlobePlate(0, v0, 0.0) });
    }

    // Two cells sharing an edge on one plate — gives the solid builder a non-trivial cap (rim + walls)
    // so the top-position-immutability proof exercises a real closed solid, not a degenerate triangle.
    private static WorldGlobeSnapshot TwoCellSinglePlateSnapshot()
    {
        var v0 = new GlobeVec3(0f, 0f, 1f);
        var v1 = new GlobeVec3(1f, 0f, 1f);
        var v2 = new GlobeVec3(0f, 1f, 1f);
        var v3 = new GlobeVec3(-1f, 1f, 1f);
        var cells = new[]
        {
            new GlobeCell(0, 0, v0, v1, v2),
            new GlobeCell(1, 0, v0, v2, v3),
        };
        var plates = new[] { new GlobePlate(0, new GlobeVec3(0, 0, 1), 0.0) };
        return new WorldGlobeSnapshot(0, 2, 1, 100_000L, cells, plates);
    }
}
