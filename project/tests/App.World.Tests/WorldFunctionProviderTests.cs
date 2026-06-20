using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using FantaSim.App.World;
using FantaSim.App.World.Recipes;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Proves the World axis plugs into the general node-graph paradigm as a function provider (pure C#,
/// no Godot): it claims the crust function family, and its <c>crust.generate</c> handler runs the real
/// crust pipeline end-to-end when driven through the shared <see cref="GraphExecutor"/>.
/// </summary>
public sealed class WorldFunctionProviderTests
{
    // ---------------------------------------------------------------------
    // Behavior 1: Supports claims world/geosphere/crust function ids and
    // declines other axes' families (e.g. iii's comfy.*).
    // ---------------------------------------------------------------------
    [Fact]
    public void Supports_claims_crust_family_and_declines_other_axes()
    {
        var provider = new WorldFunctionProvider();

        Assert.True(provider.Supports("crust.generate"));
        Assert.True(provider.Supports("geosphere.crust.evolve"));
        Assert.True(provider.Supports("world.generate"));

        Assert.False(provider.Supports("comfy.x"));
        Assert.False(provider.Supports("blender.refine"));
        Assert.False(provider.Supports("asset.to_gltf"));
        Assert.False(provider.Supports("ping"));
    }

    // ---------------------------------------------------------------------
    // Behavior 2: the CrustGenerationGraph recipe, run through the REAL
    // GraphExecutor with the WorldFunctionProvider, produces a crust run whose
    // topology has active boundaries and whose features include at least one
    // mountain (the 3-plate default ⇒ a convergent continent-continent boundary
    // ⇒ non-zero orogenic pressure crossing the threshold).
    // ---------------------------------------------------------------------
    [Fact]
    public async Task CrustGenerationGraph_through_executor_yields_active_boundaries_and_a_mountain()
    {
        // Given the general executor wired with ONLY the World function provider
        var provider = new WorldFunctionProvider();
        var executor = new GraphExecutor(new[] { (INodeFunctionProvider)provider });
        var graph = CrustGenerationGraph.Build();

        // When the crust-generation graph is executed
        var result = await executor.ExecuteAsync(graph);

        // Then the run summary reports a real tessellation and active plate boundaries
        Assert.True((int)result["cellCount"]! > 0, "expected a non-empty tessellation");
        Assert.True((int)result["boundaryCount"]! > 0, "expected inter-plate boundaries");
        Assert.True((bool)result["activeBoundaries"]!, "expected at least one active (non-inactive) boundary");

        // And the boundary classification includes a convergent boundary (drives orogeny)
        var boundaryTypes = result["boundaryTypes"]!.AsObject();
        Assert.True(boundaryTypes.ContainsKey("Convergent"), "expected a convergent boundary in the 3-plate setup");

        // And orogenic pressure accumulated above zero, yielding at least one mountain feature
        Assert.True((double)result["peakOrogenicPressure"]! > 0.0, "expected non-zero peak orogenic pressure");
        Assert.True((int)result["mountainCount"]! >= 1, "expected at least one mountain feature");
    }

    // ---------------------------------------------------------------------
    // Behavior 3: invoking crust.generate directly (the provider contract)
    // returns the same shaped summary the executor relays from the sink node.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task InvokeAsync_crust_generate_returns_a_summary_object()
    {
        var provider = new WorldFunctionProvider();

        var summary = await provider.InvokeAsync("crust.generate", new JsonObject());

        Assert.Equal("crust.generate", (string?)summary["function"]);
        Assert.True((int)summary["plateCount"]! == 3, "default setup is the proven 3-plate world");
        Assert.True((int)summary["featureCount"]! > 0, "expected derived crust features");
    }
}
