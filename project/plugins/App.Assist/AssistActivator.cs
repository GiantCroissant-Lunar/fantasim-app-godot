using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;                   // SceneActivatorBase
using Microsoft.Extensions.DependencyInjection; // IServiceCollection, AddSingleton, GetRequiredService

namespace FantaSim.App.Assist;

/// <summary>
/// Activates the App.Assist tier under a dynamic parent (stage). The shared-kernel forwarding and the
/// child-scope build/teardown live in <see cref="SceneActivatorBase"/>; this scene only registers and
/// runs its own <see cref="Bootstrap"/>. Assist shares the one app kernel through the parent chain.
/// </summary>
public sealed class AssistActivator : SceneActivatorBase
{
    public override string SceneId => "assist";

    protected override void Configure(IServiceCollection services, IServiceProvider parent)
        => services.AddSingleton<Bootstrap>();

    protected override Task OnActivatedAsync(IServiceProvider services, CancellationToken cancellationToken)
        => services.GetRequiredService<Bootstrap>().RunAsync(cancellationToken);
}
