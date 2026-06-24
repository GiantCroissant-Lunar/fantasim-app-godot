using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.Activity;
using FantaSim.App.Ui.Activity;
using Xunit;
using LedgerService = FantaSim.App.Activity.IService;

namespace FantaSim.App.Ui.Tests;

public sealed class ActivityViewSourceTests
{
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
        var source = new ActivityViewSource(ledger, bus: null);

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
