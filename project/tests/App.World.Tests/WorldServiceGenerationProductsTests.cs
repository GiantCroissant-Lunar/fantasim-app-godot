using FantaSim.App.World.Dto;
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
        Assert.Empty(products.CachedTicks);
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
        Assert.Empty(products.CachedTicks);
    }
}
