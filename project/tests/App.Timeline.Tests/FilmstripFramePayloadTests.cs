using FantaSim.App.Timeline.Seam;
using FantaSim.App.World;
using Xunit;

namespace FantaSim.App.Timeline.Tests;

public sealed class FilmstripFramePayloadTests
{
    [Fact]
    public void Payload_preserves_source_kind_and_both_ticks()
    {
        var map = Map();

        var payload = FilmstripFramePayloadPolicy.Build(null!, map);

        Assert.Equal("crust-low-res", payload.Metadata.SourceKind);
        Assert.Equal(107_500_000L, payload.Metadata.RequestedTick);
        Assert.Equal(105_000_000L, payload.Metadata.SnapshotTick);
        Assert.Equal(12, payload.Metadata.GraphRevision);
    }

    [Fact]
    public void Controller_cache_hit_returns_complete_metadata_and_invalidates_revision()
    {
        var map = Map(
            requestedTick: 22L,
            snapshotTick: 20L,
            graphRevision: 7,
            sourceKind: "plate-low-res");
        var request = new LayerFilmstripPreviewRequest(
            map.SphereId,
            map.LayerId,
            map.RequestedTick,
            map.ViewRung,
            map.GraphRevision,
            map.Width,
            map.Height);
        var expected = FilmstripFramePayloadPolicy.Build(null!, map);
        var cache = new System.Collections.Generic.Dictionary<FilmstripTextureCacheKey, FilmstripFramePayload>();
        var requestKeys = new System.Collections.Generic.Dictionary<string, FilmstripTextureCacheKey>();
        var requestKey = FilmstripFramePayloadPolicy.RequestKey(request);
        var cacheKey = FilmstripFramePayloadPolicy.CacheKey(map);
        requestKeys[requestKey] = cacheKey;
        cache[cacheKey] = expected;

        var hit = FilmstripFramePayloadPolicy.TryGetCached(
            request,
            requestKeys,
            cache,
            out var actual);

        Assert.True(hit);
        Assert.Equal(expected.Metadata, actual.Metadata);

        var changedRevision = request with { GraphRevision = request.GraphRevision + 1 };
        Assert.False(FilmstripFramePayloadPolicy.TryGetCached(
            changedRevision,
            requestKeys,
            cache,
            out _));
    }

    [Fact]
    public void Provider_map_must_match_every_request_identity_field()
    {
        var map = Map();
        var request = new LayerFilmstripPreviewRequest(
            map.SphereId,
            map.LayerId,
            map.RequestedTick,
            map.ViewRung,
            map.GraphRevision,
            map.Width,
            map.Height);

        Assert.True(FilmstripFramePayloadPolicy.Matches(map, request));
        Assert.False(FilmstripFramePayloadPolicy.Matches(map with { SphereId = "atmosphere" }, request));
        Assert.False(FilmstripFramePayloadPolicy.Matches(map with { LayerId = "geosphere.plate" }, request));
        Assert.False(FilmstripFramePayloadPolicy.Matches(map with { RequestedTick = map.RequestedTick + 1 }, request));
        Assert.False(FilmstripFramePayloadPolicy.Matches(map with { ViewRung = "ka" }, request));
        Assert.False(FilmstripFramePayloadPolicy.Matches(map with { Width = map.Width + 1 }, request));
        Assert.False(FilmstripFramePayloadPolicy.Matches(map with { Height = map.Height + 1 }, request));
        Assert.False(FilmstripFramePayloadPolicy.Matches(map with { GraphRevision = map.GraphRevision + 1 }, request));
    }

    private static LayerFilmstripPreviewMap Map(
        long requestedTick = 107_500_000L,
        long snapshotTick = 105_000_000L,
        int graphRevision = 12,
        string sourceKind = "crust-low-res")
        => new(
            "geosphere",
            "geosphere.crust",
            requestedTick,
            snapshotTick,
            graphRevision,
            "kb",
            3,
            96,
            48,
            sourceKind,
            new byte[96 * 48 * 4]);
}
