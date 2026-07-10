using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.World.Composition.Tests;

/// <summary>
/// I/O-backed coverage for <see cref="LayerTrackRegistryService"/> (Task 2): pipeline/declared-
/// layers assets and the archive-overlay sidecar all live under a per-test temp directory so
/// nothing touches the real app's config folder.
/// </summary>
public sealed class LayerTrackRegistryServiceTests : IDisposable
{
    private readonly string _root;

    public LayerTrackRegistryServiceTests()
    {
        _root = Directory.CreateTempSubdirectory("layer-track-registry-tests-").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private string PipelinePath => Path.Combine(_root, "track-pipeline.json");
    private string DeclaredLayersPath => Path.Combine(_root, "declared-layers.json");
    private string ArchiveOverlayPath => Path.Combine(_root, "layer-track-archive.json");

    private const string PipelineJson = """
    {
      "documentId": "track-pipeline",
      "schemaVersion": 1,
      "revision": 1,
      "nodes": [
        { "nodeId": "family", "kind": "family-layers", "params": {} },
        { "nodeId": "declared", "kind": "declared-layers", "params": {} },
        { "nodeId": "discovery", "kind": "stream-discovery", "params": {} },
        { "nodeId": "trackSet", "kind": "track-set", "params": {} }
      ],
      "wires": [
        { "fromNodeId": "family", "toNodeId": "trackSet" },
        { "fromNodeId": "declared", "toNodeId": "trackSet" },
        { "fromNodeId": "discovery", "toNodeId": "trackSet" }
      ]
    }
    """;

    private const string DeclaredLayersJson = """
    {
      "schemaVersion": 1,
      "layers": [
        { "sphereId": "atmosphere", "layerId": "atmosphere.bulk", "displayName": "Bulk Atmosphere", "capabilities": ["scrub", "toggle"] }
      ]
    }
    """;

    private LayerTrackRegistryService CreateService(
        WorldGenerationGraphFamilyDocument? family = null,
        Func<IReadOnlyList<DiscoveredTrackRecord>>? discoveredTracksProvider = null)
    {
        File.WriteAllText(PipelinePath, PipelineJson);
        File.WriteAllText(DeclaredLayersPath, DeclaredLayersJson);
        return new LayerTrackRegistryService(
            () => family,
            PipelinePath,
            DeclaredLayersPath,
            ArchiveOverlayPath,
            loggerFactory: null,
            discoveredTracksProvider: discoveredTracksProvider);
    }

    [Fact]
    public void Current_OnConstruction_ReflectsShippedAssets()
    {
        using var service = CreateService();

        var track = Assert.Single(service.Current.Tracks);
        Assert.Equal("atmosphere.bulk", track.LayerId);
    }

    [Fact]
    public void SetArchived_FlipsState_AndFiresChangedExactlyOnce()
    {
        using var service = CreateService();
        int changedCount = 0;
        service.Changed += _ => changedCount++;

        service.SetArchived("atmosphere", "atmosphere.bulk", archived: true);

        Assert.Equal(1, changedCount);
        Assert.Equal(LayerTrackStates.Archived, service.Current.Tracks.Single().State);
    }

    [Fact]
    public void SetArchived_NoOpWhenAlreadyInRequestedState_DoesNotFireChanged()
    {
        using var service = CreateService();
        service.SetArchived("atmosphere", "atmosphere.bulk", archived: true);

        int changedCount = 0;
        service.Changed += _ => changedCount++;
        service.SetArchived("atmosphere", "atmosphere.bulk", archived: true);

        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void SetArchived_PersistsOverlay_SurvivingANewServiceInstance()
    {
        using (var service = CreateService())
        {
            service.SetArchived("atmosphere", "atmosphere.bulk", archived: true);
        }

        using var rebuilt = new LayerTrackRegistryService(
            () => null,
            PipelinePath,
            DeclaredLayersPath,
            ArchiveOverlayPath);

        Assert.Equal(LayerTrackStates.Archived, rebuilt.Current.Tracks.Single().State);
    }

    [Fact]
    public void Reload_ReReadsDeclaredLayersFromDisk_AndFiresChanged()
    {
        using var service = CreateService();
        Assert.Single(service.Current.Tracks);

        File.WriteAllText(DeclaredLayersPath, """
        {
          "schemaVersion": 1,
          "layers": [
            { "sphereId": "atmosphere", "layerId": "atmosphere.bulk", "displayName": "Bulk Atmosphere" },
            { "sphereId": "hydrosphere", "layerId": "hydrosphere.ocean", "displayName": "Ocean" }
          ]
        }
        """);

        int changedCount = 0;
        service.Changed += _ => changedCount++;
        service.Reload();

        Assert.Equal(1, changedCount);
        Assert.Equal(2, service.Current.Tracks.Count);
        Assert.Contains(service.Current.Tracks, t => t.LayerId == "hydrosphere.ocean");
    }

    [Fact]
    public void Revision_IncrementsOnEveryMutation()
    {
        using var service = CreateService();
        var initialRevision = service.Current.Revision;

        service.SetArchived("atmosphere", "atmosphere.bulk", archived: true);
        Assert.True(service.Current.Revision > initialRevision);

        var afterArchive = service.Current.Revision;
        service.Reload();
        Assert.True(service.Current.Revision > afterArchive);
    }

    [Fact]
    public void Current_NoDiscoveredTracksProvider_YieldsNoDiscoveredTracks()
    {
        using var service = CreateService();

        Assert.DoesNotContain(service.Current.Tracks, t => t.State == LayerTrackStates.Discovered);
    }

    [Fact]
    public void Current_DiscoveredTracksProvider_ContributesDiscoveredTracks()
    {
        var record = new DiscoveredTrackRecord(
            SphereId: "world",
            LayerId: "world.truth-events",
            StreamId: new LayerTrackStreamId("app", "main", "L0", "world", "default"),
            DisplayName: "Truth Events",
            ContentType: LayerTrackContentTypes.Events,
            ContentSource: "app:main:0:world:default");
        using var service = CreateService(discoveredTracksProvider: () => new[] { record });

        var track = Assert.Single(service.Current.Tracks, t => t.SphereId == "world");
        Assert.Equal(LayerTrackStates.Discovered, track.State);
        Assert.Equal("world.truth-events", track.LayerId);
    }

    [Fact]
    public void Current_DiscoveredTracksProviderThrows_LogsWarningAndBuildsWithoutDiscoveredTracks()
    {
        using var service = CreateService(discoveredTracksProvider: () => throw new InvalidOperationException("boom"));

        Assert.DoesNotContain(service.Current.Tracks, t => t.State == LayerTrackStates.Discovered);
        // The rest of the pipeline (family + declared) still builds successfully despite the
        // throwing provider -- matches the family-document provider's try/log-warn/empty-fallback
        // discipline (LayerTrackRegistryService.BuildSnapshotLocked).
        Assert.Single(service.Current.Tracks);
    }
}
