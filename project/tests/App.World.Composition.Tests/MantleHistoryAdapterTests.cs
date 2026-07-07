using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Asthenosphere.Convection;
using Xunit;

namespace App.World.Composition.Tests;

// Mantle x-ray view (M-A), task 1 + task 5 tests: history adapter + sampling determinism.

public class MantleHistoryAdapterTests
{
    private static readonly GlobeVec3 P0 = Unit(1, 0, 0);
    private static readonly GlobeVec3 P1 = Unit(0, 1, 0);
    private static readonly GlobeVec3 P2 = Unit(-1, 0, 0);

    private static GlobeVec3 Unit(double x, double y, double z)
    {
        var len = System.Math.Sqrt(x * x + y * y + z * z);
        return new GlobeVec3((float)(x / len), (float)(y / len), (float)(z / len));
    }

    [Fact]
    public void Build_NullArcs_ReturnsEmptyHistory()
    {
        var history = MantleHistoryAdapter.Build(arcs: null, plateOnsetTick: 100);
        Assert.Empty(history.Segments);
    }

    [Fact]
    public void Build_EmptyArcs_ReturnsEmptyHistory()
    {
        var history = MantleHistoryAdapter.Build(arcs: new List<PlateBoundaryArc>(), plateOnsetTick: 100);
        Assert.Empty(history.Segments);
    }

    [Fact]
    public void Build_ConvergentArc_MapsToConvergentSegments()
    {
        // A 3-point convergent (trench) arc -> 2 segments, both Convergent.
        var arc = new PlateBoundaryArc(PlateA: 0, PlateB: 1, Kind: PlateBoundaryKind.Convergent,
            Points: new[] { P0, P1, P2 });

        var history = MantleHistoryAdapter.Build(new[] { arc }, plateOnsetTick: 500);

        Assert.Equal(2, history.Segments.Count);
        Assert.All(history.Segments, s => Assert.Equal(PlateHistoryKind.Convergent, s.Kind));
        Assert.All(history.Segments, s => Assert.Equal(500.0, s.ActiveSinceTick));
        Assert.All(history.Segments, s => Assert.Equal(MantleHistoryAdapter.DefaultRelativeRateRadPerTick, s.RelativeRateRadPerTick));
    }

    [Fact]
    public void Build_DivergentArc_MapsToDivergentSegments()
    {
        // A ridge (divergent) arc -> Divergent segments (hot upwelling curtains).
        var arc = new PlateBoundaryArc(PlateA: 1, PlateB: 2, Kind: PlateBoundaryKind.Divergent,
            Points: new[] { P0, P1 });

        var history = MantleHistoryAdapter.Build(new[] { arc }, plateOnsetTick: 0);

        var seg = Assert.Single(history.Segments);
        Assert.Equal(PlateHistoryKind.Divergent, seg.Kind);
    }

    [Fact]
    public void Build_TransformAndInactiveArcs_AreSkipped()
    {
        // Transform / Inactive boundaries have no forcing model -> dropped.
        var transform = new PlateBoundaryArc(0, 1, PlateBoundaryKind.Transform, new[] { P0, P1 });
        var inactive = new PlateBoundaryArc(1, 2, PlateBoundaryKind.Inactive, new[] { P1, P2 });

        var history = MantleHistoryAdapter.Build(new[] { transform, inactive }, plateOnsetTick: 0);

        Assert.Empty(history.Segments);
    }

    [Fact]
    public void Build_SegmentEndpointsAreUnitVectorsFromArcPoints()
    {
        var arc = new PlateBoundaryArc(0, 1, PlateBoundaryKind.Convergent, new[] { P0, P1 });

        var history = MantleHistoryAdapter.Build(new[] { arc }, plateOnsetTick: 0);

        var seg = Assert.Single(history.Segments);
        AssertApproxEqual(P0, seg.A);
        AssertApproxEqual(P1, seg.B);
    }

    [Fact]
    public void Build_ExplicitRateIsAppliedToEverySegment()
    {
        var arc = new PlateBoundaryArc(0, 1, PlateBoundaryKind.Convergent, new[] { P0, P1 });

        var history = MantleHistoryAdapter.Build(new[] { arc }, plateOnsetTick: 0, relativeRateRadPerTick: 1.5e-7);

        var seg = Assert.Single(history.Segments);
        Assert.Equal(1.5e-7, seg.RelativeRateRadPerTick);
    }

    [Fact]
    public void Build_ShortArc_BecomesOneSegment_NotPerPointPair()
    {
        // 11 closely spaced points spanning ~0.5 rad: the engine polyline-samples each SEGMENT
        // internally, so the adapter emits ONE span, not 10 point-pair kernels.
        var points = new GlobeVec3[11];
        for (int i = 0; i < points.Length; i++)
        {
            double ang = 0.05 * i;
            points[i] = Unit(System.Math.Cos(ang), System.Math.Sin(ang), 0);
        }
        var arc = new PlateBoundaryArc(0, 1, PlateBoundaryKind.Convergent, points);

        var history = MantleHistoryAdapter.Build(new[] { arc }, plateOnsetTick: 0);

        var seg = Assert.Single(history.Segments);
        AssertApproxEqual(points[0], seg.A);
        AssertApproxEqual(points[10], seg.B);
    }

    [Fact]
    public void Build_LongArc_IsChunked_ByAngularSpan_AndStaysContiguous()
    {
        // 25 points spanning ~2.16 rad: must split into spans each within the ~1.2 rad angular cap,
        // contiguous (chunk N ends where chunk N+1 starts), covering the whole arc.
        var points = new GlobeVec3[25];
        for (int i = 0; i < points.Length; i++)
        {
            double ang = 0.09 * i;
            points[i] = Unit(System.Math.Cos(ang), System.Math.Sin(ang), 0);
        }
        var arc = new PlateBoundaryArc(0, 1, PlateBoundaryKind.Convergent, points);

        var history = MantleHistoryAdapter.Build(new[] { arc }, plateOnsetTick: 0);

        Assert.Equal(2, history.Segments.Count);
        AssertApproxEqual(points[0], history.Segments[0].A);
        AssertApproxEqual(points[24], history.Segments[1].B);
        // Contiguous: first chunk's end is the second chunk's start.
        Assert.Equal(history.Segments[0].B, history.Segments[1].A);
        // Every chunk within the angular cap.
        foreach (var seg in history.Segments)
            Assert.True(UnifyMaths.Vector3D.Dot(seg.A, seg.B) > MantleHistoryAdapter.MaxSegmentSpanDot - 1e-9);
    }

    private static void AssertApproxEqual(GlobeVec3 expected, UnifyMaths.Vector3D actual)
    {
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
        Assert.Equal(expected.Z, actual.Z, 5);
    }
}
