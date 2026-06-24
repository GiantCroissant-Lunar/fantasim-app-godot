using FantaSim.App.Common;
using FantaSim.App.Ui;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Ui.Seam;

public static class UiComposition
{
    public static void ComposeUi(HostCompositionContext ctx, Godot.SceneTree tree)
    {
        var uiRoot = new Godot.Control { Name = "UiRoot" };
        uiRoot.SetAnchorsPreset(Godot.Control.LayoutPreset.FullRect);
        tree.Root.CallDeferred("add_child", uiRoot);

        var viewHost = new FantaSim.App.Ui.Seam.ViewHost(
            uiRoot,
            ctx.Registry,
            ctx.Registry.Get<FantaSim.App.Resource.IService>(),
            ctx.LoggerFactory);

        var orchestration = ctx.Registry.Get<FantaSim.App.Command.Orchestration.IWorldOrchestration>();
        var runtimeSource = new RuntimeStatusViewSource(
            orchestration,
            ctx.LoggerFactory.CreateLogger<RuntimeStatusViewSource>());
        ctx.Registry.Register<FantaSim.App.Ui.IViewSource>(
            runtimeSource,
            new ServiceRegistration { Tags = new[] { "ui", "runtime-status" }, Description = "Runtime status view source" });

        // Register IViewHost so bundle plugins (e.g. TimelinePlugin) can resolve it and call
        // Mount() directly without going through IService.ShowAsync — which would re-enter the
        // BundleHost gate and deadlock when called from within a plugin's InitializeAsync.
        ctx.Registry.Register<FantaSim.App.Ui.Providers.IViewHost>(
            viewHost,
            new ServiceRegistration { Tags = new[] { "ui" }, Description = "UI view host" });

        var ui = new FantaSim.App.Ui.Services.Service(
            viewHost,
            ctx.Registry.Get<FantaSim.App.Resource.IService>(),
            ctx.Registry.Get<CrosscutFoundation.Messaging.IMessageBus>(),
            ctx.LoggerFactory);
        ctx.Registry.Register<FantaSim.App.Ui.IService>(
            ui,
            new ServiceRegistration { Tags = new[] { "ui" }, Description = "UI view service" });
        Godot.GD.Print("[Host] registered: Ui (IViewHost + IService)");
    }
}
