using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Ui;
using Microsoft.Extensions.DependencyInjection;
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
        var registry = context.Services.GetRequiredService<IRegistry>();
        var ledger = registry.TryGet<FantaSim.App.Activity.IService>();
        var bus = registry.TryGet<CrosscutFoundation.Messaging.IMessageBus>();
        _source = new ActivityViewSource(ledger, bus);
        _registration = registry.RegisterOwned<IViewSource>(
            _source,
            new ServiceRegistration { Tags = new[] { "ui-view" }, Description = "activity-ledger UI view (bundle)" });
        return ValueTask.CompletedTask;
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
