using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Activity;
using FantaSim.App.Ui;
using FantaSim.App.Ui.Activity;
using FantaSim.App.Ui.Presentation;
using Xunit;
using LedgerService = FantaSim.App.Activity.IService;

namespace FantaSim.App.Ui.Tests;

public sealed class ActivityViewSourceTests
{
    private static readonly string TemplateJson = LoadCanonicalTemplateJson();

    private static string LoadCanonicalTemplateJson()
    {
        // The canonical template lives in the App.Ui.Activity bundle assembly as an embedded
        // resource (linked from project/bundles/activity/activity.presentation.json). Loading it
        // through the generic loader exercises the same path the plugin uses at hot-reload time.
        var assembly = typeof(ActivityViewSource).Assembly;
        return PresentationDocumentLoader.LoadText(
            assembly,
            fileName: "activity.presentation.json",
            embeddedResourceSuffix: ".Presentation.activity.presentation.json");
    }

    [Fact]
    public void BuildDocument_UsesPresentationTemplateForRootAndActions()
    {
        var source = new ActivityViewSource(new FakeLedger(), bus: null, TemplateJson);

        var document = source.BuildDocument();

        Assert.Equal("activity", document.SurfaceId);
        Assert.Equal("boomhud.runtime.basic.v1", document.CatalogId);
        Assert.Equal("root", document.Root.Id);
        Assert.Equal("container", document.Root.Type);
        Assert.NotNull(FindById(document.Root, "activity-title"));

        // The card list lives inside a scroll wrapper (Slice A1) so it can grow past the viewport.
        var scroll = FindById(document.Root, "activity-scroll");
        Assert.NotNull(scroll);
        Assert.Equal("scroll", scroll!.Type);

        // Refresh/Hide are nested under the toolbar, not direct root children.
        var refresh = FindById(document.Root, "activity-refresh");
        Assert.NotNull(refresh);
        Assert.Equal("button", refresh!.Type);
        Assert.Contains(refresh.Actions, action => action.Command == "refresh");

        var hide = FindById(document.Root, "activity-hide");
        Assert.NotNull(hide);
        Assert.Equal("button", hide!.Type);
        Assert.Contains(hide.Actions, action => action.Command == "hide");
    }

    [Fact]
    public void BuildDocument_RendersExternalToolInspectorPayloadDetails()
    {
        var ledger = new FakeLedger(new ActivityEntry(
            EntryId: "entry-1",
            Kind: ActivityEntryKind.UiOperation,
            Timestamp: new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero),
            Actor: new ActivityActor("user", "godot"),
            Name: "external-tool.inspect",
            Category: "external-tool",
            PayloadJson: new JsonObject
            {
                ["viewId"] = "external-tool-vplanet",
                ["title"] = "VPLanet Earth Output",
                ["kind"] = "table",
                ["jobId"] = "vplanet-live-smoke",
                ["bodyName"] = "earth",
                ["rowCount"] = 2,
                ["sourcePath"] = "build/_artifacts/generated/vplanet-live-smoke/vplanet/earth.forward",
            }.ToJsonString(),
            CorrelationId: "vplanet-live-smoke",
            Outcome: "inspector mounted for VPLanet Earth Output"));
        var source = new ActivityViewSource(ledger, bus: null, TemplateJson);

        var document = source.BuildDocument();
        // Detail is carried on each card's tooltip (and inline when expanded); collect both.
        var text = CollectText(document.Root, includeTooltip: true);

        Assert.Contains("external-tool.inspect", text);
        Assert.Contains("tool: VPLanet Earth Output", text);
        Assert.Contains("job: vplanet-live-smoke", text);
        Assert.Contains("body: earth", text);
        Assert.Contains("rows: 2", text);
        Assert.Contains("source: .../vplanet/earth.forward", text);
    }

    [Fact]
    public void BuildDocument_RendersUserAndSystemAuditDetails()
    {
        var ledger = new FakeLedger(
            new ActivityEntry(
                EntryId: "entry-user",
                Kind: ActivityEntryKind.UiOperation,
                Timestamp: new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero),
                Actor: new ActivityActor("user", "godot"),
                Name: "ui.graph.run",
                Category: "node-graph",
                PayloadJson: new JsonObject
                {
                    ["runId"] = "run-1",
                    ["recipe"] = "text-to-3d",
                    ["nodeCount"] = 3,
                    ["wireCount"] = 2,
                }.ToJsonString(),
                CorrelationId: "run-1",
                Outcome: "run requested"),
            new ActivityEntry(
                EntryId: "entry-system",
                Kind: ActivityEntryKind.Log,
                Timestamp: new DateTimeOffset(2026, 6, 24, 13, 0, 1, TimeSpan.Zero),
                Actor: new ActivityActor("system", "app"),
                Name: "scene.enter",
                Category: "scene",
                PayloadJson: new JsonObject
                {
                    ["sceneId"] = "stage",
                    ["bundleLoaded"] = true,
                    ["activeScenes"] = 1,
                }.ToJsonString(),
                Outcome: "bundle loaded"));
        var source = new ActivityViewSource(ledger, bus: null, TemplateJson);

        var document = source.BuildDocument();
        var text = CollectText(document.Root, includeTooltip: true);

        // Title now summarizes counts as "<n> recent - <c> cmd - <f> failed".
        Assert.Contains("2 recent", text);
        Assert.Contains("ui.graph.run", text);
        Assert.Contains("run: run-1", text);
        Assert.Contains("recipe: text-to-3d", text);
        Assert.Contains("nodes: 3", text);
        Assert.Contains("wires: 2", text);
        Assert.Contains("scene.enter", text);
        Assert.Contains("scene: stage", text);
        Assert.Contains("active scenes: 1", text);
    }

    [Fact]
    public void BuildDocument_RendersStructuredCommandCard()
    {
        var ledger = new FakeLedger(
            new ActivityEntry(
                EntryId: "cmd-req",
                Kind: ActivityEntryKind.DomainCommand,
                Timestamp: new DateTimeOffset(2026, 6, 26, 10, 0, 0, TimeSpan.Zero),
                Actor: new ActivityActor("user", "godot"),
                Name: "world.orchestrate",
                Category: "orchestration",
                PayloadJson: new JsonObject
                {
                    ["command"] = "world.orchestrate",
                    ["descriptorTitle"] = "Orchestrate world",
                    ["descriptorDescription"] = "Delegates to the active orchestrator.",
                    ["category"] = "orchestration",
                    ["actorKind"] = "user",
                    ["actorId"] = "godot",
                    ["correlationId"] = "corr-1",
                    ["payloadJson"] = "{\"command\":\"world.refresh\"}",
                }.ToJsonString(),
                CorrelationId: "corr-1",
                Outcome: "requested"),
            new ActivityEntry(
                EntryId: "cmd-res",
                Kind: ActivityEntryKind.CommandResult,
                Timestamp: new DateTimeOffset(2026, 6, 26, 10, 0, 1, TimeSpan.Zero),
                Actor: new ActivityActor("system", "command"),
                Name: "world.orchestrate.result",
                Category: "orchestration",
                PayloadJson: new JsonObject
                {
                    ["command"] = "world.orchestrate",
                    ["descriptorTitle"] = "Orchestrate world",
                    ["descriptorDescription"] = "Delegates to the active orchestrator.",
                    ["category"] = "orchestration",
                    ["actorKind"] = "user",
                    ["actorId"] = "godot",
                    ["correlationId"] = "corr-1",
                    ["causationId"] = "cmd-req",
                    ["ok"] = true,
                    ["resultJson"] = "{\"success\":true}",
                }.ToJsonString(),
                CausationId: "cmd-req",
                CorrelationId: "corr-1",
                Outcome: "ok"));
        var source = new ActivityViewSource(ledger, bus: null, TemplateJson);

        var document = source.BuildDocument();

        // Each command entry is its own card panel keyed by EntryId.
        Assert.NotNull(FindById(document.Root, "activity-card-cmd-req"));
        Assert.NotNull(FindById(document.Root, "activity-card-cmd-res"));

        // The request card is a "cmd", the result card a "res"; kind badges carry those tags.
        var badgeText = CollectByType(document.Root, "badge");
        Assert.Contains("cmd", badgeText);
        Assert.Contains("res", badgeText);

        var text = CollectText(document.Root, includeTooltip: true);
        Assert.Contains("Orchestrate world", text);
        Assert.Contains("Delegates to the active orchestrator.", text);
        Assert.Contains("world.orchestrate", text);
        // Request status pill/outcome is "requested"; result outcome is "ok".
        Assert.Contains("requested", text);
        Assert.Contains("outcome: ok", text);
    }

    [Fact]
    public void Dispatch_TogglesInlineDetailRowsForEntry()
    {
        var ledger = new FakeLedger(new ActivityEntry(
            EntryId: "entry-x",
            Kind: ActivityEntryKind.UiOperation,
            Timestamp: new DateTimeOffset(2026, 6, 24, 13, 0, 0, TimeSpan.Zero),
            Actor: new ActivityActor("user", "godot"),
            Name: "ui.graph.run",
            Category: "node-graph",
            PayloadJson: new JsonObject { ["runId"] = "run-1", ["recipe"] = "text-to-3d" }.ToJsonString(),
            Outcome: "run requested"));
        var source = new ActivityViewSource(ledger, bus: null, TemplateJson);

        // Collapsed: detail is only on the tooltip, never as a visible label.
        var collapsed = CollectText(source.BuildDocument().Root, includeTooltip: false);
        Assert.DoesNotContain("run: run-1", collapsed);

        // Expand the entry — its detail now renders as inline label rows.
        source.Dispatch("toggle:entry-x", componentId: null);
        var expanded = CollectText(source.BuildDocument().Root, includeTooltip: false);
        Assert.Contains("run: run-1", expanded);
        Assert.Contains("recipe: text-to-3d", expanded);

        // Toggle again — back to collapsed.
        source.Dispatch("toggle:entry-x", componentId: null);
        var recollapsed = CollectText(source.BuildDocument().Root, includeTooltip: false);
        Assert.DoesNotContain("run: run-1", recollapsed);
    }

    // ----- tree helpers (the document is a nested RuntimeComponentNode tree) -----

    private static RuntimeComponentNode? FindById(RuntimeComponentNode node, string id)
    {
        if (node.Id == id)
            return node;
        foreach (var child in node.Children)
            if (FindById(child, id) is { } found)
                return found;
        return null;
    }

    private static string CollectText(RuntimeComponentNode node, bool includeTooltip)
    {
        var builder = new StringBuilder();
        Collect(node);
        return builder.ToString();

        void Collect(RuntimeComponentNode current)
        {
            Append(current, "text");
            if (includeTooltip)
                Append(current, "tooltip");
            foreach (var child in current.Children)
                Collect(child);
        }

        void Append(RuntimeComponentNode current, string property)
        {
            if (current.Properties.TryGetValue(property, out var value)
                && value.Literal?.GetValue<string>() is { } literal)
                builder.Append(literal).Append('\n');
        }
    }

    private static string CollectByType(RuntimeComponentNode node, string type)
    {
        var builder = new StringBuilder();
        Collect(node);
        return builder.ToString();

        void Collect(RuntimeComponentNode current)
        {
            if (current.Type == type
                && current.Properties.TryGetValue("text", out var value)
                && value.Literal?.GetValue<string>() is { } literal)
                builder.Append(literal).Append('\n');
            foreach (var child in current.Children)
                Collect(child);
        }
    }

    private sealed class FakeLedger : LedgerService
    {
        private readonly List<ActivityEntry> _entries;

        public FakeLedger(params ActivityEntry[] entries) => _entries = entries.ToList();

        public void Append(ActivityEntry entry) => _entries.Add(entry);

        public IReadOnlyList<ActivityEntry> QueryLatest(int count)
            => _entries.TakeLast(count).Reverse().ToArray();

        public IReadOnlyList<ActivityEntry> QueryByCorrelationId(string correlationId)
            => _entries.Where(entry => entry.CorrelationId == correlationId).ToArray();
    }
}
