# AGENT-SUMMARY — D5 first slice (stacked active layer set)

Branch: `wt/2026-07-08-stacked-layers`. Spec:
`vault/specs/2026-07-07-layer-presentation-input-parity-canonical-units-directives.md` (section D5;
D7c read as context only — D7 out of scope). Status: **implementation + tests complete; not
pushed; windowed eye-check is the lead's job**.

## What changed

Timeline layer selection moved from single-select to a **stacked active set** (per-sphere, several
layers active at once). The presentation composes every active layer's contribution.

**Selection model** (`contracts/App.World/Composition/`)
- `ITimelineController.ActiveLayers` (`IReadOnlyList<TimelineLayerSelection>`, insertion-ordered)
  and `ToggleLayer(sphereId, layerId)`. `SelectLayer` is preserved as "set becomes EXACTLY {layer}"
  (single-select back-compat). `SelectedLayer` is now the DERIVED **primary** (first element, or
  null when empty) so graph followers (`WorldGenerationTimelineGraphBinding`,
  `PlanetGenerationGraphSource`) and the atmosphere-rim gate (`WorldViewContentGate`) keep working
  unchanged — they read the primary, which is null exactly when the set is empty.
- `LayerSelectionChanged` now fires on EVERY set mutation (carrying the new primary, even when the
  primary itself did not change) so stacked-set consumers reading `ActiveLayers` react to every
  toggle. Graph followers re-follow on the same event (their no-op-when-already-current guard
  absorbs the redundant firing).
- New `LayerActiveSet` (pure, Godot-free) owns the ordered-list + primary semantics; both production
  controllers delegate to it. Default interface implementations (`ActiveLayers => empty`,
  `ToggleLayer => no-op`) keep the four single-select test fakes compiling unchanged.

**Composition resolver** (`GlobeViewMode.cs` + new `LayerCompositionDecision.cs`)
- `GlobeViewModeResolver.ResolveComposition(regime, activeLayers, plateViewOverride)` returns a
  `LayerCompositionDecision`: `DerivedViewMode` (nearest existing `GlobeViewMode`), a
  `MountMantleInterior` flag, a `SurfaceColoring` owner (`SurfaceColoringKind`), and a declared
  `TerrainRelief`. The combo rules are documented on the method.
- `GlobeViewMode` stays the DERIVED value for the binder's transition/lighting/boundary/cutaway
  gates (bounded churn). A full dissolution into per-layer presentation contributions is the D7b
  follow-up (see below).

**Ingress** (`App.Timeline.Seam/HostComposition/TimelineComposition.cs`)
- New `timeline.toggle_layer` command mirroring `timeline.select_layer`. Guarded ON (layer must be
  schedule-active at the current tick); permissive OFF (a stale set can always be cleared). Returns
  the full `activeLayers` array so track-button multi-highlight stays in sync.

**Timeline face** (`App.Timeline.Seam/TimelineFace.cs`)
- `OnTrackPressed` now issues `timeline.toggle_layer` / `ToggleLayer` (was `select_layer`).
- Track-button styling highlights EVERY active track (multi-select), not just the single primary.
- Edits localized to `OnTrackPressed` + the style block. No `TimelineFace` zoom/playhead work
  touched (owned by a parallel agent).

**Binder** (`App.Presentation/PlanetPresentationBinder.cs` + new `SurfaceColoringKindExtensions.cs`)
- Resolves a `LayerCompositionDecision` from `ActiveLayers` at bind, on every tick apply, and on
  every `LayerSelectionChanged`. Drives:
  - mantle mount/free from `decision.MountMantleInterior`;
  - the plate-surface build/bind from a **surface-appearance mode** (`decision.SurfaceColoring`
    mapped back to a `GlobeViewMode` via `ToSurfaceViewMode()`);
  - everything else (lighting, boundaries, cutaway gate, status indicator) from the DERIVED mode.
- The separated-slab-top coloring (`BuildExplodedTopDto`) follows the surface-coloring owner
  automatically — it reads the cached `_lastIsTerrain`/`_lastViewMode` state that the plate-surface
  bind now populates from the coloring owner. No change to `BuildExplodedTopDto` itself was needed.
- The mantle reconcile also rebuilds the slabs when the surface coloring changes while mantle stays
  active (e.g. `Mantle+Crust` -> `Mantle+Crust+Plate`), so slab tops never go stale.

## Combos the windowed eye-check must cover

Lead: verify each of these in the exported windowed app (`task run:exported` + hot-reload per
`.agent/rules/bundle-hot-reload-verify.md`). The mantle path is heavy; toggle, watch the slab tops
recolor, then toggle off and confirm the mantle root is freed (look for `Mantle interior layer
mounted` / the `MantleInteriorLayer` node disappearing).

| Active set | Expected look |
|---|---|
| (empty) | World view (bare-rock terrain). |
| `{Crust}` | Hypsometric terrain (elevation tint, flat-zero plate identity gone). |
| `{Plate}` | Continents (land/ocean by continental fraction). With `globe:plateView=identity`, PlateIdentity flat caps. |
| `{Mantle}` | Interior + separated slabs. **Behavior change:** slab tops now show the World terrain ramp (was identity). Confirm this reads as "a separated terrain shell" — if it looks wrong, the fallback is `SurfaceColoringKind.PlateIdentity` for mantle-alone. |
| `{Mantle, Crust}` | Interior + slabs whose TOPS are hypsometric terrain. **Key D5 combo.** |
| `{Mantle, Plate}` | Interior + slabs whose TOPS are continents (identity under override). **Key D5 combo.** |
| `{Plate, Crust}` | Continents coloring (identity wins the surface). **Not yet realized:** terrain relief geometry does NOT stay in this slice — the surface is flat continents. See "Known gaps". |
| `{Mantle, Plate, Crust}` | Interior + slabs with continents tops (plate wins the surface over crust). |
| Any non-mobile-plate regime | Inactive (mantle-era look, no layer switching). |

Also: confirm track buttons multi-highlight (every active track in the `SelectedStyle`), and that
toggling a second layer on does NOT unhighlight the first.

## Known gaps / behavior changes

1. **`Plate+Crust` terrain relief not realized.** The resolver declares `TerrainRelief=true` for
   this combo (identity coloring WITH terrain relief geometry, per D5), but the first-slice binder
   builds the plate surface from the surface-appearance mode alone — `Continents` produces flat
   caps. The resolver test (`Plate_plus_crust_continents_coloring_with_declared_terrain_relief`)
   asserts the declared intent; realizing combined identity-color + terrain-relief needs the
   cap-mesh branch to read `TerrainRelief` independently of the coloring mode. This is the natural
   next slice.

2. **Mantle-alone slab tops changed from identity to World terrain.** Per the D5 coloring-owner rule
   ("else default World look"), the slabs now carry the World terrain ramp. Wave-5 had them as flat
   identity. Eye-check #4 above decides whether this stays.

3. **`GlobeViewMode` is still the binder's plumbing currency.** A full dissolution (D7b) would
   replace it with per-layer presentation contributions and declared composition nodes (graph-wired).
   What that needs: (a) split `BindPlateSurface`'s appearance axes (coloring, relief fabric, color
   mode, normal mode, material tuning, projection profile) so each is driven by its own decision
   field instead of a single `GlobeViewMode`; today those helpers still take the derived mode and
   are only correct-by-coincidence for mantle combos (the plate surface is hidden, so only the
   cached slab-top state matters); (b) move the composition rules out of the C# resolver and into
   graph wiring (per D7b); (c) retire `LayerCompositionDecision.DerivedViewMode`.

## Test coverage

- `LayerCompositionDecisionTests` — every combo (empty / single / mantle-pair / plate+crust /
  triple / atmosphere-only / unknown / order-invariant / non-mobile-plate). 13 facts.
- `LayerActiveSetTests` — toggle/select/primary-stability/clear/distinct-spheres. 12 facts.
- All existing resolver / gate / selection tests unchanged and green.
- Full suite: **1003 passed, 0 failed across 17 projects** (`dotnet test project/FantaSim.sln`).
  The known `App.World.Tests` ~1-in-3 transient did not surface in this run; re-run if a single
  unrelated test flakes.

## Files

New: `LayerActiveSet.cs`, `LayerCompositionDecision.cs`, `SurfaceColoringKindExtensions.cs`,
`LayerActiveSetTests.cs`, `LayerCompositionDecisionTests.cs`.
Modified: `GlobeViewMode.cs`, `ITimelineController.cs`, `TimelineController.cs` (World.Seam),
`PlanetPresentationBinder.cs` (incl. nested `PlanetTimelineController`), `TimelineComposition.cs`,
`TimelineFace.cs`.

## Out of scope (per directive)

Node graph, field/isosurface machinery, `App.Camera`/`App.Timeline` zoom code, D6 playhead/zoom,
D7a graph-panel toggle, D7b graph-wired composition rules, D7c per-track dropdown detail.
