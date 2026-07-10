using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.World.Composition.Tests;

/// <summary>
/// Pure-function coverage for <see cref="LayerTrackRegistryBuilder"/> (Task 2): no file I/O --
/// every input is a constructed fixture. Mirrors
/// vault/plans/2026-07-10-layer-track-registry-slice1-plan.md Task 2's test list.
/// </summary>
public sealed class LayerTrackRegistryBuilderTests
{
    private static TrackPipelineDocument DefaultPipeline() => new(
        DocumentId: "track-pipeline.test",
        SchemaVersion: 1,
        Revision: 1,
        Nodes: new[]
        {
            new TrackPipelineNode("family", TrackPipelineNodeKinds.FamilyLayers, new JsonObject()),
            new TrackPipelineNode("declared", TrackPipelineNodeKinds.DeclaredLayers, new JsonObject()),
            new TrackPipelineNode("trackSet", TrackPipelineNodeKinds.TrackSet, new JsonObject()),
        },
        Wires: new[]
        {
            new TrackPipelineWire("family", "trackSet"),
            new TrackPipelineWire("declared", "trackSet"),
        });

    private static WorldGenerationGraphFamilyDocument FakeFamilyDocument(
        params WorldLayerGraphBinding[] bindings)
        => new(
            DocumentId: "fake-family",
            SchemaVersion: 1,
            Revision: 1,
            BaseGraph: new WorldGenerationGraphView(
                "base", "Base", "base graph", Array.Empty<WorldGenerationGraphNode>(), Array.Empty<WorldGenerationGraphWire>()),
            Graphs: Array.Empty<WorldGenerationGraphView>(),
            RegimeGraphBindings: Array.Empty<WorldRegimeGraphBinding>(),
            GraphOverrides: Array.Empty<WorldGenerationGraphScopedOverride>(),
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: DateTimeOffset.UnixEpoch,
            LayerGraphBindings: bindings);

    [Fact]
    public void Build_YieldsOneDeclaredDescriptorPerLayerScopeGraph()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer", RegimeId: "mobile-plate"),
            new WorldLayerGraphBinding("geosphere", "geosphere.plate", "geosphere.plate.layer", RegimeId: "mobile-plate"));

        var snapshot = LayerTrackRegistryBuilder.Build(
            DefaultPipeline(), family, declaredLayers: null, archivedKeys: EmptyKeys, revision: 1);

        Assert.Equal(2, snapshot.Tracks.Count);
        Assert.All(snapshot.Tracks, track => Assert.Equal(LayerTrackStates.Declared, track.State));
        Assert.Contains(snapshot.Tracks, t => t.LayerId == "geosphere.crust" && t.SphereId == "geosphere");
        Assert.Contains(snapshot.Tracks, t => t.LayerId == "geosphere.plate" && t.SphereId == "geosphere");
    }

    [Fact]
    public void Build_DerivesSphereIdFromLayerIdPrefix()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("ignored-sphere-field", "hydrosphere.ocean", "hydrosphere.ocean.layer"));

        var snapshot = LayerTrackRegistryBuilder.Build(
            DefaultPipeline(), family, declaredLayers: null, archivedKeys: EmptyKeys, revision: 1);

        var track = Assert.Single(snapshot.Tracks);
        Assert.Equal("hydrosphere", track.SphereId);
    }

    [Fact]
    public void Build_MergesDeclaredLayersAlongsideFamilyLayers()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));
        var declared = new DeclaredLayersDocument(
            SchemaVersion: 1,
            Layers: new[]
            {
                new DeclaredLayerEntry(
                    SphereId: "atmosphere",
                    LayerId: "atmosphere.bulk",
                    DisplayName: "Bulk Atmosphere",
                    ContentType: LayerTrackContentTypes.Filmstrip,
                    ContentSource: null,
                    CadenceTicks: null,
                    Capabilities: new[] { "scrub", "toggle" },
                    SourceRef: null),
            });

        var snapshot = LayerTrackRegistryBuilder.Build(
            DefaultPipeline(), family, declared, archivedKeys: EmptyKeys, revision: 1);

        Assert.Equal(2, snapshot.Tracks.Count);
        Assert.Contains(snapshot.Tracks, t => t.SphereId == "geosphere" && t.LayerId == "geosphere.crust");
        Assert.Contains(snapshot.Tracks, t => t.SphereId == "atmosphere" && t.LayerId == "atmosphere.bulk" && t.DisplayName == "Bulk Atmosphere");
    }

    [Fact]
    public void Build_AppliesArchiveOverlay_FlippingState()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));
        var archived = new HashSet<string> { LayerTrackRegistryBuilder.ArchiveKey("geosphere", "geosphere.crust") };

        var snapshot = LayerTrackRegistryBuilder.Build(
            DefaultPipeline(), family, declaredLayers: null, archivedKeys: archived, revision: 1);

        var track = Assert.Single(snapshot.Tracks);
        Assert.Equal(LayerTrackStates.Archived, track.State);
    }

    [Fact]
    public void Build_SortsTracksStableBySphereIdThenLayerId()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.plate", "geosphere.plate.layer"),
            new WorldLayerGraphBinding("atmosphere", "atmosphere.coupled", "atmosphere.coupled.layer"),
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));

        var snapshot = LayerTrackRegistryBuilder.Build(
            DefaultPipeline(), family, declaredLayers: null, archivedKeys: EmptyKeys, revision: 1);

        Assert.Equal(
            new[] { "atmosphere.coupled", "geosphere.crust", "geosphere.plate" },
            snapshot.Tracks.Select(t => t.LayerId));
    }

    [Fact]
    public void Build_SetsRequestedRevision()
    {
        var snapshot = LayerTrackRegistryBuilder.Build(
            DefaultPipeline(), familyDocument: null, declaredLayers: null, archivedKeys: EmptyKeys, revision: 42);

        Assert.Equal(42, snapshot.Revision);
    }

    [Fact]
    public void Build_UnknownNodeKind_ThrowsNamingTheKind()
    {
        var pipeline = DefaultPipeline() with
        {
            Nodes = new[]
            {
                new TrackPipelineNode("mystery", "not-a-real-kind", new JsonObject()),
            },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            LayerTrackRegistryBuilder.Build(pipeline, familyDocument: null, declaredLayers: null, archivedKeys: EmptyKeys, revision: 1));

        Assert.Contains("not-a-real-kind", ex.Message, StringComparison.Ordinal);
    }

    private static readonly IReadOnlySet<string> EmptyKeys = new HashSet<string>();
}
