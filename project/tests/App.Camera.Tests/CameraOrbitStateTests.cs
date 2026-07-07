using FantaSim.App.Camera;
using Xunit;

namespace App.Camera.Tests;

public sealed class CameraOrbitStateTests
{
    [Fact]
    public void Apply_BeforeBind_RemembersPendingValues_ForFirstBind()
    {
        var sut = new CameraOrbitState();
        sut.ConfigureInitial(35.0, -25.0, 4.0);

        var pending = sut.Apply(10.0, null, 9.0);

        Assert.False(sut.IsBound);
        Assert.Equal(10.0, pending.YawDeg);
        Assert.Equal(-25.0, pending.PitchDeg);
        Assert.Equal(8.0, pending.Distance);

        var bound = sut.Bind();

        Assert.True(sut.IsBound);
        Assert.Equal(10.0, bound.YawDeg);
        Assert.Equal(-25.0, bound.PitchDeg);
        Assert.Equal(8.0, bound.Distance);
    }

    [Fact]
    public void Apply_AfterBind_UpdatesCurrentAndClamps()
    {
        var sut = new CameraOrbitState();
        sut.ConfigureInitial(35.0, -25.0, 4.0);
        sut.Bind();

        var applied = sut.Apply(null, -120.0, 0.5);

        Assert.Equal(35.0, applied.YawDeg);
        Assert.Equal(-85.0, applied.PitchDeg);
        Assert.Equal(1.5, applied.Distance);
    }

    [Fact]
    public void NoOpApply_KeepsCurrentValues()
    {
        var sut = new CameraOrbitState();
        sut.ConfigureInitial(35.0, -25.0, 4.0);
        sut.Bind();
        sut.Apply(20.0, -10.0, 3.0);

        var applied = sut.Apply(null, null, null);

        Assert.Equal(20.0, applied.YawDeg);
        Assert.Equal(-10.0, applied.PitchDeg);
        Assert.Equal(3.0, applied.Distance);
    }

    [Fact]
    public void MouseOrbitAndZoom_UseSameState()
    {
        var sut = new CameraOrbitState();
        sut.ConfigureInitial(35.0, -25.0, 4.0);
        sut.Bind();

        var orbit = sut.OrbitBy(-5.0, 10.0);
        var zoom = sut.ZoomByFactor(0.5);

        Assert.Equal(30.0, orbit.YawDeg);
        Assert.Equal(-15.0, orbit.PitchDeg);
        Assert.Equal(30.0, zoom.YawDeg);
        Assert.Equal(-15.0, zoom.PitchDeg);
        Assert.Equal(2.0, zoom.Distance);
    }
}
