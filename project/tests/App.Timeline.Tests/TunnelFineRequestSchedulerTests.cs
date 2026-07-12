using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World;
using Xunit;

namespace FantaSim.App.Timeline.Tests;

public sealed class TunnelFineRequestSchedulerTests
{
    [Fact]
    public void Consumer_key_is_derived_losslessly_from_the_revision_bearing_request()
    {
        var request = new LayerFilmstripPreviewRequest(
            SphereId: "geosphere",
            LayerId: "geosphere.crust",
            Tick: 123_456L,
            ViewRung: "ka",
            GraphRevision: 17,
            Width: 96,
            Height: 48);

        var key = TunnelFineRequestKey.From(
            request,
            mountGeneration: 9,
            fineEpoch: 11L,
            bucket: 13L);

        Assert.Equal(request.SphereId, key.SphereId);
        Assert.Equal(request.LayerId, key.LayerId);
        Assert.Equal(request.Tick, key.SampleTick);
        Assert.Equal(request.ViewRung, key.ViewRung);
        Assert.Equal(request.GraphRevision, key.GraphRevision);
        Assert.Equal(9, key.MountGeneration);
        Assert.Equal(11L, key.Epoch);
        Assert.Equal(13L, key.Bucket);
    }

    [Fact]
    public void Consumer_key_rejects_dimensions_the_scheduler_cannot_preserve()
    {
        var request = new LayerFilmstripPreviewRequest(
            "geosphere",
            "geosphere.crust",
            Tick: 123L,
            ViewRung: "ka",
            GraphRevision: 7,
            Width: TimelineFilmstrip.ThumbnailWidth + 1,
            Height: TimelineFilmstrip.ThumbnailHeight);

        Assert.Throws<ArgumentOutOfRangeException>(() => TunnelFineRequestKey.From(
            request,
            mountGeneration: 1,
            fineEpoch: 2L,
            bucket: 3L));
    }

    [Fact]
    public void Latest_wins_without_overlap_and_respects_minimum_start_interval()
    {
        var scheduler = new TunnelFineRequestScheduler();
        var first = Key(bucket: 1, epoch: 1);
        var second = Key(bucket: 2, epoch: 1);
        var third = Key(bucket: 3, epoch: 1);

        var start = scheduler.Offer(first, nowMs: 0);
        var firstToken = scheduler.ActiveToken;
        Assert.Equal(TunnelFineScheduleAction.Start, start.Action);
        Assert.Equal(first, start.Key);
        Assert.Equal(TunnelFineScheduleAction.None, scheduler.Offer(first, 1).Action);

        Assert.Equal(TunnelFineScheduleAction.CancelActive, scheduler.Offer(second, 2).Action);
        Assert.True(firstToken.IsCancellationRequested);
        Assert.Equal(TunnelFineScheduleAction.CancelActive, scheduler.Offer(third, 3).Action);

        var early = scheduler.ActiveFinished(first, nowMs: 4);
        Assert.Equal(TunnelFineScheduleAction.Wait, early.Action);
        Assert.Equal(100L, early.DueAtMs);
        Assert.Equal(TunnelFineScheduleAction.Wait, scheduler.TakeDue(99).Action);

        var newest = scheduler.TakeDue(100);
        Assert.Equal(TunnelFineScheduleAction.Start, newest.Action);
        Assert.Equal(third, newest.Key);
    }

    [Fact]
    public void Reset_cancels_active_keeps_it_until_unwind_and_drops_pending()
    {
        var scheduler = new TunnelFineRequestScheduler();
        var first = Key(bucket: 1, epoch: 1);
        var pending = Key(bucket: 2, epoch: 1);
        scheduler.Offer(first, 0);
        var token = scheduler.ActiveToken;
        scheduler.Offer(pending, 1);

        Assert.True(scheduler.Reset());
        Assert.True(token.IsCancellationRequested);
        Assert.True(scheduler.ActiveToken.CanBeCanceled);
        Assert.Equal(TunnelFineScheduleAction.None, scheduler.TakeDue(100).Action);
        Assert.Equal(TunnelFineScheduleAction.None, scheduler.ActiveFinished(Key(1, epoch: 0), 100).Action);
        Assert.True(scheduler.ActiveToken.CanBeCanceled);
        Assert.Equal(TunnelFineScheduleAction.None, scheduler.ActiveFinished(first, 100).Action);
        Assert.False(scheduler.ActiveToken.CanBeCanceled);
    }

    [Fact]
    public async Task Blocking_provider_unwinds_before_only_newest_pending_starts()
    {
        var scheduler = new TunnelFineRequestScheduler();
        var stats = new ProviderStats();
        var first = Key(bucket: 1, epoch: 1);
        var skipped = Key(bucket: 2, epoch: 1);
        var newest = Key(bucket: 3, epoch: 2);
        var firstStarted = scheduler.Offer(first, 0);
        Assert.Equal(TunnelFineScheduleAction.Start, firstStarted.Action);

        var firstEntered = NewSignal();
        var firstCancelled = NewSignal();
        var staleProvider = RunUntilCancelled(
            scheduler.ActiveToken,
            stats,
            firstEntered,
            firstCancelled);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(TunnelFineScheduleAction.CancelActive, scheduler.Offer(skipped, 1).Action);
        Assert.Equal(TunnelFineScheduleAction.CancelActive, scheduler.Offer(newest, 2).Action);
        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await staleProvider.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, stats.Active);
        if (TunnelFineCompletionPolicy.CanApply(
                first,
                expected: newest,
                cancellationRequested: true,
                capturedLaneEpoch: 1,
                currentLaneEpoch: 1))
            stats.ApplyToSink();
        Assert.Equal(0, stats.SinkApplications);

        var startNewest = scheduler.ActiveFinished(first, 100);
        Assert.Equal(TunnelFineScheduleAction.Start, startNewest.Action);
        Assert.Equal(newest, startNewest.Key);

        var newestEntered = NewSignal();
        var releaseNewest = NewSignal();
        var newestProvider = RunUntilReleased(
            scheduler.ActiveToken,
            stats,
            newestEntered,
            releaseNewest.Task);
        await newestEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseNewest.SetResult();
        await newestProvider.WaitAsync(TimeSpan.FromSeconds(5));
        if (TunnelFineCompletionPolicy.CanApply(
                newest,
                expected: newest,
                cancellationRequested: false,
                capturedLaneEpoch: 1,
                currentLaneEpoch: 1))
            stats.ApplyToSink();
        var completeNewest = scheduler.ActiveFinished(newest, 200);

        Assert.Equal(TunnelFineScheduleAction.None, completeNewest.Action);
        Assert.Equal(1, stats.MaxActive);
        Assert.Equal(1, stats.SinkApplications);
        Assert.False(scheduler.ActiveToken.CanBeCanceled);
    }

    [Fact]
    public async Task Reset_waits_for_blocking_provider_unwind_before_clearing_active_token()
    {
        var scheduler = new TunnelFineRequestScheduler();
        var stats = new ProviderStats();
        var oldEpoch = Key(bucket: 8, epoch: 4);
        scheduler.Offer(oldEpoch, 0);
        var entered = NewSignal();
        var cancelled = NewSignal();
        var provider = RunUntilCancelled(scheduler.ActiveToken, stats, entered, cancelled);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(scheduler.Reset());
        Assert.True(scheduler.ActiveToken.IsCancellationRequested);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await provider.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, stats.Active);
        Assert.True(scheduler.ActiveToken.CanBeCanceled);

        scheduler.ActiveFinished(oldEpoch, 100);

        Assert.False(scheduler.ActiveToken.CanBeCanceled);
        Assert.Equal(1, stats.MaxActive);
    }

    [Fact]
    public void Stale_completion_cannot_clear_newer_active_key()
    {
        var scheduler = new TunnelFineRequestScheduler();
        var old = Key(bucket: 1, epoch: 1);
        var current = Key(bucket: 2, epoch: 2);
        scheduler.Offer(old, 0);
        scheduler.Offer(current, 1);
        scheduler.ActiveFinished(old, 100);

        Assert.Equal(TunnelFineScheduleAction.None, scheduler.ActiveFinished(old, 101).Action);
        Assert.True(scheduler.ActiveToken.CanBeCanceled);
        Assert.False(scheduler.ActiveToken.IsCancellationRequested);
        Assert.Equal(TunnelFineScheduleAction.None, scheduler.Offer(current, 101).Action);
    }

    [Fact]
    public void Abandoning_exact_completion_drops_pending_without_creating_unstarted_active_key()
    {
        var scheduler = new TunnelFineRequestScheduler();
        var active = Key(bucket: 1, epoch: 1);
        var pending = Key(bucket: 2, epoch: 2);
        scheduler.Offer(active, 0);
        scheduler.Offer(pending, 1);

        Assert.True(scheduler.AbandonFinished(active));
        Assert.False(scheduler.ActiveToken.CanBeCanceled);
        Assert.Equal(TunnelFineScheduleAction.None, scheduler.TakeDue(100).Action);
        Assert.False(scheduler.AbandonFinished(active));

        var next = scheduler.Offer(Key(bucket: 3, epoch: 3), 100);
        Assert.Equal(TunnelFineScheduleAction.Start, next.Action);
    }

    [Fact]
    public void Ten_starts_span_at_least_nine_hundred_milliseconds()
    {
        var scheduler = new TunnelFineRequestScheduler();
        long firstStart = -1;
        long lastStart = -1;

        for (var i = 0; i < 10; i++)
        {
            var now = i * TunnelFineRequestScheduler.MinimumStartIntervalMs;
            var key = Key(bucket: i, epoch: 1);
            var decision = scheduler.Offer(key, now);
            Assert.Equal(TunnelFineScheduleAction.Start, decision.Action);
            firstStart = firstStart < 0 ? now : firstStart;
            lastStart = now;
            scheduler.ActiveFinished(key, now);
        }

        Assert.True(lastStart - firstStart >= 900L);
    }

    [Fact]
    public void Old_epoch_key_is_distinct_and_cancels_instead_of_aliasing()
    {
        var scheduler = new TunnelFineRequestScheduler();
        scheduler.Offer(Key(bucket: 5, epoch: 1), 0);

        var decision = scheduler.Offer(Key(bucket: 5, epoch: 2), 1);

        Assert.Equal(TunnelFineScheduleAction.CancelActive, decision.Action);
    }

    private static TunnelFineRequestKey Key(long bucket, long epoch)
        => new(
            SphereId: "geosphere",
            LayerId: "geosphere.crust",
            SampleTick: 100 + bucket,
            Bucket: bucket,
            ViewRung: "ka",
            GraphRevision: 7,
            MountGeneration: 3,
            Epoch: epoch);

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task RunUntilCancelled(
        CancellationToken token,
        ProviderStats stats,
        TaskCompletionSource entered,
        TaskCompletionSource cancelled)
    {
        stats.Enter();
        entered.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            cancelled.TrySetResult();
        }
        finally
        {
            stats.Exit();
        }
    }

    private static async Task RunUntilReleased(
        CancellationToken token,
        ProviderStats stats,
        TaskCompletionSource entered,
        Task released)
    {
        stats.Enter();
        entered.TrySetResult();
        try
        {
            await released.WaitAsync(token);
        }
        finally
        {
            stats.Exit();
        }
    }

    private sealed class ProviderStats
    {
        private int _active;
        private int _maxActive;
        private int _sinkApplications;

        internal int Active => Volatile.Read(ref _active);
        internal int MaxActive => Volatile.Read(ref _maxActive);
        internal int SinkApplications => Volatile.Read(ref _sinkApplications);

        internal void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            int observed;
            do
            {
                observed = Volatile.Read(ref _maxActive);
                if (observed >= active)
                    break;
            }
            while (Interlocked.CompareExchange(ref _maxActive, active, observed) != observed);
        }

        internal void Exit() => Interlocked.Decrement(ref _active);
        internal void ApplyToSink() => Interlocked.Increment(ref _sinkApplications);
    }
}
