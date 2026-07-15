using System.Collections.Generic;
using System.Reflection;
using FantaSim.App.Command;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;
using CommandService = FantaSim.App.Command.IService;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Late-binding proof for the crust-generation trigger. The resident host binder registers its
/// <c>ITimelineController</c> AFTER the world bundle's <c>WorldPlugin.InitializeAsync</c> has already
/// run, so the one-shot <c>TryGet</c> at init logs "disabled" and the trigger never arms. These
/// tests assert the plugin arms the trigger whenever the controller becomes available later,
/// regardless of registration order.
/// </summary>
public sealed class WorldPluginCrustTriggerLateBindingTests
{
    private static readonly long PlateOnsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;

    private static IRegistry NewRegistry() => new ServiceRegistry();

    private static IPluginContext NewContext(IRegistry registry, ILoggerFactory? loggerFactory = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRegistry>(registry);
        services.AddSingleton<ILoggerFactory>(loggerFactory ?? NullLoggerFactory.Instance);
        return new FakePluginContext(services.BuildServiceProvider());
    }

    [Fact]
    public async Task Controller_registered_after_init_arms_trigger_via_generation_changed()
    {
        // Given a registry with the command service (so RegisterWorldCommand succeeds) but NO
        // ITimelineController at init time -- mirroring the exported-app startup race.
        var registry = NewRegistry();
        var commandService = new FakeCommandService();
        registry.Register<CommandService>(commandService, new ServiceRegistration { Tags = new[] { "command" } });

        var plugin = new WorldPlugin();
        await plugin.InitializeAsync(NewContext(registry), CancellationToken.None);

        // The controller is registered AFTER the plugin initialized (host binder runs later). At
        // this point the trigger must NOT be armed yet (no TickChanged subscriber). The tick is
        // kept out of the mobile-plate regime so arming does not itself kick off a generation run.
        var controller = new LateFakeController(
            SphereRegimeScheduleDefaults.GeosphereFor(PlateOnsetTick),
            SphereRegimeScheduleDefaults.AtmosphereFor(PlateOnsetTick),
            tick: 0);
        Assert.Equal(0, controller.TickChangedSubscriberCount());

        registry.Register<ITimelineController>(controller, new ServiceRegistration { Tags = new[] { "world", "timeline" } });

        // When the world service emits a generation-changed event (the late-arm hook the plugin
        // owns), the trigger should attach to the now-registered controller.
        var world = registry.TryGet<FantaSim.App.World.IService>();
        Assert.NotNull(world);
        world!.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "late-arm-test",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)));

        // Then the trigger armed: TickChanged now has a subscriber (CrustGenerationTrigger.Start
        // attaches its OnTickChanged handler).
        Assert.Equal(1, controller.TickChangedSubscriberCount());
    }

    [Fact]
    public async Task Controller_registered_after_init_arms_trigger_via_presentation_fetch()
    {
        // Same startup race as above, but WITHOUT any generation run: in the exported app no
        // generation event fires organically before the trigger would be needed, so waiting on
        // generation-changed alone is a chicken-and-egg (the trigger IS the first generation).
        // The binder calls GetPlanetPresentationAsync right after registering the controller --
        // that fetch must arm the trigger.
        var registry = NewRegistry();
        var commandService = new FakeCommandService();
        registry.Register<CommandService>(commandService, new ServiceRegistration { Tags = new[] { "command" } });

        var plugin = new WorldPlugin();
        await plugin.InitializeAsync(NewContext(registry), CancellationToken.None);

        var controller = new LateFakeController(
            SphereRegimeScheduleDefaults.GeosphereFor(PlateOnsetTick),
            SphereRegimeScheduleDefaults.AtmosphereFor(PlateOnsetTick),
            tick: 0);
        registry.Register<ITimelineController>(controller, new ServiceRegistration { Tags = new[] { "world", "timeline" } });
        Assert.Equal(0, controller.TickChangedSubscriberCount());

        var world = registry.TryGet<FantaSim.App.World.IService>();
        Assert.NotNull(world);
        _ = world!.GetPlanetPresentationAsync();

        Assert.Equal(1, controller.TickChangedSubscriberCount());
    }

    [Fact]
    public async Task PartialRollback_guard_blocks_rearm_via_late_hooks_even_when_controller_becomes_available()
    {
        // Reproduces the F2 window: a partial-init rollback (ShutdownPartial) is in flight, but the
        // late-arm generation-changed subscription and the PresentationRequested hook have not been
        // torn down yet (or, in a genuine concurrent teardown, a callback that was already
        // dispatched runs regardless of subscription state). If TryArmCrustTrigger has no shutdown
        // guard, a controller becoming available in that window lets these still-live callbacks
        // arm a FRESH CrustGenerationTrigger that is never disposed -- a resident-ALC pin. The fix
        // is a volatile guard flag set at the top of both shutdown paths and checked first in
        // TryArmCrustTrigger, independent of disposal order.
        var registry = NewRegistry();
        var commandService = new FakeCommandService();
        registry.Register<CommandService>(commandService, new ServiceRegistration { Tags = new[] { "command" } });

        var plugin = new WorldPlugin();
        await plugin.InitializeAsync(NewContext(registry), CancellationToken.None);

        var pluginType = typeof(WorldPlugin);
        var crustTriggerField = pluginType.GetField("_crustTrigger", BindingFlags.Instance | BindingFlags.NonPublic);
        var crustArmedField = pluginType.GetField("_crustArmed", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(crustTriggerField);
        Assert.NotNull(crustArmedField);

        // No ITimelineController at init -> late-arm path is active (crust trigger itself is NOT
        // armed yet; the generation-changed subscription and presentation hook are live).
        Assert.Null(crustTriggerField!.GetValue(plugin));
        Assert.False((bool)crustArmedField!.GetValue(plugin)!);

        // Controller becomes available AFTER init, mid-rollback -- the exact race window.
        var controller = new LateFakeController(
            SphereRegimeScheduleDefaults.GeosphereFor(PlateOnsetTick),
            SphereRegimeScheduleDefaults.AtmosphereFor(PlateOnsetTick),
            tick: 0);
        registry.Register<ITimelineController>(controller, new ServiceRegistration { Tags = new[] { "world", "timeline" } });

        // Simulate the shutdown/rollback guard having been set (per the fix, this happens at the
        // very top of ShutdownAsync/ShutdownPartial, before any disposal step runs).
        var guardField = pluginType.GetField("_shuttingDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(guardField);
        guardField!.SetValue(plugin, true);

        // Fire the still-live production callbacks a real caller could still trigger while
        // rollback is in flight.
        var world = registry.TryGet<FantaSim.App.World.IService>();
        Assert.NotNull(world);
        world!.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "rollback-race-test",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)));
        _ = world.GetPlanetPresentationAsync();

        // Then: no new trigger was armed and the controller was never subscribed.
        Assert.Equal(0, controller.TickChangedSubscriberCount());
        Assert.Null(crustTriggerField.GetValue(plugin));
        Assert.False((bool)crustArmedField.GetValue(plugin)!);
    }

    private sealed class LateFakeController : ITimelineController
    {
        private TimelineLayerSelection? _selectedLayer;

        public LateFakeController(SphereRegimeSchedule geosphere, SphereRegimeSchedule atmosphere, long tick)
        {
            GeosphereSchedule = geosphere;
            AtmosphereSchedule = atmosphere;
            Tick = tick;
            MaxTick = PlateOnsetTick + 20_000_000L;
        }

        public long Tick { get; private set; }
        public long MaxTick { get; }
        public bool IsPlaying => false;
        public SphereRegimeSchedule GeosphereSchedule { get; }
        public SphereRegimeSchedule AtmosphereSchedule { get; }
        public TimelineLayerSelection? SelectedLayer => _selectedLayer;
        public event Action<long>? TickChanged;
        public event Action<TimelineLayerSelection?>? LayerSelectionChanged;

        public int TickChangedSubscriberCount()
        {
            // event backing field is private; count via the raw delegate (the compiler-generated
            // add/remove use normal multicast semantics). Reflection keeps the fake Godot-free.
            var field = typeof(LateFakeController).GetField("TickChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is null)
                return 0;
            var dlg = field.GetValue(this) as Delegate;
            return dlg?.GetInvocationList().Length ?? 0;
        }

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

    private sealed class FakePluginContext : IPluginContext
    {
        private readonly IServiceProvider _services;
        public FakePluginContext(IServiceProvider services) => _services = services;
        public IServiceProvider Services => _services;
    }

    private sealed class FakeCommandService : CommandService
    {
        public IReadOnlyList<CommandDescriptor> Commands => Array.Empty<CommandDescriptor>();
        public void Register(CommandDescriptor descriptor, CommandHandler handler) { }
        public void Unregister(string commandId) { }
        public Task<CommandResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult(Id: "fake", Ok: false, ResultJson: string.Empty, Error: null));
    }
}
