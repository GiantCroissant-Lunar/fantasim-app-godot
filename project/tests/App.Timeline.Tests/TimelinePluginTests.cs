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

public sealed class TimelinePluginTests
{
    [Fact]
    public async Task InitializeRegistersTimelineServiceWhenControllerExists()
    {
        var registry = NewRegistry();
        var controller = new FakeTimelineController();
        var proxy = new FakeFaceProxy();
        registry.Register<ITimelineController>(controller);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        var service = registry.TryGet<FantaSim.App.Timeline.IService>();
        var context = registry.TryGet<ITimelineFaceContext>();
        Assert.NotNull(service);
        Assert.NotNull(context);
        Assert.Same(controller, context.Controller);
        Assert.Same(proxy, context.Proxy);
    }

    [Fact]
    public async Task ShutdownUnregistersTimelineServiceAndClearsFaceContext()
    {
        var registry = NewRegistry();
        registry.Register<ITimelineController>(new FakeTimelineController());
        var proxy = new FakeFaceProxy();

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));
        await plugin.ShutdownAsync();

        Assert.Null(registry.TryGet<FantaSim.App.Timeline.IService>());
        Assert.Null(registry.TryGet<ITimelineFaceContext>());
        Assert.Equal(1, proxy.RebindResidentContextCalls);
        Assert.Equal(1, proxy.UnbindCrossTargetCalls);
    }

    [Fact]
    public async Task ShutdownUnregistersTimelineCommands()
    {
        var registry = NewRegistry();
        var commands = new FakeCommandService();
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<ITimelineController>(new FakeTimelineController());

        var plugin = new TimelinePlugin(() => new FakeFaceProxy());
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));
        Assert.Contains(commands.Commands, command => command.Id == TimelinePlugin.SeekCommandId);
        Assert.Contains(commands.Commands, command => command.Id == TimelinePlugin.SelectLayerCommandId);
        Assert.Contains(commands.Commands, command => command.Id == TimelinePlugin.ToggleLayerCommandId);

        await plugin.ShutdownAsync();

        Assert.DoesNotContain(commands.Commands, command => command.Id.StartsWith("timeline.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PendingWorldRebindIsNotConsumedWhileControllerRegistrationIsAbsent()
    {
        var registry = NewRegistry();
        var resource = new FakeResourceService { WorldLoaded = true };
        var proxy = new FakeFaceProxy();
        registry.Register<FantaSim.App.Resource.IService>(resource);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        Assert.False(plugin.TryConsumePendingWorldRebind());
        Assert.Equal(0, proxy.RebindResidentContextCalls);

        registry.Register<ITimelineController>(new FakeTimelineController());

        Assert.True(plugin.TryConsumePendingWorldRebind());
        Assert.NotNull(registry.TryGet<ITimelineFaceContext>());
        Assert.Equal(1, proxy.RebindResidentContextCalls);
    }

    [Fact]
    public async Task WorldRuntimeChangingSeversFaceContextButKeepsProxyForRebind()
    {
        var registry = NewRegistry();
        var resource = new FakeResourceService { WorldLoaded = true };
        var controller = new FakeTimelineController();
        var proxy = new FakeFaceProxy();
        registry.Register<FantaSim.App.Resource.IService>(resource);
        registry.Register<ITimelineController>(controller);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));
        Assert.Same(controller, registry.TryGet<ITimelineFaceContext>()?.Controller);

        resource.RaiseRuntimeChanging("world", ResourceRuntimeOperation.Reload);

        Assert.Null(registry.TryGet<ITimelineFaceContext>());
        Assert.Null(registry.TryGet<FantaSim.App.Timeline.IService>());
        Assert.Equal(1, controller.UnregisterPlaybackCalls);
        Assert.Equal(1, proxy.RebindResidentContextCalls);
        Assert.Equal(0, proxy.UnbindCrossTargetCalls);
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
        public int RebindResidentContextCalls { get; private set; }
        public int UnbindCrossTargetCalls { get; private set; }
        public bool IsCrossBound { get; private set; }
        public ITimelineFace? Target { get; private set; }

        public void RebindResidentContext()
        {
            RebindResidentContextCalls++;
            Target?.RebindResidentContext();
        }

        public void BindCrossTarget(ITimelineFace target)
        {
            Target = target;
            IsCrossBound = true;
        }

        public void UnbindCrossTarget()
        {
            Target = null;
            IsCrossBound = false;
            UnbindCrossTargetCalls++;
        }

        public void Play() => Target?.Play();
        public void Pause() => Target?.Pause();
        public void SeekTo(long tick) => Target?.SeekTo(tick);
        public void ApplyView(TimelineViewSnapshot snapshot) => Target?.ApplyView(snapshot);
        public bool HudVisible = true;
        public void SetHudVisible(bool visible) { HudVisible = visible; Target?.SetHudVisible(visible); }
    }

    private sealed class FakeCommandService : FantaSim.App.Command.IService
    {
        private readonly Dictionary<string, (CommandDescriptor Descriptor, CommandHandler Handler)> _handlers = new(StringComparer.Ordinal);

        public IReadOnlyList<CommandDescriptor> Commands => _handlers.Values.Select(entry => entry.Descriptor).ToArray();

        public void Register(CommandDescriptor descriptor, CommandHandler handler)
            => _handlers[descriptor.Id] = (descriptor, handler);

        public void Unregister(string commandId)
            => _handlers.Remove(commandId);

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

    private sealed class FakeTimelineController : ITimelineController
    {
        private long _tick;
        private TimelineLayerSelection? _selectedLayer;
        private readonly List<TimelineLayerSelection> _activeLayers = new();

        public long Tick => _tick;
        public long MaxTick => 120_000_000;
        public bool IsPlaying { get; private set; }
        public int UnregisterPlaybackCalls { get; private set; }
        public SphereRegimeSchedule GeosphereSchedule { get; } =
            TimelineTestSchedules.Geosphere();
        public SphereRegimeSchedule AtmosphereSchedule { get; } =
            TimelineTestSchedules.Atmosphere();
        public TimelineLayerSelection? SelectedLayer => _selectedLayer;
        public IReadOnlyList<TimelineLayerSelection> ActiveLayers => _activeLayers;
        public event Action<long>? TickChanged;
        public event Action<TimelineLayerSelection?>? LayerSelectionChanged;

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void SeekTo(long tick) => PushTick(tick);

        public void SelectLayer(string sphereId, string layerId)
        {
            _selectedLayer = new TimelineLayerSelection(sphereId, layerId);
            _activeLayers.Clear();
            _activeLayers.Add(_selectedLayer);
            LayerSelectionChanged?.Invoke(_selectedLayer);
        }

        public void ToggleLayer(string sphereId, string layerId)
        {
            var selection = new TimelineLayerSelection(sphereId, layerId);
            var index = _activeLayers.FindIndex(layer => layer.Equals(selection));
            if (index >= 0)
                _activeLayers.RemoveAt(index);
            else
                _activeLayers.Add(selection);
            _selectedLayer = _activeLayers.Count == 0 ? null : _activeLayers[0];
            LayerSelectionChanged?.Invoke(_selectedLayer);
        }

        public void PushTick(long tick)
        {
            _tick = Math.Clamp(tick, 0L, MaxTick);
            TickChanged?.Invoke(_tick);
        }

        public void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying) { }
        public void UnregisterPlayback() => UnregisterPlaybackCalls++;
    }
}
