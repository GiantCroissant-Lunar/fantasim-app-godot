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

        Assert.True(crust.Amplitude > GlobePlateSurfaces.DefaultPeaks.Amplitude);
        Assert.InRange(crust.BaseFrequency, 5.0, 10.0);
        Assert.True(crust.Octaves >= 4);

        var sampler = new TectonicDetailSampler(
            SingleCellSnapshot(),
            new[] { new CellCrustFeature(Kind: 1, Magnitude: 10_000.0) },
            crust,
            PlateSurfaceReliefFabric.InteriorAmplitudeMultiplierForView(GlobeViewMode.HypsometricTerrain),
            PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(GlobeViewMode.HypsometricTerrain));
        double standardDeviation = SampledRadialStandardDeviation(sampler);

        Assert.InRange(standardDeviation, 0.06, 0.22);
    }

    [Fact]
    public void ForView_gives_dry_crust_interiors_visible_roughness_at_declared_scale()
    {
        var crust = PlateSurfaceReliefFabric.ForView(GlobeViewMode.HypsometricTerrain);
        var sampler = new TectonicDetailSampler(
            SingleCellSnapshot(),
            new[] { default(CellCrustFeature) },
            crust,
            PlateSurfaceReliefFabric.InteriorAmplitudeMultiplierForView(GlobeViewMode.HypsometricTerrain),
            PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(GlobeViewMode.HypsometricTerrain));

        double standardDeviation = SampledRadialStandardDeviation(sampler);

        Assert.True(
            standardDeviation >= 0.025,
            $"Expected visible dry-crust interior roughness; sampled radial standard deviation was {standardDeviation:0.0000}.");
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
    public void ForView_keeps_active_diagnostic_features_below_blade_peak_range()
    {
        var crust = PlateSurfaceReliefFabric.ForView(GlobeViewMode.HypsometricTerrain);
        var sampler = new TectonicDetailSampler(
            SingleCellSnapshot(),
            new[] { new CellCrustFeature(Kind: 1, Magnitude: 10_000.0) },
            crust,
            PlateSurfaceReliefFabric.InteriorAmplitudeMultiplierForView(GlobeViewMode.HypsometricTerrain),
            PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(GlobeViewMode.HypsometricTerrain),
            PlateSurfaceReliefFabric.ActiveAmplitudeMultiplierForView(GlobeViewMode.HypsometricTerrain));

        double peak = MaxSampledAbsoluteRadialDisplacement(sampler);

        Assert.True(
            peak <= 0.16,
            $"Expected active diagnostic crust peaks below blade range; max sampled radial displacement was {peak:0.000}.");
    }

    [Fact]
    public void ForView_keeps_plate_identity_flat()
    {
        var identity = PlateSurfaceReliefFabric.ForView(GlobeViewMode.PlateIdentity);

        Assert.Equal(0.0, identity.Amplitude);
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
