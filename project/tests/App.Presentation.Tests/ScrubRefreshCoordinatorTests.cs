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

    // (e) standard with heavy -> exactly one requestRefresh(null); standard WITHOUT heavy is a
    // scrub-state no-op (it fires nothing here; its no-op-ness is pinned by the dedicated tests
    // below — every refresh apply echoes such a tick, see the lead-fix comment in HandleTick).
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

    // Standard/no-heavy ticks must NOT wipe scrub state: the pending rest survives and the climb
    // still runs. In production every refresh's document apply echoes exactly such a tick
    // (face SeekTo echo, PlanetTimelineController.UpdateFrom -> PushTick) — the 2026-07-11 D8b
    // gate proved the old cancel-on-standard wiped the rest after each preview's low-rung
    // refresh, stranding the planet below full resolution.
    [Fact]
    public void StandardNoHeavyPreservesPendingRestAndClimb()
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
        Assert.Equal(new int?[] { 2 }, events);
        events.Clear();

        sut.HandleTick(100, TimelineTickOrigin.Standard, heavyRefreshRequested: false);

        clock = 1000;
        DrainDeferred(deferred);
        Assert.Equal(new int?[] { 3, null }, events);
    }

    // IsScrubActive gates the binder's generation-changed subscription: true from first pending
    // preview through the end of the climb; false once the climb completes or a heavy standard
    // tick supersedes the scrub.
    [Fact]
    public void IsScrubActiveTracksRestAndClimbLifecycle()
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

        Assert.False(sut.IsScrubActive);

        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        Assert.True(sut.IsScrubActive);

        clock = 1000;
        DrainDeferred(deferred);
        Assert.False(sut.IsScrubActive); // climb completed naturally

        sut.HandleTick(200, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        Assert.True(sut.IsScrubActive);
        sut.HandleTick(210, TimelineTickOrigin.Standard, heavyRefreshRequested: true);
        Assert.False(sut.IsScrubActive); // heavy standard supersedes the scrub
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
    // (face SeekTo echo, RefreshPresentationForRegime -> UpdateFrom -> PushTick). With the
    // no-op-on-benign-standard policy the echo is harmless AT the coordinator: the ladder runs
    // to completion even when every refresh re-enters HandleTick. (Also pins that re-entrant
    // calls inside a rung callback must not crash — the climb token is captured before the
    // callback runs; an earlier draft threw ObjectDisposedException here.)
    [Fact]
    public void RefreshEchoDoesNotStrandLadder()
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
        events.Clear();

        clock = 1000;
        DrainDeferred(deferred);
        // The climb completes despite every step's echo: Mid then FULL both arrive.
        Assert.Equal(new int?[] { 3, null }, events);
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