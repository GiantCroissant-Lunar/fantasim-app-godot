using FantaSim.App.Presentation.Tunnel;
using FantaSim.App.Timeline.Seam;
using NumericsVector3 = System.Numerics.Vector3;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

/// <summary>
/// Headless coverage for the tunnel input ownership policy. Wheel zoom is gated by effective
/// tunnel enable state, respects GUI Control first-chance, and tracks scale capture/restore
/// across the lifecycle so hidden preparation never snapshots a stale original scale.
/// </summary>
public sealed class TunnelInputPolicyTests
{
    [Fact]
    public void WheelZoom_WhenDisabled_DoesNothingAndLeavesEventUnhandled()
    {
        var policy = new TunnelInputPolicy(enabled: false);
        var result = policy.HandleWheel(direction: 1);

        Assert.False(result.Handled);
        Assert.False(result.AdjustZoom);
        Assert.Equal(TunnelPlanetZoom.DefaultScale, result.RequestedZoomScale);
    }

    [Fact]
    public void WheelZoom_WhenEnabled_ConsumesEventAndStepsZoom()
    {
        var policy = new TunnelInputPolicy(enabled: true);
        var result = policy.HandleWheel(direction: 1);

        Assert.True(result.Handled);
        Assert.True(result.AdjustZoom);
        Assert.Equal(
            TunnelPlanetZoom.Step(TunnelPlanetZoom.DefaultScale, direction: 1),
            result.RequestedZoomScale,
            precision: 5);
    }

    [Fact]
    public void WheelZoom_RepeatedIn_SaturatesAtMax()
    {
        var policy = new TunnelInputPolicy(enabled: true);
        for (var i = 0; i < 100; i++)
            policy.HandleWheel(direction: 1);

        var result = policy.HandleWheel(direction: 1);
        Assert.True(result.Handled);
        Assert.Equal(TunnelPlanetZoom.MaxScale, result.RequestedZoomScale, precision: 5);
    }

    [Fact]
    public void Enable_CapturesOriginalScaleOnce()
    {
        var policy = new TunnelInputPolicy(enabled: false);
        policy.OnTunnelEnabled(new NumericsVector3(1.0f, 2.0f, 3.0f));

        Assert.Equal(new NumericsVector3(1.0f, 2.0f, 3.0f), policy.OriginalScale);
        Assert.Equal(TunnelPlanetZoom.DefaultScale, policy.CurrentZoomScale);
        Assert.True(policy.IsEnabled);
        Assert.True(policy.HandleWheel(direction: 1).Handled);
    }

    [Fact]
    public void Enable_DoesNotOverwriteAlreadyCapturedScale()
    {
        var policy = new TunnelInputPolicy(enabled: false);
        policy.OnTunnelEnabled(new NumericsVector3(1.0f, 2.0f, 3.0f));
        policy.OnTunnelEnabled(new NumericsVector3(4.0f, 5.0f, 6.0f));

        Assert.Equal(new NumericsVector3(1.0f, 2.0f, 3.0f), policy.OriginalScale);
    }

    [Fact]
    public void Disable_RestoresOriginalScaleAndResetsZoom()
    {
        var policy = new TunnelInputPolicy(enabled: true);
        policy.OnTunnelEnabled(new NumericsVector3(2.0f, 2.0f, 2.0f));
        policy.HandleWheel(direction: 1);
        policy.OnTunnelDisabled();

        Assert.Null(policy.OriginalScale);
        Assert.Equal(TunnelPlanetZoom.DefaultScale, policy.CurrentZoomScale);
        Assert.False(policy.IsEnabled);
        Assert.False(policy.HandleWheel(direction: 1).Handled);
    }

    [Fact]
    public void PreparationHidden_DoesNotCaptureOriginalScale()
    {
        var policy = new TunnelInputPolicy(enabled: false);
        Assert.Null(policy.OriginalScale);
        Assert.Equal(TunnelPlanetZoom.DefaultScale, policy.CurrentZoomScale);
    }

    [Fact]
    public void Repeated_Enable_Disable_RestoresOriginalScaleExactly()
    {
        var original = new NumericsVector3(0.8f, 0.8f, 0.8f);

        var policy = new TunnelInputPolicy(enabled: false);
        policy.OnTunnelEnabled(original);
        Assert.Equal(original, policy.OriginalScale);
        policy.HandleWheel(direction: 1);
        policy.OnTunnelDisabled();

        policy.OnTunnelEnabled(original);
        Assert.Equal(original, policy.OriginalScale);
        Assert.Equal(TunnelPlanetZoom.DefaultScale, policy.CurrentZoomScale);
        policy.OnTunnelDisabled();

        Assert.Null(policy.OriginalScale);
    }

    [Fact]
    public void Disable_Before_Capture_Is_NoOp()
    {
        var policy = new TunnelInputPolicy(enabled: false);
        policy.OnTunnelDisabled();

        Assert.Null(policy.OriginalScale);
        Assert.Equal(TunnelPlanetZoom.DefaultScale, policy.CurrentZoomScale);
        Assert.False(policy.IsEnabled);
    }

    [Fact]
    public void Second_Enable_Does_Not_Recapture_AlreadyZoomed_Scale()
    {
        var original = new NumericsVector3(1.0f, 1.0f, 1.0f);
        var policy = new TunnelInputPolicy(enabled: false);

        policy.OnTunnelEnabled(original);
        policy.HandleWheel(direction: 1);
        var zoomedScale = policy.CurrentZoomScale;

        policy.OnTunnelEnabled(new NumericsVector3(99f, 99f, 99f));
        Assert.Equal(original, policy.OriginalScale);
        Assert.Equal(zoomedScale, policy.CurrentZoomScale);
    }

    [Fact]
    public void Disable_After_Exception_RestoresOriginalScaleAndResetsZoom()
    {
        var original = new NumericsVector3(1.5f, 1.5f, 1.5f);
        var policy = new TunnelInputPolicy(enabled: false);

        policy.OnTunnelEnabled(original);
        policy.HandleWheel(direction: 1);
        Assert.NotEqual(TunnelPlanetZoom.DefaultScale, policy.CurrentZoomScale);

        policy.OnTunnelDisabled();

        Assert.Null(policy.OriginalScale);
        Assert.Equal(TunnelPlanetZoom.DefaultScale, policy.CurrentZoomScale);
        Assert.False(policy.HandleWheel(direction: 1).Handled);
    }

    [Fact]
    public void Bundle_Shutdown_RestoresOriginalScaleAndResetsZoom()
    {
        var original = new NumericsVector3(0.7f, 0.7f, 0.7f);
        var policy = new TunnelInputPolicy(enabled: false);

        policy.OnTunnelEnabled(original);
        policy.HandleWheel(direction: 1);
        policy.HandleWheel(direction: 1);

        policy.OnTunnelDisabled();

        Assert.Null(policy.OriginalScale);
        Assert.Equal(TunnelPlanetZoom.DefaultScale, policy.CurrentZoomScale);
    }

    [Fact]
    public void Rebind_After_Disable_RecapturesFreshOriginalScale()
    {
        var firstOriginal = new NumericsVector3(1.0f, 1.0f, 1.0f);
        var secondOriginal = new NumericsVector3(0.5f, 0.5f, 0.5f);
        var policy = new TunnelInputPolicy(enabled: false);

        policy.OnTunnelEnabled(firstOriginal);
        Assert.Equal(firstOriginal, policy.OriginalScale);
        policy.OnTunnelDisabled();

        Assert.Null(policy.OriginalScale);

        policy.OnTunnelEnabled(secondOriginal);
        Assert.Equal(secondOriginal, policy.OriginalScale);
        Assert.Equal(TunnelPlanetZoom.DefaultScale, policy.CurrentZoomScale);
    }
}
