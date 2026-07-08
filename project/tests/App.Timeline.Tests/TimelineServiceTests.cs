using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.Timeline.Services;
using FantaSim.App.World.Composition;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Timeline.Tests;

public class TimelineServiceTests
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
    }

    private static (Service svc, FakeFace face) Build(long maxTick = 120_000_000)
    {
        var face = new FakeFace();
        var geo = TimelineTestSchedules.Geosphere();
        var atmo = TimelineTestSchedules.Atmosphere();
        // The T3 service holds a controller reference for schedule lookups but drives the
        // face directly. We use a minimal fake controller shape via a stub is not needed -
        // the Service takes ITimelineController for read-only schedule access.
        // For the test we construct a real TimelineController stub is hard; instead the
        // Service takes schedules directly (see Service ctor in step 4).
        var svc = new Service(face, geo, atmo, maxTick, NullLoggerFactory.Instance);
        return (svc, face);
    }

    [Fact]
    public async Task Play_TransitionsToPlaying_AndCallsFacePlay()
    {
        var (svc, face) = Build();
        Assert.Equal(TimelinePlaybackState.Idle, svc.State);
        await svc.PlayAsync();
        Assert.Equal(TimelinePlaybackState.Playing, svc.State);
        Assert.Equal(1, face.PlayCalls);
        Assert.True(face.ApplyViewCalls >= 1);
    }

    [Fact]
    public async Task Pause_TransitionsToIdle_AndCallsFacePause()
    {
        var (svc, face) = Build();
        await svc.PlayAsync();
        await svc.PauseAsync();
        Assert.Equal(TimelinePlaybackState.Idle, svc.State);
        Assert.Equal(1, face.PauseCalls);
    }

    [Fact]
    public async Task Seek_ClampsToMaxTick_AndCallsFaceSeek()
    {
        var (svc, face) = Build(maxTick: 1_000_000);
        await svc.SeekAsync(5_000_000);
        Assert.Equal(1_000_000, svc.Tick);
        Assert.Equal(1, face.SeekCalls);
        Assert.Equal(1_000_000, face.LastSeekTick);
    }

    [Fact]
    public async Task Seek_NegativeClampsToZero()
    {
        var (svc, face) = Build();
        await svc.SeekAsync(-100);
        Assert.Equal(0, svc.Tick);
        Assert.Equal(0, face.LastSeekTick);
    }

    [Fact]
    public async Task ViewChanged_RaisedOnStateChange()
    {
        var (svc, face) = Build();
        TimelineViewSnapshot? captured = null;
        svc.ViewChanged += snap => captured = snap;
        await svc.PlayAsync();
        Assert.NotNull(captured);
        Assert.Equal(TimelinePlaybackState.Playing, captured!.State);
    }
}
