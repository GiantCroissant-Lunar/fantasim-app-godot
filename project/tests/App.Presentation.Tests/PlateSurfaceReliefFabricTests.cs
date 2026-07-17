using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Shared;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlateSurfaceReliefFabricTests
{
    [Fact]
    public void ForView_keeps_crust_diagnostic_feature_roughness_inside_linear_displacement_budget()
    {
        var crust = PlateSurfaceReliefFabric.ForView(GlobeViewMode.HypsometricTerrain);

        Assert.InRange(crust.BaseFrequency, 5.0, 10.0);
        Assert.True(crust.Octaves >= 4);

        var sampler = new TectonicDetailSampler(
            SingleCellSnapshot(),
            new[] { new CellCrustFeature(Kind: 1, Magnitude: 10_000.0) },
            crust,
            PlateSurfaceReliefFabric.InteriorAmplitudeMultiplierForView(GlobeViewMode.HypsometricTerrain),
            PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(GlobeViewMode.HypsometricTerrain));
        double standardDeviation = SampledRadialStandardDeviation(sampler);

        Assert.True(standardDeviation > 0.0,
            $"feature belt must add non-zero roughness; std dev was {standardDeviation}");
        Assert.True(standardDeviation <= 0.02,
            $"bounded residual fabric must stay subtle; std dev {standardDeviation} exceeds the 250 m peak budget at this scale");
    }

    [Fact]
    public void ForView_gives_dry_crust_interiors_calm_roughness_so_belts_read()
    {
        // North-star spec §3: "Most of the surface is calm; that calm is what makes belts read."
        // Interior fabric (no active feature) must stay calm at the declared scale so the thin
        // ridged boundary belts stand out against it.
        var crust = PlateSurfaceReliefFabric.ForView(GlobeViewMode.HypsometricTerrain);
        var sampler = new TectonicDetailSampler(
            SingleCellSnapshot(),
            new[] { default(CellCrustFeature) },
            crust,
            PlateSurfaceReliefFabric.InteriorAmplitudeMultiplierForView(GlobeViewMode.HypsometricTerrain),
            PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(GlobeViewMode.HypsometricTerrain));

        double standardDeviation = SampledRadialStandardDeviation(sampler);

        Assert.True(
            standardDeviation <= 0.025,
            $"Expected calm dry-crust interiors so belts read; sampled radial standard deviation was {standardDeviation:0.0000}.");
    }

    [Fact]
    public void ForView_favors_broad_dry_crust_crumples_over_high_frequency_chatter()
    {
        var crust = PlateSurfaceReliefFabric.ForView(GlobeViewMode.HypsometricTerrain);
        var sampler = new TectonicDetailSampler(
            SingleCellSnapshot(),
            new[] { default(CellCrustFeature) },
            crust,
            PlateSurfaceReliefFabric.InteriorAmplitudeMultiplierForView(GlobeViewMode.HypsometricTerrain),
            PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(GlobeViewMode.HypsometricTerrain));

        var samples = PairedNearbyRadialDisplacements(sampler, radians: 0.012, count: 384).ToArray();
        double roughness = SampleStandardDeviation(samples.Select(pair => pair.A).Concat(samples.Select(pair => pair.B)));
        double nearbyDelta = samples.Average(pair => Math.Abs(pair.A - pair.B));
        double chatterRatio = nearbyDelta / roughness;

        Assert.True(
            chatterRatio <= 0.30,
            $"Expected broad crumpled dry crust, but nearby displacement chatter ratio was {chatterRatio:0.000}.");
    }

    [Fact]
    public void ForView_keeps_plate_identity_flat()
    {
        var identity = PlateSurfaceReliefFabric.ForView(GlobeViewMode.PlateIdentity);

        Assert.Equal(0.0, identity.Amplitude);
    }

    // === Concentrated drama (north-star spec §3) ===================================================
    //
    // Interior fabric amplitude <= 0.15x the effective belt (active) amplitude. Belts are the
    // drama now that the silhouette is capped (§1). The resolved TectonicDetailProfile amplitudes
    // must satisfy interior <= 0.15 * belt for representative active feature kinds in BOTH planet
    // views (World + HypsometricTerrain). Crust diagnostic ridges active features and does NOT
    // damp active amplitude (no 0.45 multiplier).

    private static readonly CellCrustFeature[] RepresentativeActiveFeatures =
    {
        new(Kind: 1, Magnitude: 10_000.0), // Mountain
        new(Kind: 2, Magnitude: 10_000.0), // Volcanic arc
        new(Kind: 3, Magnitude: 10_000.0), // Trench
        new(Kind: 4, Magnitude: 10_000.0), // Ridge
    };

    [Theory]
    [InlineData(GlobeViewMode.World)]
    [InlineData(GlobeViewMode.HypsometricTerrain)]
    public void ForView_interior_amplitude_is_at_most_15_percent_of_belt_for_active_features(GlobeViewMode view)
    {
        var relief = PlateSurfaceReliefFabric.ForView(view);
        var interiorMult = PlateSurfaceReliefFabric.InteriorAmplitudeMultiplierForView(view);
        var activeMult = PlateSurfaceReliefFabric.ActiveAmplitudeMultiplierForView(view);

        foreach (var feature in RepresentativeActiveFeatures)
        {
            var snapshot = SingleCellSnapshot();
            var features = new[] { feature };
            var sampler = new TectonicDetailSampler(
                snapshot,
                features,
                relief,
                interiorMult,
                PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(view),
                activeMult);

            // High-magnitude feature -> weight ~1 -> resolved amplitude is the belt amplitude.
            var belt = sampler.ResolveProfile(CellCenterOf(snapshot)).Noise.Amplitude;

            // Interior sampler (kind 0) -> the interior amplitude.
            var interiorSampler = new TectonicDetailSampler(
                snapshot,
                new[] { default(CellCrustFeature) },
                relief,
                interiorMult,
                PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(view),
                activeMult);
            var interior = interiorSampler.ResolveProfile(CellCenterOf(snapshot)).Noise.Amplitude;

            Assert.True(
                interior <= 0.15 * belt + 1e-9,
                $"view {view} kind {feature.Kind}: interior {interior:G5} > 0.15*belt {0.15*belt:G5}");
        }
    }

    [Fact]
    public void ForView_crust_diagnostic_ridges_active_features()
    {
        // Spec §3: ridging ON for active features (mountain/arc/trench/ridge) in the crust
        // diagnostic. Belts are thin, ridged, boundary-aligned — mountain CHAINS, not crumple.
        Assert.True(PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(GlobeViewMode.HypsometricTerrain));
    }

    [Fact]
    public void ForView_crust_diagnostic_does_not_damp_active_amplitude()
    {
        Assert.Equal(1.0, PlateSurfaceReliefFabric.ActiveAmplitudeMultiplierForView(GlobeViewMode.HypsometricTerrain));
    }

    [Fact]
    public void ForView_world_keeps_active_amplitude_undamped()
    {
        Assert.Equal(1.0, PlateSurfaceReliefFabric.ActiveAmplitudeMultiplierForView(GlobeViewMode.World));
    }

    [Fact]
    public void Trench_residual_profile_is_never_ridged()
    {
        var crust = PlateSurfaceReliefFabric.ForView(GlobeViewMode.HypsometricTerrain);
        var sampler = new TectonicDetailSampler(
            SingleCellSnapshot(),
            new[] { new CellCrustFeature(Kind: 3, Magnitude: 10_000.0) },
            crust,
            PlateSurfaceReliefFabric.InteriorAmplitudeMultiplierForView(GlobeViewMode.HypsometricTerrain),
            PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(GlobeViewMode.HypsometricTerrain));

        var profile = sampler.ResolveProfile(CellCenterOf(SingleCellSnapshot()));

        Assert.False(profile.Noise.Ridged,
            $"Trench residual fabric must never be ridged (positive ridged detail); got Ridged={profile.Noise.Ridged}");
    }

    [Theory]
    [InlineData(GlobeViewMode.World)]
    [InlineData(GlobeViewMode.HypsometricTerrain)]
    public void Residual_noise_amplitude_never_exceeds_250m_for_any_feature_kind(GlobeViewMode view)
    {
        var relief = PlateSurfaceReliefFabric.ForView(view);
        var interiorMult = PlateSurfaceReliefFabric.InteriorAmplitudeMultiplierForView(view);
        var activeMult = PlateSurfaceReliefFabric.ActiveAmplitudeMultiplierForView(view);
        var ridgeActive = PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(view);

        for (byte kind = 0; kind <= 5; kind++)
        {
            var snapshot = SingleCellSnapshot();
            var features = new[] { new CellCrustFeature(kind, 10_000.0) };
            var sampler = new TectonicDetailSampler(snapshot, features, relief, interiorMult, ridgeActive, activeMult);
            var profile = sampler.ResolveProfile(CellCenterOf(snapshot));

            Assert.True(
                profile.Noise.Amplitude <= TectonicDetailSampler.MaxResidualAmplitudeMetres + 1e-9,
                $"view {view} kind {kind}: amplitude {profile.Noise.Amplitude:G5} exceeds {TectonicDetailSampler.MaxResidualAmplitudeMetres} m");
        }
    }

    [Fact]
    public void Residual_noise_amplitude_is_at_most_one_third_of_smallest_mandatory_signal()
    {
        Assert.Equal(800.0, TectonicDetailSampler.SmallestMandatoryBoundarySignalMetres);
        // Assembled-world north-star: calm-interiors 1/3 law retired (see WorldCrustMaterializerTests
        // twin comment); the cap guards runaway residuals only.
        Assert.True(
            TectonicDetailSampler.MaxResidualAmplitudeMetres
                <= TectonicDetailSampler.SmallestMandatoryBoundarySignalMetres * 2.0,
            $"Max residual {TectonicDetailSampler.MaxResidualAmplitudeMetres} must stay within 2x of {TectonicDetailSampler.SmallestMandatoryBoundarySignalMetres}");
    }

    [Fact]
    public void TectonicFeatureKind_mapping_is_exhaustive_and_degrades_unknown_to_none()
    {
        Assert.Equal(TectonicFeatureKind.None, TectonicFeatureKindExtensions.FromWireByte(0));
        Assert.Equal(TectonicFeatureKind.Mountain, TectonicFeatureKindExtensions.FromWireByte(1));
        Assert.Equal(TectonicFeatureKind.VolcanicArc, TectonicFeatureKindExtensions.FromWireByte(2));
        Assert.Equal(TectonicFeatureKind.Trench, TectonicFeatureKindExtensions.FromWireByte(3));
        Assert.Equal(TectonicFeatureKind.Ridge, TectonicFeatureKindExtensions.FromWireByte(4));
        Assert.Equal(TectonicFeatureKind.Fault, TectonicFeatureKindExtensions.FromWireByte(5));
        Assert.Equal(TectonicFeatureKind.None, TectonicFeatureKindExtensions.FromWireByte(255));
        Assert.Equal((byte)0, TectonicFeatureKind.None.ToWireByte());
        Assert.Equal((byte)1, TectonicFeatureKind.Mountain.ToWireByte());
        Assert.Equal((byte)2, TectonicFeatureKind.VolcanicArc.ToWireByte());
        Assert.Equal((byte)3, TectonicFeatureKind.Trench.ToWireByte());
        Assert.Equal((byte)4, TectonicFeatureKind.Ridge.ToWireByte());
        Assert.Equal((byte)5, TectonicFeatureKind.Fault.ToWireByte());
    }

    private static CartesianPoint3 CellCenterOf(WorldGlobeSnapshot snapshot)
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

    private static double SampledRadialStandardDeviation(TectonicDetailSampler sampler)
    {
        const double defaultCrustMetresToUnitRadius = 0.00003;
        var displacements = FibonacciDirections(384)
            .Select(point => sampler.Sample(point) * defaultCrustMetresToUnitRadius)
            .ToArray();

        double mean = displacements.Average();
        return Math.Sqrt(displacements.Average(v => (v - mean) * (v - mean)));
    }

    private static double MaxSampledAbsoluteRadialDisplacement(TectonicDetailSampler sampler)
    {
        const double defaultCrustMetresToUnitRadius = 0.00003;
        return FibonacciDirections(768)
            .Max(point => Math.Abs(sampler.Sample(point) * defaultCrustMetresToUnitRadius));
    }

    private static double SampleStandardDeviation(IEnumerable<double> values)
    {
        var samples = values.ToArray();
        double mean = samples.Average();
        return Math.Sqrt(samples.Average(v => (v - mean) * (v - mean)));
    }

    private static IEnumerable<(double A, double B)> PairedNearbyRadialDisplacements(
        TectonicDetailSampler sampler,
        double radians,
        int count)
    {
        const double defaultCrustMetresToUnitRadius = 0.00003;
        foreach (var point in FibonacciDirections(count))
        {
            var nearby = RotateTowardEast(point, radians);
            yield return (
                sampler.Sample(point) * defaultCrustMetresToUnitRadius,
                sampler.Sample(nearby) * defaultCrustMetresToUnitRadius);
        }
    }

    private static CartesianPoint3 RotateTowardEast(CartesianPoint3 point, double radians)
    {
        double eastX = -point.Y;
        double eastY = point.X;
        double eastLength = Math.Sqrt((eastX * eastX) + (eastY * eastY));
        if (eastLength <= 1e-12)
        {
            eastX = 1.0;
            eastY = 0.0;
            eastLength = 1.0;
        }

        eastX /= eastLength;
        eastY /= eastLength;

        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return new CartesianPoint3(
            (point.X * cos) + (eastX * sin),
            (point.Y * cos) + (eastY * sin),
            point.Z * cos);
    }

    private static IEnumerable<CartesianPoint3> FibonacciDirections(int count)
    {
        const double GoldenAngle = Math.PI * (3.0 - 2.23606797749979);
        for (int i = 0; i < count; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5) / count);
            double radius = Math.Sqrt(Math.Max(0.0, 1.0 - (z * z)));
            double theta = i * GoldenAngle;
            yield return new CartesianPoint3(
                radius * Math.Cos(theta),
                radius * Math.Sin(theta),
                z);
        }
    }
}
