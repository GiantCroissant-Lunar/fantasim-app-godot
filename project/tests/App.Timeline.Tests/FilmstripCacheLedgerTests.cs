using System;
using System.Linq;
using FantaSim.App.Timeline.Seam;
using Xunit;

namespace FantaSim.App.Timeline.Tests;

public sealed class FilmstripCacheLedgerTests
{
    [Fact]
    public void Record_UnderCap_ReturnsNull()
    {
        var ledger = new FilmstripCacheLedger(cap: 4);
        Assert.Null(ledger.Record("a"));
        Assert.Null(ledger.Record("b"));
        Assert.Null(ledger.Record("c"));
        Assert.Equal(3, ledger.Count);
    }

    [Fact]
    public void Record_OverCap_ReturnsOldestKey()
    {
        var ledger = new FilmstripCacheLedger(cap: 3);
        ledger.Record("a");
        ledger.Record("b");
        ledger.Record("c");
        var evicted = ledger.Record("d");
        Assert.Equal("a", evicted);
        Assert.Equal(3, ledger.Count);
        Assert.DoesNotContain("a", ledger.Keys);
    }

    [Fact]
    public void Record_Duplicate_ReturnsNull_AndDoesNotChangeEvictionPosition()
    {
        var ledger = new FilmstripCacheLedger(cap: 3);
        ledger.Record("a");
        ledger.Record("b");
        ledger.Record("c");
        Assert.Null(ledger.Record("b"));
        Assert.Equal(3, ledger.Count);
        Assert.Contains("b", ledger.Keys);
        var evicted = ledger.Record("d");
        Assert.Equal("a", evicted);
        Assert.DoesNotContain("a", ledger.Keys);
        Assert.Contains("b", ledger.Keys);
        Assert.Contains("c", ledger.Keys);
        Assert.Contains("d", ledger.Keys);
    }

    [Fact]
    public void Contains_ReturnsTrueForRecordedKey()
    {
        var ledger = new FilmstripCacheLedger(cap: 4);
        ledger.Record("a");
        Assert.True(ledger.Contains("a"));
        Assert.False(ledger.Contains("b"));
    }

    [Fact]
    public void Keys_AreInInsertionOrder()
    {
        var ledger = new FilmstripCacheLedger(cap: 4);
        ledger.Record("first");
        ledger.Record("second");
        ledger.Record("third");
        Assert.Equal(new[] { "first", "second", "third" }, ledger.Keys.ToArray());
    }

    [Fact]
    public void Clear_EmptiesTheLedger()
    {
        var ledger = new FilmstripCacheLedger(cap: 4);
        ledger.Record("a");
        ledger.Record("b");
        ledger.Clear();
        Assert.Equal(0, ledger.Count);
        Assert.Empty(ledger.Keys);
        Assert.False(ledger.Contains("a"));
    }

    [Fact]
    public void Record_AfterClear_RecordsNormally()
    {
        var ledger = new FilmstripCacheLedger(cap: 2);
        ledger.Record("a");
        ledger.Record("b");
        ledger.Clear();
        Assert.Null(ledger.Record("c"));
        Assert.Equal(1, ledger.Count);
        Assert.Contains("c", ledger.Keys);
    }

    [Fact]
    public void Record_CapOfOne_EvictsImmediately()
    {
        var ledger = new FilmstripCacheLedger(cap: 1);
        Assert.Null(ledger.Record("a"));
        Assert.Equal("a", ledger.Record("b"));
        Assert.Equal(1, ledger.Count);
        Assert.DoesNotContain("a", ledger.Keys);
        Assert.Contains("b", ledger.Keys);
    }
}