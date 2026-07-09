using System.Text.Json.Nodes;
using FantaSim.App.Timeline;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Timeline.Tests;

public sealed class TimelineSeekOriginParsingTests
{
    [Theory]
    [InlineData("standard", TimelineTickOrigin.Standard)]
    [InlineData("scrubPreview", TimelineTickOrigin.ScrubPreview)]
    [InlineData("scrubCommit", TimelineTickOrigin.ScrubCommit)]
    [InlineData("STANDARD", TimelineTickOrigin.Standard)]
    [InlineData("ScrubPreview", TimelineTickOrigin.ScrubPreview)]
    [InlineData("SCRUBCOMMIT", TimelineTickOrigin.ScrubCommit)]
    public void ParseSeekOrigin_RecognizesKnownValuesCaseInsensitive(string origin, TimelineTickOrigin expected)
    {
        var payload = new JsonObject { ["tick"] = 1, ["origin"] = origin };

        Assert.Equal(expected, TimelinePlugin.ParseSeekOrigin(payload));
    }

    [Fact]
    public void ParseSeekOrigin_DefaultsToStandardWhenFieldAbsent()
    {
        var payload = new JsonObject { ["tick"] = 1 };

        Assert.Equal(TimelineTickOrigin.Standard, TimelinePlugin.ParseSeekOrigin(payload));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("scrub_preview")]
    public void ParseSeekOrigin_DefaultsToStandardForUnknown(string origin)
    {
        var payload = new JsonObject { ["tick"] = 1, ["origin"] = origin };

        Assert.Equal(TimelineTickOrigin.Standard, TimelinePlugin.ParseSeekOrigin(payload));
    }
}
