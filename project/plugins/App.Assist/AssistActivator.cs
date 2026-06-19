using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;                   // ISceneActivator, ISceneActivation
using Microsoft.Extensions.DependencyInjection; // ServiceCollection, GetRequiredService
using Microsoft.Extensions.Logging;             // ILoggerFactory
using ServiceArchi.Contracts;                   // IRegistry

namespace FantaSim.App.Assist;

/// <summary>
/// Activates the App.Assist tier under a <b>dynamic</b> parent (stage). It forwards the parent scene's
/// shared kernel (registry + logger factory) — which stage itself forwarded from app-root — into a
/// plain child <see cref="ServiceCollection"/>, registers the assist <see cref="Bootstrap"/>, builds
/// the child provider, and runs Bootstrap. So assist shares the one app kernel through the parent
/// chain, across two collectible ALCs.
/// </summary>
public sealed class AssistActivator : ISceneActivator
{
    public string SceneId => "assist";

    public async Task<ISceneActivation> ActivateAsync(IServiceProvider parent, CancellationToken cancellationToken = default)
    {
        if (parent is null) throw new ArgumentNullException(nameof(parent));

        var services = new ServiceCollection();
        services.AddSingleton(parent.GetRequiredService<IRegistry>());
        services.AddSingleton(parent.GetRequiredService<ILoggerFactory>());
        services.AddSingleton<Bootstrap>();

        var child = services.BuildServiceProvider();
        await child.GetRequiredService<Bootstrap>().RunAsync(cancellationToken).ConfigureAwait(false);

        return new AssistActivation(SceneId, child);
    }
}
