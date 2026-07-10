using System;
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
        { "nodeId": "trackSet", "kind": "track-set", "params": {} }
      ],
      "wires": [
        { "fromNodeId": "family", "toNodeId": "trackSet" },
        { "fromNodeId": "declared", "toNodeId": "trackSet" }
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

    private LayerTrackRegistryService CreateService(WorldGenerationGraphFamilyDocument? family = null)
    {
        File.WriteAllText(PipelinePath, PipelineJson);
        File.WriteAllText(DeclaredLayersPath, DeclaredLayersJson);
        return new LayerTrackRegistryService(
            () => family,
            PipelinePath,
            DeclaredLayersPath,
            ArchiveOverlayPath);
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
}
