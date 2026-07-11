using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for the two-ring prototype's provisional fine-preview mapping
/// (vault/plans/2026-07-12-rotating-tunnel-two-ring-prototype-plan.md Task 3). Pure Godot-free:
/// focused-track binding (owner/rung/active), ±360deg clamped preview quantities, axial cursor
/// position toward the throat, and the integral-vs-fractional distinction that never rounds a
/// sub-tick quantity into authority.
/// </summary>
public sealed class TunnelFinePreviewMapperTests
{
    private const double RailCenterZ = -7.0;
    private const double RailHalfLength = 2.5;

    private static LayerTrackDescriptor Descriptor(string layerId = "geosphere.crust", string rung = "ka") => new(
        SphereId: "geosphere",
        LayerId: layerId,
        StreamId: new LayerTrackStreamId("main", "default", "L0", "world", "default"),
        DisplayName: layerId,
        State: LayerTrackStates.Declared,
        TimeDomain: new LayerTrackTimeDomain(0L, null, rung),
        Content: new LayerTrackContent("filmstrip"),
        Capabilities: new[] { "scrub" },
        SourceRef: layerId);

    private static TimelineLadderRung GlobalFallback => TimelineModel.GetLadderRungs().First();

    private static TunnelFineTrackBinding ActiveBinding(string rung = "ka")
        => TunnelFinePreviewMapper.Bind(Descriptor(rung: rung), isActive: true, GlobalFallback);

    // ---- Bind ----

    [Fact]
    public void Bind_ActiveKnownRung_CanAdjust()
    {
        var binding = ActiveBinding();

        Assert.NotNull(binding.Descriptor);
        Assert.NotNull(binding.Rung);
        Assert.True(binding.IsActive);
        Assert.True(binding.CanAdjust);
        Assert.Equal("geosphere.crust", binding.OwnerLabel);
    }

    [Fact]
    public void Bind_InactiveTrack_CannotAdjust()
    {
        var binding = TunnelFinePreviewMapper.Bind(Descriptor(), isActive: false, GlobalFallback);

        Assert.False(binding.CanAdjust);
        Assert.Equal("geosphere.crust", binding.OwnerLabel);
    }

    [Fact]
    public void Bind_NullDescriptor_OwnerLabelIsNoTrack()
    {
        var binding = TunnelFinePreviewMapper.Bind(null, isActive: false, GlobalFallback);

        Assert.Null(binding.Descriptor);
        Assert.False(binding.CanAdjust);
        Assert.Equal("No track", binding.OwnerLabel);
    }

    [Fact]
    public void Bind_UnknownRungSymbol_FallsBackToGlobal()
    {
        var binding = TunnelFinePreviewMapper.Bind(Descriptor(rung: "bogus"), isActive: true, GlobalFallback);

        Assert.Equal(GlobalFallback.Symbol, binding.Rung?.Symbol);
    }

    // ---- Map: clamping and quantities ----

    [Fact]
    public void Map_ClampsToPlusMinus360()
    {
        var binding = ActiveBinding();

        var over = TunnelFinePreviewMapper.Map(binding, accumulatedDegrees: 720.0, RailCenterZ, RailHalfLength);
        var under = TunnelFinePreviewMapper.Map(binding, accumulatedDegrees: -720.0, RailCenterZ, RailHalfLength);

        Assert.Equal(360.0, over.AccumulatedDegrees, precision: 6);
        Assert.Equal(1.0, over.RungUnits, precision: 6);
        Assert.Equal(-360.0, under.AccumulatedDegrees, precision: 6);
        Assert.Equal(-1.0, under.RungUnits, precision: 6);
    }

    [Fact]
    public void Map_PositiveAngleMovesCursorTowardThroat()
    {
        var binding = ActiveBinding();
        var preview = TunnelFinePreviewMapper.Map(binding, 180.0, RailCenterZ, RailHalfLength);

        // RungUnits = 0.5; CursorZ = center - 0.5 * halfLength = -7 - 1.25 = -8.25 (deeper, toward throat).
        Assert.Equal(0.5, preview.RungUnits, precision: 6);
        Assert.Equal(RailCenterZ - 0.5 * RailHalfLength, preview.CursorZ, precision: 6);
        Assert.True(preview.CursorZ < RailCenterZ);
    }

    [Fact]
    public void Map_RawTickQuantityIsRungUnitsTimesUnitTicks()
    {
        var binding = ActiveBinding();
        var preview = TunnelFinePreviewMapper.Map(binding, 90.0, RailCenterZ, RailHalfLength);

        Assert.Equal(0.25, preview.RungUnits, precision: 6);
        Assert.Equal(0.25 * binding.Rung!.UnitTicks, preview.RawTickQuantity, precision: 6);
    }

    // ---- Map: integral vs fractional ----

    [Fact]
    public void Map_WholeTickDelta_RecordsIntegralTickDelta()
    {
        // Find a rung whose UnitTicks is itself near-whole so that one ±360° revolution
        // (which is all the inner ring can represent after clamping) produces an integral quantity.
        var rung = TimelineModel.GetLadderRungs()
            .FirstOrDefault(r => r.UnitTicks >= 1.0 && IsRelativelyWhole(r.UnitTicks));
        if (rung is null)
            return; // No rung with whole UnitTicks in the current ladder; skip gracefully.

        var binding = TunnelFinePreviewMapper.Bind(Descriptor(rung: rung.Symbol), true, GlobalFallback);

        var preview = TunnelFinePreviewMapper.Map(binding, 360.0, RailCenterZ, RailHalfLength);

        Assert.NotNull(preview.IntegralTickDelta);
        Assert.Equal((long)Math.Round(rung.UnitTicks, MidpointRounding.AwayFromZero), preview.IntegralTickDelta);
        Assert.False(preview.IsFractionalPresentation);
    }

    [Fact]
    public void Map_FractionalDelta_LeavesIntegralNull_NeverRoundsIntoAuthority()
    {
        var binding = ActiveBinding();
        // 1 degree: RungUnits = 1/360 -> RawTickQuantity = UnitTicks/360, fractional for real rungs.
        var preview = TunnelFinePreviewMapper.Map(binding, 1.0, RailCenterZ, RailHalfLength);

        Assert.Null(preview.IntegralTickDelta);
        Assert.True(preview.IsFractionalPresentation);
    }

    [Fact]
    public void Map_InactiveBinding_StaysCenteredAndZero()
    {
        var inactive = TunnelFinePreviewMapper.Bind(Descriptor(), isActive: false, GlobalFallback);

        var preview = TunnelFinePreviewMapper.Map(inactive, 180.0, RailCenterZ, RailHalfLength);

        Assert.Equal(0.0, preview.AccumulatedDegrees, precision: 6);
        Assert.Equal(0.0, preview.RungUnits, precision: 6);
        Assert.Equal(0.0, preview.RawTickQuantity, precision: 6);
        Assert.Null(preview.IntegralTickDelta);
        Assert.Equal(RailCenterZ, preview.CursorZ, precision: 6);
    }

    // ---- Reset ----

    [Fact]
    public void Reset_ReturnsCenteredPreviewAtRailCenter()
    {
        var binding = ActiveBinding();

        var reset = TunnelFinePreviewMapper.Reset(binding, RailCenterZ, RailHalfLength);

        Assert.Equal(0.0, reset.AccumulatedDegrees, precision: 6);
        Assert.Equal(0.0, reset.RungUnits, precision: 6);
        Assert.Equal(RailCenterZ, reset.CursorZ, precision: 6);
        Assert.Null(reset.IntegralTickDelta);
    }

    [Fact]
    public void Map_ZeroAccumulatedOnActiveBinding_ExposesIntegralZero_NotFractional()
    {
        var binding = ActiveBinding();

        var preview = TunnelFinePreviewMapper.Map(binding, 0.0, RailCenterZ, RailHalfLength);

        Assert.NotNull(preview.IntegralTickDelta);
        Assert.Equal(0L, preview.IntegralTickDelta);
        Assert.False(preview.IsFractionalPresentation);
    }

    [Fact]
    public void Map_SubTickRungAtFullRevolution_RemainsFractional_NotIntegralZero()
    {
        // Real sub-tick ladder rungs (e.g. jw/jv with UnitTicks far below 1) must not be collapsed
        // to integral 0 by an absolute tolerance. The relative test keeps them fractional.
        var subTickRung = TimelineModel.GetLadderRungs()
            .FirstOrDefault(r => r.UnitTicks > 0 && r.UnitTicks < 1e-3);

        if (subTickRung is null)
            return; // No sub-tick rung exists in the current ladder; skip gracefully.

        var binding = TunnelFinePreviewMapper.Bind(Descriptor(rung: subTickRung.Symbol), isActive: true, GlobalFallback);

        var preview = TunnelFinePreviewMapper.Map(binding, 360.0, RailCenterZ, RailHalfLength);

        Assert.True(preview.RawTickQuantity > 0, "Sub-tick quantity at +360 must be nonzero");
        Assert.Null(preview.IntegralTickDelta);
        Assert.True(preview.IsFractionalPresentation);

        var negPreview = TunnelFinePreviewMapper.Map(binding, -360.0, RailCenterZ, RailHalfLength);
        Assert.True(negPreview.RawTickQuantity < 0, "Sub-tick quantity at -360 must be nonzero");
        Assert.Null(negPreview.IntegralTickDelta);
    }

    // Finds the smallest positive integer K such that K * unitTicks is within 1e-6 of a whole number.
    private static bool IsRelativelyWhole(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return Math.Abs(value - rounded) <= 1e-6 * Math.Abs(value);
    }
}
