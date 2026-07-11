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
    public void Map_KbFullRevolution_RecordsExactIntegralTickDelta()
    {
        var rung = TimelineModel.GetLadderRungs().Single(r => r.Symbol == "kb");
        var binding = TunnelFinePreviewMapper.Bind(Descriptor(rung: "kb"), true, GlobalFallback);

        var preview = TunnelFinePreviewMapper.Map(binding, 360.0, RailCenterZ, RailHalfLength);

        Assert.Equal(100_000_000d, rung.UnitTicks, precision: 6);
        Assert.Equal(100_000_000L, preview.IntegralTickDelta);
        Assert.False(preview.IsFractionalPresentation);
    }

    [Fact]
    public void Map_KbOneDegree_RemainsFractional_NotRoundedByMagnitudeScaledTolerance()
    {
        var binding = ActiveBinding("kb");

        var preview = TunnelFinePreviewMapper.Map(binding, 1.0, RailCenterZ, RailHalfLength);

        Assert.Equal(100_000_000d / 360d, preview.RawTickQuantity, precision: 6);
        Assert.Null(preview.IntegralTickDelta);
        Assert.True(preview.IsFractionalPresentation);
    }

    [Fact]
    public void Map_KfFullRevolution_OutsideLongRange_RemainsPresentationOnly()
    {
        var rung = TimelineModel.GetLadderRungs().Single(r => r.Symbol == "kf");
        var binding = ActiveBinding("kf");

        var preview = TunnelFinePreviewMapper.Map(binding, 360.0, RailCenterZ, RailHalfLength);

        Assert.True(rung.UnitTicks >= 1e20);
        Assert.Null(preview.IntegralTickDelta);
        Assert.True(preview.IsFractionalPresentation);
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
        Assert.Equal(0L, reset.IntegralTickDelta);
        Assert.False(reset.IsFractionalPresentation);
    }

    [Fact]
    public void Reset_InactiveBinding_RemainsNonAdjustableWithoutIntegralAuthority()
    {
        var binding = TunnelFinePreviewMapper.Bind(Descriptor(), isActive: false, GlobalFallback);

        var reset = TunnelFinePreviewMapper.Reset(binding, RailCenterZ, RailHalfLength);

        Assert.Null(reset.IntegralTickDelta);
        Assert.False(reset.IsFractionalPresentation);
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

    [Theory]
    [InlineData("jw")]
    [InlineData("jv")]
    public void Map_SubTickRungAtFullRevolution_RemainsFractional_NotIntegralZero(string symbol)
    {
        var subTickRung = TimelineModel.GetLadderRungs().Single(r => r.Symbol == symbol);
        var binding = TunnelFinePreviewMapper.Bind(Descriptor(rung: symbol), isActive: true, GlobalFallback);

        var preview = TunnelFinePreviewMapper.Map(binding, 360.0, RailCenterZ, RailHalfLength);

        Assert.True(preview.RawTickQuantity > 0, "Sub-tick quantity at +360 must be nonzero");
        Assert.Null(preview.IntegralTickDelta);
        Assert.True(preview.IsFractionalPresentation);

        var negPreview = TunnelFinePreviewMapper.Map(binding, -360.0, RailCenterZ, RailHalfLength);
        Assert.True(negPreview.RawTickQuantity < 0, "Sub-tick quantity at -360 must be nonzero");
        Assert.Null(negPreview.IntegralTickDelta);
    }
}
