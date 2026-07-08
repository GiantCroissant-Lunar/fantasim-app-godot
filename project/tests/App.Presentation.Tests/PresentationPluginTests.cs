using System;
using System.Threading.Tasks;
using FantaSim.App.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public class PresentationPluginTests
{
    private sealed class FakePresentation : IPlanetPresentation
    {
        public bool Disposed;
        public void Rebind() { }
        public void UpdateCutaway(double azimuthDeg, double widthDeg) { }
        public void UpdateExploded(double factor) { }
        public void UpdateMantle(bool enabled) { }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeContext : IPluginContext
    {
        public FakeContext(IServiceProvider services) => Services = services;
        public IServiceProvider Services { get; }
    }

    private static (PresentationPlugin plugin, IRegistry registry, FakePresentation presentation) Arrange()
    {
        var registry = new ServiceRegistry();
        var presentation = new FakePresentation();
        var services = BuildProvider(registry);
        var plugin = new PresentationPlugin(_ => presentation, isOnMainThread: () => true);
        return (plugin, registry, presentation);
    }

    [Fact]
    public async Task InitializeRegistersThePresentationContract()
    {
        var (plugin, registry, presentation) = Arrange();
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)), default);
        Assert.Same(presentation, registry.TryGet<IPlanetPresentation>());
    }

    [Fact]
    public async Task ShutdownUnregistersAndDisposes()
    {
        var (plugin, registry, presentation) = Arrange();
        await plugin.InitializeAsync(new FakeContext(BuildProvider(registry)), default);
        await plugin.ShutdownAsync();
        Assert.Null(registry.TryGet<IPlanetPresentation>());
        Assert.True(presentation.Disposed);
    }

    private static IServiceProvider BuildProvider(IRegistry registry)
        => new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
}
