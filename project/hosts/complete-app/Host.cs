using System;
using FantaSim.App.Common;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Common.Entry;

public partial class Host : Node
{
    private AppComposition? _composition;
    private CollectibleBundles? _collectibleBundles;

    public override void _Ready()
    {
        GD.Print("[Host] composition root starting...");

        _composition = AppComposition.Activate();

        _collectibleBundles = LoadCollectibleBundles();
        _composition.Bootstrap.BuildPluginHost(_collectibleBundles);
        _ = _composition.Bootstrap.RunAsync();

        ComposeResource(_composition);
        ComposeSceneFlow(_composition);
        ComposeEcs(_composition);
        ComposeUi(_composition);

        GD.Print("[Host] composition activated.");
    }

    private CollectibleBundles LoadCollectibleBundles()
    {
        const string configPath = "res://config/collectible-bundles.json";
        if (!Godot.FileAccess.FileExists(configPath))
            return CollectibleBundles.Empty;
        var json = Godot.FileAccess.GetFileAsString(configPath);
        return CollectibleBundles.ParseJson(json);
    }

    private void ComposeResource(AppComposition composition)
    {
        var loggerFactory = composition.Bootstrap.LoggerFactory;
        var providerRegistry = new RegistryArchi.Core.Registry();
        providerRegistry.Register<FantaSim.App.Resource.Providers.IProvider>(
            new FantaSim.App.Resource.Bundle.BundleProvider(
                this, composition.Bootstrap.PluginHost, loggerFactory,
                _collectibleBundles!.ContainsAssembly));

        var resource = new FantaSim.App.Resource.Services.Service(
            providerRegistry,
            new FantaSim.App.Resource.Bundle.GodotBundleDirectoryResolver(),
            loggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.Resource.IService>(
            resource,
            new ServiceRegistration { Tags = new[] { "resource" }, Description = "Resource (bundle) service" });
    }

    private void ComposeSceneFlow(AppComposition composition)
    {
        var sceneFlow = new FantaSim.App.SceneFlow.Services.Service(
            composition.RootServices,
            composition.Bootstrap.Registry,
            composition.Bootstrap.LoggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.SceneFlow.IService>(
            sceneFlow,
            new ServiceRegistration { Tags = new[] { "scene-flow" }, Description = "SceneFlow service" });
    }

    private void ComposeEcs(AppComposition composition)
    {
        var ecs = new FantaSim.App.Ecs.Services.Service(
            composition.Bootstrap.ActorSystem,
            composition.Bootstrap.LoggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.Ecs.IService>(
            ecs,
            new ServiceRegistration { Tags = new[] { "ecs" }, Description = "ECS service" });
    }

    private void ComposeUi(AppComposition composition)
    {
        var uiRoot = new Control { Name = "UiRoot" };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        GetTree().Root.CallDeferred("add_child", uiRoot);

        var viewHost = new FantaSim.App.Ui.Seam.ViewHost(
            uiRoot,
            composition.Bootstrap.Registry,
            composition.Bootstrap.Registry.Get<FantaSim.App.Resource.IService>(),
            composition.Bootstrap.LoggerFactory);

        var ui = new FantaSim.App.Ui.Services.Service(
            viewHost,
            composition.Bootstrap.Registry.Get<FantaSim.App.Resource.IService>(),
            composition.Bootstrap.Registry.Get<CrosscutFoundation.Messaging.IMessageBus>(),
            composition.Bootstrap.LoggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.Ui.IService>(
            ui,
            new ServiceRegistration { Tags = new[] { "ui" }, Description = "UI view service" });
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationExitTree)
        {
            _composition?.Dispose();
        }
        base._Notification(what);
    }
}
