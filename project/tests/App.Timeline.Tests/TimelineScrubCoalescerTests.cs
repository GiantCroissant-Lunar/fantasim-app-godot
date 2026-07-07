using FantaSim.App.Timeline;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Timeline.Tests;

public sealed class TimelineScrubCoalescerTests
{
    [Fact]
    public void Press_AppliesImmediately_AsScrubPreview()
    {
        var coalescer = new TimelineScrubCoalescer();

        var action = coalescer.Press(10);

        Assert.True(action.ShouldApply);
        Assert.Equal(10, action.Tick);
        Assert.Equal(TimelineTickOrigin.ScrubPreview, action.Origin);
        Assert.True(coalescer.IsDragging);
        Assert.Null(coalescer.PendingTick);
    }

    [Fact]
    public void Motion_WhileDragging_CoalescesLatestTickUntilFrame()
    {
        var coalescer = new TimelineScrubCoalescer();
        coalescer.Press(10);

        Assert.False(coalescer.Motion(20).ShouldApply);
        Assert.False(coalescer.Motion(30).ShouldApply);

        var action = coalescer.ConsumeFrame();
        Assert.True(action.ShouldApply);
        Assert.Equal(30, action.Tick);
        Assert.Equal(TimelineTickOrigin.ScrubPreview, action.Origin);
        Assert.Null(coalescer.PendingTick);
        Assert.False(coalescer.ConsumeFrame().ShouldApply);
    }

    [Fact]
    public void Release_DropsPendingMotion_AndCommitsFinalTick()
    {
        var coalescer = new TimelineScrubCoalescer();
        coalescer.Press(10);
        coalescer.Motion(20);
        coalescer.Motion(30);

        var action = coalescer.Release(40);

        Assert.True(action.ShouldApply);
        Assert.Equal(40, action.Tick);
        Assert.Equal(TimelineTickOrigin.ScrubCommit, action.Origin);
        Assert.False(coalescer.IsDragging);
        Assert.Null(coalescer.PendingTick);
        Assert.False(coalescer.ConsumeFrame().ShouldApply);
    }
}
