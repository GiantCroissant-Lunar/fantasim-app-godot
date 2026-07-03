using System;
using FantaSim.App.World.Composition;
using UnifyMaths;
using Xunit;

namespace App.World.Composition.Tests;

public class CutawayWedgeTests
{
    private static readonly Vector3D Up = new(0, 0, 1);

    [Fact]
    public void Contains_ZeroWidthIsInactive_NeverContains()
    {
        var wedge = new CutawayWedge(Up, startAzimuthDeg: 0, widthDeg: 0);
        Assert.False(wedge.Contains(new Vector3D(1, 0, 0)));
        Assert.False(wedge.Contains(new Vector3D(0, 1, 0)));
        Assert.False(wedge.Contains(new Vector3D(0, -1, 0)));
    }

    [Fact]
    public void Contains_FullWidthContainsEverythingExceptAxis()
    {
        var wedge = new CutawayWedge(Up, startAzimuthDeg: 0, widthDeg: 360);
        Assert.True(wedge.Contains(new Vector3D(1, 0, 0)));
        Assert.True(wedge.Contains(new Vector3D(0, 1, 0)));
        Assert.True(wedge.Contains(new Vector3D(-1, 0, 0)));
        Assert.True(wedge.Contains(new Vector3D(0, -1, 0)));
        Assert.True(wedge.Contains(new Vector3D(1, 1, 0)));
    }

    [Fact]
    public void Contains_NinetyDegreeWedgeFromZero_ContainsEastQuadrant()
    {
        var wedge = new CutawayWedge(Up, startAzimuthDeg: 0, widthDeg: 90);
        Assert.True(wedge.Contains(new Vector3D(1, 0, 0)));       // 0°
        Assert.True(wedge.Contains(new Vector3D(1, 1, 0)));        // 45°
        Assert.False(wedge.Contains(new Vector3D(0, 1, 0)));       // 90° edge → exclusive end
        Assert.False(wedge.Contains(new Vector3D(-1, 0, 0)));      // 180°
        Assert.False(wedge.Contains(new Vector3D(0, -1, 0)));      // 270°
    }

    [Fact]
    public void Contains_StartAzimuthIsInclusive()
    {
        var wedge = new CutawayWedge(Up, startAzimuthDeg: 90, widthDeg: 90);
        Assert.True(wedge.Contains(new Vector3D(0, 1, 0)));        // 90° = start → inclusive
        Assert.True(wedge.Contains(new Vector3D(-1, 1, 0)));        // 135°
        Assert.False(wedge.Contains(new Vector3D(-1, 0, 0)));       // 180° = end → exclusive
    }

    [Fact]
    public void Contains_WrapAroundAzimuth_HandlesCrossingZero()
    {
        // Wedge from 315° to 45° (crossing the 0°/360° boundary); end at 45° is exclusive.
        var wedge = new CutawayWedge(Up, startAzimuthDeg: 315, widthDeg: 90);
        Assert.True(wedge.Contains(new Vector3D(1, 0, 0)));        // 0° — inside wrap
        Assert.True(wedge.Contains(new Vector3D(1, -1, 0)));        // 315° = start (inclusive)
        Assert.False(wedge.Contains(new Vector3D(1, 1, 0)));       // 45° = end → exclusive
        Assert.False(wedge.Contains(new Vector3D(0, 1, 0)));        // 90° — outside
        Assert.False(wedge.Contains(new Vector3D(-1, 0, 0)));      // 180° — outside
    }

    [Fact]
    public void Contains_DirectionAlongAxis_NeverContained()
    {
        var wedge = new CutawayWedge(Up, startAzimuthDeg: 0, widthDeg: 360);
        Assert.False(wedge.Contains(Up));                         // exact axis
        Assert.False(wedge.Contains(new Vector3D(0, 0, -1)));     // antipodal axis
    }

    [Fact]
    public void Contains_OffAxisDirectionsWithZComponent_ProjectsCorrectly()
    {
        // Axis = Z. Direction with z-component but also x-component → azimuth still 0°.
        var wedge = new CutawayWedge(Up, startAzimuthDeg: 0, widthDeg: 90);
        Assert.True(wedge.Contains(new Vector3D(1, 0, 0.5)));      // azimuth 0° with z lift
        Assert.False(wedge.Contains(new Vector3D(0, 1, 0.5)));     // azimuth 90° → end exclusive
    }

    [Fact]
    public void Contains_TiltedAxisUsesCorrectReference()
    {
        // Axis = X (east). Basis: seed=(0,0,1), reference = seed×axis = +Y, referenceCross = axis×ref = +Z.
        // So azimuth 0°=+Y, 45°=(0,1,1), 90°=+Z.
        var xAxis = new Vector3D(1, 0, 0);
        var wedge = new CutawayWedge(xAxis, startAzimuthDeg: 0, widthDeg: 90);
        Assert.True(wedge.Contains(new Vector3D(0, 1, 0)));        // reference dir (+Y) at 0°
        Assert.True(wedge.Contains(new Vector3D(0, 1, 1)));        // 45° in YZ
        Assert.False(wedge.Contains(new Vector3D(0, 0, 1)));       // 90° = +Z → end exclusive
    }

    [Fact]
    public void Inactive_WedgeIsInactiveWhenWidthZero()
    {
        Assert.True(new CutawayWedge(Up, 0, 0).IsInactive);
        Assert.False(new CutawayWedge(Up, 0, 90).IsInactive);
        Assert.False(new CutawayWedge(Up, 0, 360).IsInactive);
    }

    [Fact]
    public void Constructor_NegativeWidthClampedToZero()
    {
        var wedge = new CutawayWedge(Up, 0, -10);
        Assert.True(wedge.IsInactive);
        Assert.False(wedge.Contains(new Vector3D(1, 0, 0)));
    }

    [Fact]
    public void Constructor_WidthClampedTo360()
    {
        var wedge = new CutawayWedge(Up, 0, 400);
        Assert.True(wedge.Contains(new Vector3D(1, 0, 0)));
        Assert.True(wedge.Contains(new Vector3D(0, 1, 0)));
    }

    [Fact]
    public void Constructor_NonUnitAxisIsNormalized()
    {
        var wedge = new CutawayWedge(new Vector3D(0, 0, 5), 0, 90);
        Assert.True(wedge.Contains(new Vector3D(1, 0, 0)));
        Assert.False(wedge.Contains(new Vector3D(0, 1, 0)));
    }

    [Fact]
    public void EndAzimuthDeg_ReturnsStartPlusWidthClampedTo360()
    {
        Assert.Equal(90, new CutawayWedge(Up, 0, 90).EndAzimuthDeg);
        Assert.Equal(360, new CutawayWedge(Up, 270, 90).EndAzimuthDeg); // wrap → 360 not 0
    }

    [Fact]
    public void NormalizedStart_ReturnsStartIn0To360()
    {
        Assert.Equal(0, new CutawayWedge(Up, 0, 90).NormalizedStart);
        Assert.Equal(270, new CutawayWedge(Up, -90, 90).NormalizedStart);
        Assert.Equal(90, new CutawayWedge(Up, 450, 90).NormalizedStart);
    }
}