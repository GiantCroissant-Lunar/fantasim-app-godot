using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;                   // SceneActivatorBase
using Microsoft.Extensions.DependencyInjection; // IServiceCollection, AddSingleton, GetRequiredService

namespace FantaSim.App.Stage;

/// <summary>
/// Activates the App.Stage tier under a dynamic parent. The shared-kernel forwarding and the
/// child-scope build/teardown live in <see cref="SceneActivatorBase"/>; this scene only registers
/// and runs its own <see cref="Bootstrap"/>.
/// </summary>
public sealed class StageActivator : SceneActivatorBase
{
    public override string SceneId => "stage";

    protected override void Configure(IServiceCollection services, IServiceProvider parent)
        => services.AddSingleton<Bootstrap>();

    protected override Task OnActivatedAsync(IServiceProvider services, CancellationToken cancellationToken)
        => services.GetRequiredService<Bootstrap>().RunAsync(cancellationToken);
}
