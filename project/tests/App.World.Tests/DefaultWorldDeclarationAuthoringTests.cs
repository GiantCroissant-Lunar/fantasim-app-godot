using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FantaSim.App.World.Topography;
using FantaSim.Mythosphere.Cosmology.Contracts;
using FantaSim.Mythosphere.Cosmology.Registry;
using FantaSim.Mythosphere.Cosmology.Science;
using Xunit;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Tests for <see cref="DefaultWorldDeclarationAuthoring"/>: proves the app's default world
/// declaration matches the locked authoring table from the approved G-010 plan (exact seed,
/// 17 truth tuning keys, empty packs/domain deltas, graph-derived layer order, exact science
/// layer digests, presenter/style/lane mapping, and complete option classification).
///
/// Cross-repo anti-drift: the four science layer descriptors are compared against the real
/// production <see cref="WorldGenerationGraphDefaults.BuildFamily"/>.LayerGraphBindings.
/// </summary>
public sealed class DefaultWorldDeclarationAuthoringTests
{
    private const string DefaultWorldName = "default";
    private const string DefaultRegistrant = "app.world";
    private const string DefaultStyleId = "app.world/default-globe-style/v1";

    // ── Core claim + structure ───────────────────────────────────────────────

    [Fact]
    public void AppendTo_claims_default_for_app_world_and_returns_declaration()
    {
        var (registry, science) = NewRegistryWithScience();
        var world = DefaultWorldDeclarationAuthoring.AppendTo(registry, science);

        Assert.NotNull(world);
        Assert.Equal(DefaultWorldName, world.Definition.WorldName);
        Assert.Equal("1", world.Definition.Version);
        Assert.NotNull(world.TruthDigest);
        Assert.NotNull(world.PresentationDigest);
    }

    [Fact]
    public void Seed_matches_default_options()
    {
        var world = AuthorDefault();
        Assert.Equal(WorldGenerationRenderOptions.Default.Seed, world.Definition.Seed);
    }

    // ── Exact 17 truth tuning keys ───────────────────────────────────────────

    [Fact]
    public void Tuning_contains_exactly_17_expected_keys()
    {
        var world = AuthorDefault();
        var tuning = world.Definition.Tuning;

        Assert.Equal(17, tuning.Count);

        var expectedKeys = new[]
        {
            "tessellationFrequency",
            "spinRateRadiansPerMegaAnnum",
            "hydrosphereMode",
            "convergentTrenchDepth",
            "convergentTrenchHalfWidthRad",
            "convergentArcHeight",
            "convergentArcSetbackRad",
            "convergentArcHalfWidthRad",
            "convergentCollisionHeight",
            "convergentCollisionHalfWidthRad",
            "divergentSwellHeight",
            "divergentSwellHalfWidthRad",
            "divergentRiftNotchDepth",
            "divergentRiftHalfWidthRad",
            "transformScarpAmplitude",
            "transformHalfWidthRad",
            "transformScarpPeriodPoints",
        };

        foreach (var key in expectedKeys)
            Assert.True(tuning.ContainsKey(key), $"Missing tuning key: {key}");
    }

    [Fact]
    public void Tuning_values_match_current_default_options_invariant_format()
    {
        var opts = WorldGenerationRenderOptions.Default;
        var world = AuthorDefault();
        var tuning = world.Definition.Tuning;
        var bps = opts.BoundaryProfiles;

        Assert.Equal(opts.TessellationFrequency.ToString(CultureInfo.InvariantCulture), tuning["tessellationFrequency"]);
        Assert.Equal(opts.SpinRateRadiansPerMegaAnnum.ToString("R", CultureInfo.InvariantCulture), tuning["spinRateRadiansPerMegaAnnum"]);
        Assert.Equal(opts.HydrosphereMode.ToString(), tuning["hydrosphereMode"]);
        Assert.Equal(bps.ConvergentTrenchDepth.ToString("R", CultureInfo.InvariantCulture), tuning["convergentTrenchDepth"]);
        Assert.Equal(bps.ConvergentTrenchHalfWidthRad.ToString("R", CultureInfo.InvariantCulture), tuning["convergentTrenchHalfWidthRad"]);
        Assert.Equal(bps.ConvergentArcHeight.ToString("R", CultureInfo.InvariantCulture), tuning["convergentArcHeight"]);
        Assert.Equal(bps.ConvergentArcSetbackRad.ToString("R", CultureInfo.InvariantCulture), tuning["convergentArcSetbackRad"]);
        Assert.Equal(bps.ConvergentArcHalfWidthRad.ToString("R", CultureInfo.InvariantCulture), tuning["convergentArcHalfWidthRad"]);
        Assert.Equal(bps.ConvergentCollisionHeight.ToString("R", CultureInfo.InvariantCulture), tuning["convergentCollisionHeight"]);
        Assert.Equal(bps.ConvergentCollisionHalfWidthRad.ToString("R", CultureInfo.InvariantCulture), tuning["convergentCollisionHalfWidthRad"]);
        Assert.Equal(bps.DivergentSwellHeight.ToString("R", CultureInfo.InvariantCulture), tuning["divergentSwellHeight"]);
        Assert.Equal(bps.DivergentSwellHalfWidthRad.ToString("R", CultureInfo.InvariantCulture), tuning["divergentSwellHalfWidthRad"]);
        Assert.Equal(bps.DivergentRiftNotchDepth.ToString("R", CultureInfo.InvariantCulture), tuning["divergentRiftNotchDepth"]);
        Assert.Equal(bps.DivergentRiftHalfWidthRad.ToString("R", CultureInfo.InvariantCulture), tuning["divergentRiftHalfWidthRad"]);
        Assert.Equal(bps.TransformScarpAmplitude.ToString("R", CultureInfo.InvariantCulture), tuning["transformScarpAmplitude"]);
        Assert.Equal(bps.TransformHalfWidthRad.ToString("R", CultureInfo.InvariantCulture), tuning["transformHalfWidthRad"]);
        Assert.Equal(bps.TransformScarpPeriodPoints.ToString("R", CultureInfo.InvariantCulture), tuning["transformScarpPeriodPoints"]);
    }

    // ── Empty packs / domain deltas ──────────────────────────────────────────

    [Fact]
    public void EnabledPacks_is_empty()
    {
        var world = AuthorDefault();
        Assert.Empty(world.Definition.EnabledPacks);
    }

    [Fact]
    public void DomainAdditions_and_Exclusions_are_empty()
    {
        var world = AuthorDefault();
        Assert.Empty(world.Definition.DomainAdditions);
        Assert.Empty(world.Definition.DomainExclusions);
    }

    // ── Graph-derived layer order ────────────────────────────────────────────

    [Fact]
    public void EnabledLayers_match_production_graph_binding_order()
    {
        var world = AuthorDefault();
        var graphLayers = WorldGenerationGraphDefaults.BuildFamily().LayerGraphBindings!;

        Assert.Equal(graphLayers.Count, world.Definition.EnabledLayers.Count);

        for (var i = 0; i < graphLayers.Count; i++)
            Assert.Equal(graphLayers[i].LayerId, world.Definition.EnabledLayers[i].LayerId);
    }

    [Fact]
    public void EnabledLayers_are_exactly_magma_ocean_stagnant_lid_plate_crust()
    {
        var world = AuthorDefault();
        var ids = world.Definition.EnabledLayers.Select(l => l.LayerId).ToArray();

        Assert.Equal(
            new[] { "geosphere.magma-ocean", "geosphere.stagnant-lid", "geosphere.plate", "geosphere.crust" },
            ids);
    }

    // ── Exact science layer digests ──────────────────────────────────────────

    [Fact]
    public void EnabledLayer_content_digests_match_science_lawset_layers()
    {
        var (registry, science) = NewRegistryWithScience();
        var world = DefaultWorldDeclarationAuthoring.AppendTo(registry, science);

        foreach (var layer in world.Definition.EnabledLayers)
        {
            var scienceLayer = science.Definition.Layers[layer.LayerId];
            Assert.Equal(scienceLayer.ContentDigest, layer.ContentDigest);
        }
    }

    [Fact]
    public void EnabledLayer_content_digests_match_science_catalog()
    {
        var world = AuthorDefault();
        var catalog = FantaSimScienceLawSetAuthoring.LayerCatalog;

        foreach (var layer in world.Definition.EnabledLayers)
        {
            var descriptor = catalog.Single(d => d.LayerId == layer.LayerId);
            Assert.Equal(descriptor.ContentDigest, layer.ContentDigest);
        }
    }

    [Fact]
    public void EnabledLayer_produced_domains_match_science_catalog()
    {
        var world = AuthorDefault();
        var catalog = FantaSimScienceLawSetAuthoring.LayerCatalog;

        foreach (var layer in world.Definition.EnabledLayers)
        {
            var descriptor = catalog.Single(d => d.LayerId == layer.LayerId);
            Assert.Equal(descriptor.ProducedDomains, layer.ProducedDomains);
        }
    }

    // ── Presenter / style / lane mapping ─────────────────────────────────────

    [Fact]
    public void PresentationBindings_use_existing_renderer_contracts()
    {
        var world = AuthorDefault();

        var magma = AssertLayer(world, "geosphere.magma-ocean");
        Assert.Equal(WorldGenerationGraphDefaults.DefaultLayerRendererContract, magma.Presentation.PresenterId);

        var stagnant = AssertLayer(world, "geosphere.stagnant-lid");
        Assert.Equal(WorldGenerationGraphDefaults.DefaultLayerRendererContract, stagnant.Presentation.PresenterId);

        var plate = AssertLayer(world, "geosphere.plate");
        Assert.Equal(WorldGenerationGraphDefaults.PlateLayerRendererContract, plate.Presentation.PresenterId);

        var crust = AssertLayer(world, "geosphere.crust");
        Assert.Equal(WorldGenerationGraphDefaults.CrustLayerRendererContract, crust.Presentation.PresenterId);
    }

    [Fact]
    public void PresentationBindings_share_default_style_id()
    {
        var world = AuthorDefault();
        foreach (var layer in world.Definition.EnabledLayers)
            Assert.Equal(DefaultStyleId, layer.Presentation.StyleId);
    }

    [Fact]
    public void PresentationBindings_lane_order_is_layer_index()
    {
        var world = AuthorDefault();
        for (var i = 0; i < world.Definition.EnabledLayers.Count; i++)
            Assert.Equal(i, world.Definition.EnabledLayers[i].Presentation.LaneOrder);
    }

    [Fact]
    public void PresentationBindings_are_all_visible()
    {
        var world = AuthorDefault();
        foreach (var layer in world.Definition.EnabledLayers)
            Assert.True(layer.Presentation.Visible);
    }

    // ── Complete option classification ───────────────────────────────────────

    [Fact]
    public void Every_default_render_option_is_classified_exactly_once()
    {
        var world = AuthorDefault();
        var tuning = world.Definition.Tuning;

        // Seed is NOT in tuning (it's draft.Seed).
        Assert.False(tuning.ContainsKey("seed"));
        Assert.Equal(WorldGenerationRenderOptions.Default.Seed, world.Definition.Seed);

        // Presentation-only properties are NOT in tuning.
        Assert.False(tuning.ContainsKey("verticalExaggeration"));
        Assert.False(tuning.ContainsKey("surfaceSubdivision"));
        Assert.False(tuning.ContainsKey("adaptiveSubdivisionMaxDepth"));
        Assert.False(tuning.ContainsKey("adaptiveSubdivisionEdgeHeightDelta"));
        Assert.False(tuning.ContainsKey("adaptiveSubdivisionFeatureWeightDelta"));

        // Exactly 17 truth keys remain (no presentation-only leaked in, no truth missing).
        Assert.Equal(17, tuning.Count);
    }

    // ── Determinism ──────────────────────────────────────────────────────────

    [Fact]
    public void Two_fresh_registries_produce_equal_truth_and_presentation_digests()
    {
        var world1 = AuthorDefault();
        var world2 = AuthorDefault();

        Assert.Equal(world1.TruthDigest, world2.TruthDigest);
        Assert.Equal(world1.PresentationDigest, world2.PresentationDigest);
    }

    // ── Truth vs presentation perturbation ───────────────────────────────────

    [Fact]
    public void Changing_tessellation_frequency_changes_truth_not_presentation()
    {
        var world1 = AuthorDefault();

        var (registry2, science2) = NewRegistryWithScience();
        var opts2 = WorldGenerationRenderOptions.Default with { TessellationFrequency = 5 };
        var world2 = DefaultWorldDeclarationAuthoring.AppendTo(registry2, science2, opts2);

        Assert.NotEqual(world1.TruthDigest, world2.TruthDigest);
        Assert.Equal(world1.PresentationDigest, world2.PresentationDigest);
    }

    [Fact]
    public void Changing_vertical_exaggeration_changes_presentation_not_truth()
    {
        var world1 = AuthorDefault();

        var (registry2, science2) = NewRegistryWithScience();
        var opts2 = WorldGenerationRenderOptions.Default with { VerticalExaggeration = 0.0001 };
        var world2 = DefaultWorldDeclarationAuthoring.AppendTo(registry2, science2, opts2);

        Assert.Equal(world1.TruthDigest, world2.TruthDigest);
        Assert.NotEqual(world1.PresentationDigest, world2.PresentationDigest);
    }

    // ── Cross-repo anti-drift gate ───────────────────────────────────────────

    [Fact]
    public void Science_catalog_layer_ids_match_production_graph_bindings()
    {
        var catalog = FantaSimScienceLawSetAuthoring.LayerCatalog;
        var graphLayers = WorldGenerationGraphDefaults.BuildFamily().LayerGraphBindings!;

        var catalogIds = catalog.Select(d => d.LayerId).OrderBy(i => i, StringComparer.Ordinal).ToArray();
        var graphIds = graphLayers.Select(b => b.LayerId).OrderBy(i => i, StringComparer.Ordinal).ToArray();

        Assert.Equal(graphIds, catalogIds);
    }

    [Fact]
    public void Science_catalog_order_matches_production_graph_binding_order()
    {
        var catalog = FantaSimScienceLawSetAuthoring.LayerCatalog;
        var graphLayers = WorldGenerationGraphDefaults.BuildFamily().LayerGraphBindings!;

        Assert.Equal(graphLayers.Count, catalog.Count);
        for (var i = 0; i < graphLayers.Count; i++)
            Assert.Equal(graphLayers[i].LayerId, catalog[i].LayerId);
    }

    [Fact]
    public void LawSet_reference_is_pinned_to_science_digest()
    {
        var (registry, science) = NewRegistryWithScience();
        var world = DefaultWorldDeclarationAuthoring.AppendTo(registry, science);

        Assert.True(world.Definition.LawSetRef.IsPinned);
        Assert.Equal(science.Digest, world.Definition.LawSetRef.Digest);
        Assert.Equal(FantaSimScienceLawSetAuthoring.LawSetId, world.Definition.LawSetRef.LawSetId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static WorldDeclaration AuthorDefault()
    {
        var (registry, science) = NewRegistryWithScience();
        return DefaultWorldDeclarationAuthoring.AppendTo(registry, science);
    }

    private static (InMemoryWorldDeclarationRegistry registry, LawSet science) NewRegistryWithScience()
    {
        var registry = new InMemoryWorldDeclarationRegistry();
        // Science authoring claims fantasim/science for fantasim/platform with Platform authority
        // internally — the caller does not duplicate the reserved claim.
        var science = FantaSimScienceLawSetAuthoring.AppendTo(registry);
        return (registry, science);
    }

    private static LayerSelection AssertLayer(WorldDeclaration world, string layerId)
    {
        var layer = world.Definition.EnabledLayers.FirstOrDefault(l => l.LayerId == layerId);
        Assert.NotNull(layer);
        Assert.Equal(layerId, layer!.LayerId);
        return layer;
    }
}
