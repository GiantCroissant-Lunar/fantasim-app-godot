using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;                   // ISceneActivator
using Microsoft.Extensions.DependencyInjection; // GetRequiredService
using PluginArchi.Extensibility.Abstractions;   // [Plugin], ILifecyclePlugin, IPluginContext
using ServiceArchi.Contracts;                   // IRegistry, ServiceRegistration

namespace FantaSim.App.Assist;

/// <summary>
/// The App.Assist bundle's plugin. When assist.pck is loaded into its collectible ALC, the plugin host
/// runs this; it resolves the shared kernel registry from the plugin context and registers App.Assist's
/// <see cref="AssistActivator"/> into it, so the resident SceneFlow service can find and activate the
/// assist scene under its dynamic parent (stage). On unload the registration is disposed.
/// </summary>
[Plugin("app.assist", Name = "Assist Scene Tier", Description = "Registers the assist scene activator.", Tags = "scene-tier")]
public sealed partial class AssistPlugin : ILifecyclePlugin
{
    private IDisposable? _registration;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();
        _registration = registry.RegisterOwned<ISceneActivator>(
            new AssistActivator(),
            new ServiceRegistration { Tags = new[] { "scene-activator" }, Description = "assist activator (bundle)" });
        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _registration?.Dispose();
        _registration = null;
        return ValueTask.CompletedTask;
    }
}
