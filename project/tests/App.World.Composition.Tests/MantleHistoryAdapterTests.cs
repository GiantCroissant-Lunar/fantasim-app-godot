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
    public void Build_ConnectedEdgeLocalArcsOfSameBoundary_CoalesceBeforeChunking()
    {
        var p0 = Unit(1.0, 0.0, 0.0);
        var p1 = Unit(System.Math.Cos(0.15), System.Math.Sin(0.15), 0.0);
        var p2 = Unit(System.Math.Cos(0.30), System.Math.Sin(0.30), 0.0);
        var p3 = Unit(System.Math.Cos(0.45), System.Math.Sin(0.45), 0.0);

        // Visual reconstruction intentionally publishes one record per real tessellation edge.
        // Mantle forcing must recover the connected semantic boundary before applying its angular
        // chunk cap. Records are scrambled and one is reversed to prove input ordering/orientation
        // do not recreate one expensive mantle ribbon per visual edge.
        var arcs = new[]
        {
            new PlateBoundaryArc(0, 1, PlateBoundaryKind.Convergent, new[] { p2, p3 }),
            new PlateBoundaryArc(1, 0, PlateBoundaryKind.Convergent, new[] { p2, p1 }),
            new PlateBoundaryArc(0, 1, PlateBoundaryKind.Convergent, new[] { p0, p1 }),
        };

        var history = MantleHistoryAdapter.Build(arcs, plateOnsetTick: 0);

        var segment = Assert.Single(history.Segments);
        Assert.True(
            ApproxEqual(p0, segment.A) && ApproxEqual(p3, segment.B)
                || ApproxEqual(p3, segment.A) && ApproxEqual(p0, segment.B),
            "Coalesced mantle forcing must cover the complete connected boundary in either orientation.");
    }

    [Fact]
    public void Build_DisconnectedArcsWithSamePairAndKind_NeverBridgeTheGap()
    {
        var a0 = Unit(1.0, 0.0, 0.0);
        var a1 = Unit(System.Math.Cos(0.15), System.Math.Sin(0.15), 0.0);
        var b0 = Unit(System.Math.Cos(1.0), System.Math.Sin(1.0), 0.0);
        var b1 = Unit(System.Math.Cos(1.15), System.Math.Sin(1.15), 0.0);
        var arcs = new[]
        {
            new PlateBoundaryArc(0, 1, PlateBoundaryKind.Divergent, new[] { b0, b1 }),
            new PlateBoundaryArc(1, 0, PlateBoundaryKind.Divergent, new[] { a1, a0 }),
        };

        var history = MantleHistoryAdapter.Build(arcs, plateOnsetTick: 0);

        Assert.Equal(2, history.Segments.Count);
        Assert.DoesNotContain(
            history.Segments,
            segment => ApproxEqual(a0, segment.A) && ApproxEqual(b1, segment.B)
                || ApproxEqual(b1, segment.A) && ApproxEqual(a0, segment.B));
    }

    [Fact]
    public void Build_YJunction_PreservesBranchesWithoutLeafToLeafShortcut()
    {
        var junction = Unit(1.0, 0.0, 0.0);
        var northLeaf = Unit(System.Math.Cos(0.25), System.Math.Sin(0.25), 0.0);
        var southLeaf = Unit(System.Math.Cos(0.25), -System.Math.Sin(0.25), 0.0);
        var upperLeaf = Unit(System.Math.Cos(0.25), 0.0, System.Math.Sin(0.25));

        // Three same-boundary branches share one endpoint. Records are shuffled, plate ids are
        // reversed, and two polylines point toward the junction so ordering/orientation cannot
        // justify pairing two leaves through the degree-three vertex.
        var arcs = new[]
        {
            new PlateBoundaryArc(1, 0, PlateBoundaryKind.Convergent, new[] { northLeaf, junction }),
            new PlateBoundaryArc(0, 1, PlateBoundaryKind.Convergent, new[] { junction, upperLeaf }),
            new PlateBoundaryArc(1, 0, PlateBoundaryKind.Convergent, new[] { southLeaf, junction }),
        };

        var history = MantleHistoryAdapter.Build(arcs, plateOnsetTick: 0);

        Assert.Equal(3, history.Segments.Count);
        Assert.All(
            history.Segments,
            segment => Assert.True(
                ApproxEqual(junction, segment.A) || ApproxEqual(junction, segment.B),
                "Every mantle segment at a Y-junction must terminate at the junction; a segment " +
                "whose endpoints are both leaves is an invalid leaf-to-leaf shortcut."));

        foreach (var leaf in new[] { northLeaf, southLeaf, upperLeaf })
        {
            Assert.Contains(
                history.Segments,
                segment => ApproxEqual(leaf, segment.A) && ApproxEqual(junction, segment.B)
                    || ApproxEqual(junction, segment.A) && ApproxEqual(leaf, segment.B));
        }
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

    private static bool ApproxEqual(GlobeVec3 expected, UnifyMaths.Vector3D actual)
        => System.Math.Abs(expected.X - actual.X) < 1e-5
            && System.Math.Abs(expected.Y - actual.Y) < 1e-5
            && System.Math.Abs(expected.Z - actual.Z) < 1e-5;
}
