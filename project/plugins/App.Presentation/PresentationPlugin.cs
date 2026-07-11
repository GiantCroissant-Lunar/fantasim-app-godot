using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Resource.Bundle;               // IBundleSceneRegistry
using Godot;                                      // Callable + OS (main-thread marshal on shutdown)
using Microsoft.Extensions.DependencyInjection;   // GetRequiredService
using Microsoft.Extensions.Logging;
using PluginArchi.Extensibility.Abstractions;     // [Plugin], ILifecyclePlugin, IPluginContext
using ServiceArchi.Contracts;                     // IRegistry, ServiceRegistration
using ResourceService = FantaSim.App.Resource.IService;

namespace FantaSim.App.Presentation;

/// <summary>
/// World-bundle plugin entry for the planet presentation (bundle-maximalism phase 1). Ships INSIDE
/// world.pck — same collectible ALC as the world data service — creates the
/// PlanetPresentationBinder and registers it behind the shared IPlanetPresentation contract. The
/// resident host resolves the contract after the bundle loads, calls Rebind() on the main thread,
/// and wires the render/camera ingress targets; the host must sever those references on world
/// RuntimeChanging (Host.OnResourceRuntimeChanging) or the old ALC never collects.
/// </summary>
[Plugin("app.presentation", Name = "Planet Presentation", Description = "Registers the planet presentation binder behind IPlanetPresentation.", Tags = "domain-bundle")]
public sealed partial class PresentationPlugin : ILifecyclePlugin
{
    private readonly Func<IPluginContext, IPlanetPresentation> _factory;
    private readonly Func<bool> _isOnMainThread;
    private IDisposable? _registration;
    private IPlanetPresentation? _presentation;
    private IDisposable? _tunnelRegistration;
    private ITunnelPresentation? _tunnelPresentation;
    private ILogger? _log;

    public PresentationPlugin()
        : this(CreateDefault, isOnMainThread: null)
    {
    }

    // Test seam: App.Presentation.Tests injects a fake factory + main-thread answer so the
    // lifecycle is verifiable headless (no Godot engine in the test host).
    internal PresentationPlugin(Func<IPluginContext, IPlanetPresentation> factory, Func<bool>? isOnMainThread)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _isOnMainThread = isOnMainThread ?? (static () => OS.GetThreadCallerId() == OS.GetMainThreadId());
    }

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();
        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        _log = loggerFactory.CreateLogger("PresentationPlugin");

        _presentation = _factory(context);
        _registration = registry.RegisterOwned<IPlanetPresentation>(
            _presentation,
            new ServiceRegistration { Tags = new[] { "presentation", "world-bundle" }, Description = "planet presentation binder (world bundle)" });
        _log.LogInformation("PresentationPlugin: IPlanetPresentation registered.");

        // Tunnel slice-1 (vault/plans/2026-07-11-tunnel-slice1-plan.md Task 7 Step 3): constructed
        // AFTER _presentation above -- creating PlanetPresentationBinder first is what registers
        // ITimelineController, which the tunnel binder's own Rebind() resolves. IBundleSceneRegistry
        // is resolved via TryGet (not Get) rather than assumed present: production composition
        // always registers it before this bundle loads (CreateDefault's own IPlanetPresentation path
        // resolves it the same way), but PresentationPluginTests' fake registry does not, and a
        // hard Get<T>() there would turn every existing lifecycle test into a tunnel-construction
        // test it was never meant to be -- degrade gracefully (log + skip), matching the codebase's
        // consistent "resolve optionally, skip if absent" doctrine (BindPlanetPresentation,
        // ComposeTimeline) rather than throwing.
        var sceneRegistry = registry.TryGet<IBundleSceneRegistry>();
        if (sceneRegistry is not null)
        {
            _tunnelPresentation = PresentationComposition.CreateTunnelPresentation(registry, sceneRegistry, loggerFactory);
            _tunnelRegistration = registry.RegisterOwned<ITunnelPresentation>(
                _tunnelPresentation,
                new ServiceRegistration
                {
                    Tags = new[] { "presentation", "tunnel", "world-bundle" },
                    Description = "tunnel timeline binder (world bundle)"
                });
            _log.LogInformation("PresentationPlugin: ITunnelPresentation registered.");
        }
        else
        {
            _log.LogWarning("PresentationPlugin: IBundleSceneRegistry not registered; tunnel presentation skipped.");
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _registration?.Dispose();
        _registration = null;

        var presentation = _presentation;
        _presentation = null;
        if (presentation is not null)
        {
            // Binder disposal frees Godot nodes — main-thread only. The reload path may run
            // ShutdownAsync off the main thread (RemoveGroupWithDiagnosticsAsync); marshal and WAIT
            // so the unmount completes BEFORE the ALC unloads.
            if (_isOnMainThread())
            {
                presentation.Dispose();
            }
            else
            {
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Callable.From(() =>
                {
                    try
                    {
                        presentation.Dispose();
                        done.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        done.TrySetException(ex);
                    }
                }).CallDeferred();
                await done.Task.ConfigureAwait(false);
            }
        }

        // Tunnel slice-1: same main-thread-marshal-and-wait discipline as the planet presentation
        // above (its binder also frees Godot nodes on Dispose).
        _tunnelRegistration?.Dispose();
        _tunnelRegistration = null;

        var tunnelPresentation = _tunnelPresentation;
        _tunnelPresentation = null;
        if (tunnelPresentation is not null)
        {
            if (_isOnMainThread())
            {
                tunnelPresentation.Dispose();
            }
            else
            {
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Callable.From(() =>
                {
                    try
                    {
                        tunnelPresentation.Dispose();
                        done.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        done.TrySetException(ex);
                    }
                }).CallDeferred();
                await done.Task.ConfigureAwait(false);
            }
        }

        _log?.LogInformation("PresentationPlugin: shutdown completed.");
        _log = null;
    }

    private static IPlanetPresentation CreateDefault(IPluginContext context)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();
        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        var resource = registry.Get<ResourceService>();
        var sceneRegistry = registry.Get<IBundleSceneRegistry>();
        // Config knobs arrive via the host-registered options record (the seam itself may not read
        // config — SeamConfigBanTests); absent registration means defaults.
        var options = registry.TryGet<PlanetPresentationOptions>() ?? PlanetPresentationOptions.Default;
        return PresentationComposition.CreatePlanetPresentation(
            registry,
            resource,
            sceneRegistry,
            loggerFactory,
            options.PlateViewOverride,
            options.ShowWorldGraph);
    }
}
