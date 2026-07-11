using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Crust;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Recipes;
using FantaSim.World.Contracts.Units;
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
    // topology uses the shared presentation defaults and has active boundaries.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task CrustGenerationGraph_through_executor_yields_active_boundaries_for_default_roster()
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
        Assert.Equal(WorldGenerationRenderOptions.Default.TessellationFrequency, (int)result["frequency"]!);
        Assert.Equal(DefaultPresentationPlateCount(), (int)result["plateCount"]!);
        Assert.Equal(UnitConverter.MegaAnnumToTickDelta(8.0), (long)result["canonicalTick"]!);
        Assert.Equal(UnitConverter.MegaAnnumToTickDelta(8.0), (long)result["durationTicks"]!);

        // And the boundary classification includes a convergent boundary (drives orogeny)
        var boundaryTypes = result["boundaryTypes"]!.AsObject();
        Assert.True(boundaryTypes.ContainsKey("Convergent"), "expected a convergent boundary in the default setup");
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
        Assert.Equal(WorldGenerationRenderOptions.Default.TessellationFrequency, (int)summary["frequency"]!);
        Assert.Equal(DefaultPresentationPlateCount(), (int)summary["plateCount"]!);
        Assert.True((int)summary["featureCount"]! > 0, "expected derived crust features");
    }

    [Fact]
    public async Task InvokeAsync_crust_generate_accepts_canonical_tick_and_reports_it()
    {
        var provider = new WorldFunctionProvider();

        var summary = await provider.InvokeAsync("crust.generate", new JsonObject
        {
            ["canonicalTick"] = UnitConverter.MegaAnnumToTickDelta(1.25),
        });

        Assert.Equal(UnitConverter.MegaAnnumToTickDelta(1.25), (long)summary["canonicalTick"]!);
        Assert.Equal(UnitConverter.MegaAnnumToTickDelta(1.25), (long)summary["durationTicks"]!);

        var timeScale = summary["timeScale"]!.AsObject();
        Assert.Equal("ka", (string?)timeScale["rung"]);
        Assert.Equal(UnitConverter.TicksPerMegaAnnum, (long)timeScale["ticksPerRung"]!);
    }

    [Fact]
    public async Task CrustGenerationGraph_can_pin_an_explicit_canonical_tick()
    {
        var provider = new WorldFunctionProvider();
        var executor = new GraphExecutor(new[] { (INodeFunctionProvider)provider });
        var graph = CrustGenerationGraph.Build(canonicalTick: 12_345);

        var result = await executor.ExecuteAsync(graph);

        Assert.Equal(12_345L, (long)result["canonicalTick"]!);
        Assert.Equal(12_345L, (long)result["durationTicks"]!);
    }

    [Fact]
    public async Task InvokeAsync_crust_generate_explicit_frequency_and_plates_override_defaults()
    {
        var provider = new WorldFunctionProvider();

        var summary = await provider.InvokeAsync("crust.generate", new JsonObject
        {
            ["frequency"] = 2,
            ["plates"] = new JsonArray(
                new JsonObject { ["id"] = 0, ["lat"] = 10.0, ["lon"] = 20.0, ["ratePerTick"] = 1.0e-7 },
                new JsonObject { ["id"] = 1, ["lat"] = -10.0, ["lon"] = -20.0, ["ratePerTick"] = 0.0 }),
        });

        Assert.Equal(2, (int)summary["frequency"]!);
        Assert.Equal(2, (int)summary["plateCount"]!);
    }

    // ---------------------------------------------------------------------
    // Guard (D4.1): crust.generate result JSON uses canonical vocabulary only.
    // No Ma/MegaAnnum/annum leak anywhere in keys or values; the canonical
    // duration/time-scale fields are present. Mirrors the CanonicalTimeLabelTests
    // guard pattern but applied to the whole function-result JSON.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Crust_generate_result_emits_no_MegaAnnum_and_uses_canonical_fields()
    {
        var provider = new WorldFunctionProvider();

        var result = await provider.InvokeAsync("crust.generate", new JsonObject());
        var json = result.ToJsonString();

        // Case-insensitive "annum" catches durationMegaAnnum/ticksPerMegaAnnum/Megaannum; the quoted
        // "Ma" catches any "unit":"Ma" value. A bare "Ma" would false-positive on "main"/"Mountain".
        Assert.DoesNotContain("annum", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Ma\"", json, StringComparison.Ordinal);

        Assert.True(result.ContainsKey("durationTicks"));
        Assert.NotEmpty((string?)result["durationLabel"]);
        Assert.DoesNotContain("Ma", (string?)result["durationLabel"], StringComparison.Ordinal);

        var timeScale = result["timeScale"]!.AsObject();
        Assert.Equal("ka", (string?)timeScale["rung"]);
        Assert.Equal(UnitConverter.TicksPerMegaAnnum, (long)timeScale["ticksPerRung"]!);
    }

    private static int DefaultPresentationPlateCount()
        => WorldCrustRunSpec.ForPresentation(
            WorldGenerationRenderOptions.Default,
            SphereRegimeScheduleDefaults.PlateOnsetTick,
            UnitConverter.MegaAnnumToTickDelta(8.0)).Plates.Count;
}
