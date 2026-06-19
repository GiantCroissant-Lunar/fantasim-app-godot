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
        ComposeWorld(_composition);
        ComposeCommand(_composition);
        ComposeUi(_composition);

        GD.Print("[Host] composed services: Resource, SceneFlow, Ecs, World, Command, Ui");
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
        GD.Print("[Host] registered: Resource");
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
        GD.Print("[Host] registered: SceneFlow");
    }

    private void ComposeEcs(AppComposition composition)
    {
        var ecs = new FantaSim.App.Ecs.Services.Service(
            composition.Bootstrap.ActorSystem,
            composition.Bootstrap.LoggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.Ecs.IService>(
            ecs,
            new ServiceRegistration { Tags = new[] { "ecs" }, Description = "ECS service" });
        GD.Print("[Host] registered: Ecs");
    }

    private void ComposeWorld(AppComposition composition)
    {
        var world = new FantaSim.App.World.Services.Service(composition.Bootstrap.Registry);
        composition.Bootstrap.Registry.Register<FantaSim.App.World.IService>(
            world,
            new ServiceRegistration { Tags = new[] { "world" }, Description = "World service" });
        GD.Print("[Host] registered: World");

        var projection = new FantaSim.App.World.Projection.Services.FieldProjectionService(
            world,
            new[] { "app.elevation-m" },
            new[] { "app.elevation-m" });
        composition.Bootstrap.Registry.Register<FantaSim.App.World.Projection.Services.FieldProjectionService>(
            projection,
            new ServiceRegistration { Tags = new[] { "world", "projection" }, Description = "Field projection service" });
        GD.Print("[Host] World detail: projection registered");
    }

    private void ComposeCommand(AppComposition composition)
    {
        var loggerFactory = composition.Bootstrap.LoggerFactory;
        var registry = composition.Bootstrap.Registry;

        var orchestration = new FantaSim.App.Command.Orchestration.LocalOrchestrator(registry, loggerFactory);
        registry.Register<FantaSim.App.Command.Orchestration.IWorldOrchestration>(
            orchestration,
            new ServiceRegistration { Tags = new[] { "command", "orchestration" }, Description = "World orchestration seam (local in-process)" });

        var dispatcher = new FantaSim.App.Command.Providers.ImmediateMainThreadDispatcher();
        var commands = new FantaSim.App.Command.Services.Service(dispatcher, registry, loggerFactory, orchestration);
        registry.Register<FantaSim.App.Command.IService>(
            commands,
            new ServiceRegistration { Tags = new[] { "command" }, Description = "Command service" });

        var client = new FantaSim.App.Command.Clients.InProcessClient(commands, loggerFactory);
        registry.Register<FantaSim.App.Command.IClient>(
            client,
            new ServiceRegistration { Tags = new[] { "command", "client" }, Description = "In-process command client" });

        var health = orchestration.HealthAsync().GetAwaiter().GetResult();
        GD.Print($"[Host] registered: Command (orchestration {(health.Ok ? "healthy" : "degraded")}, {health.Commands} commands)");
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

        var orchestration = composition.Bootstrap.Registry.Get<FantaSim.App.Command.Orchestration.IWorldOrchestration>();
        var runtimeSource = new RuntimeStatusViewSource(
            orchestration,
            composition.Bootstrap.LoggerFactory.CreateLogger<RuntimeStatusViewSource>());
        composition.Bootstrap.Registry.Register<FantaSim.App.Ui.IViewSource>(
            runtimeSource,
            new ServiceRegistration { Tags = new[] { "ui", "runtime-status" }, Description = "Runtime status view source" });

        var ui = new FantaSim.App.Ui.Services.Service(
            viewHost,
            composition.Bootstrap.Registry.Get<FantaSim.App.Resource.IService>(),
            composition.Bootstrap.Registry.Get<CrosscutFoundation.Messaging.IMessageBus>(),
            composition.Bootstrap.LoggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.Ui.IService>(
            ui,
            new ServiceRegistration { Tags = new[] { "ui" }, Description = "UI view service" });
        GD.Print("[Host] registered: Ui");
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
