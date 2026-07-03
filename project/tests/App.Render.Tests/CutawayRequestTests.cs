using System;
using Xunit;

namespace App.Render.Tests;

public class CutawayRequestTests
{
    [Fact]
    public void Parse_NullOrEmpty_ReturnsInactive()
    {
        var r = FantaSim.App.Render.CutawayRequestParser.Parse(null);
        Assert.True(r.IsInactive);
        Assert.Equal(0.0, r.AzimuthDeg);
        Assert.Equal(0.0, r.WidthDeg);

        r = FantaSim.App.Render.CutawayRequestParser.Parse("");
        Assert.True(r.IsInactive);
    }

    [Fact]
    public void Parse_EmptyObject_ReturnsInactive()
    {
        var r = FantaSim.App.Render.CutawayRequestParser.Parse("{}");
        Assert.True(r.IsInactive);
        Assert.Equal(0.0, r.AzimuthDeg);
        Assert.Equal(0.0, r.WidthDeg);
    }

    [Fact]
    public void Parse_WidthZero_ReturnsInactive()
    {
        var r = FantaSim.App.Render.CutawayRequestParser.Parse("{\"azimuthDeg\":45,\"widthDeg\":0}");
        Assert.True(r.IsInactive);
        Assert.Equal(45.0, r.AzimuthDeg);
        Assert.Equal(0.0, r.WidthDeg);
    }

    [Fact]
    public void Parse_ValidPayload_ReturnsValues()
    {
        var r = FantaSim.App.Render.CutawayRequestParser.Parse("{\"azimuthDeg\":90,\"widthDeg\":45}");
        Assert.False(r.IsInactive);
        Assert.Equal(90.0, r.AzimuthDeg);
        Assert.Equal(45.0, r.WidthDeg);
    }

    [Fact]
    public void Parse_NegativeWidth_ClampedToZero()
    {
        var r = FantaSim.App.Render.CutawayRequestParser.Parse("{\"azimuthDeg\":0,\"widthDeg\":-10}");
        Assert.True(r.IsInactive);
        Assert.Equal(0.0, r.WidthDeg);
    }

    [Fact]
    public void Parse_WidthOver360_ClampedTo360()
    {
        var r = FantaSim.App.Render.CutawayRequestParser.Parse("{\"azimuthDeg\":0,\"widthDeg\":400}");
        Assert.False(r.IsInactive);
        Assert.Equal(360.0, r.WidthDeg);
    }

    [Fact]
    public void Parse_AzimuthNormalizedTo0To360()
    {
        var r = FantaSim.App.Render.CutawayRequestParser.Parse("{\"azimuthDeg\":-90,\"widthDeg\":45}");
        Assert.Equal(270.0, r.AzimuthDeg);
        Assert.False(r.IsInactive);

        r = FantaSim.App.Render.CutawayRequestParser.Parse("{\"azimuthDeg\":450,\"widthDeg\":45}");
        Assert.Equal(90.0, r.AzimuthDeg);
    }

    [Fact]
    public void Parse_AzimuthAlone_NoWidth_ReturnsInactive()
    {
        var r = FantaSim.App.Render.CutawayRequestParser.Parse("{\"azimuthDeg\":90}");
        Assert.True(r.IsInactive);
        Assert.Equal(90.0, r.AzimuthDeg);
        Assert.Equal(0.0, r.WidthDeg);
    }

    [Fact]
    public void Parse_WidthAlone_NoAzimuth_DefaultsAzimuthZero()
    {
        var r = FantaSim.App.Render.CutawayRequestParser.Parse("{\"widthDeg\":90}");
        Assert.False(r.IsInactive);
        Assert.Equal(0.0, r.AzimuthDeg);
        Assert.Equal(90.0, r.WidthDeg);
    }

    [Fact]
    public void Parse_NotAnObject_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.CutawayRequestParser.Parse("\"not an object\""));
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.CutawayRequestParser.Parse("[1,2,3]"));
    }

    [Fact]
    public void Parse_NonNumericAzimuth_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.CutawayRequestParser.Parse("{\"azimuthDeg\":\"east\",\"widthDeg\":45}"));
    }

    [Fact]
    public void Parse_NonNumericWidth_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            FantaSim.App.Render.CutawayRequestParser.Parse("{\"azimuthDeg\":0,\"widthDeg\":\"wide\"}"));
    }

    [Fact]
    public void Parse_IntegerValues_AcceptedAsDoubles()
    {
        var r = FantaSim.App.Render.CutawayRequestParser.Parse("{\"azimuthDeg\":45,\"widthDeg\":90}");
        Assert.Equal(45.0, r.AzimuthDeg);
        Assert.Equal(90.0, r.WidthDeg);
    }
}