using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;
using Microsoft.Extensions.DependencyInjection;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;

namespace FantaSim.App.Timeline;

[Plugin("app.timeline", Name = "Timeline HUD", Description = "Registers the timeline scene activator.", Tags = "scene-tier")]
public sealed partial class TimelinePlugin : ILifecyclePlugin
{
    private IDisposable? _activatorRegistration;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();

        _activatorRegistration = registry.RegisterOwned<ISceneActivator>(
            new TimelineActivator(),
            new ServiceRegistration { Tags = new[] { "scene-activator" }, Description = "timeline activator (bundle)" });

        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _activatorRegistration?.Dispose();
        _activatorRegistration = null;
        return ValueTask.CompletedTask;
    }
}
