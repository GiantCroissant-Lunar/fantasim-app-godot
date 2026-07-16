using System.Text.Json.Nodes;
using FantaSim.App.Render;
using Xunit;

namespace App.Render.Tests;

/// <summary>
/// The DEPRECATED render.mantle alias (directive 2, 2026-07-16): render.mantle no longer toggles an
/// x-ray mode; it routes to the geosphere.mantle LAYER selection (the same path as
/// timeline.select_layer). These tests pin the Godot-free result contract — the deprecation note on
/// every successful result, the loud ok:false on rejection (never a silent no-op), and that the alias
/// targets the exact layer select_layer targets. The layer-driving + composition equivalence is
/// covered in App.World.Tests (MantleLayerAliasCompositionTests).
/// </summary>
public class MantleAliasTests
{
    [Fact]
    public void Target_is_geosphere_mantle_layer()
    {
        Assert.Equal("geosphere", MantleAlias.TargetSphereId);
        Assert.Equal("geosphere.mantle", MantleAlias.TargetLayerId);
    }

    // Plan TDD #1: render.mantle enabled -> the result carries the deprecation note + points at the
    // timeline.select_layer redirect. Asserts the RESULT contract (not pixels).
    [Fact]
    public void BuildResultJson_successful_activation_carries_deprecation_note()
    {
        var json = MantleAlias.BuildResultJson(ok: true, enabled: true, error: null);
        var obj = JsonNode.Parse(json)!.AsObject();

        Assert.True(obj["ok"]!.GetValue<bool>());
        Assert.True(obj["enabled"]!.GetValue<bool>());
        Assert.Equal(MantleAlias.DeprecationNote, obj["deprecated"]!.GetValue<string>());
        // The redirect names the exact command + sphere/layer the alias routes to.
        var redirect = obj["redirect"]!.GetValue<string>();
        Assert.Contains("timeline.select_layer", redirect);
        Assert.Contains("geosphere", redirect);
        Assert.Contains("geosphere.mantle", redirect);
    }

    // A deselection (enabled:false) is still a successful alias use, so it carries the deprecation
    // note too — the user is told to migrate either way.
    [Fact]
    public void BuildResultJson_successful_deselection_carries_deprecation_note()
    {
        var json = MantleAlias.BuildResultJson(ok: true, enabled: false, error: null);
        var obj = JsonNode.Parse(json)!.AsObject();

        Assert.True(obj["ok"]!.GetValue<bool>());
        Assert.False(obj["enabled"]!.GetValue<bool>());
        Assert.Equal(MantleAlias.DeprecationNote, obj["deprecated"]!.GetValue<string>());
    }

    // Plan TDD #2: the alias loud-fails (ok:false + message) when activation is rejected — NEVER a
    // silent no-op (the select_layer silent-failure gotcha burned gates twice; no third path).
    [Fact]
    public void BuildResultJson_rejected_activation_is_loud_ok_false_with_message()
    {
        const string reason = "Layer 'geosphere.mantle' in sphere 'geosphere' is not active at tick 200.";
        var json = MantleAlias.BuildResultJson(ok: false, enabled: true, error: reason);
        var obj = JsonNode.Parse(json)!.AsObject();

        Assert.False(obj["ok"]!.GetValue<bool>());
        Assert.Equal(reason, obj["error"]!.GetValue<string>());
        // A rejection must not masquerade as a successful deprecated alias (no deprecation banner).
        Assert.Null(obj["deprecated"]);
        Assert.Null(obj["enabled"]);
    }

    // Defensive: a rejected result with a null error still reports a non-empty message rather than
    // an empty ok:false (the contract is "always a clear message").
    [Fact]
    public void BuildResultJson_rejected_with_null_error_still_has_message()
    {
        var json = MantleAlias.BuildResultJson(ok: false, enabled: true, error: null);
        var obj = JsonNode.Parse(json)!.AsObject();

        Assert.False(obj["ok"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(obj["error"]!.GetValue<string>()));
    }
}
