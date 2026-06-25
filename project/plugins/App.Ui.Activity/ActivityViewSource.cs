using System;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using CrosscutFoundation.Messaging;
using FantaSim.App.Activity;
using FantaSim.App.Ui;
using LedgerService = FantaSim.App.Activity.IService;

namespace FantaSim.App.Ui.Activity;

/// <summary>
/// A live view of the activity ledger: the most recent <see cref="MaxEntries"/> entries, newest first
/// (the same stream the <c>activity.recent</c> remote command returns). Pure C# — no Godot, no ReactiveUI.
///
/// The presentation template JSON is supplied by the caller (the bundle plugin loads it via
/// <see cref="FantaSim.App.Ui.Presentation.PresentationDocumentLoader"/>); this source does not know
/// how or where templates are stored.
///
/// It refreshes itself: it subscribes to <see cref="ActivityEntry"/> on the resident crosscut bus and
/// raises <see cref="Changed"/> on each new entry, so the resident <c>ViewRenderer</c> re-reads it. That
/// bus subscription is the ONE resident root into this bundle's ALC, so it is dropped in
/// <see cref="Dispose"/> (the plugin disposes the source on unload) — otherwise the collectible ALC
/// would leak across reloads.
/// </summary>
public sealed class ActivityViewSource : IViewSource, IDisposable
{
    private const int MaxEntries = 60;

    private readonly LedgerService? _ledger;
    private readonly IMessageBus? _bus;
    private readonly IDisposable? _entrySubscription;
    private readonly string _templateJson;
    private int _revision;
    private bool _disposed;

    public ActivityViewSource(LedgerService? ledger, IMessageBus? bus, string templateJson)
    {
        if (string.IsNullOrWhiteSpace(templateJson))
            throw new ArgumentException("Activity presentation template JSON must be supplied.", nameof(templateJson));

        _ledger = ledger;
        _bus = bus;
        _templateJson = templateJson;
        _entrySubscription = bus?.Subscribe<ActivityEntry>(_ => Changed?.Invoke());
    }

    public string ViewId => "activity";

    /// <summary>Raised when a new ledger entry arrives (off the main thread) or on an explicit refresh.</summary>
    public event Action? Changed;

    public RuntimeSurfaceDocument BuildDocument()
    {
        var entries = _ledger?.QueryLatest(MaxEntries) ?? (IReadOnlyList<ActivityEntry>)Array.Empty<ActivityEntry>();
        return ActivityPresentationDocumentBuilder.Build(
            _templateJson,
            entries,
            ledgerAvailable: _ledger is not null,
            revision: ++_revision);
    }

    public void Dispatch(string action, string? componentId)
    {
        switch (action)
        {
            case "refresh":
                PublishUiAction("ui.activity.refresh", componentId);
                Changed?.Invoke();
                break;
            case "hide":
                PublishUiAction("ui.activity.hide", componentId);
                _bus?.Publish(new HideViewMessage("activity"));
                break;
        }
    }

    private void PublishUiAction(string name, string? componentId)
    {
        _bus?.Publish(new ActivityEntry(
            EntryId: Guid.NewGuid().ToString("N"),
            Kind: ActivityEntryKind.UiOperation,
            Timestamp: DateTimeOffset.UtcNow,
            Actor: new ActivityActor("user", "godot"),
            Name: name,
            Category: "ui",
            PayloadJson: new JsonObject { ["componentId"] = componentId ?? string.Empty }.ToJsonString(),
            Outcome: "dispatched"));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _entrySubscription?.Dispose();
    }
}
