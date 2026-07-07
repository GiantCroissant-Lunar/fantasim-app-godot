using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlanetTimelineControllerScrubOriginTests
{
    [Fact]
    public void ScrubPreview_AppliesTickButDoesNotPublishTickChanged()
    {
        var applied = new List<(long Tick, TimelineTickOrigin Origin)>();
        var controller = new PlanetTimelineController((tick, origin) => applied.Add((tick, origin)));
        var tickChanged = 0;
        controller.TickChanged += _ => tickChanged++;

        controller.PushTick(1, TimelineTickOrigin.ScrubPreview); // default MaxTick is 1; the clamp is not under test

        Assert.Equal(1, controller.Tick);
        Assert.Equal((1L, TimelineTickOrigin.ScrubPreview), applied.Single());
        Assert.Equal(0, tickChanged);
    }

    [Fact]
    public void ScrubCommit_PublishesTickChanged()
    {
        var controller = new PlanetTimelineController((_, _) => { });
        long? publishedTick = null;
        controller.TickChanged += tick => publishedTick = tick;

        controller.PushTick(1, TimelineTickOrigin.ScrubCommit); // default MaxTick is 1

        Assert.Equal(1, publishedTick);
    }
}
