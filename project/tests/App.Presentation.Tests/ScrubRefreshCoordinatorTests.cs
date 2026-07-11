using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public sealed class ScrubRefreshCoordinatorTests
{
    private static ScrubRefreshCoordinator Make(
        out List<string> log, long restDelayMs = 300L, Func<long>? nowMs = null)
    {
        var events = new List<string>();
        log = events;
        long clock = 0;
        return new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(restDelayMs),
            requestHeavyRefresh: () => events.Add("heavy"),
            deferToMainThread: a => a(),                       // synchronous in tests
            nowMs: nowMs ?? (() => clock),
            delayAsync: (_, ct) => Task.CompletedTask);        // rest delay elapses instantly
    }

    // Deviation from the plan's draft (see AGENT-SUMMARY.md Task 3): the plan's version of this
    // test reused Make() (synchronous deferToMainThread + already-completed delayAsync + a FIXED
    // nowMs clock) and asserted the result after a single `await Task.Yield()`. Traced against the
    // real ScrubApplyScheduler contract (ScrubApplySchedulerTests.LaterPreviewResetsDueTime_...)
    // that combination cannot produce "exactly one heavy": with delayAsync completing synchronously,
    // await never suspends, so each HandleTick's deferred flush-check runs to completion (including
    // deferToMainThread) BEFORE the next HandleTick call — there is no window in which the second
    // preview's generation bump can supersede the first's flush check, so with a fixed clock neither
    // check is ever "due" (0 events), and with an advancing clock BOTH checks independently fire (2
    // events). Rewritten to make deferToMainThread queue instead of run inline — matching what
    // Callable.From(...).CallDeferred() actually does in production (defers to a LATER frame, not
    // immediately) — so both HandleTick calls land before either deferred flush-check runs; the
    // stale (generation 1) check is then correctly rejected by ScrubApplyScheduler.ConsumeDue and
    // only the live (generation 2) check flushes. This is a closer characterization of the real
    // debounce behavior than the plan's draft, not a loosened assertion (still exactly one "heavy").
    [Fact]
    public void PreviewBurstThenRestRequestsExactlyOneHeavyRefresh()
    {
        var events = new List<string>();
        var deferred = new List<Action>();
        long clock = 0;
        var sut = new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(restDelayMs: 300L),
            requestHeavyRefresh: () => events.Add("heavy"),
            deferToMainThread: deferred.Add,
            nowMs: () => clock,
            delayAsync: (_, ct) => Task.CompletedTask);

        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        sut.HandleTick(200, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);

        clock = 1000; // advance past the 300ms rest window before the deferred checks run
        foreach (var action in deferred)
            action();

        Assert.Single(events, "heavy");
    }

    [Fact]
    public void CommitFlushesPendingRestAndHonorsHeavyRequest()
    {
        var sut = Make(out var log);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        sut.HandleTick(150, TimelineTickOrigin.ScrubCommit, heavyRefreshRequested: true);
        // one from the flushed rest refresh + one from the commit's own heavy request —
        // dedup is the binder's _regimeRefreshPending job, not the coordinator's.
        Assert.Equal(2, log.Count(e => e == "heavy"));
    }

    [Fact]
    public void StandardOriginCancelsPendingRestAndOnlyHonorsExplicitHeavy()
    {
        var sut = Make(out var log);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        sut.HandleTick(150, TimelineTickOrigin.Standard, heavyRefreshRequested: false);
        Assert.Empty(log);
        sut.HandleTick(160, TimelineTickOrigin.Standard, heavyRefreshRequested: true);
        Assert.Single(log, "heavy");
    }

    [Fact]
    public async Task DisposeSuppressesLateFlush()
    {
        var tcs = new TaskCompletionSource();
        var events = new List<string>();
        var sut = new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(300L),
            () => events.Add("heavy"),
            a => a(),
            () => 0L,
            (_, ct) => tcs.Task);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        sut.Dispose();
        tcs.SetResult();
        await Task.Yield();
        Assert.Empty(events);
    }
}
