using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelSnapshotSourcePolicyTests
{
    [Theory]
    [InlineData("crust-low-res")]
    [InlineData("plate-low-res")]
    [InlineData("mantle-shell-low-res")]
    public void IsReal_accepts_only_proven_world_sources(string sourceKind)
        => Assert.True(TunnelSnapshotSourcePolicy.IsReal(sourceKind));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pre-crust")]
    [InlineData("magma-ocean")]
    [InlineData("stagnant-lid")]
    [InlineData("atmosphere-placeholder")]
    [InlineData("layer-placeholder")]
    [InlineData("unknown")]
    [InlineData("Crust-Low-Res")]
    public void IsReal_rejects_placeholder_unknown_and_nonordinal_values(string? sourceKind)
        => Assert.False(TunnelSnapshotSourcePolicy.IsReal(sourceKind));

    [Fact]
    public void Rejected_payload_state_cannot_reveal_sphere()
    {
        var state = TunnelSnapshotSourcePolicy.StateFor("layer-placeholder");

        Assert.False(state.SphereVisible);
        Assert.True(state.UnavailableVisible);
    }
}
