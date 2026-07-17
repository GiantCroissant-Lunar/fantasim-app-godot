using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.Ecs.Cells;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Crust;
using FantaSim.App.World.Dto;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Rendering;
using FantaSim.App.World.Services;
using FantaSim.App.World.Topography;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Shared;
using FantaSim.Geosphere.Crust;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// End-to-end proof (Godot-free, real <see cref="GlobeReconstructor"/>) that the boundary-profile
/// contribution (P4) is purely additive and that the Earth-like defaults produce real topographic
/// signatures: convergent trench+arc asymmetry, divergent swell+rift, and small transform scarps.
/// </summary>
public sealed class BoundaryProfileIntegrationTests
{
    // Deterministic real-pipeline acceptance fixture. At frequency 2 / tick 67,000,000 the seeded
    // crust fold contains all four signed morphology categories in one snapshot: mountain, volcanic
    // arc, trench, and ridge. This is deliberately locked rather than conditionally skipping absent
    // feature categories.
    private const int FixtureFrequency = 2;
    private const int PublicServiceFixtureFrequency = 2;
    private const long Tick = 67_000_000L;
    private const long PublicServiceFixtureTick = 200_000_000L;
    private static readonly long[] FixtureTicks = Enumerable.Range(1, 67)
        .Select(anchor => anchor * 1_000_000L)
        .ToArray();

    private sealed record CrustField(
        double[] Baseline,
        double[] WithDefault,
        IReadOnlyList<CellBoundarySample> Field,
        IReadOnlyDictionary<int, CellCrustState> State,
        IReadOnlyDictionary<int, CrustFeature>? Features,
        WorldGlobeSnapshot Globe);

    private sealed record CurrentFrameFixture(
        IReadOnlyDictionary<int, CellCrustState> State,
        IReadOnlyDictionary<int, CrustFeature> Features,
        IReadOnlyList<CellBoundarySample> BoundaryField);

    private static CrustField BuildField(BoundaryProfileParameters parameters) => BuildField(parameters, FixtureFrequency);

    private static CurrentFrameFixture BuildCurrentFrameFixture(int frequency, long tick)
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var options = WorldGenerationRenderOptions.Default with { TessellationFrequency = frequency };
        var schedule = SphereRegimeScheduleDefaults.GeosphereFor(onsetTick);
        var mobilePlate = schedule.RegimeAt(tick)!;
        long snapshotTick = CrustSnapshotTickSeries.ForRegime(
                mobilePlate,
                CrustSnapshotTickSeries.DefaultSpacingTicks,
                onsetTick + Service.MobilePlateWindowTicks)
            .SelectSnapshotForPlayhead(tick) ?? tick;
        var materialization = WorldCrustMaterializer.MaterializeAsync(
                WorldCrustRunSpec.ForPresentation(options, onsetTick, snapshotTick))
            .GetAwaiter()
            .GetResult();
        var roster = OnsetRoster.Build(options.Seed, onsetTick, frequency, options.SpinRateRadiansPerMegaAnnum);
        var reconstructor = GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, frequency);
        var globe = reconstructor.BuildGlobeAt(tick);
        var arcs = reconstructor.BuildBoundaryArcsAt(tick);
        var stateAtTick = new Dictionary<long, IReadOnlyDictionary<int, CellCrustState>>
        {
            [tick] = materialization.Result.StateByTick[snapshotTick],
        };
        var sampler = new PlateFrameSampler(
            materialization.Tessellation,
            materialization.Result.Plates,
            materialization.Topology,
            onsetTick);
        var currentAssignment = globe.Cells.ToDictionary(cell => cell.CellId, cell => cell.PlateId);
        var state = sampler.SampleAt(tick, stateAtTick, currentAssignment);
        var features = sampler.SampleFeaturesAt(globe, state, arcs);
        var resolvedArcs = ConvergentPolarity.Attach(arcs, globe.Cells, features, state);
        return new CurrentFrameFixture(state, features, CellBoundaryField.Build(globe.Cells, resolvedArcs));
    }

    private static CrustField BuildField(BoundaryProfileParameters parameters, int frequency)
    {
        var reconstructor = new GlobeReconstructor(frequency);
        var snapshot = reconstructor.RunCrustSnapshot(FixtureTicks);
        var state = snapshot.StateByTick.TryGetValue(Tick, out var s) ? s : new Dictionary<int, CellCrustState>();
        snapshot.FeaturesByTick.TryGetValue(Tick, out var features);

        // Field geometry at the onset (tick-0) frame: the static mesh frame the elevations displace.
        var globe = reconstructor.BuildGlobeAt(0);
        var arcs = reconstructor.BuildBoundaryArcsAt(0);

        var contributions = BoundaryProfileContribution.Build(globe, arcs, state, features, parameters);
        var resolvedArcs = ConvergentPolarity.Attach(arcs, globe.Cells, features, state);
        var field = CellBoundaryField.Build(globe.Cells, resolvedArcs);

        int n = globe.CellCount;
        var baseline = new double[n];
        var withDefault = new double[n];
        for (int c = 0; c < n; c++)
        {
            double derive = state.TryGetValue(c, out var cs)
                ? CellElevationSystem.Derive(
                    new CrustSample(cs.ContinentalFraction, cs.OrogenicPressure, cs.VolcanicActivity, cs.CrustAgeTicks),
                    CellElevationHydrosphereMode.Absent)
                : 0.0;
            baseline[c] = derive;
            withDefault[c] = derive + contributions[c];
        }
        return new CrustField(baseline, withDefault, field, state, features, globe);
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

        Assert.NotEmpty(convergentContributions);

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

        Assert.NotEmpty(divergentContributions);

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

        Assert.True(convergentMax > 0.0, "fixture must contain a shaped convergent boundary");
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

        Assert.NotEmpty(trenchContributions);
        Assert.True(trenchContributions.Min() < -100.0, $"trench cells must dip, min {trenchContributions.Min()}");
    }

    [Fact]
    public void Lower_pipeline_fixture_proves_collision_mountain_and_divergent_ridge_counterfactuals()
    {
        var def = BuildField(BoundaryProfileParameters.Default);
        var zero = BuildField(BoundaryProfileParameters.Zero);

        int n = def.Baseline.Length;
        var contributions = new double[n];
        for (int c = 0; c < n; c++)
            contributions[c] = def.WithDefault[c] - zero.WithDefault[c];

        Assert.True(def.Features is not null, "pipeline must produce features at this tick");

        var mountainCells = CellsWithFeature(def.Features, CrustFeatureKind.Mountain);
        var ridgeCells = CellsWithFeature(def.Features, CrustFeatureKind.Ridge);

        Assert.Contains(85, mountainCells);
        Assert.Contains(196, ridgeCells);
        Assert.Equal(PlateBoundaryKind.Convergent, def.Field[85].Kind);
        Assert.True(def.Field[85].IsCollision);
        Assert.Equal(PlateBoundaryKind.Divergent, def.Field[196].Kind);

        var mountainState = def.State[85];
        double mountainWithoutOrogenyOrProfiles = CellElevationSystem.Derive(
            new CrustSample(
                mountainState.ContinentalFraction,
                OrogenicPressure: 0.0,
                mountainState.VolcanicActivity,
                mountainState.CrustAgeTicks),
            CellElevationHydrosphereMode.Absent);
        double mountainSignal = def.WithDefault[85] - mountainWithoutOrogenyOrProfiles;

        Assert.True(mountainSignal >= 750.0,
            $"mountain cell 85 must gain >= 750 m versus no-orogeny/no-profile counterfactual; was {mountainSignal:F1}");
        Assert.True(contributions[196] >= 300.0,
            $"ridge cell 196 must gain >= 300 m; was {contributions[196]:F1}");
    }

    [Fact]
    public void Service_document_preserves_all_four_feature_lineages_with_signed_profile_relief()
    {
        using var service = new Service(new ServiceRegistry());
        var shaped = service.GetPlanetPresentationAsync(PublicServiceFixtureTick, PublicServiceFixtureFrequency);
        var zero = service.GetPlanetPresentationAsyncCore(
            PublicServiceFixtureTick,
            WorldGenerationRenderOptions.Default with
            {
                TessellationFrequency = PublicServiceFixtureFrequency,
                BoundaryProfiles = BoundaryProfileParameters.Zero,
            });

        Assert.NotNull(shaped.CellFeatures);
        Assert.NotNull(shaped.CellElevations);
        Assert.NotNull(zero.CellElevations);
        Assert.NotNull(shaped.ContinentalFractionByCell);

        Assert.Equal(TectonicFeatureKind.Mountain.ToWireByte(), shaped.CellFeatures![134].Kind);
        Assert.Equal(TectonicFeatureKind.VolcanicArc.ToWireByte(), shaped.CellFeatures[137].Kind);
        // Corrected Eulerian fixed-cell classification moved the locked topology fixtures:
        // old Trench 40 -> current Trench 4; old Ridge 224 -> current Ridge 40.
        Assert.Equal(TectonicFeatureKind.Trench.ToWireByte(), shaped.CellFeatures[4].Kind);
        Assert.Equal(TectonicFeatureKind.Ridge.ToWireByte(), shaped.CellFeatures[40].Kind);

        var current = BuildCurrentFrameFixture(PublicServiceFixtureFrequency, PublicServiceFixtureTick);
        Assert.Equal(CrustFeatureKind.Trench, current.Features[4].Kind);
        Assert.Equal(PlateBoundaryKind.Convergent, current.BoundaryField[4].Kind);
        Assert.Equal(current.BoundaryField[4].CellPlateId, current.BoundaryField[4].SubductingPlateId);
        Assert.False(current.BoundaryField[4].IsCollision);
        Assert.Equal(CrustFeatureKind.Ridge, current.Features[40].Kind);
        Assert.Equal(PlateBoundaryKind.Divergent, current.BoundaryField[40].Kind);

        double dryCrustBaseline = shaped.ContinentalFractionByCell![134] * CellElevationSystem.ContinentalAmp;
        double stateDerivedRelief = zero.CellElevations![134] - dryCrustBaseline;
        Assert.True(stateDerivedRelief >= 750.0,
            $"mountain-labelled cell 134 must retain >= 750 m of state-derived relief above dry crust; was {stateDerivedRelief:F6}");
        Assert.True(shaped.CellElevations![137] - zero.CellElevations[137] > 0.0,
            $"volcanic arc 137 must rise in the all-four visual fixture; delta={shaped.CellElevations[137] - zero.CellElevations[137]:F6}");
        Assert.True(shaped.CellElevations[4] - zero.CellElevations[4] <= -750.0,
            $"current subducting trench 4 must lose >= 750 m; delta={shaped.CellElevations[4] - zero.CellElevations[4]:F6}");
        Assert.True(shaped.CellElevations[40] - zero.CellElevations[40] >= 300.0,
            $"current divergent ridge 40 must gain >= 300 m; delta={shaped.CellElevations[40] - zero.CellElevations[40]:F6}");
    }

    [Fact]
    public void Frequency_four_service_document_carries_active_overriding_volcanic_arc_to_final_mesh()
    {
        const int frequency = 4;
        const int volcanicCell = 803;
        using var service = new Service(new ServiceRegistry());
        var shaped = service.GetPlanetPresentationAsync(PublicServiceFixtureTick, frequency);
        var zero = service.GetPlanetPresentationAsyncCore(
            PublicServiceFixtureTick,
            WorldGenerationRenderOptions.Default with
            {
                TessellationFrequency = frequency,
                BoundaryProfiles = BoundaryProfileParameters.Zero,
            });

        Assert.NotNull(shaped.GlobeSnapshot);
        Assert.Equal(frequency, shaped.GlobeSnapshot!.Frequency);
        Assert.NotNull(shaped.CellFeatures);
        Assert.NotNull(shaped.CellElevations);
        Assert.NotNull(zero.CellElevations);
        Assert.Equal(TectonicFeatureKind.VolcanicArc.ToWireByte(), shaped.CellFeatures![volcanicCell].Kind);

        var current = BuildCurrentFrameFixture(frequency, PublicServiceFixtureTick);
        Assert.Equal(CrustFeatureKind.VolcanicArc, current.Features[volcanicCell].Kind);
        Assert.Equal(PlateBoundaryKind.Convergent, current.BoundaryField[volcanicCell].Kind);
        Assert.False(current.BoundaryField[volcanicCell].IsCollision);
        Assert.NotEqual(
            current.BoundaryField[volcanicCell].SubductingPlateId,
            current.BoundaryField[volcanicCell].CellPlateId);

        double volcanicSignal = shaped.CellElevations![volcanicCell] - zero.CellElevations![volcanicCell];
        Assert.True(volcanicSignal >= 750.0,
            $"active overriding volcanic arc {volcanicCell} must gain >= 750 m versus zero profiles; was {volcanicSignal:F6}");

        var shapedRadii = FinalMeshMeanRadiiByCell(shaped, volcanicCell);
        var baselineRadii = FinalMeshMeanRadiiByCell(zero, volcanicCell);
        Assert.True(shapedRadii[volcanicCell] - baselineRadii[volcanicCell] > 1e-6);
    }

    [Fact]
    public void Service_document_signed_morphology_reaches_adaptive_final_mesh_for_same_cells()
    {
        using var service = new Service(new ServiceRegistry());
        var shaped = service.GetPlanetPresentationAsync(PublicServiceFixtureTick, PublicServiceFixtureFrequency);
        var zero = service.GetPlanetPresentationAsyncCore(
            PublicServiceFixtureTick,
            WorldGenerationRenderOptions.Default with
            {
                TessellationFrequency = PublicServiceFixtureFrequency,
                BoundaryProfiles = BoundaryProfileParameters.Zero,
            });

        var shapedRadii = FinalMeshMeanRadiiByCell(shaped, 134, 137, 4, 40);
        var baselineRadii = FinalMeshMeanRadiiByCell(zero, 134, 137, 4, 40);
        double dryCrustBaseline = shaped.ContinentalFractionByCell![134] * CellElevationSystem.ContinentalAmp;
        var mountainCounterfactualElevations = zero.CellElevations!.ToArray();
        mountainCounterfactualElevations[134] = dryCrustBaseline;
        var mountainCounterfactual = zero with { CellElevations = mountainCounterfactualElevations };
        var mountainCounterfactualRadii = FinalMeshMeanRadiiByCell(mountainCounterfactual, 134);

        Assert.True(baselineRadii[134] - mountainCounterfactualRadii[134] > 1e-6);
        Assert.True(shapedRadii[137] - baselineRadii[137] > 1e-6);
        Assert.True(baselineRadii[4] - shapedRadii[4] > 1e-6);
        Assert.True(shapedRadii[40] - baselineRadii[40] > 1e-6);
    }

    private static IReadOnlyDictionary<int, double> FinalMeshMeanRadiiByCell(
        PlanetPresentationDocument document,
        params int[] cellIds)
    {
        Assert.NotNull(document.GlobeSnapshot);
        Assert.NotNull(document.CellElevations);
        var projection = LayerProjectionProfileResolver.ResolveForView(
            document,
            GlobeViewMode.HypsometricTerrain,
            worldMetresToUnitRadius: 0.0005,
            worldHeightExponent: 0.5);
        var field = new CrustField(
            Array.Empty<double>(),
            document.CellElevations!.ToArray(),
            Array.Empty<CellBoundarySample>(),
            new Dictionary<int, CellCrustState>(),
            null,
            document.GlobeSnapshot!);
        return FinalMeshMeanRadiiByCell(field, projection, cellIds);
    }

    [Fact]
    public void Signed_boundary_morphology_survives_adaptive_surface_and_final_mesh_for_the_same_cells()
    {
        var shaped = BuildField(BoundaryProfileParameters.Default);
        var baseline = BuildField(BoundaryProfileParameters.Zero);
        var mountainState = shaped.State[85];
        var mountainCounterfactualElevations = baseline.WithDefault.ToArray();
        mountainCounterfactualElevations[85] = CellElevationSystem.Derive(
            new CrustSample(
                mountainState.ContinentalFraction,
                OrogenicPressure: 0.0,
                mountainState.VolcanicActivity,
                mountainState.CrustAgeTicks),
            CellElevationHydrosphereMode.Absent);
        var mountainCounterfactual = baseline with { WithDefault = mountainCounterfactualElevations };

        var profile = PlanetLayerProjectionProfile.Crust(
            metresToUnitRadius: WorldGenerationRenderOptions.DefaultVerticalExaggeration,
            surfaceSubdivision: WorldGenerationRenderOptions.Default.SurfaceSubdivision,
            adaptiveSubdivisionMaxDepth: WorldGenerationRenderOptions.Default.AdaptiveSubdivisionMaxDepth,
            adaptiveSubdivisionEdgeHeightDelta: WorldGenerationRenderOptions.Default.AdaptiveSubdivisionEdgeHeightDelta,
            adaptiveSubdivisionFeatureWeightDelta: WorldGenerationRenderOptions.Default.AdaptiveSubdivisionFeatureWeightDelta);
        var document = new PlanetPresentationDocument(
            "boundary-profile-fixture",
            "boundary-profile-fixture",
            Tick,
            1,
            Array.Empty<PlanetPresentationLayer>(),
            Array.Empty<RenderEntityDto>())
        {
            LayerProjectionProfiles = new[] { profile },
        };
        var projection = LayerProjectionProfileResolver.ResolveForView(
            document,
            GlobeViewMode.HypsometricTerrain,
            worldMetresToUnitRadius: 0.0005,
            worldHeightExponent: 0.5);

        Assert.Equal(1.0, projection.HeightExponent);
        Assert.True(projection.UseAdaptiveSurface);
        Assert.Equal(WorldGenerationRenderOptions.Default.AdaptiveSubdivisionMaxDepth, projection.AdaptiveSubdivisionMaxDepth);
        Assert.Equal(WorldGenerationRenderOptions.Default.AdaptiveSubdivisionFeatureWeightDelta, projection.AdaptiveSubdivisionFeatureWeightDelta);
        Assert.Equal(PlanetLayerProjectionProfile.DefaultSilhouetteBudgetUnitRadius, projection.MaxDisplacementUnitRadius);
        Assert.Equal(
            PlanetLayerProjectionProfile.DefaultSilhouetteBudgetUnitRadius
                / PlanetLayerProjectionProfile.DefaultReferenceMaxReliefMetres,
            projection.MetresToUnitRadius,
            12);
        Assert.Equal(
            WorldGenerationRenderOptions.Default.AdaptiveSubdivisionEdgeHeightDelta
                * (projection.MetresToUnitRadius / WorldGenerationRenderOptions.DefaultVerticalExaggeration),
            projection.AdaptiveSubdivisionEdgeHeightDelta,
            12);

        var shapedRadii = FinalMeshMeanRadiiByCell(shaped, projection, 85, 144, 182, 196);
        var baselineRadii = FinalMeshMeanRadiiByCell(baseline, projection, 85, 144, 182, 196);
        var mountainCounterfactualRadii = FinalMeshMeanRadiiByCell(mountainCounterfactual, projection, 85);

        Assert.True(shapedRadii[85] - mountainCounterfactualRadii[85] > 1e-6,
            $"mountain cell 85 must move outward versus its no-orogeny/no-profile final mesh: shaped={shapedRadii[85]:F9}, baseline={mountainCounterfactualRadii[85]:F9}");
        Assert.True(shapedRadii[144] - baselineRadii[144] > 1e-6,
            $"volcanic-arc cell 144 must move outward in the final mesh: shaped={shapedRadii[144]:F9}, baseline={baselineRadii[144]:F9}");
        Assert.True(baselineRadii[182] - shapedRadii[182] > 1e-6,
            $"trench cell 182 must move inward in the final mesh: shaped={shapedRadii[182]:F9}, baseline={baselineRadii[182]:F9}");
        Assert.True(shapedRadii[196] - baselineRadii[196] > 1e-6,
            $"ridge cell 196 must move outward in the final mesh: shaped={shapedRadii[196]:F9}, baseline={baselineRadii[196]:F9}");
    }

    private static IReadOnlyDictionary<int, double> FinalMeshMeanRadiiByCell(
        CrustField field,
        ResolvedLayerProjection projection,
        params int[] cellIds)
    {
        var options = new AdaptiveSubdivisionOptions(
            projection.AdaptiveSubdivisionMaxDepth,
            projection.AdaptiveSubdivisionEdgeHeightDelta,
            projection.AdaptiveSubdivisionFeatureWeightDelta);
        var caps = new GlobePlateSurfaces(field.Globe, noise: new NoiseParams(Amplitude: 0.0))
            .BuildAdaptiveSurfaces(
                field.WithDefault,
                projection.MetresToUnitRadius,
                options,
                projection.HeightExponent,
                featureWeightsByCell: null,
                projection.BaseRadius,
                projection.MaxDisplacementUnitRadius);

        var colors = Enumerable.Repeat(new RampColor(0.5, 0.5, 0.5), field.Globe.CellCount).ToArray();
        var emission = new float[field.Globe.CellCount];
        var result = new Dictionary<int, double>(cellIds.Length);

        foreach (int cellId in cellIds)
        {
            var cap = Assert.Single(caps, candidate => candidate.CellIds.Contains(cellId));
            var mesh = PlateCapMeshBuilder.BuildTerrain(
                cap,
                new Dictionary<int, RampColor[]>(),
                emission,
                jitter: null,
                colorMode: PlateCapMeshColorMode.SourceCellFacet,
                perCellColors: colors);

            var radii = new List<double>();
            for (int triangle = 0; triangle < cap.CellIds.Length; triangle++)
            {
                if (cap.CellIds[triangle] != cellId)
                    continue;

                for (int corner = 0; corner < 3; corner++)
                {
                    int position = ((triangle * 3) + corner) * 3;
                    double x = mesh.Positions[position + 0];
                    double y = mesh.Positions[position + 1];
                    double z = mesh.Positions[position + 2];
                    double radius = Math.Sqrt((x * x) + (y * y) + (z * z));
                    Assert.InRange(
                        radius - projection.BaseRadius,
                        -projection.MaxDisplacementUnitRadius - 1e-6,
                        projection.MaxDisplacementUnitRadius + 1e-6);
                    radii.Add(radius);
                }
            }

            Assert.NotEmpty(radii);
            result[cellId] = radii.Average();
        }

        return result;
    }

    private static List<int> CellsWithFeature(
        IReadOnlyDictionary<int, CrustFeature>? features,
        CrustFeatureKind kind)
    {
        if (features is null) return new List<int>();
        return features
            .Where(kv => kv.Value.Kind == kind)
            .Select(kv => kv.Key)
            .ToList();
    }
}
