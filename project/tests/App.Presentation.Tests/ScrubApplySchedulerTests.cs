using FantaSim.App.Presentation;
using Xunit;

namespace App.Presentation.Tests;

public sealed class ScrubApplySchedulerTests
{
    [Fact]
    public void PreviewWithoutHeavyRequest_DoesNotSchedule()
    {
        var scheduler = new ScrubApplyScheduler(restDelayMs: 300);

        var schedule = scheduler.RecordPreview(tick: 10, heavyRequested: false, nowMs: 1_000);

        Assert.Null(schedule);
        Assert.False(scheduler.HasPending);
    }

    [Fact]
    public void PreviewWithHeavyRequest_SchedulesAfterRestDelay()
    {
        var scheduler = new ScrubApplyScheduler(restDelayMs: 300);

        var schedule = scheduler.RecordPreview(tick: 10, heavyRequested: true, nowMs: 1_000);

        Assert.NotNull(schedule);
        Assert.Equal(1_300, schedule!.Value.DueAtMs);
        Assert.True(scheduler.HasPending);
        Assert.Null(scheduler.ConsumeDue(schedule.Value.Generation, nowMs: 1_299));
        Assert.Equal(10, scheduler.ConsumeDue(schedule.Value.Generation, nowMs: 1_300));
        Assert.False(scheduler.HasPending);
    }

    [Fact]
    public void LaterPreviewResetsDueTime_AndInvalidatesEarlierGeneration()
    {
        var scheduler = new ScrubApplyScheduler(restDelayMs: 300);

        var first = scheduler.RecordPreview(tick: 10, heavyRequested: true, nowMs: 1_000)!.Value;
        var second = scheduler.RecordPreview(tick: 20, heavyRequested: false, nowMs: 1_100)!.Value;

        Assert.Null(scheduler.ConsumeDue(first.Generation, nowMs: 1_400));
        Assert.Null(scheduler.ConsumeDue(second.Generation, nowMs: 1_399));
        Assert.Equal(20, scheduler.ConsumeDue(second.Generation, nowMs: 1_400));
    }

    [Fact]
    public void CommitFlushesPendingHeavyRefreshAtFinalTick()
    {
        var scheduler = new ScrubApplyScheduler(restDelayMs: 300);
        scheduler.RecordPreview(tick: 10, heavyRequested: true, nowMs: 1_000);

        var tick = scheduler.ConsumeCommit(42);

        Assert.Equal(42, tick);
        Assert.False(scheduler.HasPending);
    }
}
