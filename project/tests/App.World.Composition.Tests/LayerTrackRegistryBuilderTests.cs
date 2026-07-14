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

    private static TrackPipelineDocument PipelineWithDiscovery() => new(
        DocumentId: "track-pipeline.discovery-test",
        SchemaVersion: 1,
        Revision: 1,
        Nodes: new[]
        {
            new TrackPipelineNode("family", TrackPipelineNodeKinds.FamilyLayers, new JsonObject()),
            new TrackPipelineNode("declared", TrackPipelineNodeKinds.DeclaredLayers, new JsonObject()),
            new TrackPipelineNode("discovery", TrackPipelineNodeKinds.StreamDiscovery, new JsonObject()),
            new TrackPipelineNode("trackSet", TrackPipelineNodeKinds.TrackSet, new JsonObject()),
        },
        Wires: new[]
        {
            new TrackPipelineWire("family", "trackSet"),
            new TrackPipelineWire("declared", "trackSet"),
            new TrackPipelineWire("discovery", "trackSet"),
        });

    private static TrackPipelineDocument PipelineWithLaneOrder(JsonObject trackSetParams) => new(
        DocumentId: "track-pipeline.lane-order-test",
        SchemaVersion: 1,
        Revision: 1,
        Nodes: new[]
        {
            new TrackPipelineNode("family", TrackPipelineNodeKinds.FamilyLayers, new JsonObject()),
            new TrackPipelineNode("declared", TrackPipelineNodeKinds.DeclaredLayers, new JsonObject()),
            new TrackPipelineNode("discovery", TrackPipelineNodeKinds.StreamDiscovery, new JsonObject()),
            new TrackPipelineNode("trackSet", TrackPipelineNodeKinds.TrackSet, trackSetParams),
        },
        Wires: new[]
        {
            new TrackPipelineWire("family", "trackSet"),
            new TrackPipelineWire("declared", "trackSet"),
            new TrackPipelineWire("discovery", "trackSet"),
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

    [Fact]
    public void Build_StreamDiscovery_MapsRecordFieldByField()
    {
        var streamId = new LayerTrackStreamId("app", "main", "L2", "world", "default");
        var record = new DiscoveredTrackRecord(
            SphereId: "world",
            LayerId: "world.truth-events",
            StreamId: streamId,
            DisplayName: "Truth Events",
            ContentType: LayerTrackContentTypes.Events,
            ContentSource: "app:main:0:world:default",
            CadenceTicks: null,
            Capabilities: null,
            SourceRef: null);

        var snapshot = LayerTrackRegistryBuilder.Build(
            PipelineWithDiscovery(), familyDocument: null, declaredLayers: null,
            archivedKeys: EmptyKeys, revision: 1, discoveredTracks: new[] { record });

        var track = Assert.Single(snapshot.Tracks);
        Assert.Equal("world", track.SphereId);
        Assert.Equal("world.truth-events", track.LayerId);
        Assert.Equal(streamId, track.StreamId);
        Assert.Equal("Truth Events", track.DisplayName);
        Assert.Equal(LayerTrackStates.Discovered, track.State);
        Assert.Equal(0L, track.TimeDomain.StartTick);
        Assert.Null(track.TimeDomain.EndTick);
        Assert.Equal("ka", track.TimeDomain.Rung);
        Assert.Equal(LayerTrackContentTypes.Events, track.Content.Type);
        Assert.Equal("app:main:0:world:default", track.Content.Source);
        Assert.Null(track.Content.CadenceTicks);
        Assert.Equal(new[] { "scrub", "toggle" }, track.Capabilities);
        Assert.Equal("app:main:0:world:default", track.SourceRef);
    }

    [Fact]
    public void Build_StreamDiscovery_DefaultsSourceRefToStreamDiscoveryWhenNoContentSource()
    {
        var record = new DiscoveredTrackRecord(
            SphereId: "world",
            LayerId: "world.truth-events",
            StreamId: new LayerTrackStreamId("app", "main", "L2", "world", "default"),
            DisplayName: "Truth Events",
            ContentType: LayerTrackContentTypes.Events);

        var snapshot = LayerTrackRegistryBuilder.Build(
            PipelineWithDiscovery(), familyDocument: null, declaredLayers: null,
            archivedKeys: EmptyKeys, revision: 1, discoveredTracks: new[] { record });

        Assert.Equal("stream-discovery", Assert.Single(snapshot.Tracks).SourceRef);
    }

    [Fact]
    public void Build_StreamDiscovery_AbsentRecords_YieldsNoDiscoveredTracks()
    {
        var snapshot = LayerTrackRegistryBuilder.Build(
            PipelineWithDiscovery(), familyDocument: null, declaredLayers: null,
            archivedKeys: EmptyKeys, revision: 1);

        Assert.Empty(snapshot.Tracks);
    }

    [Fact]
    public void Build_MergesDiscoveredAlongsideDeclaredAndFamily_ArchiveOverlayAppliesToBoth()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));
        var record = new DiscoveredTrackRecord(
            SphereId: "world",
            LayerId: "world.truth-events",
            StreamId: new LayerTrackStreamId("app", "main", "L2", "world", "default"),
            DisplayName: "Truth Events",
            ContentType: LayerTrackContentTypes.Events);
        var archived = new HashSet<string>
        {
            LayerTrackRegistryBuilder.ArchiveKey("geosphere", "geosphere.crust"),
            LayerTrackRegistryBuilder.ArchiveKey("world", "world.truth-events"),
        };

        var snapshot = LayerTrackRegistryBuilder.Build(
            PipelineWithDiscovery(), family, declaredLayers: null,
            archivedKeys: archived, revision: 1, discoveredTracks: new[] { record });

        Assert.Equal(2, snapshot.Tracks.Count);
        Assert.All(snapshot.Tracks, t => Assert.Equal(LayerTrackStates.Archived, t.State));
    }

    [Fact]
    public void ArchiveThenRestore_DiscoveredTrack_ReturnsToDiscovered_NotDeclared()
    {
        // Task 1 (vault/plans/2026-07-10-layer-track-registry-slice2-plan.md): restoring from
        // archive must return a track to the state ITS SOURCE produced, not a hardcoded
        // "declared" -- meaningful only once a source other than family/declared-layers exists,
        // i.e. stream-discovery (Task 2). Two separate Build() calls mirror the real service:
        // archivedKeys mutates between LayerTrackRegistryService.SetArchived calls, and every
        // call rebuilds the source fresh (LayerTrackRegistryService.BuildSnapshotLocked).
        var record = new DiscoveredTrackRecord(
            SphereId: "world",
            LayerId: "world.truth-events",
            StreamId: new LayerTrackStreamId("app", "main", "L2", "world", "default"),
            DisplayName: "Truth Events",
            ContentType: LayerTrackContentTypes.Events);
        var pipeline = PipelineWithDiscovery();
        var archived = new HashSet<string> { LayerTrackRegistryBuilder.ArchiveKey("world", "world.truth-events") };

        var archivedSnapshot = LayerTrackRegistryBuilder.Build(
            pipeline, familyDocument: null, declaredLayers: null,
            archivedKeys: archived, revision: 1, discoveredTracks: new[] { record });
        Assert.Equal(LayerTrackStates.Archived, Assert.Single(archivedSnapshot.Tracks).State);

        var restoredSnapshot = LayerTrackRegistryBuilder.Build(
            pipeline, familyDocument: null, declaredLayers: null,
            archivedKeys: EmptyKeys, revision: 2, discoveredTracks: new[] { record });

        Assert.Equal(LayerTrackStates.Discovered, Assert.Single(restoredSnapshot.Tracks).State);
    }

    [Fact]
    public void ArchiveThenRestore_DeclaredTrack_ReturnsToDeclared()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));
        var pipeline = DefaultPipeline();
        var archived = new HashSet<string> { LayerTrackRegistryBuilder.ArchiveKey("geosphere", "geosphere.crust") };

        var archivedSnapshot = LayerTrackRegistryBuilder.Build(
            pipeline, family, declaredLayers: null, archivedKeys: archived, revision: 1);
        Assert.Equal(LayerTrackStates.Archived, Assert.Single(archivedSnapshot.Tracks).State);

        var restoredSnapshot = LayerTrackRegistryBuilder.Build(
            pipeline, family, declaredLayers: null, archivedKeys: EmptyKeys, revision: 2);

        Assert.Equal(LayerTrackStates.Declared, Assert.Single(restoredSnapshot.Tracks).State);
    }

    [Fact]
    public void LaneOrder_ReordersSpheres_GeosphereBeforeAtmosphere()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));
        var declared = new DeclaredLayersDocument(1, new[]
        {
            new DeclaredLayerEntry("atmosphere", "atmosphere.bulk", "Bulk Atmosphere"),
        });
        var trackSetParams = new JsonObject { ["laneOrder"] = new JsonArray("geosphere", "atmosphere") };

        var snapshot = LayerTrackRegistryBuilder.Build(
            PipelineWithLaneOrder(trackSetParams), family, declared, EmptyKeys, revision: 1);

        Assert.Equal(new[] { "geosphere", "atmosphere" }, snapshot.Tracks.Select(t => t.SphereId));
    }

    [Fact]
    public void LaneOrder_UnlistedSpheres_FollowListedOnes_OrdinalAmongThemselves()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));
        var declared = new DeclaredLayersDocument(1, new[]
        {
            new DeclaredLayerEntry("atmosphere", "atmosphere.bulk", "Bulk Atmosphere"),
        });
        var record = new DiscoveredTrackRecord(
            SphereId: "world",
            LayerId: "world.truth-events",
            StreamId: new LayerTrackStreamId("app", "main", "L2", "world", "default"),
            DisplayName: "Truth Events",
            ContentType: LayerTrackContentTypes.Events);
        var trackSetParams = new JsonObject { ["laneOrder"] = new JsonArray("geosphere") };

        var snapshot = LayerTrackRegistryBuilder.Build(
            PipelineWithLaneOrder(trackSetParams), family, declared, EmptyKeys, revision: 1,
            discoveredTracks: new[] { record });

        // geosphere is listed (rank 0); atmosphere and world are both unlisted, so they fall back
        // to ordinal sphereId order among themselves -- "atmosphere" < "world".
        Assert.Equal(new[] { "geosphere", "atmosphere", "world" }, snapshot.Tracks.Select(t => t.SphereId));
    }

    [Fact]
    public void LaneOrder_MissingParam_PreservesAlphabeticalOrder()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));
        var declared = new DeclaredLayersDocument(1, new[]
        {
            new DeclaredLayerEntry("atmosphere", "atmosphere.bulk", "Bulk Atmosphere"),
        });

        var snapshot = LayerTrackRegistryBuilder.Build(
            PipelineWithLaneOrder(new JsonObject()), family, declared, EmptyKeys, revision: 1);

        Assert.Equal(new[] { "atmosphere", "geosphere" }, snapshot.Tracks.Select(t => t.SphereId));
    }

    [Fact]
    public void LaneOrder_EmptyArray_PreservesAlphabeticalOrder()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));
        var declared = new DeclaredLayersDocument(1, new[]
        {
            new DeclaredLayerEntry("atmosphere", "atmosphere.bulk", "Bulk Atmosphere"),
        });
        var trackSetParams = new JsonObject { ["laneOrder"] = new JsonArray() };

        var snapshot = LayerTrackRegistryBuilder.Build(
            PipelineWithLaneOrder(trackSetParams), family, declared, EmptyKeys, revision: 1);

        Assert.Equal(new[] { "atmosphere", "geosphere" }, snapshot.Tracks.Select(t => t.SphereId));
    }

    [Fact]
    public void LaneOrder_MalformedEntries_AreIgnored()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));
        var declared = new DeclaredLayersDocument(1, new[]
        {
            new DeclaredLayerEntry("atmosphere", "atmosphere.bulk", "Bulk Atmosphere"),
        });
        var trackSetParams = new JsonObject
        {
            ["laneOrder"] = new JsonArray(JsonValue.Create(1), null, JsonValue.Create("geosphere")),
        };

        var snapshot = LayerTrackRegistryBuilder.Build(
            PipelineWithLaneOrder(trackSetParams), family, declared, EmptyKeys, revision: 1);

        Assert.Equal(new[] { "geosphere", "atmosphere" }, snapshot.Tracks.Select(t => t.SphereId));
    }

    [Fact]
    public void LaneOrder_LayerOrderWithinSphere_UnaffectedByLaneOrder()
    {
        var family = FakeFamilyDocument(
            new WorldLayerGraphBinding("geosphere", "geosphere.plate", "geosphere.plate.layer"),
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer"));
        var trackSetParams = new JsonObject { ["laneOrder"] = new JsonArray("geosphere") };

        var snapshot = LayerTrackRegistryBuilder.Build(
            PipelineWithLaneOrder(trackSetParams), family, declaredLayers: null, EmptyKeys, revision: 1);

        Assert.Equal(new[] { "geosphere.crust", "geosphere.plate" }, snapshot.Tracks.Select(t => t.LayerId));
    }

    private static readonly IReadOnlySet<string> EmptyKeys = new HashSet<string>();
}
