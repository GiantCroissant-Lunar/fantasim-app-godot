using System;
using System.IO;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.World.Composition.Tests;

/// <summary>
/// Guards the SHIPPED config assets (project/hosts/complete-app/config/track-pipeline.json +
/// declared-layers.json) against a typo silently breaking the default pipeline. The family-graph
/// bindings mirrored here are the same four <c>WorldLayerGraphBinding</c> entries
/// <c>WorldGenerationGraphDefaults.BuildFamily()</c> produces today (App.World plugin -- not
/// referenced from this test project, so the bindings are reproduced by hand rather than by a
/// live call) per vault/plans/2026-07-10-layer-track-registry-slice1-plan.md Task 2.
/// </summary>
public sealed class LayerTrackRegistryDefaultAssetsTests
{
    private static string RepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "project", "FantaSim.sln");
            if (File.Exists(candidate))
                return current;

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate project/FantaSim.sln by walking up from " + AppContext.BaseDirectory);
    }

    private static WorldGenerationGraphFamilyDocument FakeFamilyDocumentMirroringDefaults() => new(
        DocumentId: "world-generation.family",
        SchemaVersion: 1,
        Revision: 1,
        BaseGraph: new WorldGenerationGraphView(
            "world.base", "World Creation", "test stand-in", Array.Empty<WorldGenerationGraphNode>(), Array.Empty<WorldGenerationGraphWire>()),
        Graphs: Array.Empty<WorldGenerationGraphView>(),
        RegimeGraphBindings: Array.Empty<WorldRegimeGraphBinding>(),
        GraphOverrides: Array.Empty<WorldGenerationGraphScopedOverride>(),
        LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
        RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
        UpdatedUtc: DateTimeOffset.UnixEpoch,
        LayerGraphBindings: new[]
        {
            new WorldLayerGraphBinding("geosphere", "geosphere.magma-ocean", "geosphere.magma-ocean", RegimeId: "magma-ocean"),
            new WorldLayerGraphBinding("geosphere", "geosphere.stagnant-lid", "geosphere.stagnant-lid", RegimeId: "stagnant-lid"),
            new WorldLayerGraphBinding("geosphere", "geosphere.plate", "geosphere.plate.layer", RegimeId: "mobile-plate"),
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer", RegimeId: "mobile-plate"),
        });

    [Fact]
    public void ShippedAssets_ReproduceTodaysSevenTracks_AcrossTwoSpheres_GeosphereFirst()
    {
        var configDir = Path.Combine(RepoRoot(), "project", "hosts", "complete-app", "config");
        var pipelineJson = File.ReadAllText(Path.Combine(configDir, "track-pipeline.json"));
        var declaredJson = File.ReadAllText(Path.Combine(configDir, "declared-layers.json"));

        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        var pipeline = System.Text.Json.JsonSerializer.Deserialize<TrackPipelineDocument>(pipelineJson, options)!;
        var declared = System.Text.Json.JsonSerializer.Deserialize<DeclaredLayersDocument>(declaredJson, options)!;

        var snapshot = LayerTrackRegistryBuilder.Build(
            pipeline,
            FakeFamilyDocumentMirroringDefaults(),
            declared,
            archivedKeys: new System.Collections.Generic.HashSet<string>(),
            revision: 1);

        Assert.Equal(7, snapshot.Tracks.Count);
        Assert.Equal(2, snapshot.Tracks.Select(t => t.SphereId).Distinct().Count());
        Assert.DoesNotContain(snapshot.Tracks, t => t.SphereId == "hydrosphere" || t.LayerId == "hydrosphere.ocean");

        // Revision 2 (vault/plans/2026-07-10-layer-track-registry-slice2-plan.md Task 3) ships a
        // laneOrder param restoring the pre-slice-1 geosphere-first lane order -- this was
        // alphabetical (atmosphere before geosphere) before Task 3.
        var expectedLayerIds = new[]
        {
            "geosphere.crust",
            "geosphere.magma-ocean",
            "geosphere.mantle",
            "geosphere.plate",
            "geosphere.stagnant-lid",
            "atmosphere.bulk",
            "atmosphere.coupled",
        };
        Assert.Equal(expectedLayerIds, snapshot.Tracks.Select(t => t.LayerId));
    }

    [Fact]
    public void ShippedAssets_StreamDiscoverySource_ContributesDiscoveredTrack_WhenProvided()
    {
        var configDir = Path.Combine(RepoRoot(), "project", "hosts", "complete-app", "config");
        var pipelineJson = File.ReadAllText(Path.Combine(configDir, "track-pipeline.json"));
        var declaredJson = File.ReadAllText(Path.Combine(configDir, "declared-layers.json"));

        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        var pipeline = System.Text.Json.JsonSerializer.Deserialize<TrackPipelineDocument>(pipelineJson, options)!;
        var declared = System.Text.Json.JsonSerializer.Deserialize<DeclaredLayersDocument>(declaredJson, options)!;
        var record = new DiscoveredTrackRecord(
            SphereId: "world",
            LayerId: "world.truth-events",
            StreamId: new LayerTrackStreamId("app", "main", "L2", "world", "default"),
            DisplayName: "Truth Events",
            ContentType: LayerTrackContentTypes.Events);

        var snapshot = LayerTrackRegistryBuilder.Build(
            pipeline,
            FakeFamilyDocumentMirroringDefaults(),
            declared,
            archivedKeys: new System.Collections.Generic.HashSet<string>(),
            revision: 1,
            discoveredTracks: new[] { record });

        Assert.Equal(8, snapshot.Tracks.Count);
        // "world" is unlisted in the shipped laneOrder, so it lands last.
        Assert.Equal("world.truth-events", snapshot.Tracks[^1].LayerId);
        Assert.Equal(LayerTrackStates.Discovered, snapshot.Tracks[^1].State);
    }
}
