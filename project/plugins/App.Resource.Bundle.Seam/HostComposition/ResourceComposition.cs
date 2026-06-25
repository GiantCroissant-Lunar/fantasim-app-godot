using System;
using ServiceArchi.Contracts;
using FantaSim.App.Common;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Resource.Bundle;

public static class ResourceComposition
{
    public static void ComposeResource(HostCompositionContext ctx, Godot.Node hostNode, FantaSim.App.Common.CollectibleBundles bundles)
    {
        var composition = ctx.Composition
            ?? throw new InvalidOperationException("ResourceComposition requires the host AppComposition.");
        var loggerFactory = ctx.LoggerFactory;
        var log = loggerFactory.CreateLogger("HostComposition.Resource");
        var providerRegistry = new RegistryArchi.Core.Registry();
        var sceneHost = new FantaSim.App.Resource.Bundle.BundleSceneHost(
            hostNode,
            loggerFactory.CreateLogger("BundleSceneHost"));
        ctx.Registry.Register<FantaSim.App.Resource.Bundle.IBundleSceneRegistry>(
            sceneHost,
            new ServiceRegistration { Tags = new[] { "resource", "bundle-scenes" }, Description = "Bundle scene root registry" });
        ctx.Registry.Register<FantaSim.App.Resource.Bundle.BundleSceneHost>(
            sceneHost,
            new ServiceRegistration { Tags = new[] { "resource", "bundle-scenes" }, Description = "Bundle scene root host" });
        providerRegistry.Register<FantaSim.App.Resource.Providers.IProvider>(
            new FantaSim.App.Resource.Bundle.BundleProvider(
                sceneHost: sceneHost,
                pluginHost: composition.Bootstrap.PluginHost,
                loggerFactory: loggerFactory,
                isCollectibleAssembly: bundles.ContainsAssembly));

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
