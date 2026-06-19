using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;                   // ISceneActivator
using Microsoft.Extensions.DependencyInjection; // GetRequiredService
using PluginArchi.Extensibility.Abstractions;   // [Plugin], ILifecyclePlugin, IPluginContext
using ServiceArchi.Contracts;                   // IRegistry, ServiceRegistration

namespace FantaSim.App.Stage;

/// <summary>
/// The App.Stage bundle's plugin. When stage.pck is loaded into its collectible ALC, the plugin host
/// runs this; it resolves the shared kernel registry from the plugin context and registers App.Stage's
/// <see cref="StageActivator"/> into it, so the resident SceneFlow service can find and activate the
/// stage scene. On unload the registration is disposed, removing the activator from the registry.
/// </summary>
[Plugin("app.stage", Name = "Stage Scene Tier", Description = "Registers the stage scene activator.", Tags = "scene-tier")]
public sealed partial class StagePlugin : ILifecyclePlugin
{
    private IDisposable? _registration;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();
        _registration = registry.RegisterOwned<ISceneActivator>(
            new StageActivator(),
            new ServiceRegistration { Tags = new[] { "scene-activator" }, Description = "stage activator (bundle)" });
        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _registration?.Dispose();
        _registration = null;
        return ValueTask.CompletedTask;
    }
}
