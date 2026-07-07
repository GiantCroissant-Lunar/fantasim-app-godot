using FantaSim.App.World.Composition;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// D5 stacked active-set semantics (vault/specs/2026-07-07-...-directives.md, section D5). The set
/// is insertion-ordered; the primary is the first element (or null when empty). Toggling ON
/// appends (primary stays stable); toggling OFF shifts the primary when the head is removed.
/// SelectLayer replaces the set with exactly one element (back-compat single-select).
/// </summary>
public sealed class LayerActiveSetTests
{
    private static TimelineLayerSelection Geo(string layerId) => new("geosphere", layerId);

    [Fact]
    public void Empty_set_has_null_primary()
    {
        var set = new LayerActiveSet();
        Assert.Null(set.Primary);
        Assert.Empty(set.Layers);
    }

    [Fact]
    public void SetExclusive_replaces_with_exactly_one()
    {
        var set = new LayerActiveSet();
        var changed = set.SetExclusive(Geo("geosphere.crust"));

        Assert.True(changed);
        Assert.Equal(Geo("geosphere.crust"), set.Primary);
        Assert.Single(set.Layers);
    }

    [Fact]
    public void SetExclusive_same_value_is_noop()
    {
        var set = new LayerActiveSet();
        set.SetExclusive(Geo("geosphere.crust"));

        var changed = set.SetExclusive(Geo("geosphere.crust"));

        Assert.False(changed);
        Assert.Single(set.Layers);
    }

    [Fact]
    public void SetExclusive_different_value_replaces()
    {
        var set = new LayerActiveSet();
        set.SetExclusive(Geo("geosphere.crust"));

        set.SetExclusive(Geo("geosphere.plate"));

        Assert.Single(set.Layers);
        Assert.Equal(Geo("geosphere.plate"), set.Primary);
    }

    [Fact]
    public void Toggle_on_appends_without_changing_primary()
    {
        var set = new LayerActiveSet();
        set.SetExclusive(Geo("geosphere.plate"));

        set.Toggle(Geo("geosphere.crust"));

        Assert.Equal(2, set.Layers.Count);
        Assert.Equal(Geo("geosphere.plate"), set.Primary);
        Assert.Equal(Geo("geosphere.crust"), set.Layers[1]);
    }

    [Fact]
    public void Toggle_off_non_primary_keeps_primary_stable()
    {
        var set = new LayerActiveSet();
        set.SetExclusive(Geo("geosphere.plate"));
        set.Toggle(Geo("geosphere.crust"));

        set.Toggle(Geo("geosphere.crust"));

        Assert.Single(set.Layers);
        Assert.Equal(Geo("geosphere.plate"), set.Primary);
    }

    [Fact]
    public void Toggle_off_primary_shifts_primary_to_next()
    {
        var set = new LayerActiveSet();
        set.SetExclusive(Geo("geosphere.plate"));
        set.Toggle(Geo("geosphere.crust"));

        set.Toggle(Geo("geosphere.plate"));

        Assert.Single(set.Layers);
        Assert.Equal(Geo("geosphere.crust"), set.Primary);
    }

    [Fact]
    public void Toggle_is_idempotent_round_trip()
    {
        var set = new LayerActiveSet();
        set.Toggle(Geo("geosphere.mantle"));
        Assert.True(set.Toggle(Geo("geosphere.mantle")));
        Assert.Empty(set.Layers);
        Assert.Null(set.Primary);
    }

    [Fact]
    public void Toggle_always_returns_true()
    {
        var set = new LayerActiveSet();
        Assert.True(set.Toggle(Geo("geosphere.crust")));
        Assert.True(set.Toggle(Geo("geosphere.crust")));
    }

    [Fact]
    public void Insertion_order_preserved_across_toggles()
    {
        var set = new LayerActiveSet();
        set.Toggle(Geo("geosphere.plate"));
        set.Toggle(Geo("geosphere.crust"));
        set.Toggle(Geo("geosphere.mantle"));

        Assert.Equal(3, set.Layers.Count);
        Assert.Equal("geosphere.plate", set.Layers[0].LayerId);
        Assert.Equal("geosphere.crust", set.Layers[1].LayerId);
        Assert.Equal("geosphere.mantle", set.Layers[2].LayerId);
    }

    [Fact]
    public void SetExclusive_after_toggles_clears_to_one()
    {
        var set = new LayerActiveSet();
        set.Toggle(Geo("geosphere.plate"));
        set.Toggle(Geo("geosphere.crust"));
        set.Toggle(Geo("geosphere.mantle"));

        set.SetExclusive(Geo("geosphere.crust"));

        Assert.Single(set.Layers);
        Assert.Equal(Geo("geosphere.crust"), set.Primary);
    }

    [Fact]
    public void Distinct_spheres_are_independent_entries()
    {
        var set = new LayerActiveSet();
        set.Toggle(new TimelineLayerSelection("geosphere", "geosphere.crust"));
        set.Toggle(new TimelineLayerSelection("atmosphere", "atmosphere.weather"));

        Assert.Equal(2, set.Layers.Count);
        Assert.Equal(new TimelineLayerSelection("geosphere", "geosphere.crust"), set.Primary);
    }
}
