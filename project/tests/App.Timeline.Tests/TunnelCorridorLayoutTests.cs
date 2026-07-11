using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for the two-ring prototype's five-slot carousel and bottom-center focus
/// (vault/plans/2026-07-12-rotating-tunnel-two-ring-prototype-plan.md Task 2). Pure Godot-free:
/// registry-source selection (drop archived), cyclic focus normalization, the focus-0/-1/+1/-2/+2
/// window with (SphereId, LayerId) dedup, 30deg slot pitch, 15deg symmetric snap, and the retained
/// rung-resolution path.
/// </summary>
public sealed class TunnelCorridorLayoutTests
{
    private static LayerTrackDescriptor Descriptor(
        string sphereId,
        string layerId,
        string state = LayerTrackStates.Declared) => new(
        SphereId: sphereId,
        LayerId: layerId,
        StreamId: new LayerTrackStreamId("main", "default", "L0", "world", "default"),
        DisplayName: layerId,
        State: state,
        TimeDomain: new LayerTrackTimeDomain(0L, null, "ka"),
        Content: new LayerTrackContent("filmstrip"),
        Capabilities: new[] { "scrub", "toggle" },
        SourceRef: layerId);

    private static IReadOnlyList<LayerTrackDescriptor> Tracks(params string[] layerIds)
        => layerIds.Select((id, i) => Descriptor("geosphere", id)).ToList();

    private static LayerTrackRegistrySnapshot Snapshot(params LayerTrackDescriptor[] tracks)
        => new(Revision: 1, Tracks: tracks);

    // ---- SelectSourceTracks ----

    [Fact]
    public void SelectSourceTracks_DropsOnlyArchived_PreservesOrder()
    {
        var a = Descriptor("geosphere", "a", LayerTrackStates.Declared);
        var b = Descriptor("geosphere", "b", LayerTrackStates.Archived);
        var c = Descriptor("atmosphere", "c", LayerTrackStates.Discovered);

        var source = TunnelCorridorLayout.SelectSourceTracks(Snapshot(a, b, c));

        Assert.Equal(new[] { "a", "c" }, source.Select(t => t.LayerId));
    }

    [Fact]
    public void SelectSourceTracks_EmptySnapshot_ReturnsEmpty()
    {
        Assert.Empty(TunnelCorridorLayout.SelectSourceTracks(Snapshot()));
    }

    // ---- InitialFocusIndex / NormalizeFocusIndex ----

    [Theory]
    [InlineData(0, -1)]
    [InlineData(1, 0)]
    [InlineData(5, 0)]
    public void InitialFocusIndex_EmptyIsMinusOne_NonEmptyIsZero(int count, int expected)
    {
        Assert.Equal(expected, TunnelCorridorLayout.InitialFocusIndex(count));
    }

    [Fact]
    public void NormalizeFocusIndex_WrapsCyclically()
    {
        Assert.Equal(0, TunnelCorridorLayout.NormalizeFocusIndex(0, 3));
        Assert.Equal(0, TunnelCorridorLayout.NormalizeFocusIndex(3, 3));
        Assert.Equal(2, TunnelCorridorLayout.NormalizeFocusIndex(-1, 3));
        Assert.Equal(1, TunnelCorridorLayout.NormalizeFocusIndex(7, 3));
        Assert.Equal(0, TunnelCorridorLayout.NormalizeFocusIndex(-3, 3));
    }

    [Fact]
    public void NormalizeFocusIndex_ZeroOrNegativeCount_ReturnsMinusOne()
    {
        Assert.Equal(-1, TunnelCorridorLayout.NormalizeFocusIndex(5, 0));
        Assert.Equal(-1, TunnelCorridorLayout.NormalizeFocusIndex(5, -1));
    }

    // ---- ResolveFocusedTrack ----

    [Fact]
    public void ResolveFocusedTrack_ReturnsDescriptor_OrNullOrEmpty()
    {
        var tracks = Tracks("a", "b", "c");

        Assert.Equal("b", TunnelCorridorLayout.ResolveFocusedTrack(tracks, 1)?.LayerId);
        Assert.Null(TunnelCorridorLayout.ResolveFocusedTrack(tracks, -1));
        Assert.Null(TunnelCorridorLayout.ResolveFocusedTrack(System.Array.Empty<LayerTrackDescriptor>(), 0));
    }

    // ---- BuildFocusedWindow: N = 0..6 slot sets ----

    [Fact]
    public void BuildFocusedWindow_N0_ReturnsEmpty()
    {
        Assert.Empty(TunnelCorridorLayout.BuildFocusedWindow(System.Array.Empty<LayerTrackDescriptor>(), -1));
    }

    [Fact]
    public void BuildFocusedWindow_N1_SingleFocusedSlot()
    {
        var slots = TunnelCorridorLayout.BuildFocusedWindow(Tracks("a"), 0);

        var slot = Assert.Single(slots);
        Assert.Equal(0, slot.RelativeSlot);
        Assert.True(slot.IsFocused);
        Assert.Equal("a", slot.Descriptor.LayerId);
    }

    [Fact]
    public void BuildFocusedWindow_N2_OccupiesMinusOneAndZero()
    {
        var slots = TunnelCorridorLayout.BuildFocusedWindow(Tracks("a", "b"), 0);

        Assert.Equal(new[] { -1, 0 }, slots.Select(s => s.RelativeSlot));
        Assert.Equal("b", slots.Single(s => s.RelativeSlot == -1).Descriptor.LayerId);
        Assert.Equal("a", slots.Single(s => s.RelativeSlot == 0).Descriptor.LayerId);
    }

    [Fact]
    public void BuildFocusedWindow_N3_OccupiesMinusOneZeroPlusOne()
    {
        var slots = TunnelCorridorLayout.BuildFocusedWindow(Tracks("a", "b", "c"), 0);

        Assert.Equal(new[] { -1, 0, 1 }, slots.Select(s => s.RelativeSlot));
        Assert.Equal("c", slots.Single(s => s.RelativeSlot == -1).Descriptor.LayerId);
        Assert.Equal("a", slots.Single(s => s.RelativeSlot == 0).Descriptor.LayerId);
        Assert.Equal("b", slots.Single(s => s.RelativeSlot == 1).Descriptor.LayerId);
    }

    [Fact]
    public void BuildFocusedWindow_N4_OccupiesMinusTwoMinusOneZeroPlusOne()
    {
        var slots = TunnelCorridorLayout.BuildFocusedWindow(Tracks("a", "b", "c", "d"), 0);

        Assert.Equal(new[] { -2, -1, 0, 1 }, slots.Select(s => s.RelativeSlot));
    }

    [Fact]
    public void BuildFocusedWindow_N5_OccupiesAllFiveSlots()
    {
        var slots = TunnelCorridorLayout.BuildFocusedWindow(Tracks("a", "b", "c", "d", "e"), 0);

        Assert.Equal(new[] { -2, -1, 0, 1, 2 }, slots.Select(s => s.RelativeSlot));
        Assert.Equal("a", slots.Single(s => s.RelativeSlot == 0).Descriptor.LayerId);
    }

    [Fact]
    public void BuildFocusedWindow_N6_StillFiveUniqueSlotsAroundFocus()
    {
        var slots = TunnelCorridorLayout.BuildFocusedWindow(Tracks("a", "b", "c", "d", "e", "f"), 0);

        Assert.Equal(5, slots.Count);
        Assert.Equal(new[] { -2, -1, 0, 1, 2 }, slots.Select(s => s.RelativeSlot));
        var identities = slots.Select(s => (s.Descriptor.SphereId, s.Descriptor.LayerId)).ToList();
        Assert.Equal(identities.Distinct().Count(), identities.Count);
    }

    // ---- uniqueness / stable identities ----

    [Fact]
    public void BuildFocusedWindow_DeduplicatesBySphereIdLayerId()
    {
        // Two tracks share (geosphere, dup) in different slots -> only the first occurrence mounts.
        var tracks = new[]
        {
            Descriptor("geosphere", "dup"),
            Descriptor("atmosphere", "x"),
            Descriptor("geosphere", "dup"),
        };

        var slots = TunnelCorridorLayout.BuildFocusedWindow(tracks, 0);

        var identities = slots.Select(s => (s.Descriptor.SphereId, s.Descriptor.LayerId)).ToList();
        Assert.Equal(identities.Distinct().Count(), identities.Count);
    }

    // ---- 30deg slot pitch and +30deg visual polarity ----

    [Fact]
    public void BuildFocusedWindow_SlotCentersAreThirtyDegreesApart_AtBottomFocus()
    {
        var slots = TunnelCorridorLayout.BuildFocusedWindow(Tracks("a", "b", "c", "d", "e"), 0);

        // accumulated = 0: CenterAngle = -90 + relativeSlot*30.
        Assert.Equal(-90.0 - 60.0, slots.Single(s => s.RelativeSlot == -2).CenterAngleDegrees, precision: 6);
        Assert.Equal(-90.0, slots.Single(s => s.RelativeSlot == 0).CenterAngleDegrees, precision: 6);
        Assert.Equal(-90.0 + 60.0, slots.Single(s => s.RelativeSlot == 2).CenterAngleDegrees, precision: 6);
    }

    [Fact]
    public void BuildFocusedWindow_PositiveAccumulatedShiftsNextTrackTowardBottomFocus()
    {
        // accumulated = +30: the +1 track's center moves to -90 (bottom), proving clockwise advances
        // focus visually before the index advances on snap.
        var slots = TunnelCorridorLayout.BuildFocusedWindow(Tracks("a", "b", "c", "d", "e"), 0, accumulatedDegrees: 30.0);

        Assert.Equal(-90.0, slots.Single(s => s.RelativeSlot == 1).CenterAngleDegrees, precision: 6);
    }

    // ---- SnapFocus: 15deg threshold and multi-step drags ----

    [Fact]
    public void SnapFocus_BelowThreshold_StaysPut()
    {
        var snap = TunnelCorridorLayout.SnapFocus(0, 5, accumulatedDegrees: 14.9);

        Assert.Equal(0L, snap.StepDelta);
        Assert.Equal(0, snap.FocusIndex);
        Assert.Equal(0.0, snap.SnappedAngleDegrees, precision: 6);
    }

    [Fact]
    public void SnapFocus_AtThreshold_AdvancesByOne()
    {
        var snap = TunnelCorridorLayout.SnapFocus(0, 5, accumulatedDegrees: 15.0);

        Assert.Equal(1L, snap.StepDelta);
        Assert.Equal(1, snap.FocusIndex);
        Assert.Equal(30.0, snap.SnappedAngleDegrees, precision: 6);
    }

    [Fact]
    public void SnapFocus_NegativeThreshold_DecrementsSymmetrically()
    {
        var snap = TunnelCorridorLayout.SnapFocus(3, 5, accumulatedDegrees: -15.0);

        Assert.Equal(-1L, snap.StepDelta);
        Assert.Equal(2, snap.FocusIndex);
        Assert.Equal(-30.0, snap.SnappedAngleDegrees, precision: 6);
    }

    [Fact]
    public void SnapFocus_MultiStepDrag_WrapsCyclically()
    {
        // +75 deg = 2.5 steps -> rounds to 3 (AwayFromZero at .5); wraps 4+3=7 -> 7%5=2.
        var snap = TunnelCorridorLayout.SnapFocus(4, 5, accumulatedDegrees: 75.0);

        Assert.Equal(3L, snap.StepDelta);
        Assert.Equal(2, snap.FocusIndex);
        Assert.Equal(90.0, snap.SnappedAngleDegrees, precision: 6);
    }

    [Fact]
    public void SnapFocus_TrackCountOneOrLess_IsHardNoOp()
    {
        var snap1 = TunnelCorridorLayout.SnapFocus(0, 1, accumulatedDegrees: 100.0);
        Assert.Equal(0L, snap1.StepDelta);
        Assert.Equal(0, snap1.FocusIndex);

        var snap0 = TunnelCorridorLayout.SnapFocus(-1, 0, accumulatedDegrees: 100.0);
        Assert.Equal(0L, snap0.StepDelta);
        Assert.Equal(-1, snap0.FocusIndex);
    }

    [Fact]
    public void SnapFocus_StepDeltaBeyondIntMaxValue_ReducesModuloBeforeCombining()
    {
        // A stepDelta beyond int.MaxValue must be reduced modulo trackCount (as long) before
        // narrowing to int; (int)(focusIndex + stepDelta) would overflow and produce garbage.
        long bigStep = (long)int.MaxValue + 100; // 2147483747, well beyond int range
        var snap = TunnelCorridorLayout.SnapFocus(2, 5, accumulatedDegrees: bigStep * 30.0);

        Assert.True(snap.StepDelta > int.MaxValue);
        Assert.Equal(bigStep, snap.StepDelta);
        Assert.True(snap.FocusIndex >= 0 && snap.FocusIndex < 5);
    }

    [Fact]
    public void SnapFocus_StepDeltaBelowIntMinValue_ReducesModuloBeforeCombining()
    {
        long bigNegStep = (long)int.MinValue - 100; // -2147483748, well beyond int range
        var snap = TunnelCorridorLayout.SnapFocus(3, 5, accumulatedDegrees: bigNegStep * 30.0);

        Assert.True(snap.StepDelta < int.MinValue);
        Assert.Equal(bigNegStep, snap.StepDelta);
        Assert.True(snap.FocusIndex >= 0 && snap.FocusIndex < 5);
    }

    // ---- ResolveCorridorRung (retained from slice 1) ----

    [Fact]
    public void ResolveCorridorRung_KnownSymbol_ReturnsThatRungNotTheFallback()
    {
        var allRungs = TimelineModel.GetLadderRungs();
        var expected = allRungs.Single(r => r.Symbol == "ka");
        var fallback = allRungs.First(r => r.Symbol != "ka");

        var resolved = TunnelCorridorLayout.ResolveCorridorRung("ka", fallback);

        Assert.Equal(expected.Symbol, resolved.Symbol);
        Assert.NotEqual(fallback.Symbol, resolved.Symbol);
    }

    [Fact]
    public void ResolveCorridorRung_NullSymbol_ReturnsFallbackExactly()
    {
        var fallback = TimelineModel.GetLadderRungs().First();

        var resolved = TunnelCorridorLayout.ResolveCorridorRung(null, fallback);

        Assert.Equal(fallback.Symbol, resolved.Symbol);
    }

    [Fact]
    public void ResolveCorridorRung_UnrecognizedSymbol_DegradesToFallback_NeverThrows()
    {
        var fallback = TimelineModel.GetLadderRungs().First();

        var resolved = TunnelCorridorLayout.ResolveCorridorRung("bogus", fallback);

        Assert.Equal(fallback.Symbol, resolved.Symbol);
    }
}
