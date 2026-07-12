using System;
using System.Threading;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Timeline.Tests;

public sealed class TimelineHudReplayTests
{
    [Fact]
    public void ContextCarriesPreBindHiddenStateAndKeepsLatestEpoch()
    {
        var context = NewContext(new TimelineHudState(Visible: false, ModeEpoch: 3L));

        Assert.Equal(new TimelineHudState(false, 3L), context.DesiredHudState);

        context.SetDesiredHudState(new TimelineHudState(true, 4L));

        Assert.Equal(new TimelineHudState(true, 4L), context.DesiredHudState);
    }

    [Theory]
    [InlineData(2L, 2L, 7L, 6L, true)]
    [InlineData(2L, 2L, 5L, 6L, false)]
    [InlineData(1L, 2L, 99L, 6L, false)]
    public void ReplayPolicyRejectsStaleEpochsAndPriorBindGenerations(
        int capturedBindGeneration,
        int currentBindGeneration,
        long incomingModeEpoch,
        long currentModeEpoch,
        bool expected)
    {
        Assert.Equal(expected, TimelineHudReplayPolicy.CanApply(
            capturedBindGeneration,
            currentBindGeneration,
            incomingModeEpoch,
            currentModeEpoch));
    }

    [Fact]
    public void NewBindGenerationAcceptsResetEpochWhileOldDeferredWorkIsRejected()
    {
        Assert.True(TimelineHudReplayPolicy.CanApply(
            capturedBindGeneration: 8,
            currentBindGeneration: 8,
            incomingModeEpoch: 0L,
            currentModeEpoch: -1L));
        Assert.False(TimelineHudReplayPolicy.CanApply(
            capturedBindGeneration: 7,
            currentBindGeneration: 8,
            incomingModeEpoch: 99L,
            currentModeEpoch: -1L));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void ForcedResidentSafetyRejectsOnlyHiddenHudWrites(
        bool incomingVisible,
        bool forceHudVisible,
        bool expected)
    {
        Assert.Equal(expected, TimelineHudReplayPolicy.CanApply(
            capturedBindGeneration: 8,
            currentBindGeneration: 8,
            incomingModeEpoch: 9L,
            currentModeEpoch: 8L,
            incomingVisible,
            forceHudVisible));
    }

    private static TimelineFaceContext NewContext(TimelineHudState desiredHudState)
        => new(
            controller: new FakeTimelineController(),
            proxy: new FakeFaceProxy(),
            commandClient: null,
            generationGraphFamilyProvider: _ => null,
            filmstripGraphRevisionProvider: () => 0,
            filmstripPreviewProvider: (_, _) => null,
            layerTrackRegistry: null,
            loggerFactory: NullLoggerFactory.Instance,
            ticksPerSecond: 5_000_000.0,
            desiredHudState: desiredHudState);

    private sealed class FakeFaceProxy : ITimelineFaceProxy
    {
        public bool IsCrossBound { get; private set; }
        public void RebindResidentContext() { }
        public void BindCrossTarget(ITimelineFace target) => IsCrossBound = true;
        public void UnbindCrossTarget() => IsCrossBound = false;
        public void Play() { }
        public void Pause() { }
        public void SeekTo(long tick) { }
        public void ApplyView(TimelineViewSnapshot snapshot) { }
        public void ApplyHudState(TimelineHudState state) { }
    }

    private sealed class FakeTimelineController : ITimelineController
    {
        public long Tick => 0L;
        public long MaxTick => 1L;
        public bool IsPlaying => false;
        public SphereRegimeSchedule GeosphereSchedule { get; } = TimelineTestSchedules.Geosphere();
        public SphereRegimeSchedule AtmosphereSchedule { get; } = TimelineTestSchedules.Atmosphere();
        public TimelineLayerSelection? SelectedLayer => null;
        public System.Collections.Generic.IReadOnlyList<TimelineLayerSelection> ActiveLayers
            => Array.Empty<TimelineLayerSelection>();
        public event Action<long>? TickChanged;
        public event Action<TimelineLayerSelection?>? LayerSelectionChanged;
        public void Play() { }
        public void Pause() { }
        public void SeekTo(long tick) { }
        public void SelectLayer(string sphereId, string layerId) => LayerSelectionChanged?.Invoke(null);
        public void ToggleLayer(string sphereId, string layerId) { }
        public void PushTick(long tick) => TickChanged?.Invoke(tick);
        public void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying) { }
        public void UnregisterPlayback() { }
    }
}
