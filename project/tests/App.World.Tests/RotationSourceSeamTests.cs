using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.World.Crust;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Services;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class RotationSourceSeamTests
{
    // PLATES4: MovingPlateId TimeMa PoleLatDeg PoleLonDeg AngleDeg FixedPlateId
    private const string FourPlateRotText = @"
1 0.0 90.0 0.0 0.0 0
1 4.0 90.0 0.0 6.0 0
1 8.0 90.0 0.0 12.0 0
2 0.0 0.0 90.0 0.0 0
2 4.0 0.0 90.0 10.0 0
2 8.0 0.0 90.0 20.0 0
3 0.0 -45.0 180.0 0.0 0
3 4.0 -45.0 180.0 -8.0 0
3 8.0 -45.0 180.0 -16.0 0
4 0.0 45.0 90.0 0.0 0
4 4.0 45.0 90.0 14.0 0
4 8.0 45.0 90.0 28.0 0
";

    [Fact]
    public void ReadRotationSourceRecipe_absent_payload_yields_default_generated()
    {
        var recipe = WorldCrustRunSpec.ReadRotationSourceRecipe(new JsonObject());

        Assert.Equal(RotationSourceKind.Generated, recipe.Kind);
        Assert.Null(recipe.RotText);
    }

    [Fact]
    public void ReadRotationSourceRecipe_explicit_generated_kind_yields_default()
    {
        var payload = new JsonObject { ["rotationSource"] = new JsonObject { ["kind"] = "generated" } };

        var recipe = WorldCrustRunSpec.ReadRotationSourceRecipe(payload);

        Assert.Equal(RotationSourceKind.Generated, recipe.Kind);
    }

    [Fact]
    public void ReadRotationSourceRecipe_imported_kind_with_payload_selects_parser_path()
    {
        var payload = new JsonObject
        {
            ["rotationSource"] = new JsonObject
            {
                ["kind"] = "imported",
                ["payload"] = FourPlateRotText,
                ["name"] = "four-plate-fixture",
            },
        };

        var recipe = WorldCrustRunSpec.ReadRotationSourceRecipe(payload);

        Assert.Equal(RotationSourceKind.Imported, recipe.Kind);
        Assert.Equal(FourPlateRotText, recipe.RotText);
        Assert.Equal("four-plate-fixture", recipe.SourceName);
    }

    [Theory]
    [InlineData("imported", false)]
    [InlineData("imported", true)]
    public void ReadRotationSourceRecipe_imported_without_payload_throws_clear_error(string kind, bool emptyPayload)
    {
        var rs = new JsonObject { ["kind"] = kind };
        if (emptyPayload)
            rs["payload"] = "   ";

        var payload = new JsonObject { ["rotationSource"] = rs };

        var ex = Assert.Throws<ArgumentException>(() =>
            WorldCrustRunSpec.ReadRotationSourceRecipe(payload));

        Assert.Contains("payload", ex.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadRotationSourceRecipe_unknown_kind_throws_clear_error()
    {
        var payload = new JsonObject
        {
            ["rotationSource"] = new JsonObject { ["kind"] = "fabricated" },
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            WorldCrustRunSpec.ReadRotationSourceRecipe(payload));

        Assert.Contains("fabricated", ex.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("generated", ex.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void FromExecutionPayload_carries_rotation_source_through_to_spec()
    {
        var payload = new JsonObject
        {
            ["options"] = new JsonObject
            {
                ["rotationSource"] = new JsonObject
                {
                    ["kind"] = "imported",
                    ["payload"] = FourPlateRotText,
                },
            },
        };

        var spec = WorldCrustRunSpec.FromExecutionPayload(payload);

        Assert.NotNull(spec.RotationSource);
        Assert.Equal(RotationSourceKind.Imported, spec.RotationSource!.Kind);
    }

    [Fact]
    public void Imported_graph_request_with_malformed_source_fails_before_generation_append()
    {
        using var service = new Service(new ServiceRegistry());
        var request = new WorldGenerationRequest(
            WorldId: "bad-import-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["rotationSourceKind"] = "imported",
                ["rotationSourcePayload"] = "1 0 90 0",
                ["rotationSourceName"] = "bad.rot",
            });

        var error = Assert.Throws<ArgumentException>(() => service.RunGenerationAsync(request));

        Assert.Contains("parse issue", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.GetOverviewAsync().IsDirty);
    }

    [Fact]
    public void Imported_graph_request_without_payload_fails_instead_of_using_generated_fallback()
    {
        using var service = new Service(new ServiceRegistry());
        var request = new WorldGenerationRequest(
            WorldId: "missing-import-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["rotationSourceKind"] = "imported",
            });

        var error = Assert.Throws<ArgumentException>(() => service.RunGenerationAsync(request));

        Assert.Contains("rotationSourcePayload", error.Message, StringComparison.Ordinal);
        Assert.False(service.GetOverviewAsync().IsDirty);
    }

    [Fact]
    public void Valid_imported_graph_request_commits_before_generation()
    {
        using var service = new Service(new ServiceRegistry());
        var request = new WorldGenerationRequest(
            WorldId: "valid-import-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["rotationSourceKind"] = "imported",
                ["rotationSourcePayload"] = FourPlateRotText,
                ["rotationSourceName"] = "four-plate-fixture.rot",
            });

        var result = service.RunGenerationAsync(request);

        Assert.True(result.Success);
        Assert.True(service.GetOverviewAsync().IsDirty);
    }

    [Fact]
    public void Production_service_uses_coordinator_materialization_without_direct_rot_reparse()
    {
        var serviceSource = File.ReadAllText(ProjectFile(
            "project/plugins/App.World/Services/Service.cs"));
        var coordinatorSource = File.ReadAllText(ProjectFile(
            "project/plugins/App.World/History/RotationImportCoordinator.cs"));

        Assert.DoesNotContain("new ImportedRotationProvider", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new RotParser", serviceSource, StringComparison.Ordinal);
        Assert.Contains("RotationModelMaterializer.MaterializeAsync", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("state.Payload.PlateCursor", coordinatorSource, StringComparison.Ordinal);
    }

    private static string ProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find project file '{relativePath}'.");
    }
}
