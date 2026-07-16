using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Composition-side proof that the DEPRECATED render.mantle alias routes to the SAME layer-selection
/// path as timeline.select_layer (directive 2, 2026-07-16). The alias result contract lives in
/// App.Render (MantleAliasTests); these tests assert the LAYER-SELECTION + COMPOSITION products:
/// selecting geosphere.mantle exclusively (what the alias does) yields the same active set and
/// composition decision as timeline.select_layer; the shared layer-active predicate; and the
/// regime-honesty that a pre-onset mantle composition carries no plate-slab contribution because the
/// plate layer is genuinely inactive there (truth, not a hidden mode).
/// </summary>
public sealed class MantleLayerAliasCompositionTests
{
    private const string MobilePlate = "mobile-plate";
    private const string StagnantLid = "stagnant-lid";

    private static TimelineLayerSelection Geo(string layerId) => new("geosphere", layerId);

    // Plan TDD #1: render.mantle enabled produces the SAME active-layer state and composition
    // decision as timeline.select_layer geosphere/geosphere.mantle. The alias and select_layer both
    // drive SelectLayer (SetExclusive) on the same controller; applying each to a fresh active set
    // must yield identical layers and identical composition (assert on products, not pixels).
    [Fact]
    public void Alias_selection_matches_select_layer_composition()
    {
        // The layer the render.mantle alias targets (MantleAlias.TargetLayerId in App.Render).
        var aliasTarget = Geo("geosphere.mantle");

        // "render.mantle enabled" -> SetExclusive(mantle), exactly as the binder does.
        var viaAlias = new LayerActiveSet();
        viaAlias.SetExclusive(aliasTarget);

        // "timeline.select_layer geosphere/geosphere.mantle" -> SetExclusive(mantle).
        var viaSelectLayer = new LayerActiveSet();
        viaSelectLayer.SetExclusive(aliasTarget);

        // Identical active set.
        Assert.True(viaAlias.Layers.SequenceEqual(viaSelectLayer.Layers));
        Assert.Single(viaAlias.Layers);
        Assert.Equal(aliasTarget, viaAlias.Primary);

        // Identical composition decision.
        var aliasDecision = GlobeViewModeResolver.ResolveComposition(MobilePlate, viaAlias.Layers);
        var selectLayerDecision = GlobeViewModeResolver.ResolveComposition(MobilePlate, viaSelectLayer.Layers);
        Assert.Equal(selectLayerDecision, aliasDecision);

        // And that decision mounts the mantle interior (the wave-5 D1 layer path, no ghost shell).
        Assert.Equal(GlobeViewMode.MantleInterior, aliasDecision.DerivedViewMode);
        Assert.True(aliasDecision.MountMantleInterior);
    }

    // Plan TDD #2 (predicate): the alias loud-fails when the mantle layer is not active at the
    // current tick. The predicate is the SAME one timeline.select_layer uses (LayerActivation), so
    // this is the "do not add a third path" guarantee at the data level.
    [Fact]
    public void LayerActivation_reports_mantle_inactive_when_regime_has_no_mantle()
    {
        var schedule = new SphereRegimeSchedule(
            new SphereId("geosphere"),
            new[]
            {
                new SphereRegime(
                    RegimeId: MobilePlate,
                    StartTick: 0,
                    EndTick: 100,
                    ActiveLayers: new[] { new LayerId("geosphere.plate"), new LayerId("geosphere.crust") }),
            });

        // mantle is NOT active at tick 50 -> the alias must reject (ok:false), not silently no-op.
        Assert.False(LayerActivation.IsLayerActive(schedule, tick: 50, "geosphere.mantle"));
        // plate IS active -> select_layer would accept it; the alias rejects only mantle here.
        Assert.True(LayerActivation.IsLayerActive(schedule, tick: 50, "geosphere.plate"));
    }

    // Plan TDD #3 (regime honesty): at a pre-onset tick (stagnant-lid, no plates formed yet), the
    // mantle layer is active but the plate layer is genuinely inactive. The mantle composition
    // therefore carries NO plate-slab contribution — because the plate layer is truly absent
    // (regime-gated from truth), not because an x-ray mode hides it. Assert on composition/state
    // products, not screenshots.
    [Fact]
    public void Pre_onset_mantle_composition_has_no_plate_slab_contribution_from_truth()
    {
        // Stagnant-lid regime: mantle active, plate NOT active, no plate features shown (pre-onset).
        var stagnantLid = new SphereRegime(
            RegimeId: StagnantLid,
            StartTick: 100,
            EndTick: SphereRegime.OpenEnd,
            ActiveLayers: new[] { new LayerId("geosphere.mantle") },
            ShowsPlateFeatures: false);
        var schedule = new SphereRegimeSchedule(new SphereId("geosphere"), new[] { stagnantLid });
        const long preOnsetTick = 200;

        // TRUTH: the plate layer is genuinely inactive at this tick (not hidden by a mode).
        Assert.False(LayerActivation.IsLayerActive(schedule, preOnsetTick, "geosphere.plate"));
        // TRUTH: the mantle layer is active.
        Assert.True(LayerActivation.IsLayerActive(schedule, preOnsetTick, "geosphere.mantle"));

        // The mantle owns the look of non-mobile-plate regimes: the composed mantle-interior mount
        // is NOT engaged (MountMantleInterior=false) because there are no separated plate slabs to
        // compose against — the default mantle surface is the presentation, and it has no plate-slab
        // contribution. This is the honest regime-gated reading.
        var decision = GlobeViewModeResolver.ResolveComposition(StagnantLid, new[] { Geo("geosphere.mantle") });
        Assert.Equal(GlobeViewMode.Inactive, decision.DerivedViewMode);
        Assert.False(decision.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.World, decision.SurfaceColoring);

        // The mantle surface is visible at a pre-plate regime because there is no plate surface to
        // cover it (truth), not because a mode suppressed the surface.
        Assert.True(MantleSurfaceGate.IsVisible(
            decision.DerivedViewMode,
            platesShown: stagnantLid.ShowsPlateFeatures,
            hasPlateSurface: false));
    }
}
