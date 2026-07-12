using System.Threading.Tasks;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.Timeline.Services;
using FantaSim.App.World.Composition;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless Play -> advance -> pause loop for the T3 timeline Service. Simulates the
/// AnimationPlayer driving InternalTick (which the face would push via PushTick ->
/// controller.TickChanged -> AcceptTickFromFace) by calling AcceptTickFromFace directly.
/// Pure C# -- no Godot types. Mirrors the FakeFace pattern in TimelineServiceTests.
/// </summary>
public class TimelinePlaybackFlowTests
{
    private sealed class FakeFace : ITimelineFace
    {
        public int PlayCalls, PauseCalls, SeekCalls, ApplyViewCalls;
        public long LastSeekTick;
        public TimelineViewSnapshot? LastSnapshot;

        public void RebindResidentContext() { }
        public void Play() => PlayCalls++;
        public void Pause() => PauseCalls++;
        public void SeekTo(long tick) { SeekCalls++; LastSeekTick = tick; }
        public void ApplyView(TimelineViewSnapshot snapshot) { ApplyViewCalls++; LastSnapshot = snapshot; }
        public bool HudVisible = true;
        public void ApplyHudState(TimelineHudState state) => HudVisible = state.Visible;
    }

    private static (Service svc, FakeFace face) Build(long maxTick = 120_000_000)
    {
        var face = new FakeFace();
        var geo = TimelineTestSchedules.Geosphere();
        var atmo = TimelineTestSchedules.Atmosphere();
        var svc = new Service(face, geo, atmo, maxTick, NullLoggerFactory.Instance);
        return (svc, face);
    }

    [Fact]
    public async Task Play_ThenAcceptTickFromFace_AdvancesTickMonotonically()
    {
        var (svc, face) = Build();
        Assert.Equal(TimelinePlaybackState.Idle, svc.State);
        Assert.Equal(0, svc.Tick);

        await svc.PlayAsync();
        Assert.Equal(TimelinePlaybackState.Playing, svc.State);
        Assert.Equal(1, face.PlayCalls);

        long prev = svc.Tick;
        for (long t = 1; t <= 10; t++)
        {
            svc.AcceptTickFromFace(t * 1_000_000);
            Assert.True(svc.Tick >= prev, $"tick regressed at step {t}: {svc.Tick} < {prev}");
            Assert.Equal(TimelinePlaybackState.Playing, svc.State);
            prev = svc.Tick;
        }
        Assert.Equal(10_000_000, svc.Tick);
        Assert.True(face.ApplyViewCalls >= 10);
    }

    [Fact]
    public async Task Pause_ThenAcceptTickFromFace_DoesNotAdvanceTick()
    {
        var (svc, face) = Build();
        await svc.PlayAsync();
        svc.AcceptTickFromFace(5_000_000);
        Assert.Equal(5_000_000, svc.Tick);

        await svc.PauseAsync();
        Assert.Equal(TimelinePlaybackState.Idle, svc.State);
        Assert.Equal(1, face.PauseCalls);

        long tickBefore = svc.Tick;
        svc.AcceptTickFromFace(50_000_000);
        Assert.Equal(tickBefore, svc.Tick);
        Assert.Equal(TimelinePlaybackState.Idle, svc.State);
    }

    [Fact]
    public async Task Play_NearMaxTick_ThenAcceptTickFromFacePastMax_ClampsToMaxTick()
    {
        const long maxTick = 10_000_000;
        var (svc, face) = Build(maxTick: maxTick);

        await svc.PlayAsync();
        Assert.Equal(TimelinePlaybackState.Playing, svc.State);

        svc.AcceptTickFromFace(maxTick - 1_000_000);
        Assert.Equal(maxTick - 1_000_000, svc.Tick);

        svc.AcceptTickFromFace(maxTick + 5_000_000);
        Assert.Equal(maxTick, svc.Tick);

        svc.AcceptTickFromFace(maxTick + 100_000_000);
        Assert.Equal(maxTick, svc.Tick);
    }

    [Fact]
    public async Task Seek_WhilePlaying_SetsStateToScrubbing_AndUpdatesTick()
    {
        var (svc, face) = Build();
        await svc.PlayAsync();
        Assert.Equal(TimelinePlaybackState.Playing, svc.State);

        await svc.SeekAsync(42_000_000);
        Assert.Equal(TimelinePlaybackState.Scrubbing, svc.State);
        Assert.Equal(42_000_000, svc.Tick);
        Assert.Equal(1, face.SeekCalls);
        Assert.Equal(42_000_000, face.LastSeekTick);

        // While in Scrubbing state, AcceptTickFromFace is guarded out (Playing-only).
        long tickBefore = svc.Tick;
        svc.AcceptTickFromFace(90_000_000);
        Assert.Equal(tickBefore, svc.Tick);
        Assert.Equal(TimelinePlaybackState.Scrubbing, svc.State);
    }

    [Fact]
    public async Task Play_AfterScrub_ResumesFromScrubbedTick()
    {
        var (svc, face) = Build();
        await svc.SeekAsync(30_000_000);
        Assert.Equal(TimelinePlaybackState.Scrubbing, svc.State);
        Assert.Equal(30_000_000, svc.Tick);

        await svc.PlayAsync();
        Assert.Equal(TimelinePlaybackState.Playing, svc.State);
        Assert.Equal(30_000_000, svc.Tick);

        svc.AcceptTickFromFace(31_000_000);
        Assert.Equal(31_000_000, svc.Tick);
        Assert.True(svc.Tick > 30_000_000);
    }

    [Fact]
    public async Task ViewChanged_RaisedOnEachAcceptedTickDuringPlay()
    {
        var (svc, face) = Build();
        var snaps = new System.Collections.Generic.List<TimelineViewSnapshot>();
        svc.ViewChanged += snap => snaps.Add(snap);

        await svc.PlayAsync();
        snaps.Clear();

        svc.AcceptTickFromFace(1_000_000);
        svc.AcceptTickFromFace(2_000_000);
        svc.AcceptTickFromFace(3_000_000);

        Assert.Equal(3, snaps.Count);
        Assert.Equal(1_000_000, snaps[0].Tick);
        Assert.Equal(2_000_000, snaps[1].Tick);
        Assert.Equal(3_000_000, snaps[2].Tick);
        Assert.All(snaps, s => Assert.Equal(TimelinePlaybackState.Playing, s.State));
    }
}
