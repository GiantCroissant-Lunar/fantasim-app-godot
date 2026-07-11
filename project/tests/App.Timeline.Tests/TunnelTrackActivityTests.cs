using System;
using System.Collections.Generic;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for the two-ring prototype's regime-active track join
/// (vault/plans/2026-07-12-rotating-tunnel-two-ring-prototype-plan.md Task 2B). Pure Godot-free:
/// resolves which sphere schedule applies by exact id, then checks the regime-at-tick active layer
/// set with ordinal comparison. Activity styles/enables; it never filters the registry list.
/// </summary>
public sealed class TunnelTrackActivityTests
{
    private static LayerTrackDescriptor Descriptor(string sphereId, string layerId) => new(
        SphereId: sphereId,
        LayerId: layerId,
        StreamId: new LayerTrackStreamId("main", "default", "L0", "world", "default"),
        DisplayName: layerId,
        State: LayerTrackStates.Declared,
        TimeDomain: new LayerTrackTimeDomain(0L, null, "ka"),
        Content: new LayerTrackContent("filmstrip"),
        Capabilities: new[] { "scrub" },
        SourceRef: layerId);

    private static SphereRegimeSchedule Schedule(
        string sphere,
        long start,
        long end,
        params string[] activeLayerIds)
        => new(
            new SphereId(sphere),
            new[]
            {
                new SphereRegime(
                    "regime-1",
                    start,
                    end,
                    System.Array.ConvertAll(activeLayerIds, id => new LayerId(id))),
            });

    private static readonly SphereRegimeSchedule GeosphereActive =
        Schedule("geosphere", 0L, 1_000_000L, "geosphere.crust", "geosphere.plate");

    private static readonly SphereRegimeSchedule AtmosphereActive =
        Schedule("atmosphere", 0L, 1_000_000L, "atmosphere.bulk");

    private static readonly SphereRegimeSchedule EmptyGeosphere =
        new(new SphereId("geosphere"), System.Array.Empty<SphereRegime>());

    [Fact]
    public void IsActive_GeosphereLayerInRegime_ReturnsTrue()
    {
        var descriptor = Descriptor("geosphere", "geosphere.crust");

        Assert.True(TunnelTrackActivity.IsActive(descriptor, 500_000L, GeosphereActive, AtmosphereActive));
    }

    [Fact]
    public void IsActive_GeosphereLayerNotInRegime_ReturnsFalse()
    {
        var descriptor = Descriptor("geosphere", "geosphere.mantle");

        Assert.False(TunnelTrackActivity.IsActive(descriptor, 500_000L, GeosphereActive, AtmosphereActive));
    }

    [Fact]
    public void IsActive_AtmosphereLayerInRegime_ReturnsTrue()
    {
        var descriptor = Descriptor("atmosphere", "atmosphere.bulk");

        Assert.True(TunnelTrackActivity.IsActive(descriptor, 500_000L, GeosphereActive, AtmosphereActive));
    }

    [Fact]
    public void IsActive_AtTickOutsideAnyRegime_ReturnsFalse()
    {
        var descriptor = Descriptor("geosphere", "geosphere.crust");

        // The regime ends at 1_000_000; tick 2_000_000 is past it.
        Assert.False(TunnelTrackActivity.IsActive(descriptor, 2_000_000L, GeosphereActive, AtmosphereActive));
    }

    [Fact]
    public void IsActive_AtRegimeBoundary_TransitionsActiveToInactive()
    {
        var descriptor = Descriptor("geosphere", "geosphere.crust");

        Assert.True(TunnelTrackActivity.IsActive(descriptor, 999_999L, GeosphereActive, AtmosphereActive));
        Assert.False(TunnelTrackActivity.IsActive(descriptor, 1_000_000L, GeosphereActive, AtmosphereActive));
    }

    [Fact]
    public void IsActive_UnknownSphere_ReturnsFalse()
    {
        var descriptor = Descriptor("hydrosphere", "hydrosphere.ocean");

        Assert.False(TunnelTrackActivity.IsActive(descriptor, 500_000L, GeosphereActive, AtmosphereActive));
    }

    [Fact]
    public void IsActive_EmptySchedule_ReturnsFalse()
    {
        var descriptor = Descriptor("geosphere", "geosphere.crust");

        Assert.False(TunnelTrackActivity.IsActive(descriptor, 500_000L, EmptyGeosphere, AtmosphereActive));
    }

    [Fact]
    public void IsActive_LayerIdCaseDifference_IsNotActive_OrdinalComparison()
    {
        // The active layer is "geosphere.crust"; a descriptor differing only by case must NOT match.
        var descriptor = Descriptor("geosphere", "GEOSPHERE.CRUST");

        Assert.False(TunnelTrackActivity.IsActive(descriptor, 500_000L, GeosphereActive, AtmosphereActive));
    }
}
