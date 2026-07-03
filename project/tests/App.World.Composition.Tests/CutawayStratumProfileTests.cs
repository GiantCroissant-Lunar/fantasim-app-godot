using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.World.Composition.Tests;

public class CutawayStratumProfileTests
{
    private const double PlanetRadiusMeters = 6_371_000.0;

    [Fact]
    public void ComputeBands_DefaultProfileHasCrustLithosphereMantle()
    {
        var bands = CutawayStratumProfile.ComputeBands(
            crustThicknessMetres: 30_000.0,
            lithosphereLidThicknessMetres: 90_000.0,
            exaggeration: 1.0,
            planetRadiusMetres: PlanetRadiusMeters);

        Assert.Equal(3, bands.Count);
        Assert.Equal("crust", bands[0].Label);
        Assert.Equal("lithosphere", bands[1].Label);
        Assert.Equal("mantle", bands[2].Label);
    }

    [Fact]
    public void ComputeBands_CrustBandOuterIsSurfaceInnerFollowsThickness()
    {
        var bands = CutawayStratumProfile.ComputeBands(
            crustThicknessMetres: 30_000.0,
            lithosphereLidThicknessMetres: 90_000.0,
            exaggeration: 1.0,
            planetRadiusMetres: PlanetRadiusMeters);

        var crust = bands[0];
        Assert.Equal(1.0, crust.OuterRadius);
        var expectedInner = 1.0 - (30_000.0 / PlanetRadiusMeters);
        Assert.Equal(expectedInner, crust.InnerRadius, precision: 10);
    }

    [Fact]
    public void ComputeBands_ExaggerationScalesAllThicknesses()
    {
        var bands = CutawayStratumProfile.ComputeBands(
            crustThicknessMetres: 30_000.0,
            lithosphereLidThicknessMetres: 90_000.0,
            exaggeration: 10.0,
            planetRadiusMetres: PlanetRadiusMeters);

        var crust = bands[0];
        var expectedCrustInner = 1.0 - (30_000.0 * 10.0 / PlanetRadiusMeters);
        Assert.Equal(expectedCrustInner, crust.InnerRadius, precision: 10);

        var litho = bands[1];
        var expectedLithoInner = expectedCrustInner - (90_000.0 * 10.0 / PlanetRadiusMeters);
        Assert.Equal(expectedLithoInner, litho.InnerRadius, precision: 10);
    }

    [Fact]
    public void ComputeBands_MantleExtendsToCenter()
    {
        var bands = CutawayStratumProfile.ComputeBands(
            crustThicknessMetres: 30_000.0,
            lithosphereLidThicknessMetres: 90_000.0,
            exaggeration: 1.0,
            planetRadiusMetres: PlanetRadiusMeters);

        Assert.Equal(0.0, bands[2].InnerRadius);
    }

    [Fact]
    public void ComputeBands_BandsAreContiguous()
    {
        var bands = CutawayStratumProfile.ComputeBands(
            crustThicknessMetres: 30_000.0,
            lithosphereLidThicknessMetres: 90_000.0,
            exaggeration: 5.0,
            planetRadiusMetres: PlanetRadiusMeters);

        Assert.Equal(bands[0].InnerRadius, bands[1].OuterRadius, precision: 10);
        Assert.Equal(bands[1].InnerRadius, bands[2].OuterRadius, precision: 10);
    }

    [Fact]
    public void ComputeBands_StratumColorsAreWarmDarkNotBlue()
    {
        var bands = CutawayStratumProfile.ComputeBands(
            crustThicknessMetres: 30_000.0,
            lithosphereLidThicknessMetres: 90_000.0,
            exaggeration: 1.0,
            planetRadiusMetres: PlanetRadiusMeters);

        // No sphere-costume: strata must not read as another sphere's subject (no blue).
        foreach (var band in bands)
        {
            Assert.True(band.Color.B < 0.4, $"Stratum '{band.Label}' color B={band.Color.B} reads blue (sphere costume).");
        }
    }

    [Fact]
    public void ComputeBands_CrustColorIsWarmDarkNotDataColor()
    {
        var bands = CutawayStratumProfile.ComputeBands(
            crustThicknessMetres: 30_000.0,
            lithosphereLidThicknessMetres: 90_000.0,
            exaggeration: 1.0,
            planetRadiusMetres: PlanetRadiusMeters);

        var crust = bands[0];
        // Crust is the truth band — a warm earthy tone, distinct from the plain lid/mantle.
        Assert.True(crust.Color.R > crust.Color.B, "Crust should be warmer than blue.");
        Assert.InRange(crust.Color.R, 0.2, 0.7);
    }

    [Fact]
    public void ComputeBands_LithosphereAndMantleAreClearlyNotDataTones()
    {
        var bands = CutawayStratumProfile.ComputeBands(
            crustThicknessMetres: 30_000.0,
            lithosphereLidThicknessMetres: 90_000.0,
            exaggeration: 1.0,
            planetRadiusMetres: PlanetRadiusMeters);

        var litho = bands[1];
        var mantle = bands[2];
        // Warm dark — clearly not-data (no truth field), so the eye distinguishes the one honest band.
        Assert.InRange(litho.Color.R, 0.05, 0.3);
        Assert.InRange(mantle.Color.R, 0.02, 0.2);
        Assert.True(litho.Color.R > mantle.Color.R, "Lithosphere should be lighter than mantle.");
    }

    [Fact]
    public void ComputeBands_PerCellCrustThickness_VariesInnerRadiusByCell()
    {
        double[] thicknesses = { 30_000.0, 60_000.0 };
        var perCell = CutawayStratumProfile.ComputePerCellCrustInnerRadii(
            crustThicknessMetresByCell: thicknesses,
            exaggeration: 1.0,
            planetRadiusMetres: PlanetRadiusMeters);

        Assert.Equal(2, perCell.Count);
        Assert.Equal(1.0 - 30_000.0 / PlanetRadiusMeters, perCell[0], precision: 10);
        Assert.Equal(1.0 - 60_000.0 / PlanetRadiusMeters, perCell[1], precision: 10);
    }

    [Fact]
    public void ComputeBands_ExaggerationZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CutawayStratumProfile.ComputeBands(
                crustThicknessMetres: 30_000.0,
                lithosphereLidThicknessMetres: 90_000.0,
                exaggeration: 0.0,
                planetRadiusMetres: PlanetRadiusMeters));
    }

    [Fact]
    public void FormatExaggerationIndicator_FollowsVerticalScaleLabelPattern()
    {
        var label = CutawayStratumProfile.FormatExaggerationIndicator(exaggeration: 10.0);
        Assert.Contains("cutaway", label);
        Assert.Contains("x10", label);
    }
}