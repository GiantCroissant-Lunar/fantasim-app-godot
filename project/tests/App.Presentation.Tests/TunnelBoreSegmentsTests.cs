using System.Collections.Generic;
using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public sealed class TunnelBoreSegmentsTests
{
    private static TunnelBoreSpline Spline()
        => TunnelBoreSpline.Create(
            seed: 1234,
            TunnelBoreContract.StraightRadius,
            TunnelBoreContract.CurvatureCapRadPerUnit,
            maxDepth: 15.0);

    [Fact]
    public void Straight_band_is_a_single_segment()
    {
        var segments = TunnelBoreSegments.Plan(Spline(), 0.0, 7.5, TunnelBoreContract.MaxSegmentLength);

        var segment = Assert.Single(segments);
        Assert.Equal(3.75, segment.MidDepth, 12);
        Assert.Equal(3.75, segment.HalfLength, 12);
        Assert.Equal(-3.75, segment.Frame.Position.Z, 12);
    }

    [Fact]
    public void Curved_band_is_subdivided_to_the_maximum_segment_length()
    {
        var segments = TunnelBoreSegments.Plan(Spline(), 7.5, 11.25, TunnelBoreContract.MaxSegmentLength);

        Assert.Equal(3, segments.Count);
        foreach (var segment in segments)
            Assert.True(segment.HalfLength * 2.0 <= TunnelBoreContract.MaxSegmentLength + 1e-9);
    }

    [Fact]
    public void Segments_tile_the_band_without_gaps()
    {
        var segments = TunnelBoreSegments.Plan(Spline(), 7.5, 15.0, TunnelBoreContract.MaxSegmentLength);

        var covered = 0.0;
        var cursor = 7.5;
        foreach (var segment in segments)
        {
            Assert.Equal(cursor + segment.HalfLength, segment.MidDepth, 9);
            cursor += segment.HalfLength * 2.0;
            covered += segment.HalfLength * 2.0;
        }
        Assert.Equal(7.5, covered, 9);
    }

    [Fact]
    public void Band_spanning_the_boundary_splits_at_the_straight_radius()
    {
        // A caller may pass a band that crosses StraightRadius; the straight part stays one
        // segment and only the curved remainder subdivides.
        var segments = TunnelBoreSegments.Plan(Spline(), 3.75, 11.25, TunnelBoreContract.MaxSegmentLength);

        Assert.True(segments.Count >= 4);
        Assert.Equal(3.75 + ((7.5 - 3.75) / 2.0), segments[0].MidDepth, 9);
        Assert.Equal((7.5 - 3.75) / 2.0, segments[0].HalfLength, 9);
    }

    [Fact]
    public void Degenerate_or_non_finite_inputs_yield_no_segments()
    {
        Assert.Empty(TunnelBoreSegments.Plan(Spline(), 5.0, 5.0, TunnelBoreContract.MaxSegmentLength));
        Assert.Empty(TunnelBoreSegments.Plan(Spline(), 9.0, 7.0, TunnelBoreContract.MaxSegmentLength));
        Assert.Empty(TunnelBoreSegments.Plan(Spline(), double.NaN, 9.0, TunnelBoreContract.MaxSegmentLength));
        Assert.Empty(TunnelBoreSegments.Plan(Spline(), 7.0, 9.0, 0.0));
    }
}
