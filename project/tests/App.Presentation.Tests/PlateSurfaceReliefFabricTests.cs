using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlateSurfaceReliefFabricTests
{
    [Fact]
    public void ForView_keeps_crust_diagnostic_fabric_inside_linear_displacement_budget()
    {
        var crust = PlateSurfaceReliefFabric.ForView(GlobeViewMode.HypsometricTerrain);

        Assert.True(crust.Amplitude > GlobePlateSurfaces.DefaultPeaks.Amplitude);
        Assert.True(crust.BaseFrequency >= 16.0);
        Assert.True(crust.Octaves >= 4);

        const double defaultCrustMetresToUnitRadius = 0.00003;
        const double maxTectonicAmplitudeMultiplier = 1.45;
        double worstCaseFeatureDisplacement =
            crust.Amplitude * maxTectonicAmplitudeMultiplier * defaultCrustMetresToUnitRadius;

        Assert.InRange(worstCaseFeatureDisplacement, 0.08, 0.14);
    }

    [Fact]
    public void ForView_keeps_plate_identity_flat()
    {
        var identity = PlateSurfaceReliefFabric.ForView(GlobeViewMode.PlateIdentity);

        Assert.Equal(0.0, identity.Amplitude);
    }
}
