using FantaSim.App.World.Topography;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Pins the Earth-like DEFAULT calibration for <see cref="BoundaryProfileParameters"/>. Every value is
/// a world parameter (the fantasy-world principle); these defaults are the reference Earth shape and must
/// not drift without an explicit rationale.
/// </summary>
public sealed class BoundaryProfileParametersTests
{
    [Fact]
    public void Default_has_calibrated_earth_like_values()
    {
        var p = BoundaryProfileParameters.Default;

        // Convergent subduction: deep trench on the down-going side, uplift arc set back on the override.
        // Trench -2000 sits the abyssal floor near -3500 on top of the -1500 oceanic base (CellElevationSystem
        // scale: ContinentalAmp=1000, OceanDeepening=1000).
        Assert.Equal(-2000.0, p.ConvergentTrenchDepth);
        Assert.Equal(0.06, p.ConvergentTrenchHalfWidthRad);

        // Arc +1500 clears the continental interior (+500) so the volcanic arc reads as a ridge.
        Assert.Equal(1500.0, p.ConvergentArcHeight);
        Assert.Equal(0.12, p.ConvergentArcSetbackRad);
        Assert.Equal(0.05, p.ConvergentArcHalfWidthRad);

        // Continent–continent collision: symmetric uplift (no subduction, no trench).
        Assert.Equal(2000.0, p.ConvergentCollisionHeight);
        Assert.Equal(0.08, p.ConvergentCollisionHalfWidthRad);

        // Divergent: symmetric swell flanks + narrow axial rift graben at the axis.
        Assert.Equal(800.0, p.DivergentSwellHeight);
        Assert.Equal(0.10, p.DivergentSwellHalfWidthRad);
        Assert.Equal(-400.0, p.DivergentRiftNotchDepth);
        Assert.Equal(0.02, p.DivergentRiftHalfWidthRad);

        // Transform: subtle narrow-band scarps (small amplitude vs convergent/divergent).
        Assert.Equal(250.0, p.TransformScarpAmplitude);
        Assert.Equal(0.04, p.TransformHalfWidthRad);
        Assert.Equal(32.0, p.TransformScarpPeriodPoints);
    }

    [Fact]
    public void Zero_has_zero_amplitudes_but_keeps_widths_well_defined()
    {
        var p = BoundaryProfileParameters.Zero;

        // Amplitudes zeroed so the contribution vanishes (regression pin: zero params reproduce the
        // pre-profile elevation exactly). Widths stay non-zero so the shape math stays well-defined.
        Assert.Equal(0.0, p.ConvergentTrenchDepth);
        Assert.Equal(0.0, p.ConvergentArcHeight);
        Assert.Equal(0.0, p.ConvergentCollisionHeight);
        Assert.Equal(0.0, p.DivergentSwellHeight);
        Assert.Equal(0.0, p.DivergentRiftNotchDepth);
        Assert.Equal(0.0, p.TransformScarpAmplitude);
        Assert.True(p.ConvergentTrenchHalfWidthRad > 0.0);
        Assert.True(p.DivergentSwellHalfWidthRad > 0.0);
        Assert.True(p.TransformHalfWidthRad > 0.0);
    }
}
