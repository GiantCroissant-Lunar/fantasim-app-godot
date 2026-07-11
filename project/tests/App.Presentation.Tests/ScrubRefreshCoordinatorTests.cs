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
    // Rung ladder (vault/plans/2026-07-11-d8b-progressive-resolution-slice1-plan.md): LowRung=2
    // follows the hand, MidRung=3 is the first climb step, Full=null resolves to the configured
    // default. These mirror Service.ResolveFilmstripFrequency's 2/3 ladder.

    private static ScrubRefreshCoordinator Make(
        out List<int?> log, long restDelayMs = 300L, Func<long>? nowMs = null)
    {
        var events = new List<int?>();
        log = events;
        long clock = 0;
        return new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(restDelayMs),
            requestRefresh: freq => events.Add(freq),
            deferToMainThread: a => a(),
            nowMs: nowMs ?? (() => clock),
            delayAsync: (_, ct) => Task.CompletedTask);
    }

    // (a) boundary-crossing preview -> immediate requestRefresh(2) exactly once, no full refresh.
    [Fact]
    public void PreviewWithHeavyRequestsLowRungImmediately()
    {
        var sut = Make(out var log);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        Assert.Single(log);
        Assert.Equal(2, log[0]);
    }

    // (b) rest after previews -> climb sequence [3, null] in order, nothing more.
    [Fact]
    public void RestAfterPreviewsClimbsToMidThenFull()
    {
        var events = new List<int?>();
        var deferred = new List<Action>();
        long clock = 0;
        var sut = new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(restDelayMs: 300L),
            requestRefresh: freq => events.Add(freq),
            deferToMainThread: deferred.Add,
            nowMs: () => clock,
            delayAsync: (_, ct) => Task.CompletedTask);

        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        events.Clear();
        clock = 1000;
        DrainDeferred(deferred);

        Assert.Equal(new int?[] { 3, null }, events);
    }

    // (c) new preview mid-climb cancels the remaining climb steps.
    [Fact]
    public void NewPreviewMidClimbCancelsRemainingClimb()
    {
        var events = new List<int?>();
        var deferred = new List<Action>();
        long clock = 0;
        var sut = new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(restDelayMs: 300L),
            requestRefresh: freq => events.Add(freq),
            deferToMainThread: deferred.Add,
            nowMs: () => clock,
            delayAsync: (_, ct) => Task.CompletedTask);

        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        events.Clear();
        clock = 1000;
        // Run StartClimbIfDue (rest-delay deferred): MidRung fires AT the rest flush (lead fix
        // 2026-07-11 — plan wording: "invoke requestRefresh(MidRung), THEN after each subsequent
        // restDelayMs interval the next higher rung"). The Full callback is enqueued by
        // ClimbAsync but NOT yet run — it is the step we want to cancel.
        RunN(deferred, 1);
        Assert.Equal(new int?[] { 3 }, events);
        events.Clear();

        // A new preview arrives before the full step runs — it should cancel the climb.
        sut.HandleTick(200, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        // The immediate low-rung request fires.
        Assert.Equal(new int?[] { 2 }, events);
        events.Clear();

        // Running any remaining deferred actions from the old climb should produce nothing.
        DrainDeferred(deferred);
        Assert.Empty(events);
    }

    // (d) commit after previews -> exactly one requestRefresh(null), climb never runs.
    [Fact]
    public void CommitAfterPreviewsRequestsFullOnly()
    {
        var sut = Make(out var log);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        log.Clear();
        sut.HandleTick(150, TimelineTickOrigin.ScrubCommit, heavyRefreshRequested: true);
        Assert.Single(log);
        Assert.Null(log[0]);
    }

    // (e) standard with heavy -> exactly one requestRefresh(null) (unchanged semantics).
    [Fact]
    public void StandardWithHeavyRequestsFullOnly()
    {
        var sut = Make(out var log);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        log.Clear();
        sut.HandleTick(150, TimelineTickOrigin.Standard, heavyRefreshRequested: false);
        Assert.Empty(log);
        sut.HandleTick(160, TimelineTickOrigin.Standard, heavyRefreshRequested: true);
        Assert.Single(log);
        Assert.Null(log[0]);
    }

    // (f) dispose mid-climb -> no further callbacks.
    [Fact]
    public async Task DisposeMidClimbSuppressesFurtherCallbacks()
    {
        var tcs = new TaskCompletionSource();
        var events = new List<int?>();
        var sut = new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(300L),
            requestRefresh: freq => events.Add(freq),
            deferToMainThread: a => a(),
            nowMs: () => 0L,
            delayAsync: (_, ct) => tcs.Task);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        // Immediate low-rung request fires synchronously
        Assert.Equal(new int?[] { 2 }, events);
        events.Clear();
        sut.Dispose();
        tcs.SetResult();
        await Task.Yield();
        Assert.Empty(events);
    }

    // Regression pin for the refresh-echo feedback loop (lead review 2026-07-11): in production,
    // every refresh's document apply echoes a Standard/no-heavy tick at the same playhead
    // (RefreshPresentationForRegime -> UpdateFrom -> PushTick). The binder SUPPRESSES that echo
    // before it reaches HandleTick (_applyingRefreshedDocument gate) — if it ever leaked through,
    // the echo's Cancel() would wipe the pending rest after each preview's low-rung refresh and
    // the climb would never start. This test documents the failure shape the gate prevents: an
    // echo-shaped re-entrant callback DOES strand the ladder at the low rung.
    [Fact]
    public void UnsuppressedRefreshEchoWouldStrandLadderAtLowRung()
    {
        var events = new List<int?>();
        var deferred = new List<Action>();
        long clock = 0;
        ScrubRefreshCoordinator sut = null!;
        sut = new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(restDelayMs: 300L),
            requestRefresh: freq =>
            {
                events.Add(freq);
                // Echo: the refresh apply re-enters as a Standard/no-heavy tick at the same playhead.
                sut.HandleTick(100, TimelineTickOrigin.Standard, heavyRefreshRequested: false);
            },
            deferToMainThread: deferred.Add,
            nowMs: () => clock,
            delayAsync: (_, ct) => Task.CompletedTask);

        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        Assert.Equal(new int?[] { 2 }, events);

        clock = 1000;
        DrainDeferred(deferred);
        // Each step's echo kills what follows it: the Mid step's echo cancels the climb, so the
        // FULL refresh (null) never arrives — the ladder strands below full resolution. The
        // binder's _applyingRefreshedDocument gate is load-bearing. (Also pins that re-entrant
        // cancellation inside the Mid callback must not crash — the climb token is captured
        // before the callback runs.)
        Assert.DoesNotContain(null, events);
    }

    // Drains the deferred-action queue, handling new actions enqueued during drain (the climb
    // sequence adds the next step via _deferToMainThread while the current step runs).
    private static void DrainDeferred(List<Action> deferred)
    {
        while (deferred.Count > 0)
        {
            var action = deferred[0];
            deferred.RemoveAt(0);
            action();
        }
    }

    // Runs at most N deferred actions, handling new actions enqueued during execution.
    private static void RunN(List<Action> deferred, int count)
    {
        for (int i = 0; i < count && deferred.Count > 0; i++)
        {
            var action = deferred[0];
            deferred.RemoveAt(0);
            action();
        }
    }
}