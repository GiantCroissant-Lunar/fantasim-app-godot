using System;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldGenerationTimelineGraphBindingTests
{
    private static SphereRegimeSchedule GeoSchedule(long onsetTick) =>
        SphereRegimeScheduleDefaults.GeosphereFor(onsetTick);

    [Fact]
    public void Binding_NoLayerSelected_FollowsGeosphereRegimeChanges()
    {
        var controller = new FakeController(GeoSchedule(SphereRegimeScheduleDefaults.PlateOnsetTick));
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "magma-ocean",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        using var binding = WorldGenerationTimelineGraphBinding.BindGeosphere(controller, source);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereMagmaOceanGraphId, source.ActiveGraphId);

        controller.PushTick(SphereRegimeScheduleDefaults.PlateOnsetTick);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereGraphId, source.ActiveGraphId);
    }

    [Fact]
    public void Binding_LayerSelected_SwitchesToLayerGraphForActiveRegime()
    {
        var controller = new FakeController(
            GeoSchedule(SphereRegimeScheduleDefaults.PlateOnsetTick),
            tick: SphereRegimeScheduleDefaults.PlateOnsetTick);
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: SphereRegimeScheduleDefaults.PlateOnsetTick,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        using var binding = WorldGenerationTimelineGraphBinding.BindGeosphere(controller, source);
        controller.SelectLayer(WorldGenerationGraphDefaults.GeosphereSphereId, "geosphere.crust");

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereCrustLayerGraphId, source.ActiveGraphId);
    }

    [Fact]
    public void Binding_LayerUnavailableAtTick_FallsBackToRegimeGraph()
    {
        var controller = new FakeController(
            GeoSchedule(SphereRegimeScheduleDefaults.PlateOnsetTick),
            tick: SphereRegimeScheduleDefaults.PlateOnsetTick);
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: SphereRegimeScheduleDefaults.PlateOnsetTick,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        using var binding = WorldGenerationTimelineGraphBinding.BindGeosphere(controller, source);
        controller.SelectLayer(WorldGenerationGraphDefaults.GeosphereSphereId, "geosphere.crust");
        Assert.Equal(WorldGenerationGraphDefaults.GeosphereCrustLayerGraphId, source.ActiveGraphId);

        // crust layer is not part of the magma-ocean regime; binding should fall back.
        controller.PushTick(0L);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereMagmaOceanGraphId, source.ActiveGraphId);
    }

    [Fact]
    public void Binding_TickChangeInsideSameLayerSelection_RecomposesLayerGraph()
    {
        var controller = new FakeController(
            GeoSchedule(SphereRegimeScheduleDefaults.PlateOnsetTick),
            tick: SphereRegimeScheduleDefaults.PlateOnsetTick);
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: SphereRegimeScheduleDefaults.PlateOnsetTick,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        using var binding = WorldGenerationTimelineGraphBinding.BindGeosphere(controller, source);
        controller.SelectLayer(WorldGenerationGraphDefaults.GeosphereSphereId, "geosphere.crust");
        Assert.Equal(WorldGenerationGraphDefaults.GeosphereCrustLayerGraphId, source.ActiveGraphId);

        var changed = 0;
        source.Changed += () => changed++;
        controller.PushTick(SphereRegimeScheduleDefaults.PlateOnsetTick + 1_000_000L);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereCrustLayerGraphId, source.ActiveGraphId);
        Assert.Equal(0, changed);
    }

    private sealed class FakeController : ITimelineController
    {
        private TimelineLayerSelection? _selectedLayer;

        public FakeController(SphereRegimeSchedule geosphere, long tick = 0)
        {
            GeosphereSchedule = geosphere;
            AtmosphereSchedule = SphereRegimeScheduleDefaults.AtmosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick);
            Tick = tick;
            MaxTick = 120_000_000L;
        }

        public long Tick { get; private set; }
        public long MaxTick { get; }
        public bool IsPlaying => false;
        public SphereRegimeSchedule GeosphereSchedule { get; }
        public SphereRegimeSchedule AtmosphereSchedule { get; }
        public TimelineLayerSelection? SelectedLayer => _selectedLayer;
        public event Action<long>? TickChanged;
        public event Action<TimelineLayerSelection?>? LayerSelectionChanged;

        public void Play() { }
        public void Pause() { }
        public void SeekTo(long tick) => PushTick(tick);

        public void SelectLayer(string sphereId, string layerId)
        {
            _selectedLayer = new TimelineLayerSelection(sphereId, layerId);
            LayerSelectionChanged?.Invoke(_selectedLayer);
        }

        public void PushTick(long tick)
        {
            Tick = tick;
            TickChanged?.Invoke(tick);
        }

        public void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying) { }
        public void UnregisterPlayback() { }
    }
}
