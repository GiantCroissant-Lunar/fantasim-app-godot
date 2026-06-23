using System;
using System.Linq;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Samples regime/layer fields using products captured from world-generation graphs. This is the
/// bridge from graph products into the existing layer field runtime; it intentionally returns plain
/// scalars so T4 render seams do not need to understand field-composition internals.
/// </summary>
public sealed class WorldGenerationRegimeFieldSampler
{
    private static readonly WorldGlobeGeometry SingleCellGeometry = new(
        PlateIds: new[] { "geosphere" },
        Cells: new[]
        {
            new PlateCellPolygon(
                "geosphere",
                new[] { new GeoPoint(0, 0), new GeoPoint(0, 1), new GeoPoint(1, 0) }),
        },
        BoundarySegments: Array.Empty<BoundaryGeoSegment>());

    public double ResolveMagmaOceanSurfaceTemperatureK(long tick, SphereHandoff? sphereHandoff)
    {
        var layer = new GeosphereMagmaOceanLayer();
        var composer = new FieldComposer();
        GeosphereFieldCatalog.DeclareInto(composer);
        composer.AddLayer(layer.Fields);
        var composition = composer.Compose();
        if (!composition.IsValid)
        {
            var problems = string.Join("; ", composition.Errors.Select(error => error.Message));
            throw new InvalidOperationException($"Magma-ocean field composition is invalid: {problems}");
        }

        var values = new FieldValueResolver().Resolve(
            composition,
            new ILayer[] { layer },
            SingleCellGeometry,
            tick,
            sphereHandoff);

        return values.Scalars
            .Single(scalar => scalar.Field == GeosphereFieldCatalog.SurfaceTemperature)
            .Values
            .Average();
    }
}
