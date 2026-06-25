using System.Linq;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Ui.ExternalTools;
using Xunit;

namespace FantaSim.App.Ui.Tests;

public sealed class ExternalToolResultViewSourceTests
{
    [Fact]
    public void BuildDocument_ProjectsVplanetOutputTableIntoBoomHudSurface()
    {
        var source = new ExternalToolResultViewSource(
            "external-tool-vplanet",
            "VPLanet Earth Output",
            new JsonObject
            {
                ["job_id"] = "vplanet-fixture-job",
                ["outputTable"] = new JsonObject
                {
                    ["bodyName"] = "earth",
                    ["fallback"] = true,
                    ["sourcePath"] = "/tmp/fantasim/vplanet/earth.forward",
                    ["columns"] = new JsonArray
                    {
                        "Time",
                        "SemiMajorAxis",
                        "Eccentricity",
                        "Obliquity",
                    },
                    ["rows"] = new JsonArray
                    {
                        new JsonArray { 0.0, 1.0, 0.0167, 23.5 },
                        new JsonArray { 1_000_000.0, 1.0, 0.0167, 23.5 },
                    },
                },
            });

        var document = source.BuildDocument();

        Assert.Equal("external-tool-vplanet", document.SurfaceId);
        Assert.Equal(RuntimeSurfaceProtocol.BasicCatalogId, document.CatalogId);
        Assert.Equal("container", document.Root.Type);
        Assert.Contains(document.Root.Children, child => child.Id == "summary" && child.Type == "panel");
        Assert.Contains(document.Root.Children, child => child.Id == "table" && child.Type == "panel");

        var toolResult = document.DataModel!["toolResult"]!.AsObject();
        Assert.Equal("VPLanet Earth Output", toolResult["title"]!.GetValue<string>());
        Assert.Equal("vplanet-fixture-job", toolResult["jobId"]!.GetValue<string>());

        var table = toolResult["table"]!.AsObject();
        Assert.Equal("earth", table["bodyName"]!.GetValue<string>());
        Assert.True(table["fallback"]!.GetValue<bool>());
        Assert.Equal("/tmp/fantasim/vplanet/earth.forward", table["sourcePath"]!.GetValue<string>());
        Assert.Equal(2, table["rowCount"]!.GetValue<int>());
        Assert.Equal("Time | SemiMajorAxis | Eccentricity | Obliquity", table["headerLine"]!.GetValue<string>());

        var displayRows = table["displayRows"]!.AsArray();
        Assert.Equal("0 | 1 | 0.0167 | 23.5", displayRows[0]!["display"]!.GetValue<string>());
        Assert.Equal("1000000 | 1 | 0.0167 | 23.5", displayRows[1]!["display"]!.GetValue<string>());

        var rowLabels = document.Root.Children
            .Single(child => child.Id == "table")
            .Children
            .Where(child => child.Id.StartsWith("row-", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, rowLabels.Length);

        var sourceLabel = document.Root.Children
            .Single(child => child.Id == "table")
            .Children
            .Single(child => child.Id == "table-source");
        Assert.Equal("source: .../vplanet/earth.forward", sourceLabel.Properties["text"].Literal!.GetValue<string>());
    }

    [Fact]
    public void UpdateResult_ReplacesResultAndRaisesChanged()
    {
        var source = new ExternalToolResultViewSource(
            "external-tool-vplanet",
            "VPLanet Earth Output",
            new JsonObject { ["job_id"] = "before" });
        var changes = 0;
        source.Changed += () => changes++;

        source.UpdateResult(new JsonObject { ["job_id"] = "after" });

        Assert.Equal(1, changes);
        Assert.Equal("after", source.BuildDocument().DataModel!["toolResult"]!["jobId"]!.GetValue<string>());
    }

    [Fact]
    public void BuildDocument_UsesDeterministicEarthOutputTable()
    {
        var source = CreateVplanetFixtureSource();

        var document = source.BuildDocument();
        var table = document.DataModel!["toolResult"]!["table"]!.AsObject();

        Assert.Equal("external-tool-vplanet", source.ViewId);
        Assert.Equal("earth", table["bodyName"]!.GetValue<string>());
        Assert.Equal("Time | SemiMajorAxis | Eccentricity | Obliquity", table["headerLine"]!.GetValue<string>());
        Assert.Equal(2, table["rowCount"]!.GetValue<int>());
    }

    [Fact]
    public void BuildDocument_ProjectsInspectorSectionsAndRawPayloadPreview()
    {
        var source = CreateVplanetFixtureSource();

        var document = source.BuildDocument();
        var toolResult = document.DataModel!["toolResult"]!.AsObject();

        Assert.NotNull(toolResult["rawPreview"]);
        var sections = toolResult["inspectorSections"]!.AsArray();
        Assert.Contains(sections, section => section!["title"]!.GetValue<string>() == "Identity");
        Assert.Contains(sections, section => section!["title"]!.GetValue<string>() == "Output table");
        Assert.Contains(sections, section => section!["title"]!.GetValue<string>() == "Provenance");

        var inspectorPanel = document.Root.Children.Single(child => child.Id == "inspector");
        Assert.Equal("panel", inspectorPanel.Type);
        Assert.Contains(inspectorPanel.Children, child => child.Id == "inspector-identity");

        var rawPanel = document.Root.Children.Single(child => child.Id == "raw");
        Assert.Equal("panel", rawPanel.Type);
        Assert.Contains(rawPanel.Children, child => child.Id == "raw-preview");
    }

    [Fact]
    public void BuildDocument_RendersCompactRawPayloadNotice()
    {
        var source = CreateVplanetFixtureSource();

        var document = source.BuildDocument();

        var rawText = document.Root.Children
            .Single(child => child.Id == "raw")
            .Children
            .Single(child => child.Id == "raw-preview")
            .Properties["text"]
            .Literal!
            .GetValue<string>();
        Assert.DoesNotContain("{\"job_id\"", rawText);
        Assert.True(rawText.Length <= 96);
    }

    [Fact]
    public void BuildActivityPayload_DescribesExternalToolInspectorResult()
    {
        var source = CreateVplanetFixtureSource();

        var payload = source.BuildActivityPayload();

        Assert.Equal("external-tool-vplanet", payload["viewId"]!.GetValue<string>());
        Assert.Equal("VPLanet Earth Output", payload["title"]!.GetValue<string>());
        Assert.Equal("table", payload["kind"]!.GetValue<string>());
        Assert.Equal("vplanet-fixture-job", payload["jobId"]!.GetValue<string>());
        Assert.Equal("earth", payload["bodyName"]!.GetValue<string>());
        Assert.Equal(2, payload["rowCount"]!.GetValue<int>());
    }

    private static ExternalToolResultViewSource CreateVplanetFixtureSource()
        => new(
            "external-tool-vplanet",
            "VPLanet Earth Output",
            new JsonObject
            {
                ["job_id"] = "vplanet-fixture-job",
                ["outputTable"] = new JsonObject
                {
                    ["bodyName"] = "earth",
                    ["fallback"] = true,
                    ["sourcePath"] = "/tmp/fantasim/vplanet/earth.forward",
                    ["columns"] = new JsonArray
                    {
                        "Time",
                        "SemiMajorAxis",
                        "Eccentricity",
                        "Obliquity",
                    },
                    ["rows"] = new JsonArray
                    {
                        new JsonArray { 0.0, 1.0, 0.0167, 23.5 },
                        new JsonArray { 1_000_000.0, 1.0, 0.0167, 23.5 },
                    },
                },
            });
}
