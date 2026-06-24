using ServiceArchi.Contracts;
using FantaSim.App.Common;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Resource.Bundle;

public static class ResourceComposition
{
    public static void ComposeResource(HostCompositionContext ctx, Godot.Node hostNode, FantaSim.App.Common.CollectibleBundles bundles)
    {
        var loggerFactory = ctx.LoggerFactory;
        var log = loggerFactory.CreateLogger("HostComposition.Resource");
        var providerRegistry = new RegistryArchi.Core.Registry();
        providerRegistry.Register<FantaSim.App.Resource.Providers.IProvider>(
            new FantaSim.App.Resource.Bundle.BundleProvider(
                hostNode, ctx.Composition.Bootstrap.PluginHost, loggerFactory,
                bundles.ContainsAssembly));

        var resource = new FantaSim.App.Resource.Services.Service(
            providerRegistry,
            new FantaSim.App.Resource.Bundle.GodotBundleDirectoryResolver(),
            loggerFactory);
        ctx.Registry.Register<FantaSim.App.Resource.IService>(
            resource,
            new ServiceRegistration { Tags = new[] { "resource" }, Description = "Resource (bundle) service" });
        log.LogInformation("registered: Resource");
    }
}
