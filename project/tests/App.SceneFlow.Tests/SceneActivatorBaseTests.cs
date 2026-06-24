#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.SceneFlow.Tests;

/// <summary>
/// Exercises the real <see cref="SceneActivatorBase"/> (not a fake): the shared-kernel forwarding,
/// the scene's own registrations, the post-build activation hook, and child-scope disposal — the
/// behavior the three scene activators (Stage/Assist/Timeline) now inherit.
/// </summary>
public class SceneActivatorBaseTests
{
    private sealed class DisposeProbe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class ProbeActivator : SceneActivatorBase
    {
        public bool Activated { get; private set; }
        public override string SceneId => "probe";

        protected override void Configure(IServiceCollection services, IServiceProvider parent)
            => services.AddSingleton<DisposeProbe>();

        protected override Task OnActivatedAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            Activated = true;
            return Task.CompletedTask;
        }
    }

    private static IServiceProvider RootWithKernel(IRegistry registry, ILoggerFactory loggerFactory)
        => new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton(loggerFactory)
            .BuildServiceProvider();

    [Fact]
    public async Task Base_forwards_kernel_runs_activation_and_exposes_scene_services()
    {
        var registry = new ServiceRegistry();
        ILoggerFactory loggerFactory = NullLoggerFactory.Instance;
        var root = RootWithKernel(registry, loggerFactory);

        var activator = new ProbeActivator();
        var activation = await activator.ActivateAsync(root);

        Assert.Same(registry, activation.Services.GetRequiredService<IRegistry>());           // kernel forwarded by identity
        Assert.Same(loggerFactory, activation.Services.GetRequiredService<ILoggerFactory>());  // logger forwarded by identity
        Assert.NotNull(activation.Services.GetRequiredService<DisposeProbe>());                // Configure registrations present
        Assert.True(activator.Activated);                                                      // OnActivatedAsync ran
    }

    [Fact]
    public async Task Disposing_the_activation_tears_down_the_child_scope()
    {
        var registry = new ServiceRegistry();
        var root = RootWithKernel(registry, NullLoggerFactory.Instance);

        var activation = await new ProbeActivator().ActivateAsync(root);
        var probe = activation.Services.GetRequiredService<DisposeProbe>();

        Assert.False(probe.Disposed);
        activation.Dispose();
        Assert.True(probe.Disposed); // the child-owned IDisposable is disposed with the scope
    }
}
