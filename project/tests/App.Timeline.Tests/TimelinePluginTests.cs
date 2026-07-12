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
        var residentModeOwner = new FakeTunnelModeOwner();
        registry.Register<ITunnelModeOwner>(residentModeOwner);
        var proxy = new FakeFaceProxy();

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));
        Assert.Same(residentModeOwner, registry.TryGet<ITunnelModeOwner>());
        await plugin.ShutdownAsync();

        Assert.Null(registry.TryGet<FantaSim.App.Timeline.IService>());
        Assert.Null(registry.TryGet<ITimelineFaceContext>());
        Assert.Same(residentModeOwner, registry.TryGet<ITunnelModeOwner>());
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
    public async Task WorldRuntimeChangingShowsHudAndLeavesGeometryTeardownToWorldBinder()
    {
        var registry = NewRegistry();
        var resource = new FakeResourceService { WorldLoaded = true };
        var controller = new FakeTimelineController();
        var proxy = new FakeFaceProxy();
        var tunnel = new FakeTunnelPresentation(
            enabled => new TunnelActivationResult(enabled, enabled, string.Empty),
            initiallyEnabled: true);
        registry.Register<FantaSim.App.Resource.IService>(resource);
        registry.Register<ITimelineController>(controller);
        registry.Register<ITunnelPresentation>(tunnel);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));
        Assert.Same(controller, registry.TryGet<ITimelineFaceContext>()?.Controller);
        Assert.False(proxy.HudVisible);

        resource.RaiseRuntimeChanging("world", ResourceRuntimeOperation.Reload);

        // The watcher raises this event off the Godot main thread. Timeline owns the HUD; the
        // world-bundle binder observes the same event and owns main-thread geometry/camera teardown.
        Assert.True(tunnel.IsEnabled);
        Assert.Equal(0, tunnel.TrySetEnabledCalls);
        Assert.True(proxy.HudVisible);
        Assert.Equal(1L, proxy.HudState.ModeEpoch);
        Assert.Null(registry.TryGet<ITimelineFaceContext>());
        Assert.Null(registry.TryGet<FantaSim.App.Timeline.IService>());
        Assert.Equal(1, controller.UnregisterPlaybackCalls);
        Assert.Equal(1, proxy.RebindResidentContextCalls);
        Assert.Equal(0, proxy.UnbindCrossTargetCalls);
    }

    [Fact]
    public async Task TimelineRuntimeChangingPreservesEffectiveTunnelAndHiddenHud()
    {
        var registry = NewRegistry();
        var resource = new FakeResourceService { WorldLoaded = true };
        var proxy = new FakeFaceProxy();
        var tunnel = new FakeTunnelPresentation(
            enabled => new TunnelActivationResult(enabled, enabled, string.Empty),
            initiallyEnabled: true);
        registry.Register<FantaSim.App.Resource.IService>(resource);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));
        Assert.False(proxy.HudVisible);

        resource.RaiseRuntimeChanging("timeline", ResourceRuntimeOperation.Reload);

        Assert.True(tunnel.IsEnabled);
        Assert.Equal(0, tunnel.TrySetEnabledCalls);
        Assert.False(proxy.HudVisible);
        Assert.Equal(1L, proxy.HudState.ModeEpoch);
        Assert.Null(registry.TryGet<ITimelineFaceContext>());
    }

    [Fact]
    public async Task FailedTimelineSelfReloadRestoresSurvivingPluginBindingsOnCompletion()
    {
        var registry = NewRegistry();
        var resource = new FakeResourceService { WorldLoaded = true };
        var commands = new FakeCommandService();
        var controller = new FakeTimelineController();
        registry.Register<FantaSim.App.Resource.IService>(resource);
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<ITimelineController>(controller);

        var plugin = new TimelinePlugin(() => new FakeFaceProxy());
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));
        resource.RaiseRuntimeChanging("timeline", ResourceRuntimeOperation.Reload);
        Assert.Null(registry.TryGet<ITimelineFaceContext>());
        Assert.DoesNotContain(commands.Commands, command => command.Id.StartsWith("timeline.", StringComparison.Ordinal));

        // Provider failed before unloading this plugin generation; completion must make the same
        // still-valid controller/context/commands live again.
        resource.RaiseRuntimeChanged();

        Assert.Same(controller, registry.TryGet<ITimelineFaceContext>()?.Controller);
        Assert.Contains(commands.Commands, command => command.Id == TimelinePlugin.TunnelViewCommandId);
        await plugin.ShutdownAsync();
    }

    [Fact]
    public async Task StageRuntimeChangingShowsHudAndLeavesGeometryTeardownToWorldBinder()
    {
        var registry = NewRegistry();
        var resource = new FakeResourceService { WorldLoaded = true };
        var proxy = new FakeFaceProxy();
        var tunnel = new FakeTunnelPresentation(
            enabled => new TunnelActivationResult(enabled, enabled, string.Empty),
            initiallyEnabled: true);
        registry.Register<FantaSim.App.Resource.IService>(resource);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        resource.RaiseRuntimeChanging("stage", ResourceRuntimeOperation.Reload);

        Assert.True(tunnel.IsEnabled);
        Assert.Equal(0, tunnel.TrySetEnabledCalls);
        Assert.True(proxy.HudVisible);
        Assert.Equal(1L, proxy.HudState.ModeEpoch);
        Assert.NotNull(registry.TryGet<ITimelineFaceContext>());
        Assert.NotNull(registry.TryGet<FantaSim.App.Timeline.IService>());
    }

    [Fact]
    public async Task TunnelCommand_SuccessReportsRequestedEffectiveReasonAndEpoch()
    {
        var registry = NewRegistry();
        var commands = new FakeCommandService();
        var proxy = new FakeFaceProxy();
        var tunnel = new FakeTunnelPresentation(enabled => new TunnelActivationResult(enabled, enabled, string.Empty));
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        var result = await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = true }.ToJsonString()));
        var payload = JsonNode.Parse(result.ResultJson!)!.AsObject();

        Assert.True(result.Ok);
        Assert.True(payload["ok"]!.GetValue<bool>());
        Assert.True(payload["requested"]!.GetValue<bool>());
        Assert.True(payload["effective"]!.GetValue<bool>());
        Assert.Equal(string.Empty, payload["failureReason"]!.GetValue<string>());
        Assert.Equal(1L, payload["modeEpoch"]!.GetValue<long>());
        Assert.False(proxy.HudVisible);
    }

    [Fact]
    public async Task ExplicitEnableAfterReloadReleasesResidentHudSafety()
    {
        var registry = NewRegistry();
        var commands = new FakeCommandService();
        var proxy = new FakeFaceProxy();
        var modeOwner = new FakeTunnelModeOwner();
        modeOwner.PrepareForTunnelLoss(TunnelModeEvent.WorldChanging);
        var tunnel = new FakeTunnelPresentation(enabled =>
            new TunnelActivationResult(enabled, enabled, string.Empty));
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);
        registry.Register<ITunnelModeOwner>(modeOwner);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        var result = await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = true }.ToJsonString()));
        var payload = JsonNode.Parse(result.ResultJson!)!.AsObject();

        Assert.True(payload["effective"]!.GetValue<bool>());
        Assert.False(modeOwner.CurrentHudSafety.ForceHudVisible);
        Assert.False(proxy.HudVisible);
    }

    [Fact]
    public async Task EnableSpanningNewLossCannotHideHudOrLeaveTunnelEnabled()
    {
        var registry = NewRegistry();
        var commands = new FakeCommandService();
        var proxy = new FakeFaceProxy();
        var modeOwner = new FakeTunnelModeOwner();
        var tunnel = new FakeTunnelPresentation(enabled =>
        {
            if (enabled)
                modeOwner.PrepareForTunnelLoss(TunnelModeEvent.WorldChanging);
            return new TunnelActivationResult(enabled, enabled, string.Empty);
        });
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);
        registry.Register<ITunnelModeOwner>(modeOwner);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        var result = await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = true }.ToJsonString()));
        var payload = JsonNode.Parse(result.ResultJson!)!.AsObject();

        Assert.False(payload["effective"]!.GetValue<bool>());
        Assert.Equal("tunnel activation superseded by resource loss", payload["failureReason"]!.GetValue<string>());
        Assert.False(tunnel.IsEnabled);
        Assert.Equal(2, tunnel.TrySetEnabledCalls);
        Assert.True(modeOwner.CurrentHudSafety.ForceHudVisible);
        Assert.True(proxy.HudVisible);
    }

    [Fact]
    public async Task EnableThatOverlapsResourceBeginFailsEvenBeforeLossCallbackAdvancesSafety()
    {
        var registry = NewRegistry();
        var commands = new FakeCommandService();
        var proxy = new FakeFaceProxy();
        var modeOwner = new FakeTunnelModeOwner();
        var resource = new FakeResourceService { WorldLoaded = true };
        var tunnel = new FakeTunnelPresentation(enabled =>
        {
            if (enabled)
                resource.BeginWithoutNotification("world");
            return new TunnelActivationResult(enabled, enabled, string.Empty);
        });
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<FantaSim.App.Resource.IService>(resource);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);
        registry.Register<ITunnelModeOwner>(modeOwner);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        var result = await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = true }.ToJsonString()));
        var payload = JsonNode.Parse(result.ResultJson!)!.AsObject();

        Assert.False(payload["effective"]!.GetValue<bool>());
        Assert.Equal("tunnel activation superseded by resource loss", payload["failureReason"]!.GetValue<string>());
        Assert.False(tunnel.IsEnabled);
        Assert.Equal(2, tunnel.TrySetEnabledCalls);
        Assert.True(proxy.HudVisible);
    }

    [Fact]
    public async Task EnableThatOverlapsResourceBeginInsideSafetyReleaseFailsClosed()
    {
        var registry = NewRegistry();
        var commands = new FakeCommandService();
        var proxy = new FakeFaceProxy();
        var resource = new FakeResourceService { WorldLoaded = true };
        var modeOwner = new FakeTunnelModeOwner
        {
            BeforeRelease = () => resource.BeginWithoutNotification("world")
        };
        var tunnel = new FakeTunnelPresentation(enabled =>
            new TunnelActivationResult(enabled, enabled, string.Empty));
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<FantaSim.App.Resource.IService>(resource);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);
        registry.Register<ITunnelModeOwner>(modeOwner);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        var result = await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = true }.ToJsonString()));
        var payload = JsonNode.Parse(result.ResultJson!)!.AsObject();

        Assert.False(payload["effective"]!.GetValue<bool>());
        Assert.Equal("tunnel activation superseded by resource loss", payload["failureReason"]!.GetValue<string>());
        Assert.False(tunnel.IsEnabled);
        Assert.Equal(2, tunnel.TrySetEnabledCalls);
        Assert.True(proxy.HudVisible);
    }

    [Fact]
    public async Task TunnelCommand_FailedEnableLeavesHudVisibleAndReportsReason()
    {
        var registry = NewRegistry();
        var commands = new FakeCommandService();
        var proxy = new FakeFaceProxy();
        var tunnel = new FakeTunnelPresentation(enabled =>
            new TunnelActivationResult(enabled, false, "stage unavailable"));
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        var result = await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = true }.ToJsonString()));
        var payload = JsonNode.Parse(result.ResultJson!)!.AsObject();

        Assert.False(payload["ok"]!.GetValue<bool>());
        Assert.True(payload["requested"]!.GetValue<bool>());
        Assert.False(payload["effective"]!.GetValue<bool>());
        Assert.Equal("stage unavailable", payload["failureReason"]!.GetValue<string>());
        Assert.True(proxy.HudVisible);
    }

    [Fact]
    public async Task TunnelCommand_MissingPresentationFailsClosedAndShowsHud()
    {
        var registry = NewRegistry();
        var commands = new FakeCommandService();
        var proxy = new FakeFaceProxy();
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<ITimelineController>(new FakeTimelineController());

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        var result = await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = true }.ToJsonString()));
        var payload = JsonNode.Parse(result.ResultJson!)!.AsObject();

        Assert.False(payload["ok"]!.GetValue<bool>());
        Assert.Equal("tunnel presentation unavailable", payload["failureReason"]!.GetValue<string>());
        Assert.True(proxy.HudVisible);
    }

    [Fact]
    public async Task TunnelCommand_DisableIsIdempotentAndReportsEffectiveFalse()
    {
        var registry = NewRegistry();
        var commands = new FakeCommandService();
        var proxy = new FakeFaceProxy();
        var tunnel = new FakeTunnelPresentation(enabled =>
            new TunnelActivationResult(enabled, false, string.Empty));
        registry.Register<FantaSim.App.Command.IService>(commands);
        registry.Register<ITimelineController>(new FakeTimelineController());
        registry.Register<ITunnelPresentation>(tunnel);

        var plugin = new TimelinePlugin(() => proxy);
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)));

        var result = await commands.ExecuteAsync(new CommandRequest(
            TimelinePlugin.TunnelViewCommandId,
            new JsonObject { ["enabled"] = false }.ToJsonString()));
        var payload = JsonNode.Parse(result.ResultJson!)!.AsObject();

        Assert.True(payload["ok"]!.GetValue<bool>());
        Assert.False(payload["requested"]!.GetValue<bool>());
        Assert.False(payload["effective"]!.GetValue<bool>());
        Assert.True(proxy.HudVisible);
    }

    [Fact]
    public void FaceContextRevisionProviderIsLazyAndSeversOnDispose()
    {
        var revision = 4;
        var context = new TimelineFaceContext(
            controller: new FakeTimelineController(),
            proxy: new FakeFaceProxy(),
            commandClient: null,
            generationGraphFamilyProvider: _ => null,
            filmstripGraphRevisionProvider: () => revision,
            filmstripPreviewProvider: (_, _) => null,
            layerTrackRegistry: null,
            loggerFactory: NullLoggerFactory.Instance,
            ticksPerSecond: 5_000_000.0,
            desiredHudState: new TimelineHudState(true, 0L));

        Assert.Equal(4, context.FilmstripGraphRevisionProvider());
        revision = 7;
        Assert.Equal(7, context.FilmstripGraphRevisionProvider());

        context.Dispose();

        Assert.Equal(0, context.FilmstripGraphRevisionProvider());
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
        public TimelineHudState HudState = new(true, 0L);
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

    private sealed class FakeTunnelPresentation : ITunnelPresentation
    {
        private readonly Func<bool, TunnelActivationResult> _activate;

        public FakeTunnelPresentation(
            Func<bool, TunnelActivationResult> activate,
            bool initiallyEnabled = false)
        {
            _activate = activate;
            IsEnabled = initiallyEnabled;
        }

        public bool IsEnabled { get; private set; }
        public int TrySetEnabledCalls { get; private set; }

        public void Rebind() { }

        public TunnelActivationResult TrySetEnabled(bool enabled)
        {
            TrySetEnabledCalls++;
            var result = _activate(enabled);
            IsEnabled = result.EffectiveEnabled;
            return result;
        }

        public void Dispose() { }
    }

    private sealed class FakeTunnelModeOwner : ITunnelModeOwner
    {
        public int PrepareCalls { get; private set; }
        public TunnelHudSafetyState CurrentHudSafety { get; private set; }
        public Action? BeforeRelease { get; init; }

        public void PrepareForTunnelLoss(TunnelModeEvent lossEvent)
        {
            PrepareCalls++;
            CurrentHudSafety = new TunnelHudSafetyState(CurrentHudSafety.Epoch + 1L, true);
        }

        public bool TryReleaseHudSafety(long expectedEpoch)
        {
            BeforeRelease?.Invoke();
            if (CurrentHudSafety.Epoch != expectedEpoch)
                return false;
            CurrentHudSafety = CurrentHudSafety with { ForceHudVisible = false };
            return true;
        }
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
