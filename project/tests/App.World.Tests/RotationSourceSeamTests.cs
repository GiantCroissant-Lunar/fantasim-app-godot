using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using FantaSim.App.World.Crust;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class RotationSourceSeamTests
{
    private const string FixtureName = "four-plate-test.rot";

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
    public void ImportedRotationProvider_parses_valid_rot_and_serves_plate_ids()
    {
        var provider = new ImportedRotationProvider("four-plate", FourPlateRotText, onsetTick: 0L);

        Assert.Equal(new[] { 1, 2, 3, 4 }, provider.ServedPlateIds.OrderBy(id => id));
    }

    [Fact]
    public void ImportedRotationProvider_malformed_rot_throws_clear_error()
    {
        const string malformed = "1 0.0 90.0 0.0\n2 not_a_number 0.0 5.0 0\n";

        var ex = Assert.Throws<ArgumentException>(() =>
            new ImportedRotationProvider("bad", malformed, onsetTick: 0L));

        Assert.Contains("parse issue", ex.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportedRotationProvider_empty_rot_throws_clear_error()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new ImportedRotationProvider("empty", "# just a comment\n\n", onsetTick: 0L));

        Assert.Contains("no rotations", ex.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportedRotationProvider_unmapped_plate_returns_identity_at_onset()
    {
        var provider = new ImportedRotationProvider("four-plate", FourPlateRotText, onsetTick: 0L);
        var rotation = provider.RotationFromOnsetTo(plateId: 999, tick: 500_000L);

        Assert.Equal(UnifyMaths.Quaternion.Identity, rotation);
    }

    [Fact]
    public void FourPlateFixtureFile_is_valid_and_parses()
    {
        var text = ReadFixtureText(FixtureName);
        var provider = new ImportedRotationProvider(FixtureName, text, onsetTick: 0L);

        Assert.Equal(4, provider.ServedPlateIds.Count);
    }

    private static string ReadFixtureText(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var dir = Path.GetDirectoryName(assembly.Location)
            ?? throw new InvalidOperationException("Could not resolve test assembly directory.");
        var path = Path.Combine(dir, "Fixtures", name);
        return File.ReadAllText(path);
    }
}
