using FantaSim.App.Timeline.Seam;
using Xunit;

namespace FantaSim.App.Timeline.Tests;

/// <summary>
/// TDD regression guard for the filmstrip cache-key completion
/// (vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md §1.3): GraphRevision must be
/// part of the key so a generation-graph edit (mid-session or across a restart) invalidates cached
/// filmstrip textures instead of aliasing onto a stale one. Mirrors TimelineFilmstripTests' direct
/// key-equality style for the T1 TimelineFilmstripCacheKey -- FilmstripTextureCacheKey is the
/// plugin-local superset used by FilmstripPreviewController's actual texture dictionary.
/// </summary>
public sealed class FilmstripTextureCacheKeyTests
{
    private static FilmstripTextureCacheKey Key(
        string sphereId = "geosphere",
        string layerId = "geosphere.crust",
        long requestedTick = 107_500_000L,
        long snapshotTick = 105_000_000L,
        string viewRung = "kb",
        int width = 96,
        int height = 48,
        int graphRevision = 1)
        => new(sphereId, layerId, requestedTick, snapshotTick, viewRung, width, height, graphRevision);

    [Fact]
    public void Keys_WithIdenticalFields_AreEqual()
    {
        Assert.Equal(Key(), Key());
    }

    [Fact]
    public void Keys_WithDifferentGraphRevision_AreNotEqual()
    {
        Assert.NotEqual(Key(graphRevision: 1), Key(graphRevision: 2));
    }

    [Fact]
    public void Keys_WithDifferentSnapshotTick_AreNotEqual()
    {
        Assert.NotEqual(Key(snapshotTick: 105_000_000L), Key(snapshotTick: 110_000_000L));
    }

    [Fact]
    public void Keys_WithDifferentRequestedTick_AreNotEqual()
    {
        Assert.NotEqual(Key(requestedTick: 107_500_000L), Key(requestedTick: 107_500_001L));
    }

    [Fact]
    public void Keys_WithDifferentViewRung_AreNotEqual()
    {
        Assert.NotEqual(Key(viewRung: "kb"), Key(viewRung: "kc"));
    }

    [Fact]
    public void Keys_WithDifferentSphereId_AreNotEqual()
    {
        Assert.NotEqual(Key(sphereId: "geosphere"), Key(sphereId: "atmosphere"));
    }


    [Fact]
    public void Keys_WithDifferentLayerId_AreNotEqual()
    {
        Assert.NotEqual(Key(layerId: "geosphere.crust"), Key(layerId: "geosphere.plate"));
    }

    [Fact]
    public void Keys_WithDifferentWidth_AreNotEqual()
    {
        Assert.NotEqual(Key(width: 96), Key(width: 97));
    }

    [Fact]
    public void Keys_WithDifferentHeight_AreNotEqual()
    {
        Assert.NotEqual(Key(height: 48), Key(height: 49));
    }
}
