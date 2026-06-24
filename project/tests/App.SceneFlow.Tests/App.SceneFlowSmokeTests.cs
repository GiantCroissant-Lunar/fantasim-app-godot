#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;
using FantaSim.App.SceneFlow.Services;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.SceneFlow.Tests;

/// <summary>
/// Real coverage for the resident SceneFlow orchestration the host depends on
/// (Host.EnterInitialScenes: app-root -> stage -> { assist, timeline }). SceneFlow.Service is
/// pure C#, so this runs headless with fake activators standing in for the collectible scene
/// bundles. Asserts the two invariants the multi-scene DI concept rests on: one shared kernel
/// across nested scopes, and child-before-parent teardown.
/// </summary>
public class SceneFlowScopingTests
{
    // Exposes only the shared kernel registry — like AppComposition.RootServices and each scene's
    // child provider do for IRegistry.
    private sealed class KernelProvider : IServiceProvider
    {
        private readonly IRegistry _registry;
        public KernelProvider(IRegistry registry) => _registry = registry;
        public object? GetService(Type serviceType)
            => serviceType == typeof(IRegistry) ? _registry : null;
    }

    private sealed class FakeActivation : ISceneActivation
    {
        private readonly Action _onDispose;
        public FakeActivation(string sceneId, IServiceProvider services, Action onDispose)
        {
            SceneId = sceneId;
            Services = services;
            _onDispose = onDispose;
        }
        public string SceneId { get; }
        public IServiceProvider Services { get; }
        public void Dispose() => _onDispose();
    }

    // Mirrors StageActivator/AssistActivator: forward the kernel from the dynamic parent into the
    // child scope, and re-expose it so a nested child forwards the SAME instance down the chain.
    private sealed class FakeActivator : ISceneActivator
    {
        private readonly IDictionary<string, IRegistry?> _received;
        private readonly IList<string> _disposed;
        public FakeActivator(string sceneId, IDictionary<string, IRegistry?> received, IList<string> disposed)
        {
            SceneId = sceneId;
            _received = received;
            _disposed = disposed;
        }
        public string SceneId { get; }
        public Task<ISceneActivation> ActivateAsync(IServiceProvider parent, CancellationToken cancellationToken = default)
        {
            var kernel = parent.GetService(typeof(IRegistry)) as IRegistry;
            _received[SceneId] = kernel;
            ISceneActivation activation = new FakeActivation(
                SceneId,
                new KernelProvider(kernel!),
                () => _disposed.Add(SceneId));
            return Task.FromResult(activation);
        }
    }

    private static Service NewSceneFlow(IRegistry registry, params ISceneActivator[] activators)
    {
        foreach (var activator in activators)
            registry.Register<ISceneActivator>(activator);
        return new Service(new KernelProvider(registry), registry, NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task Nested_scene_shares_the_one_kernel_through_its_parent_provider()
    {
        var registry = new ServiceRegistry();
        var received = new Dictionary<string, IRegistry?>();
        var disposed = new List<string>();
        var sceneFlow = NewSceneFlow(registry,
            new FakeActivator("stage", received, disposed),
            new FakeActivator("assist", received, disposed));

        await sceneFlow.EnterAsync(new SceneRequest("stage"));
        await sceneFlow.EnterAsync(new SceneRequest("assist", "stage"));

        Assert.Same(registry, received["stage"]);    // stage forwards the app-root kernel
        Assert.Same(registry, received["assist"]);   // assist forwards stage's provider -> same kernel
        Assert.Equal(2, sceneFlow.ActiveScenes.Count);
    }

    [Fact]
    public async Task Exit_tears_down_children_before_parent()
    {
        var registry = new ServiceRegistry();
        var received = new Dictionary<string, IRegistry?>();
        var disposed = new List<string>();
        var sceneFlow = NewSceneFlow(registry,
            new FakeActivator("stage", received, disposed),
            new FakeActivator("assist", received, disposed));

        await sceneFlow.EnterAsync(new SceneRequest("stage"));
        await sceneFlow.EnterAsync(new SceneRequest("assist", "stage"));

        await sceneFlow.ExitAsync("stage");

        Assert.Equal(new[] { "assist", "stage" }, disposed);  // child-before-parent
        Assert.Empty(sceneFlow.ActiveScenes);
    }

    [Fact]
    public async Task Entering_under_an_inactive_parent_throws()
    {
        var registry = new ServiceRegistry();
        var sceneFlow = NewSceneFlow(registry,
            new FakeActivator("assist", new Dictionary<string, IRegistry?>(), new List<string>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sceneFlow.EnterAsync(new SceneRequest("assist", "stage")));
    }
}
