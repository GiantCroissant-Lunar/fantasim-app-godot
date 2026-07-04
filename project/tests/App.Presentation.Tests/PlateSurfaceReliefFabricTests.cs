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
        Assert.True(crust.BaseFrequency >= 16.0);
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
