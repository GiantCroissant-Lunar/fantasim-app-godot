using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.SceneFlow;

/// <summary>
/// Base for scene-tier activators. Centralizes the dynamic-parent shared-kernel forwarding and the
/// child-scope build/teardown so each scene declares only its own registrations (<see cref="Configure"/>)
/// plus an optional post-build step (<see cref="OnActivatedAsync"/>, e.g. running the scene Bootstrap).
/// </summary>
/// <remarks>
/// The forwarded shared-kernel set lives in exactly one place (<see cref="ForwardSharedKernel"/>):
/// adding a forwarded singleton is a one-line change here, not an edit to every activator. This is
/// also the single switch point for the dependency-archi child-scope fix — when that lands, the
/// fresh-collection + manual forward becomes <c>parent.CreateScope()</c> with no scene change. See
/// <c>vault/specs/2026-06-24-dependency-archi-child-scope-singletons-followup.md</c>.
/// </remarks>
public abstract class SceneActivatorBase : ISceneActivator
{
    /// <inheritdoc />
    public abstract string SceneId { get; }

    /// <inheritdoc />
    public async Task<ISceneActivation> ActivateAsync(IServiceProvider parent, CancellationToken cancellationToken = default)
    {
        if (parent is null) throw new ArgumentNullException(nameof(parent));

        var services = new ServiceCollection();
        ForwardSharedKernel(services, parent);
        Configure(services, parent);

        var child = services.BuildServiceProvider();
        try
        {
            await OnActivatedAsync(child, cancellationToken);
        }
        catch
        {
            child.Dispose(); // don't leak the child scope if post-build activation throws
            throw;
        }

        return new SceneActivation(SceneId, child);
    }

    /// <summary>Registers the scene's own services. The shared kernel is already forwarded.</summary>
    protected abstract void Configure(IServiceCollection services, IServiceProvider parent);

    /// <summary>
    /// Optional post-build step against the built child provider — e.g. resolve and run the scene's
    /// Bootstrap. Default is a no-op.
    /// </summary>
    protected virtual Task OnActivatedAsync(IServiceProvider services, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// The single definition of the shared-kernel set forwarded from the dynamic parent into the child
    /// scope. Replace with <c>parent.CreateScope()</c> once dependency-archi shares parent singletons natively.
    /// </summary>
    private static void ForwardSharedKernel(IServiceCollection services, IServiceProvider parent)
    {
        services.AddSingleton(parent.GetRequiredService<IRegistry>());
        services.AddSingleton(parent.GetRequiredService<ILoggerFactory>());
    }
}
