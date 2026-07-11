using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Presentation;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using Xunit;

namespace App.Presentation.Tests;

/// <summary>
/// The stamp dedupes the generation-completion chase (G34 double full bind): two fetches with
/// identical rendered-surface content must produce EQUAL stamps even when product metadata
/// (Revision/ReferenceTick) flapped, and ANY content or composition difference must produce
/// unequal stamps so the 105M-identical-terrain delivery guarantee cannot regress.
/// </summary>
public sealed class PlanetSurfaceBindStampTests
{
    private static readonly TimelineLayerSelection GeoPlate = new("geosphere", "plate");

    private static PlanetPresentationDocument BuildDocument(
        int revision = 1,
        long referenceTick = 100,
        double[]? elevations = null,
        double[]? thickness = null,
        CellCrustFeature[]? features = null,
        long globeReferenceTick = 100,
        double verticalExaggeration = 0.00003,
        SurfaceSubdivisionMode subdivision = SurfaceSubdivisionMode.Fixed)
    {
        // Fresh collection instances per call — mirrors two independent service fetches.
        elevations ??= new[] { 0.1, 0.2, 0.3 };
        thickness ??= new[] { 1.0, 1.1, 1.2 };
        features ??= new[] { new CellCrustFeature(1, 0.5) };
        return new PlanetPresentationDocument(
            PlanetId: "planet-1",
            SourceWorldId: "world-1",
            ReferenceTick: referenceTick,
            Revision: revision,
            Layers: Array.Empty<PlanetPresentationLayer>(),
            RenderEntities: Array.Empty<RenderEntityDto>())
        {
            GlobeReferenceTick = globeReferenceTick,
            CellElevations = elevations.ToArray(),
            CellCrustThickness = thickness.ToArray(),
            CellFeatures = features.ToArray(),
            ContinentalFractionByCell = new Dictionary<int, double> { [0] = 0.7, [1] = 0.2 },
            VerticalExaggeration = verticalExaggeration,
            SurfaceSubdivision = subdivision,
        };
    }

    private static PlanetSurfaceBindStamp Stamp(
        PlanetPresentationDocument document,
        string? regimeId = "mobile-plate",
        IReadOnlyList<TimelineLayerSelection>? activeLayers = null,
        string? plateViewOverride = null)
        => PlanetSurfaceBindStamp.From(
            document, regimeId, activeLayers ?? new[] { GeoPlate }, plateViewOverride);

    [Fact]
    public void Equal_for_two_fetches_with_identical_content_in_fresh_collections()
    {
        Assert.Equal(Stamp(BuildDocument()), Stamp(BuildDocument()));
    }

    [Fact]
    public void Equal_when_only_product_metadata_flapped()
    {
        // The chase fetch may carry bumped Revision/ReferenceTick without any rendered change —
        // this is exactly the redundant bind the stamp exists to skip.
        Assert.Equal(
            Stamp(BuildDocument(revision: 1, referenceTick: 100)),
            Stamp(BuildDocument(revision: 2, referenceTick: 105)));
    }

    [Fact]
    public void Differs_when_a_single_elevation_value_changes()
    {
        Assert.NotEqual(
            Stamp(BuildDocument(elevations: new[] { 0.1, 0.2, 0.3 })),
            Stamp(BuildDocument(elevations: new[] { 0.1, 0.2, 0.30000001 })));
    }

    [Fact]
    public void Differs_when_cell_count_changes()
    {
        // Different tessellation frequency ⇒ different cell count — a low-rung scrub fetch must
        // never dedupe against a full-frequency bind.
        Assert.NotEqual(
            Stamp(BuildDocument(elevations: new[] { 0.1, 0.2, 0.3 })),
            Stamp(BuildDocument(elevations: new[] { 0.1, 0.2, 0.3, 0.4 })));
    }

    [Fact]
    public void Differs_when_crust_thickness_changes()
    {
        Assert.NotEqual(
            Stamp(BuildDocument(thickness: new[] { 1.0, 1.1, 1.2 })),
            Stamp(BuildDocument(thickness: new[] { 1.0, 1.1, 1.3 })));
    }

    [Fact]
    public void Differs_when_a_feature_changes()
    {
        Assert.NotEqual(
            Stamp(BuildDocument(features: new[] { new CellCrustFeature(1, 0.5) })),
            Stamp(BuildDocument(features: new[] { new CellCrustFeature(2, 0.5) })));
        Assert.NotEqual(
            Stamp(BuildDocument(features: new[] { new CellCrustFeature(1, 0.5) })),
            Stamp(BuildDocument(features: new[] { new CellCrustFeature(1, 0.6) })));
    }

    [Fact]
    public void Differs_when_globe_reference_tick_changes()
    {
        Assert.NotEqual(
            Stamp(BuildDocument(globeReferenceTick: 100)),
            Stamp(BuildDocument(globeReferenceTick: 200)));
    }

    [Fact]
    public void Differs_when_null_vs_empty_elevations()
    {
        var withNull = new PlanetPresentationDocument(
            "planet-1", "world-1", 100, 1,
            Array.Empty<PlanetPresentationLayer>(), Array.Empty<RenderEntityDto>());
        var withEmpty = withNull with { CellElevations = Array.Empty<double>() };
        Assert.NotEqual(Stamp(withNull), Stamp(withEmpty));
    }

    [Fact]
    public void Differs_when_composition_inputs_change()
    {
        var doc = BuildDocument();
        Assert.NotEqual(Stamp(doc, regimeId: "mobile-plate"), Stamp(doc, regimeId: "stagnant-lid"));
        Assert.NotEqual(
            Stamp(doc, activeLayers: new[] { GeoPlate }),
            Stamp(doc, activeLayers: new[] { GeoPlate, new TimelineLayerSelection("geosphere", "mantle") }));
        Assert.NotEqual(Stamp(doc, plateViewOverride: null), Stamp(doc, plateViewOverride: "identity"));
    }

    [Fact]
    public void Differs_when_render_tuning_changes()
    {
        Assert.NotEqual(
            Stamp(BuildDocument(verticalExaggeration: 0.00003)),
            Stamp(BuildDocument(verticalExaggeration: 0.00006)));
        Assert.NotEqual(
            Stamp(BuildDocument(subdivision: SurfaceSubdivisionMode.Fixed)),
            Stamp(BuildDocument(subdivision: SurfaceSubdivisionMode.Adaptive)));
    }

    [Fact]
    public void Differs_when_continental_fractions_change()
    {
        var a = BuildDocument() with { ContinentalFractionByCell = new Dictionary<int, double> { [0] = 0.7 } };
        var b = BuildDocument() with { ContinentalFractionByCell = new Dictionary<int, double> { [0] = 0.8 } };
        Assert.NotEqual(Stamp(a), Stamp(b));
    }

    [Fact]
    public void Fraction_hash_is_insertion_order_independent()
    {
        var a = BuildDocument() with
        {
            ContinentalFractionByCell = new Dictionary<int, double> { [0] = 0.7, [1] = 0.2 },
        };
        var b = BuildDocument() with
        {
            ContinentalFractionByCell = new Dictionary<int, double> { [1] = 0.2, [0] = 0.7 },
        };
        Assert.Equal(Stamp(a), Stamp(b));
    }
}
