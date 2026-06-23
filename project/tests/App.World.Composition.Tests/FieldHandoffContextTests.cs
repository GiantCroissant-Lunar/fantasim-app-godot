using System;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using UnifyMaths;
using Xunit;

namespace App.World.Composition.Tests;

public class FieldHandoffContextTests
{
    [Fact]
    public void MagmaOceanLayer_DefaultHandoffMatchesFallback()
    {
        var withoutHandoff = ResolveMagmaOcean(sphereHandoff: null);
        var withDefaultHandoff = ResolveMagmaOcean(DefaultHandoff(retainedHeatJ: 5.972e31));

        Assert.Equal(
            ScalarAt(withoutHandoff, GeosphereFieldCatalog.SurfaceTemperature, cellIndex: 0),
            ScalarAt(withDefaultHandoff, GeosphereFieldCatalog.SurfaceTemperature, cellIndex: 0));
        Assert.Equal(
            ScalarAt(withoutHandoff, GeosphereFieldCatalog.MeltFraction, cellIndex: 0),
            ScalarAt(withDefaultHandoff, GeosphereFieldCatalog.MeltFraction, cellIndex: 0));
    }

    [Fact]
    public void MagmaOceanLayer_LowerRetainedHeatCoolsSurface()
    {
        var defaultHeat = ResolveMagmaOcean(DefaultHandoff(retainedHeatJ: 5.972e31));
        var lowerHeat = ResolveMagmaOcean(DefaultHandoff(retainedHeatJ: 5.972e30));

        Assert.True(
            ScalarAt(lowerHeat, GeosphereFieldCatalog.SurfaceTemperature, cellIndex: 0)
            < ScalarAt(defaultHeat, GeosphereFieldCatalog.SurfaceTemperature, cellIndex: 0));
        Assert.True(
            ScalarAt(lowerHeat, GeosphereFieldCatalog.MeltFraction, cellIndex: 0)
            < ScalarAt(defaultHeat, GeosphereFieldCatalog.MeltFraction, cellIndex: 0));
    }

    private static WorldFieldValues ResolveMagmaOcean(SphereHandoff? sphereHandoff)
    {
        var layer = new GeosphereMagmaOceanLayer();
        var composer = new FieldComposer();
        GeosphereFieldCatalog.DeclareInto(composer);
        composer.AddLayer(layer.Fields);
        var composition = composer.Compose();
        Assert.True(composition.IsValid);

        return new FieldValueResolver().Resolve(
            composition,
            new ILayer[] { layer },
            SimpleGeometry(),
            tick: 0,
            sphereHandoff);
    }

    private static WorldGlobeGeometry SimpleGeometry()
        => new(
            PlateIds: new[] { "plate-0" },
            Cells: new[]
            {
                new PlateCellPolygon(
                    "plate-0",
                    new[] { new GeoPoint(0, 0), new GeoPoint(0, 1), new GeoPoint(1, 0) }),
            },
            BoundarySegments: Array.Empty<BoundaryGeoSegment>());

    private static SphereHandoff DefaultHandoff(double retainedHeatJ)
        => new(
            Tick: 0,
            SourceBodyId: "protoplanet",
            TotalMassKg: 5.972e24,
            BulkCompositionFractions: new[]
            {
                new MaterialCompositionFraction("silicate", 0.68),
                new MaterialCompositionFraction("iron", 0.30),
                new MaterialCompositionFraction("volatile", 0.02),
            },
            RetainedHeatJ: retainedHeatJ,
            RetainedVolatileMassKg: 1.1944e23,
            AngularMomentum: new Vector3D(0, 0, 0),
            LatentSubstrateSeed: "geosphere/seed-7");

    private static double ScalarAt(WorldFieldValues values, FieldId field, int cellIndex)
        => values.Scalars.Single(scalar => scalar.Field == field).Values[cellIndex];
}
