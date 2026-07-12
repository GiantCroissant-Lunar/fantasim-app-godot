using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelFineApplyPolicyTests
{
    private static readonly TunnelFineApplyReadiness Ready = new(
        BinderAlive: true,
        ExpectedGeneration: 17,
        CurrentGeneration: 17,
        ExpectedEpoch: 23,
        CurrentEpoch: 23,
        ExpectedBucket: 29,
        CurrentBucket: 29,
        ExpectedGraphRevision: 31,
        CurrentGraphRevision: 31,
        ExpectedSphereId: "geosphere",
        CurrentSphereId: "geosphere",
        ExpectedLayerId: "crust",
        CurrentLayerId: "crust");

    [Fact]
    public void Exact_live_request_is_ready_to_apply()
    {
        Assert.True(TunnelFineApplyPolicy.CanApply(Ready));
    }

    [Fact]
    public void Dead_binder_is_not_ready_to_apply()
    {
        Assert.False(TunnelFineApplyPolicy.CanApply(Ready with { BinderAlive = false }));
    }

    [Fact]
    public void Different_generation_is_not_ready_to_apply()
    {
        Assert.False(TunnelFineApplyPolicy.CanApply(Ready with { CurrentGeneration = 18 }));
    }

    [Fact]
    public void Different_fine_epoch_is_not_ready_to_apply()
    {
        Assert.False(TunnelFineApplyPolicy.CanApply(Ready with { CurrentEpoch = 24 }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(30L)]
    public void Missing_or_different_bucket_is_not_ready_to_apply(long? currentBucket)
    {
        Assert.False(TunnelFineApplyPolicy.CanApply(Ready with { CurrentBucket = currentBucket }));
    }

    [Fact]
    public void Different_graph_revision_is_not_ready_to_apply()
    {
        Assert.False(TunnelFineApplyPolicy.CanApply(Ready with { CurrentGraphRevision = 32 }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("atmosphere")]
    [InlineData("Geosphere")]
    public void Missing_different_or_differently_cased_sphere_is_not_ready_to_apply(
        string? currentSphereId)
    {
        Assert.False(TunnelFineApplyPolicy.CanApply(Ready with { CurrentSphereId = currentSphereId }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mantle")]
    [InlineData("Crust")]
    public void Missing_different_or_differently_cased_layer_is_not_ready_to_apply(
        string? currentLayerId)
    {
        Assert.False(TunnelFineApplyPolicy.CanApply(Ready with { CurrentLayerId = currentLayerId }));
    }
}
