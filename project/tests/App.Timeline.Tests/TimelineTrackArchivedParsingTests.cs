using System;
using System.Text.Json.Nodes;
using FantaSim.App.Timeline;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Payload-parsing coverage for <c>timeline.set_track_archived</c>
/// (vault/plans/2026-07-10-layer-track-registry-slice1-plan.md Task 3). Mirrors
/// TimelineSeekOriginParsingTests' style: exercise the static parse helper directly against a
/// constructed JsonObject, no command/registry plumbing.
/// </summary>
public sealed class TimelineTrackArchivedParsingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParseSetTrackArchivedPayload_ReadsSphereLayerAndArchivedFlag(bool archived)
    {
        var payload = new JsonObject
        {
            ["sphereId"] = "geosphere",
            ["layerId"] = "geosphere.crust",
            ["archived"] = archived,
        };

        var result = TimelinePlugin.ParseSetTrackArchivedPayload(payload);

        Assert.Equal("geosphere", result.SphereId);
        Assert.Equal("geosphere.crust", result.LayerId);
        Assert.Equal(archived, result.Archived);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void ParseSetTrackArchivedPayload_ToleratesStringBooleanCaseInsensitive(string archived, bool expected)
    {
        var payload = new JsonObject
        {
            ["sphereId"] = "geosphere",
            ["layerId"] = "geosphere.crust",
            ["archived"] = archived,
        };

        var result = TimelinePlugin.ParseSetTrackArchivedPayload(payload);

        Assert.Equal(expected, result.Archived);
    }

    [Fact]
    public void ParseSetTrackArchivedPayload_MissingSphereId_Throws()
    {
        var payload = new JsonObject { ["layerId"] = "geosphere.crust", ["archived"] = true };

        Assert.Throws<ArgumentException>(() => TimelinePlugin.ParseSetTrackArchivedPayload(payload));
    }

    [Fact]
    public void ParseSetTrackArchivedPayload_MissingLayerId_Throws()
    {
        var payload = new JsonObject { ["sphereId"] = "geosphere", ["archived"] = true };

        Assert.Throws<ArgumentException>(() => TimelinePlugin.ParseSetTrackArchivedPayload(payload));
    }

    [Fact]
    public void ParseSetTrackArchivedPayload_MissingArchived_Throws()
    {
        var payload = new JsonObject { ["sphereId"] = "geosphere", ["layerId"] = "geosphere.crust" };

        Assert.Throws<ArgumentException>(() => TimelinePlugin.ParseSetTrackArchivedPayload(payload));
    }

    [Fact]
    public void ParseSetTrackArchivedPayload_UnparsableArchived_Throws()
    {
        var payload = new JsonObject
        {
            ["sphereId"] = "geosphere",
            ["layerId"] = "geosphere.crust",
            ["archived"] = "not-a-bool",
        };

        Assert.Throws<ArgumentException>(() => TimelinePlugin.ParseSetTrackArchivedPayload(payload));
    }
}
