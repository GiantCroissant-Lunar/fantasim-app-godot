using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Ui;
using FantaSim.App.Ui.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;

namespace FantaSim.App.Ui.Activity;

/// <summary>
/// The App.Ui.Activity bundle's plugin. When activity.pck is loaded into its collectible ALC,
/// plugin-archi runs this; it resolves the resident activity-ledger service and crosscut bus (both
/// shared across the ALC boundary), constructs the bundle's <see cref="ActivityViewSource"/>, and
/// registers it as an <see cref="IViewSource"/> so the resident view renderer can render it. On unload
/// the registration is disposed (removing it from the registry) and the source is disposed (dropping its
/// bus subscription — the only resident root into this ALC), so the collectible ALC unloads cleanly.
/// </summary>
[Plugin("app.ui.activity", Name = "Activity Ledger", Description = "Registers the activity-ledger UI surface.", Tags = "ui-bundle")]
public sealed partial class ActivityPlugin : ILifecyclePlugin
{
    private IDisposable? _registration;
    private ActivityViewSource? _source;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var log = context.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Activity.Bootstrap");
        var registry = context.Services.GetRequiredService<IRegistry>();
        log.LogInformation(
            "Activity plugin initializing; IViewSource contract assembly={Assembly}.",
            typeof(IViewSource).Assembly.FullName);

        try
        {
            var ledger = registry.TryGet<FantaSim.App.Activity.IService>();
            var bus = registry.TryGet<CrosscutFoundation.Messaging.IMessageBus>();
            var templateJson = PresentationDocumentLoader.LoadText(
                typeof(ActivityPlugin).Assembly,
                fileName: "activity.presentation.json",
                embeddedResourceSuffix: ".Presentation.activity.presentation.json");
            _source = new ActivityViewSource(ledger, bus, templateJson);
            _registration = registry.RegisterOwned<IViewSource>(
                _source,
                new ServiceRegistration { Tags = new[] { "ui-view" }, Description = "activity-ledger UI view (bundle)" });
            log.LogInformation(
                "Activity view source registered; matching view sources={Count}; source assembly={Assembly}.",
                registry.GetAll<IViewSource>().Count(source => source.ViewId == "activity"),
                _source.GetType().Assembly.FullName);
            return ValueTask.CompletedTask;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Activity plugin initialization failed.");
            throw;
        }
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _registration?.Dispose();
        _registration = null;
        _source?.Dispose();
        _source = null;
        return ValueTask.CompletedTask;
    }
}
