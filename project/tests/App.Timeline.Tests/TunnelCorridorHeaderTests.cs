using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for the tunnel corridor header formatter (vault/plans/
/// 2026-07-13-tunnel-visual-slice1-plan.md Part A). Mirrors the normal timeline track header's
/// "name + state" minimalism, adding the canonical display rung; the active/inactive word is
/// paired with a bool the renderer color-codes.
/// </summary>
public sealed class TunnelCorridorHeaderTests
{
    private static LayerTrackDescriptor Descriptor(
        string layerId = "crust",
        string displayName = "Crust",
        string rung = "ka") => new(
        SphereId: "geosphere",
        LayerId: layerId,
        StreamId: new LayerTrackStreamId("main", "default", "L0", "world", "default"),
        DisplayName: displayName,
        State: LayerTrackStates.Declared,
        TimeDomain: new LayerTrackTimeDomain(0L, null, rung),
        Content: new LayerTrackContent("filmstrip"),
        Capabilities: new[] { "scrub" },
        SourceRef: layerId);

    [Fact]
    public void Build_ActiveTrack_TitleIsDisplayName_SubtitleHasRungAndActive()
    {
        var header = TunnelCorridorHeader.Build(Descriptor(), isActive: true);

        Assert.Equal("Crust", header.Title);
        Assert.Equal("ka · active", header.Subtitle);
        Assert.True(header.IsActive);
    }

    [Fact]
    public void Build_InactiveTrack_SubtitleSaysInactive()
    {
        var header = TunnelCorridorHeader.Build(Descriptor(), isActive: false);

        Assert.Equal("ka · inactive", header.Subtitle);
        Assert.False(header.IsActive);
    }

    [Fact]
    public void Build_BlankDisplayName_FallsBackToLayerId()
    {
        var header = TunnelCorridorHeader.Build(
            Descriptor(layerId: "magma-ocean", displayName: "  "), isActive: true);

        Assert.Equal("magma-ocean", header.Title);
    }

    [Fact]
    public void Build_BlankRung_SubtitleIsStateWordOnly()
    {
        var header = TunnelCorridorHeader.Build(
            Descriptor(rung: ""), isActive: true);

        Assert.Equal("active", header.Subtitle);
    }
}
