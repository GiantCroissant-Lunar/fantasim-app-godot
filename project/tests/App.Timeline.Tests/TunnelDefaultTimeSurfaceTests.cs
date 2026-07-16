using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Command;
using FantaSim.App.Presentation;
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
/// Directive 1 (vault/specs/2026-07-16-layer-first-presentation-directives.md §1): the tunnel
/// timeline is the DEFAULT time surface. These tests pin the three locked TDD steps from
/// vault/plans/2026-07-16-tunnel-default-time-surface-plan.md:
///  1. fresh composition → tunnel effective + HUD hidden without any command;
///  2. world-reload cycle → tunnel re-asserts effective without an explicit command;
///  3. timeline.tunnel_view {"enabled":false} escape hatch still disables (HUD visible),
///     and re-enable restores hidden HUD.
/// </summary>
public sealed class TunnelDefaultTimeSurfaceTests
{
    [Fact]
    public async Task FreshCompositionEnablesTunnelByDefaultAndHidesHud()
    {
        var (registry, commands, proxy, tunnel) = ComposeWithControllerAndTunnel();

        await InitializePluginAsync(registry, proxy);

        // TDD step 1: tunnel is effective by default, HUD suppressed, no command issued.
        Assert.True(tunnel.IsEnabled);
        Assert.True(tunnel.TrySetEnabledCalls >= 1, "plugin must proactively enable the tunnel during composition");
        Assert.False(proxy.HudVisible);
    }

    [Fact]
    public async Task WorldReloadCycleReAssertsTunnelEnabledWithoutExplicitCommand()
    {
        var (registry, commands, proxy, tunnel) = ComposeWithControllerAndTunnel();
        var resource = new FakeResourceService { WorldLoaded = true };
        registry.Register<FantaSim.App.Resource.IService>(resource);

        await InitializePluginAsync(registry, proxy);
        Assert.True(tunnel.IsEnabled);
        Assert.False(proxy.HudVisible);

        // Simulate the :225 residue path: world bundle reload resets the tunnel binder to
        // disabled, then the reload completes and the plugin recomposes.
        resource.RaiseRuntimeChanging("world", ResourceRuntimeOperation.Reload);
        // The binder resets to disabled during teardown (the residue the plan names).
        tunnel.SimulateReloadResetToDisabled();
        resource.CompleteRuntimeChange("world");
        resource.RaiseRuntimeChanged();

        // The RuntimeChanged event handler calls TryConsumePendingWorldRebind internally; the
        // tunnel must be re-enabled by that recompose without any explicit command.
        Assert.True(tunnel.IsEnabled, "tunnel must re-assert enabled after world reload (default-on survives reload)");
        Assert.False(proxy.HudVisible, "HUD must remain hidden after reload re-assert");
    }

    [Fact]
    public async Task ExplicitDisableEscapeHatchShowsHudAndReEnableRestoresHidden()
    {
        var (registry, commands, proxy, tunnel) = ComposeWithControllerAndTunnel();
        registry.Register<FantaSim.App.Command.IService>(commands);

        var plugin = await InitializePluginAsync(registry, proxy);
        Assert.True(tunnel.IsEnabled);
        Assert.False(proxy.HudVisible);

        // TDD step 3a: explicit disable via the escape-hatch command.
        var disableResult = await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = false }.ToJsonString()));
        Assert.True(disableResult.Ok);
        Assert.False(tunnel.IsEnabled);
        Assert.True(proxy.HudVisible, "HUD must be visible after explicit tunnel disable");

        // TDD step 3b: re-enable restores hidden HUD + effective tunnel.
        var enableResult = await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = true }.ToJsonString()));
        Assert.True(enableResult.Ok);
        Assert.True(tunnel.IsEnabled);
        Assert.False(proxy.HudVisible, "HUD must hide again after re-enable");
    }

    [Fact]
    public async Task ExplicitDisableSurvivesWorldReloadRebindWithoutReEnabling()
    {
        // The escape hatch is durable: a reload must NOT clobber an explicit user disable.
        var (registry, commands, proxy, tunnel) = ComposeWithControllerAndTunnel();
        var resource = new FakeResourceService { WorldLoaded = true };
        registry.Register<FantaSim.App.Resource.IService>(resource);
        registry.Register<FantaSim.App.Command.IService>(commands);

        await InitializePluginAsync(registry, proxy);
        Assert.True(tunnel.IsEnabled);

        // User explicitly disables the tunnel.
        await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = false }.ToJsonString()));
        Assert.False(tunnel.IsEnabled);
        Assert.True(proxy.HudVisible);

        // World reload cycle — re-assert must respect the explicit disable.
        resource.RaiseRuntimeChanging("world", ResourceRuntimeOperation.Reload);
        tunnel.SimulateReloadResetToDisabled();
        resource.CompleteRuntimeChange("world");
        resource.RaiseRuntimeChanged();

        Assert.False(tunnel.IsEnabled, "explicit disable must survive reload — re-assert must not re-enable");
        Assert.True(proxy.HudVisible);
    }

    [Fact]
    public async Task BootOrdering_TunnelRegistersAfterInit_RuntimeChangedRetryEnables()
    {
        // Live-boot defect (2026-07-16 windowed gate): "composition activated" precedes
        // "ITunnelPresentation registered", so the init-time re-assert finds no tunnel and the
        // one-shot chance is gone. A later RuntimeChanged completion must retry the default-on.
        var registry = new ServiceRegistry();
        var proxy = new FakeFaceProxy();
        var resource = new FakeResourceService { WorldLoaded = true };
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<FantaSim.App.Resource.IService>(resource);

        await InitializePluginAsync(registry, proxy);

        // World bundle (and its tunnel binder) registers only AFTER the first compose.
        var tunnel = new FakeTunnelPresentation(
            enabled => new TunnelActivationResult(enabled, enabled, string.Empty),
            initiallyEnabled: false);
        registry.Register<ITunnelPresentation>(tunnel);
        Assert.False(tunnel.IsEnabled);

        resource.RaiseRuntimeChanged(); // bundle-load completion signal

        Assert.True(tunnel.IsEnabled, "RuntimeChanged completion must retry the default-on assert");
        Assert.False(proxy.HudVisible, "HUD must derive hidden after the retry enables the tunnel");
    }

    [Fact]
    public async Task BootOrdering_EnableFailsWhilePreparing_RuntimeChangedRetryEnables()
    {
        // Second live-boot shape: the binder IS registered but still "prepared hidden under
        // stage Environment" — TrySetEnabled fails transiently. The failure must not be
        // terminal; the next RuntimeChanged completion retries.
        var registry = new ServiceRegistry();
        var proxy = new FakeFaceProxy();
        var resource = new FakeResourceService { WorldLoaded = true };
        var preparing = true;
        var tunnel = new FakeTunnelPresentation(
            enabled => preparing
                ? new TunnelActivationResult(enabled, false, "binder still preparing under stage")
                : new TunnelActivationResult(enabled, enabled, string.Empty),
            initiallyEnabled: false);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);
        registry.Register<FantaSim.App.Resource.IService>(resource);

        await InitializePluginAsync(registry, proxy);
        Assert.False(tunnel.IsEnabled);

        preparing = false;
        resource.RaiseRuntimeChanged();

        Assert.True(tunnel.IsEnabled, "retry after the binder finishes preparing must enable");
        Assert.False(proxy.HudVisible);
    }

    [Fact]
    public async Task OutOfBandBinderEnable_HidesHudWithoutRecompose()
    {
        // Round-3 windowed gate: the binder's pending default-enable succeeded at preparation
        // completion, but the lane HUD stayed visible — the timeline never observed the
        // out-of-band transition. The EnabledChangedOutOfBand subscription must re-derive HUD.
        var registry = new ServiceRegistry();
        var proxy = new FakeFaceProxy();
        var resource = new FakeResourceService { WorldLoaded = true };
        var preparing = true;
        var tunnel = new FakeTunnelPresentation(
            enabled => preparing
                ? new TunnelActivationResult(enabled, false, "tunnel mount unavailable")
                : new TunnelActivationResult(enabled, enabled, string.Empty),
            initiallyEnabled: false);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);
        registry.Register<FantaSim.App.Resource.IService>(resource);

        await InitializePluginAsync(registry, proxy);
        Assert.False(tunnel.IsEnabled);
        Assert.True(proxy.HudVisible, "HUD honestly visible while the boot enable keeps failing");

        // Staged preparation completes; the binder self-applies the pending default-enable.
        preparing = false;
        tunnel.SimulateOutOfBandEnable();

        Assert.False(proxy.HudVisible, "HUD must hide when the out-of-band enable is observed");
    }

    private static (IRegistry, FakeCommandService, FakeFaceProxy, FakeTunnelPresentation) ComposeWithControllerAndTunnel()
    {
        var registry = new ServiceRegistry();
        var commands = new FakeCommandService();
        var proxy = new FakeFaceProxy();
        // The fake binder starts DISABLED — production starts disabled too (the binder only sets
        // _enabled=true inside TrySetEnabled). The plugin's default-on must actively enable it.
        var tunnel = new FakeTunnelPresentation(
            enabled => new TunnelActivationResult(enabled, enabled, string.Empty),
            initiallyEnabled: false);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);
        return (registry, commands, proxy, tunnel);
    }

    private static async Task<TimelinePlugin> InitializePluginAsync(IRegistry registry, FakeFaceProxy proxy)
    {
        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));
        return plugin;
    }

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

        public bool HudVisible = true;
        public TimelineHudState HudState = new(true, 0L);

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
        public void ApplyHudState(TimelineHudState state)
        {
            HudState = state;
            HudVisible = state.Visible;
            Target?.ApplyHudState(state);
        }
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
        {
            if (!_handlers.TryGetValue(request.Command, out var entry))
                return Task.FromResult(new CommandResult(
                    request.CorrelationId ?? "test-command",
                    false,
                    Error: new CommandError("unknown-command", request.Command)));
            return ExecuteHandlerAsync(entry.Handler, request, cancellationToken);
        }

        private static async Task<CommandResult> ExecuteHandlerAsync(
            CommandHandler handler,
            CommandRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var json = await handler(request.PayloadJson, cancellationToken);
                return new CommandResult(request.CorrelationId ?? "test-command", true, ResultJson: json);
            }
            catch (Exception ex)
            {
                return new CommandResult(
                    request.CorrelationId ?? "test-command",
                    false,
                    Error: new CommandError(ex.GetType().Name, ex.Message));
            }
        }
    }

    /// <summary>Extended fake that can simulate the world-reload residue: the binder's
    /// _enabled resets to false during teardown (TimelinePlugin.cs:225 residue).</summary>
    private sealed class FakeTunnelPresentation : ITunnelPresentation
    {
        private readonly Func<bool, TunnelActivationResult> _activate;

        public FakeTunnelPresentation(
            Func<bool, TunnelActivationResult> activate,
            bool initiallyEnabled = false,
            float initialZoomScale = 1.0f)
        {
            _activate = activate;
            IsEnabled = initiallyEnabled;
            ZoomScale = initialZoomScale;
        }

        public bool IsEnabled { get; private set; }
        public int TrySetEnabledCalls { get; private set; }
        public int TrySetZoomCalls { get; private set; }
        public float ZoomScale { get; private set; }

        public void Rebind() { }

        public TunnelActivationResult TrySetEnabled(bool enabled)
        {
            TrySetEnabledCalls++;
            var result = _activate(enabled);
            IsEnabled = result.EffectiveEnabled;
            return result;
        }

        public TunnelZoomResult TrySetZoom(int direction)
        {
            TrySetZoomCalls++;
            if (!IsEnabled)
                return new TunnelZoomResult(false, ZoomScale, "tunnel mode not effective");
            ZoomScale = direction > 0 ? ZoomScale * 1.12f : ZoomScale / 1.12f;
            return new TunnelZoomResult(true, ZoomScale, string.Empty);
        }

        /// <summary>Simulates the world-reload residue: the binder tears down and _enabled
        /// resets to false (the slice-1 residue at TimelinePlugin.cs:225).</summary>
        public void SimulateReloadResetToDisabled() => IsEnabled = false;

        public event Action<bool>? EnabledChangedOutOfBand;

        /// <summary>Simulates the binder self-applying its pending default-enable when staged
        /// preparation completes (directive 1) — an enable no other bundle initiated.</summary>
        public void SimulateOutOfBandEnable()
        {
            IsEnabled = true;
            EnabledChangedOutOfBand?.Invoke(true);
        }

        public void Dispose() { }
    }

    private sealed class FakeResourceService : FantaSim.App.Resource.IService
    {
        private readonly HashSet<string> _runtimeChanges = new(StringComparer.OrdinalIgnoreCase);
        public bool WorldLoaded { get; set; }
        public event EventHandler<ResourceRuntimeChangingEventArgs>? RuntimeChanging;
        public event EventHandler? RuntimeChanged;

        public bool IsLoaded(string id)
            => string.Equals(id, "world", StringComparison.Ordinal) && WorldLoaded;

        public bool IsRuntimeChangeInProgress(string id) => _runtimeChanges.Contains(id);

        public void BeginWithoutNotification(string id) => _runtimeChanges.Add(id);

        public void RaiseRuntimeChanging(string bundleId, ResourceRuntimeOperation operation)
        {
            _runtimeChanges.Add(bundleId);
            RuntimeChanging?.Invoke(this, new ResourceRuntimeChangingEventArgs(bundleId, operation));
        }

        public void RaiseRuntimeChanged()
            => RuntimeChanged?.Invoke(this, EventArgs.Empty);

        public void CompleteRuntimeChange(string bundleId) => _runtimeChanges.Remove(bundleId);

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
        public SphereRegimeSchedule GeosphereSchedule { get; } = TimelineTestSchedules.Geosphere();
        public SphereRegimeSchedule AtmosphereSchedule { get; } = TimelineTestSchedules.Atmosphere();
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