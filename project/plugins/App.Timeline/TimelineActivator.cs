using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;                   // ISceneActivator, ISceneActivation
using Microsoft.Extensions.DependencyInjection; // ServiceCollection, GetRequiredService
using Microsoft.Extensions.Logging;             // ILoggerFactory
using ServiceArchi.Contracts;                   // IRegistry

namespace FantaSim.App.Timeline;

/// <summary>
/// Activates the App.Timeline scene-tier bundle, forwarding the shared
/// kernel (registry + logger factory) from the parent into a plain child ServiceCollection,
/// registering <see cref="Bootstrap"/>, building the provider, and running Bootstrap.
/// Mirrors <c>AssistActivator</c> exactly (scene-id "timeline" instead of "assist").
/// </summary>
public sealed class TimelineActivator : ISceneActivator
{
    public string SceneId => "timeline";

    public async Task<ISceneActivation> ActivateAsync(IServiceProvider parent, CancellationToken cancellationToken = default)
    {
        if (parent is null) throw new ArgumentNullException(nameof(parent));

        var services = new ServiceCollection();
        services.AddSingleton(parent.GetRequiredService<IRegistry>());
        services.AddSingleton(parent.GetRequiredService<ILoggerFactory>());
        services.AddSingleton<Bootstrap>();

        var child = services.BuildServiceProvider();
        await child.GetRequiredService<Bootstrap>().RunAsync(cancellationToken).ConfigureAwait(false);

        return new TimelineActivation(SceneId, child);
    }
}
