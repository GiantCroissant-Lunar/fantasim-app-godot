using FantaSim.App.World.Services;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Windowed-gate regression guard: the per-tick continental fraction product must carry real
/// land on the default generated path. The 2026-07-07 windowed pass caught an all-ocean globe
/// because the per-seek refresh overwrote good fractions with an empty/zero product.
/// </summary>
public sealed class ContinentalFractionAtTickTests
{
    [Fact]
    public void Generated_path_yields_nonzero_fractions_at_mobile_plate_tick()
    {
        using var service = new Service(new ServiceRegistry());

        var fractions = service.GetContinentalFractionByCellAt(107_000_000);

        Assert.NotEmpty(fractions);
        var land = fractions.Count(kv => kv.Value > 0.5);
        Assert.True(land > 0, $"expected continental cells at t=107M, got {land} of {fractions.Count}");
    }

    [Fact]
    public void Bind_time_document_carries_continental_fractions()
    {
        using var service = new Service(new ServiceRegistry());

        var documentSnap = service.GetPlanetPresentationAsync(105_000_000);
        var landSnap = documentSnap?.ContinentalFractionByCell?.Count(kv => kv.Value > 0.5) ?? -1;

        var document = service.GetPlanetPresentationAsync(107_000_000);

        Assert.NotNull(document?.ContinentalFractionByCell);
        var land = document!.ContinentalFractionByCell!.Count(kv => kv.Value > 0.5);
        Assert.True(land > 0, $"bind@107M(non-snapshot)={land} of {document.ContinentalFractionByCell.Count}; bind@105M(snapshot)={landSnap}");
    }

    [Fact]
    public void Fraction_keys_align_with_globe_snapshot_cells()
    {
        using var service = new Service(new ServiceRegistry());

        var snapshot = service.GetGlobeSnapshotAt(107_000_000);
        var fractions = service.GetContinentalFractionByCellAt(107_000_000);

        var snapshotIds = snapshot.Cells.Select(c => c.CellId).ToHashSet();
        var overlap = fractions.Keys.Count(snapshotIds.Contains);
        Assert.True(
            overlap == snapshot.Cells.Count && overlap == fractions.Count,
            $"painter key mismatch: snapshot={snapshot.Cells.Count} cells, fractions={fractions.Count}, overlap={overlap}");
    }
}
