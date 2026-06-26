using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
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
        Assert.Contains(document.Root.Children, child => child.Id == "activity-title");

        var refresh = document.Root.Children.Single(child => child.Id == "activity-refresh");
        Assert.Equal("button", refresh.Type);
        Assert.Contains(refresh.Actions, action => action.Command == "refresh");

        var hide = document.Root.Children.Single(child => child.Id == "activity-hide");
        Assert.Equal("button", hide.Type);
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
        var text = string.Join("\n", document.Root.Children
            .Select(child => child.Properties.TryGetValue("text", out var value)
                ? value.Literal?.GetValue<string>() ?? string.Empty
                : string.Empty));

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
        var text = string.Join("\n", document.Root.Children
            .Select(child => child.Properties.TryGetValue("text", out var value)
                ? value.Literal?.GetValue<string>() ?? string.Empty
                : string.Empty));

        Assert.Contains("user 1 / system 1", text);
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

        var rows = document.DataModel!["rows"]!.AsArray();
        var commandRows = rows
            .Select(row => row!.AsObject())
            .Where(row => row.ContainsKey("command"))
            .ToArray();
        Assert.Equal(2, commandRows.Length);
        var resultRow = commandRows.First(row => row["command"]!["ok"] is not null);
        var requestRow = commandRows.First(row => row["command"]!["ok"] is null);

        Assert.Equal("Orchestrate world", requestRow["command"]!["descriptorTitle"]!.GetValue<string>());
        Assert.Equal("world.orchestrate", requestRow["command"]!["command"]!.GetValue<string>());
        Assert.Equal("{\"command\":\"world.refresh\"}", requestRow["command"]!["payloadJson"]!.GetValue<string>());
        Assert.True(resultRow["command"]!["ok"]!.GetValue<bool>());
        Assert.Equal("cmd-req", resultRow["command"]!["causationId"]!.GetValue<string>());

        var text = string.Join("\n", document.Root.Children
            .Select(child => child.Properties.TryGetValue("text", out var value)
                ? value.Literal?.GetValue<string>() ?? string.Empty
                : string.Empty));

        Assert.Contains("Orchestrate world", text);
        Assert.Contains("Delegates to the active orchestrator.", text);
        Assert.Contains("status: ok", text);
        Assert.Contains("status: requested", text);
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
