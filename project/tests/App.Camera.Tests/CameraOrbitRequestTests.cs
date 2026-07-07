using System;
using FantaSim.App.Camera;
using Xunit;

namespace App.Camera.Tests;

public sealed class CameraOrbitRequestTests
{
    [Fact]
    public void Parse_NullOrEmpty_IsNoOp()
    {
        var r = CameraOrbitRequestParser.Parse(null);
        Assert.False(r.HasChanges);

        r = CameraOrbitRequestParser.Parse("");
        Assert.False(r.HasChanges);

        r = CameraOrbitRequestParser.Parse("   ");
        Assert.False(r.HasChanges);
    }

    [Fact]
    public void Parse_EmptyObject_IsNoOp()
    {
        var r = CameraOrbitRequestParser.Parse("{}");
        Assert.False(r.HasChanges);
    }

    [Fact]
    public void Parse_PartialPayload_KeepsMissingValues()
    {
        var r = CameraOrbitRequestParser.Parse("{\"yawDeg\":35,\"pitchDeg\":-20}");

        Assert.Equal(35.0, r.YawDeg);
        Assert.Equal(-20.0, r.PitchDeg);
        Assert.Null(r.Distance);
    }

    [Fact]
    public void Parse_AllFields_ReturnsValues()
    {
        var r = CameraOrbitRequestParser.Parse("{\"yawDeg\":15.5,\"pitchDeg\":25,\"distance\":3.25}");

        Assert.Equal(15.5, r.YawDeg);
        Assert.Equal(25.0, r.PitchDeg);
        Assert.Equal(3.25, r.Distance);
    }

    [Fact]
    public void Parse_PitchAndDistance_AreClamped()
    {
        var r = CameraOrbitRequestParser.Parse("{\"pitchDeg\":-120,\"distance\":0.5}");
        Assert.Equal(-85.0, r.PitchDeg);
        Assert.Equal(1.5, r.Distance);

        r = CameraOrbitRequestParser.Parse("{\"pitchDeg\":120,\"distance\":10}");
        Assert.Equal(85.0, r.PitchDeg);
        Assert.Equal(8.0, r.Distance);
    }

    [Fact]
    public void Parse_NonObject_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            CameraOrbitRequestParser.Parse("\"not an object\""));
        Assert.Throws<ArgumentException>(() =>
            CameraOrbitRequestParser.Parse("[1,2,3]"));
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CameraOrbitRequestParser.Parse("{"));

        Assert.Contains("camera.orbit payload must be valid JSON", ex.Message);
    }

    [Fact]
    public void Parse_NonNumericFields_ThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            CameraOrbitRequestParser.Parse("{\"yawDeg\":\"east\"}"));
        Assert.Throws<ArgumentException>(() =>
            CameraOrbitRequestParser.Parse("{\"pitchDeg\":\"down\"}"));
        Assert.Throws<ArgumentException>(() =>
            CameraOrbitRequestParser.Parse("{\"distance\":\"near\"}"));
    }
}
