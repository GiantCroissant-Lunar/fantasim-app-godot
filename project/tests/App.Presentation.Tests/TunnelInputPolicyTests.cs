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
        Assert.Equal(1.0f, result.RequestedZoomScale);
    }

    [Fact]
    public void WheelZoom_WhenEnabled_ConsumesEventAndStepsZoom()
    {
        var policy = new TunnelInputPolicy(enabled: true);
        var result = policy.HandleWheel(direction: 1);

        Assert.True(result.Handled);
        Assert.True(result.AdjustZoom);
        Assert.Equal(TunnelPlanetZoom.StepFactor, result.RequestedZoomScale, precision: 5);
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
        Assert.Equal(1.0f, policy.CurrentZoomScale);
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
        Assert.Equal(1.0f, policy.CurrentZoomScale);
    }

    [Fact]
    public void PreparationHidden_DoesNotCaptureOriginalScale()
    {
        var policy = new TunnelInputPolicy(enabled: false);
        Assert.Null(policy.OriginalScale);
        Assert.Equal(1.0f, policy.CurrentZoomScale);
    }
}
