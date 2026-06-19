using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;                   // ISceneActivator, ISceneActivation
using Microsoft.Extensions.DependencyInjection; // ServiceCollection, GetRequiredService
using Microsoft.Extensions.Logging;             // ILoggerFactory
using ServiceArchi.Contracts;                   // IRegistry

namespace FantaSim.App.Stage;

/// <summary>
/// Activates the App.Stage tier under a <b>dynamic</b> parent. It forwards the parent's shared kernel
/// (registry + logger factory) into a plain child <see cref="ServiceCollection"/>, registers the
/// stage's <see cref="Bootstrap"/>, builds the child provider, and runs Bootstrap. Same scene, any
/// parent — no compile-time scope parent. This is the app-side FieldValueResolver assembly point's
/// scene-tier analogue: world-lib types never leak through here.
/// </summary>
public sealed class StageActivator : ISceneActivator
{
    public string SceneId => "stage";

    public async Task<ISceneActivation> ActivateAsync(IServiceProvider parent, CancellationToken cancellationToken = default)
    {
        if (parent is null) throw new ArgumentNullException(nameof(parent));

        var services = new ServiceCollection();

        // Forward the shared kernel from the dynamic parent (the app-root provider exposes both;
        // a parent scene that forwarded them works too). MEDI does not dispose AddSingleton(instance)
        // singletons, so tearing down this child scope on unload never disposes the shared kernel.
        services.AddSingleton(parent.GetRequiredService<IRegistry>());
        services.AddSingleton(parent.GetRequiredService<ILoggerFactory>());
        services.AddSingleton<Bootstrap>();

        var child = services.BuildServiceProvider();
        await child.GetRequiredService<Bootstrap>().RunAsync(cancellationToken).ConfigureAwait(false);

        return new StageActivation(SceneId, child);
    }
}
