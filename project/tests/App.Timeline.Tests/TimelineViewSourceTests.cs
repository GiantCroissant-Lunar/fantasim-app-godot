using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.Timeline;
using BoomHud.Abstractions.Runtime;
using Xunit;

namespace App.Timeline.Tests;

public class TimelineViewSourceTests
{
    private static TimelineViewSource Make(long tick)
    {
        var ctl = new FakeController(tick);
        return new TimelineViewSource(ctl);
    }

    [Fact]
    public void Document_HasPlayPause_AndRegimeBands_AndTracks()
    {
        var doc = Make(500_000).BuildDocument();
        Assert.Equal("timeline", doc.SurfaceId);
        var ids = Flatten(doc.Root).Select(n => n.Id).ToList();
        Assert.Contains("btn-playpause", ids);
        Assert.Contains("band-geosphere-magma-ocean", ids);   // a band panel per geosphere regime
        Assert.Contains("track-geosphere-geosphere.magma-ocean", ids); // a track row per layer
    }

    [Fact]
    public void PlayPauseButton_DispatchesToController()
    {
        var ctl = new FakeController(0);
        var vs = new TimelineViewSource(ctl);
        vs.Dispatch("timeline.play", "btn-playpause");
        Assert.True(ctl.Played);
        vs.Dispatch("timeline.seek:100000000", "band-geosphere-mobile-plate");
        Assert.Equal(100_000_000, ctl.SeekedTo);
    }

    private static System.Collections.Generic.IEnumerable<RuntimeComponentNode> Flatten(RuntimeComponentNode n)
    {
        yield return n;
        foreach (var c in n.Children) foreach (var d in Flatten(c)) yield return d;
    }

    private sealed class FakeController : ITimelineController
    {
        public FakeController(long t) { Tick = t; }
        public long Tick { get; } public long MaxTick => 120_000_000; public bool IsPlaying => false;
        public SphereRegimeSchedule GeosphereSchedule => SphereRegimeScheduleDefaults.GeosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick);
        public SphereRegimeSchedule AtmosphereSchedule => SphereRegimeScheduleDefaults.AtmosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick);
        public bool Played; public long SeekedTo = -1;
        public void Play() => Played = true; public void Pause() { } public void SeekTo(long t) => SeekedTo = t;
        public event System.Action<long>? TickChanged;
    }
}
