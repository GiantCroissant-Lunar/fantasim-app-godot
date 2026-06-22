using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;                   // ISceneActivator
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
/// <see cref="FantaSim.App.Ui.IViewSource"/>, and calls <see cref="FantaSim.App.Ui.IService.ShowAsync"/>
/// to mount the view. On unload the registrations are disposed.
/// </summary>
[Plugin("app.timeline", Name = "Timeline HUD", Description = "Registers the timeline view source (IViewSource) and mounts the timeline HUD.", Tags = "hud-view")]
public sealed partial class TimelinePlugin : ILifecyclePlugin
{
    private IDisposable? _activatorRegistration;
    private IDisposable? _viewSourceRegistration;

    public async ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
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
            return;

        // Build and register the view source into the shared registry.
        var viewSource = new TimelineViewSource(controller);
        _viewSourceRegistration = registry.RegisterOwned<FantaSim.App.Ui.IViewSource>(
            viewSource,
            new ServiceRegistration { Tags = new[] { "ui", "timeline" }, Description = "timeline view source (bundle)" });

        // Ask the resident UI service to mount the timeline view (idempotent: ViewHost returns early if
        // already mounted). IService.ShowAsync → IViewHost.Mount — no direct Godot dependency.
        var uiService = registry.TryGet<FantaSim.App.Ui.IService>();
        if (uiService is not null)
            await uiService.ShowAsync("timeline", ct).ConfigureAwait(false);
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _viewSourceRegistration?.Dispose();
        _viewSourceRegistration = null;
        _activatorRegistration?.Dispose();
        _activatorRegistration = null;
        return ValueTask.CompletedTask;
    }
}
