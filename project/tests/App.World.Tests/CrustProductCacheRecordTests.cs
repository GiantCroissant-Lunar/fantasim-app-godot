using FantaSim.App.World.Persistence;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// TDD coverage for the crust-product cache's persisted-record shape (2026-07-11 persistence slice
/// 1, vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md §4.1/§5): MessagePack round-trip
/// and the document-id composition that makes a SchemaVersion bump or app-version change a natural
/// cache miss rather than a post-fetch version check.
/// </summary>
public sealed class CrustProductCacheRecordTests
{
    private static CrustProductCacheRecord SampleRecord() => new(
        Seed: 7,
        Frequency: 4,
        SpinRateRadiansPerMegaAnnum: 0.0035,
        GraphRevision: 1,
        SnapshotTick: 105_000_000L,
        SchemaVersion: CrustProductCacheSchema.SchemaVersion,
        AppVersionStamp: "test-stamp",
        CellStates: new[]
        {
            new CellCrustStateRecord(0, 0.8, 1.5, 0.2, 4_000_000d),
            new CellCrustStateRecord(1, 0.0, 0.0, 0.0, 4_000_000d),
        },
        Features: new[]
        {
            new CrustFeatureRecord(0, Kind: 1, Magnitude: 5.2),
        });

    [Fact]
    public void Encode_then_decode_round_trips_every_field()
    {
        var record = SampleRecord();

        var decoded = CrustProductCacheSchema.Decode(CrustProductCacheSchema.Encode(record));

        Assert.Equal(record.Seed, decoded.Seed);
        Assert.Equal(record.Frequency, decoded.Frequency);
        Assert.Equal(record.SpinRateRadiansPerMegaAnnum, decoded.SpinRateRadiansPerMegaAnnum);
        Assert.Equal(record.GraphRevision, decoded.GraphRevision);
        Assert.Equal(record.SnapshotTick, decoded.SnapshotTick);
        Assert.Equal(record.SchemaVersion, decoded.SchemaVersion);
        Assert.Equal(record.AppVersionStamp, decoded.AppVersionStamp);
        Assert.Equal(record.CellStates, decoded.CellStates);
        Assert.Equal(record.Features, decoded.Features);
    }

    [Fact]
    public void ComposeDocumentId_with_identical_fields_is_stable()
    {
        var id1 = CrustProductCacheSchema.ComposeDocumentId(7, 4, 0.0035, 1, 105_000_000L, 1, "stamp-a");
        var id2 = CrustProductCacheSchema.ComposeDocumentId(7, 4, 0.0035, 1, 105_000_000L, 1, "stamp-a");

        Assert.Equal(id1, id2);
    }

    [Theory]
    [InlineData(8, 4, 0.0035, 1, 105_000_000L, 1, "stamp-a")]      // different Seed
    [InlineData(7, 3, 0.0035, 1, 105_000_000L, 1, "stamp-a")]      // different Frequency
    [InlineData(7, 4, 0.005, 1, 105_000_000L, 1, "stamp-a")]       // different SpinRate
    [InlineData(7, 4, 0.0035, 2, 105_000_000L, 1, "stamp-a")]      // different GraphRevision
    [InlineData(7, 4, 0.0035, 1, 110_000_000L, 1, "stamp-a")]      // different SnapshotTick
    [InlineData(7, 4, 0.0035, 1, 105_000_000L, 2, "stamp-a")]      // different SchemaVersion
    [InlineData(7, 4, 0.0035, 1, 105_000_000L, 1, "stamp-b")]      // different AppVersionStamp
    public void ComposeDocumentId_changes_when_any_key_or_invalidation_field_changes(
        int seed, int frequency, double spinRate, int graphRevision, long snapshotTick,
        int schemaVersion, string appVersionStamp)
    {
        var baseline = CrustProductCacheSchema.ComposeDocumentId(7, 4, 0.0035, 1, 105_000_000L, 1, "stamp-a");
        var varied = CrustProductCacheSchema.ComposeDocumentId(
            seed, frequency, spinRate, graphRevision, snapshotTick, schemaVersion, appVersionStamp);

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void CurrentAppVersionStamp_is_non_empty_and_stable_within_the_process()
    {
        Assert.False(string.IsNullOrWhiteSpace(CrustProductCacheSchema.CurrentAppVersionStamp));
        Assert.Equal(CrustProductCacheSchema.CurrentAppVersionStamp, CrustProductCacheSchema.CurrentAppVersionStamp);
    }
}
