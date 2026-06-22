using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;                   // ISceneActivator
using FantaSim.App.Ui.Providers;               // IViewHost
using Microsoft.Extensions.DependencyInjection; // GetRequiredService
using PluginArchi.Extensibility.Abstractions;   // [Plugin], ILifecyclePlugin, IPluginContext
using ServiceArchi.Contracts;                   // IRegistry, ServiceRegistration

namespace FantaSim.App.Timeline;

/// <summary>
/// The App.Timeline bundle's plugin. When timeline.pck is loaded into its collectible ALC, the
/// plugin host runs this; it resolves the shared kernel registry from the plugin context and
/// registers App.Timeline's <see cref="TimelineActivator"/> into it, so the resident SceneFlow
/// service can find and activate the timeline scene. It also resolves <see cref="ITimelineController"/>
/// from the shared registry, builds a <see cref="TimelineViewSource"/>, registers it as
/// <see cref="FantaSim.App.Ui.IViewSource"/>, and calls <see cref="IViewHost.Mount"/> (NOT
/// <see cref="FantaSim.App.Ui.IService.ShowAsync"/>) to schedule the view mount. On unload the
/// registrations are disposed.
/// </summary>
/// <remarks>
/// Mount() is used instead of ShowAsync() to avoid re-entering the BundleHost gate.
/// InitializeAsync runs while BundleHost.LoadCoreAsync holds its SemaphoreSlim gate; ShowAsync
/// would call IsLoaded("timeline") → false → LoadFromDirectoryAsync → gate.WaitAsync → deadlock.
/// Mount() schedules a CallDeferred and returns immediately without touching the loader.
/// </remarks>
[Plugin("app.timeline", Name = "Timeline HUD", Description = "Registers the timeline view source (IViewSource) and mounts the timeline HUD.", Tags = "hud-view")]
public sealed partial class TimelinePlugin : ILifecyclePlugin
{
    private IDisposable? _activatorRegistration;
    private IDisposable? _viewSourceRegistration;
    private TimelineViewSource? _viewSource;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();

        // Register the scene activator so SceneFlow can enter "timeline".
        _activatorRegistration = registry.RegisterOwned<ISceneActivator>(
            new TimelineActivator(),
            new ServiceRegistration { Tags = new[] { "scene-activator" }, Description = "timeline activator (bundle)" });

        // Resolve ITimelineController — registered by the resident host (Task 5: ComposeWorldView)
        // before this bundle is entered. If not yet present, registering and mounting is skipped;
        // Task 5 ensures ordering so this will always succeed at the bundle's InitializeAsync time.
        var controller = registry.TryGet<FantaSim.App.World.Composition.ITimelineController>();
        if (controller is null)
            return ValueTask.CompletedTask;

        // Build and register the view source into the shared registry.
        _viewSource = new TimelineViewSource(controller);
        var viewSource = _viewSource;
        _viewSourceRegistration = registry.RegisterOwned<FantaSim.App.Ui.IViewSource>(
            viewSource,
            new ServiceRegistration { Tags = new[] { "ui", "timeline" }, Description = "timeline view source (bundle)" });

        // Mount the view directly via IViewHost.Mount — NOT via IService.ShowAsync.
        //
        // Rationale: this InitializeAsync runs while BundleHost.LoadCoreAsync holds its _gate
        // (SemaphoreSlim). ShowAsync checks IsLoaded("timeline"), finds false (the bundle is
        // not yet recorded — that happens at line 210 of BundleHost.LoadCoreAsync, AFTER
        // AddGroupAsync/InitializeAsync returns), and calls LoadFromDirectoryAsync → BundleHost.LoadAsync
        // → _gate.WaitAsync — deadlock. IViewHost.Mount does NOT re-enter the loader: it just
        // schedules a CallDeferred(MountImpl) and returns immediately. The IViewSource is already
        // registered above, so the deferred MountImpl will find it on the next Godot frame.
        var viewHost = registry.TryGet<IViewHost>();
        viewHost?.Mount("timeline");

        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _viewSource?.Dispose();
        _viewSource = null;
        _viewSourceRegistration?.Dispose();
        _viewSourceRegistration = null;
        _activatorRegistration?.Dispose();
        _activatorRegistration = null;
        return ValueTask.CompletedTask;
    }
}
