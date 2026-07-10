using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Command;
using FantaSim.App.Resource;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.World.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// The two interleavings behind "old ALC still pinned for bundle timeline" (1-in-~6 during
/// multi-pck installs, 2026-07-10): world reloads run on a threadpool thread while the timeline
/// bundle's own scene-tier reload runs on the Godot main thread, so a RuntimeChanged-driven
/// recompose can (a) land concurrently with the plugin's own ShutdownAsync and resurrect
/// registry/command registrations the shutdown already cleaned — the dying generation then stays
/// rooted by resident structures forever — and (b) consume the pending rebind against the
/// OUTGOING world controller registration, which is still resolvable mid-unload (same guard bug
/// the Host fixed for pin class 5 in 511bffe).
/// </summary>
public sealed class TimelinePluginLifecycleRaceTests
{
    [Fact]
    public async Task InFlightWorldRebindCannotResurrectRegistrationsPastShutdown()
    {
        var registry = NewRegistry();
        var resource = new FakeResourceService { WorldLoaded = true };
        var commands = new FakeCommandService();
        var controller = new GatedTimelineController();
        registry.Register<FantaSim.App.Resource.IService>(resource);
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<ITimelineController>(controller);

        var plugin = new TimelinePlugin(() => new FakeFaceProxy());
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        // World reload begins on the threadpool: sever + arm the pending rebind. The world
        // bundle re-registers, so the rebind guard sees a fresh controller instance.
        resource.RaiseRuntimeChanging("world", ResourceRuntimeOperation.Reload);
        registry.UnregisterAll<ITimelineController>();
        var newController = new GatedTimelineController();
        registry.Register<ITimelineController>(newController);

        // The RuntimeChanged handler starts recomposing on the threadpool and stalls mid-compose
        // (gate on the controller's TickChanged add) — modelling the in-flight handler the
        // resource service dispatched just before the plugin's own bundle reload began.
        newController.BlockNextTickChangedAdd();
        var rebind = Task.Run(() => plugin.TryConsumePendingWorldRebind());
        Assert.True(newController.WaitUntilAddBlocked(TimeSpan.FromSeconds(5)), "recompose never reached the controller subscription");

        // The timeline bundle's own reload shuts the plugin down on the main thread. A correct
        // lifecycle either waits for the in-flight recompose and cleans up after it, or makes the
        // recompose a no-op — it must not let the recompose finish registering afterward.
        var shutdown = Task.Run(async () => await plugin.ShutdownAsync());
        await Task.WhenAny(shutdown, Task.Delay(TimeSpan.FromMilliseconds(500)));

        newController.UnblockAdd();
        await rebind;
        await shutdown;

        Assert.Null(registry.TryGet<ITimelineFaceContext>());
        Assert.Null(registry.TryGet<FantaSim.App.Timeline.IService>());
        Assert.DoesNotContain(commands.Commands, command => command.Id.StartsWith("timeline.", StringComparison.Ordinal));
        Assert.Equal(0, newController.TickChangedSubscriberCount);
    }

    [Fact]
    public async Task PendingWorldRebindIgnoresTheOutgoingControllerRegistration()
    {
        var registry = NewRegistry();
        var resource = new FakeResourceService { WorldLoaded = true };
        var proxy = new FakeFaceProxy();
        var outgoingController = new GatedTimelineController();
        registry.Register<FantaSim.App.Resource.IService>(resource);
        registry.Register<ITimelineController>(outgoingController);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        // World reload begins; the OUTGOING controller registration is still resolvable
        // (IsLoaded("world") stays true and the old registration is only disposed mid-unload).
        resource.RaiseRuntimeChanging("world", ResourceRuntimeOperation.Reload);

        // An interleaved RuntimeChanged (another bundle's reload completing) must NOT consume the
        // pending rebind against the dying generation's controller.
        Assert.False(plugin.TryConsumePendingWorldRebind());
        Assert.Null(registry.TryGet<ITimelineFaceContext>());

        // Once the NEW generation registers a different controller instance, the rebind proceeds.
        registry.UnregisterAll<ITimelineController>();
        var newController = new GatedTimelineController();
        registry.Register<ITimelineController>(newController);

        Assert.True(plugin.TryConsumePendingWorldRebind());
        Assert.Same(newController, registry.TryGet<ITimelineFaceContext>()?.Controller);

        await plugin.ShutdownAsync();
    }

    private static IRegistry NewRegistry() => new ServiceRegistry();

    private static IServiceProvider BuildProvider(IRegistry registry)
        => new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

    private sealed class FakeContext : IPluginContext
    {
        public FakeContext(IServiceProvider services) => Services = services;
        public IServiceProvider Services { get; }
    }

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
    }

    private sealed class FakeCommandService : FantaSim.App.Command.IService
    {
        private readonly Dictionary<string, (CommandDescriptor Descriptor, CommandHandler Handler)> _handlers = new(StringComparer.Ordinal);

        public IReadOnlyList<CommandDescriptor> Commands
        {
            get
            {
                lock (_handlers)
                    return _handlers.Values.Select(entry => entry.Descriptor).ToArray();
            }
        }

        public void Register(CommandDescriptor descriptor, CommandHandler handler)
        {
            lock (_handlers)
                _handlers[descriptor.Id] = (descriptor, handler);
        }

        public void Unregister(string commandId)
        {
            lock (_handlers)
                _handlers.Remove(commandId);
        }

        public Task<CommandResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeResourceService : FantaSim.App.Resource.IService
    {
        public bool WorldLoaded { get; set; }
        public event EventHandler<ResourceRuntimeChangingEventArgs>? RuntimeChanging;
        public event EventHandler? RuntimeChanged;

        public bool IsLoaded(string id)
            => string.Equals(id, "world", StringComparison.Ordinal) && WorldLoaded;

        public void RaiseRuntimeChanging(string bundleId, ResourceRuntimeOperation operation)
            => RuntimeChanging?.Invoke(this, new ResourceRuntimeChangingEventArgs(bundleId, operation));

        public void RaiseRuntimeChanged()
            => RuntimeChanged?.Invoke(this, EventArgs.Empty);

        public IReadOnlyList<string> ListLoaded() => WorldLoaded ? new[] { "world" } : Array.Empty<string>();
        public IReadOnlyList<string> ListAvailable() => Array.Empty<string>();
        public IReadOnlyList<ResourceEntry> ListEntries() => Array.Empty<ResourceEntry>();
        public IResourceManifest? GetManifest(string id) => null;
        public Task AutoLoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadFromDirectoryAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadRemoteAsync(string url, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UnloadAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReloadAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReloadByPathAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IDisposable WatchResource(string id, TimeSpan? debounce = null) => NoopDisposable.Instance;
        public Task UnloadAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }
    }

    /// <summary>
    /// ITimelineController fake whose TickChanged ADD accessor can block on demand — the seam that
    /// lets the test freeze ComposeTimeline mid-flight exactly where the real interleave bites.
    /// </summary>
    private sealed class GatedTimelineController : ITimelineController
    {
        private readonly ManualResetEventSlim _addEntered = new(false);
        private readonly ManualResetEventSlim _addGate = new(true);
        private Action<long>? _tickChanged;
        private int _subscriberCount;

        public int TickChangedSubscriberCount => Volatile.Read(ref _subscriberCount);

        public void BlockNextTickChangedAdd()
        {
            _addEntered.Reset();
            _addGate.Reset();
        }

        public bool WaitUntilAddBlocked(TimeSpan timeout) => _addEntered.Wait(timeout);

        public void UnblockAdd() => _addGate.Set();

        public event Action<long>? TickChanged
        {
            add
            {
                _addEntered.Set();
                _addGate.Wait(TimeSpan.FromSeconds(10));
                _tickChanged += value;
                Interlocked.Increment(ref _subscriberCount);
            }
            remove
            {
                if (value is not null && _tickChanged is not null)
                {
                    var before = _tickChanged.GetInvocationList().Length;
                    _tickChanged -= value;
                    var after = _tickChanged?.GetInvocationList().Length ?? 0;
                    if (after < before)
                        Interlocked.Decrement(ref _subscriberCount);
                }
            }
        }

        public event Action<TimelineLayerSelection?>? LayerSelectionChanged;

        public long Tick => 0;
        public long MaxTick => 120_000_000;
        public bool IsPlaying => false;
        public SphereRegimeSchedule GeosphereSchedule { get; } = TimelineTestSchedules.Geosphere();
        public SphereRegimeSchedule AtmosphereSchedule { get; } = TimelineTestSchedules.Atmosphere();
        public TimelineLayerSelection? SelectedLayer => null;
        public IReadOnlyList<TimelineLayerSelection> ActiveLayers => Array.Empty<TimelineLayerSelection>();

        public void Play() { }
        public void Pause() { }
        public void SeekTo(long tick) { }
        public void SelectLayer(string sphereId, string layerId) => LayerSelectionChanged?.Invoke(null);
        public void ToggleLayer(string sphereId, string layerId) { }
        public void PushTick(long tick) => _tickChanged?.Invoke(tick);
        public void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying) { }
        public void UnregisterPlayback() { }
    }
}
