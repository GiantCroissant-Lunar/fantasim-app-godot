using FantaSim.App.World.Dto;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Services;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldServiceGenerationProductsTests
{
    [Fact]
    public void Service_CachesGenerationProductsFromSuccessfulGraphRequest()
    {
        using var service = new Service(new ServiceRegistry());
        var before = service.GetGenerationProductsAsync();
        var productAddress = "/base/main/formation/body-set@1234";

        var result = service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "graph-world",
            GenerationSpec: "body-formation:planetesimal-swarm:formation.planetesimal-swarm:G7:S1:base:main",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["graphRevision"] = 7,
                ["canonicalTick"] = 1_234L,
                ["productAddresses"] = new[] { productAddress },
            }));
        var products = service.GetGenerationProductsAsync();

        Assert.Empty(before.Products);
        Assert.True(result.Success);
        Assert.Equal(7, products.GraphRevision);
        Assert.Equal(1_234L, products.ReferenceTick);
        Assert.Equal(new[] { productAddress }, products.Products);
    }

    [Fact]
    public void Service_PreservesGenerationProductsWhenSuccessfulRequestIsNotGraphSourced()
    {
        using var service = new Service(new ServiceRegistry());
        var productAddress = "/base/main/formation/body-set@1234";
        service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "graph-world",
            GenerationSpec: "body-formation:planetesimal-swarm:formation.planetesimal-swarm:G7:S1:base:main",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["graphRevision"] = 7,
                ["canonicalTick"] = 1_234L,
                ["productAddresses"] = new[] { productAddress },
            }));

        service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "graph-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)));
        var products = service.GetGenerationProductsAsync();

        Assert.Equal(7, products.GraphRevision);
        Assert.Equal(1_234L, products.ReferenceTick);
        Assert.Equal(new[] { productAddress }, products.Products);
    }

    [Fact]
    public void Service_IdenticalReRuns_DoNotIncrementGraphRevision()
    {
        using var service = new Service(new ServiceRegistry());
        var productAddress = "/base/main/formation/body-set@1234";

        var result1 = service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "graph-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["canonicalTick"] = 1_234L,
                ["productAddresses"] = new[] { productAddress },
            }));
        var products1 = service.GetGenerationProductsAsync();
        var initialRevision = products1.GraphRevision;

        var result2 = service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "graph-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["canonicalTick"] = 1_234L,
                ["productAddresses"] = new[] { productAddress },
            }));
        var products2 = service.GetGenerationProductsAsync();

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(initialRevision, products2.GraphRevision);
    }

    [Fact]
    public void PlanetPresentation_CarriesGenerationGraphId_ForLayerProductAddress()
    {
        using var service = new Service(new ServiceRegistry());
        var productAddress = "/base/main/geosphere/mobile-plate.geosphere.crust@1234";
        service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "graph-world",
            GenerationSpec: "world.layer-scope:mobile-plate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["graphRevision"] = 3,
                ["canonicalTick"] = 1_234L,
                ["productAddresses"] = new[] { productAddress },
            }));

        var document = service.GetPlanetPresentationAsync();

        var layer = Assert.Single(document.Layers);
        Assert.Equal("geosphere.crust", layer.LayerId);
        Assert.Equal("mobile-plate", layer.RegimeId);
        Assert.Equal(WorldGenerationGraphDefaults.GeosphereCrustLayerGraphId, layer.GenerationGraphId);
    }

    [Fact]
    public void PlanetPresentation_LeavesGenerationGraphIdNull_WhenNoLayerBindingMatches()
    {
        using var service = new Service(new ServiceRegistry());
        var productAddress = "/base/main/formation/body-set@1234";
        service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "graph-world",
            GenerationSpec: "body-formation:planetesimal-swarm",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["graphRevision"] = 1,
                ["canonicalTick"] = 1_234L,
                ["productAddresses"] = new[] { productAddress },
            }));

        var document = service.GetPlanetPresentationAsync();

        var layer = Assert.Single(document.Layers);
        Assert.Null(layer.GenerationGraphId);
    }

    [Fact]
    public void PlanetPresentation_NonCrustLayer_KeepsItsAddressTick_WhenSnapshotSelected()
    {
        // Review fix 2026-07-03: the selected crust-snapshot tick must rewrite ProductTick AND
        // ProductAddress for the mobile-plate crust layer ONLY. A non-crust layer advertising the
        // selected tick against its unchanged address is contradictory metadata.
        using var service = new Service(new ServiceRegistry());
        var productAddress = "/base/main/formation/body-set@1234";
        service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "graph-world",
            GenerationSpec: "body-formation:planetesimal-swarm",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["graphRevision"] = 1,
                ["canonicalTick"] = 1_234L,
                ["productAddresses"] = new[] { productAddress },
            }));

        // Reference tick deep in mobile-plate so a crust snapshot tick IS selected internally.
        var document = service.GetPlanetPresentationAsync(105_000_000L);

        var layer = Assert.Single(document.Layers);
        Assert.Equal(1_234L, layer.ProductTick);
        Assert.EndsWith("@1234", layer.ProductAddress);
    }

    [Fact]
    public void PlanetPresentation_CrustLayer_TickAndAddressRewrittenConsistently()
    {
        using var service = new Service(new ServiceRegistry());
        var productAddress = "/base/main/geosphere/mobile-plate.geosphere.crust@1234";
        service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "graph-world",
            GenerationSpec: "world.layer-scope:mobile-plate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["graphRevision"] = 3,
                ["canonicalTick"] = 1_234L,
                ["productAddresses"] = new[] { productAddress },
            }));

        var document = service.GetPlanetPresentationAsync(105_000_000L);

        var layer = Assert.Single(document.Layers);
        Assert.Equal(105_000_000L, layer.ProductTick);
        Assert.EndsWith($"@{105_000_000L}", layer.ProductAddress);
    }

    [Fact]
    public void PlanetPresentation_CarriesCellCrustThicknessAtOnset()
    {
        using var service = new Service(new ServiceRegistry());
        var document = service.GetPlanetPresentationAsync();

        Assert.NotNull(document.CellCrustThickness);
        Assert.Equal(document.GlobeSnapshot?.CellCount ?? 0, document.CellCrustThickness!.Count);
    }

    [Fact]
    public void PlanetPresentation_CutawayExaggerationHasDeclaredDefault()
    {
        using var service = new Service(new ServiceRegistry());
        var document = service.GetPlanetPresentationAsync();

        Assert.True(document.CutawayExaggeration > 0.0);
    }
}
