# Agent Summary — D1 (mantle layer) + D3 (crust thickness ratio lock)

Branch: `wt/2026-07-08-mantle-layer`. Four Conventional Commits, not pushed. The volumetric
field method (`MantleIsosurfaceExtractor`, `MantleAnomalyField`) is untouched; only presentation
and layer plumbing moved.

## Stage 1 — D3: RadialSectionProfile (radial source of truth + ratio lock)

**New contract type** `project/contracts/App.World/RadialSectionProfile.cs` (same assembly as
`PresentationLayers.cs` / `CrustLayerId`):
- Record at `:44` with real quantities: `DefaultCrustThicknessMetres` = 30,000,
  `LithosphereLidMetres` = 90,000, `CmbRadiusFraction` = 0.55,
  `PlanetRadiusMetres` = `PlanetLayerProjectionProfile.EarthLikePlanetRadiusMetres` (6,371,000).
- Two knobs: `CrustThicknessExaggeration` = 8.0 (`:75`), `MantleDepthScale` = 1.0.
- Derived helpers: `DisplayedCrustFraction(thicknessMetres)`, `DisplayedMantleDepthFraction()`,
  `DisplayedCoreSphereRadius()` (CMB x mantle scale, `:120`), `DisplayedCrustToMantleRatio()`
  (`:126`), and `ThicknessDepthScale()` (`:103`) — the metres-to-unit-radius scale
  `PlateSolidBuilder.Build` expects (`CrustThicknessExaggeration / PlanetRadiusMetres`).
- `MantleLayerExplodeFactor` = 0.4 (`:86`) — the D1 separated-slabs factor, as a fraction of
  `PlateSolidBuilder.DefaultMaxOffset`.

**Ratio-lock arithmetic (the pinned default):**
- Displayed crust fraction = 30,000 x 8.0 / 6,371,000 = **0.03767R**
- Displayed mantle depth = (1 - 0.55) x 1.0 = **0.45R**
- Ratio = 0.03767 / 0.45 = **0.0837** (pinned by `RadialSectionProfileTests` at tolerance 0.0005)

**Consumers wired:**
- `PlateSolidBuilder.Build` (`project/contracts/App.World.Rendering/Globe/PlateSolidBuilder.cs`):
  the `exaggeration` parameter is now documented as the THICKNESS depth scale, distinct from
  surface relief; the class-level invariant (`:68-76`) names the D3 decoupling. Math unchanged
  (`depth = thk_metres * scale`).
- `PlanetPresentationBinder.cs`:
  - `BuildExplodedSolidCrust` (`:650`) passes `_radialProfile.ThicknessDepthScale()` instead of
    `_lastExaggeration`. The `:97` coupling comment now documents the decoupling.
  - `BuildCoreSphere` (`:961`) reads `_radialProfile.DisplayedCoreSphereRadius()` instead of the
    `0.55f` literal; method went static -> instance. `MantleViewConfig` isovalue fields untouched.
- `BoundarySectionBuilder` / `CutawayStratumProfile` unchanged (follow-up consumer, noted in the
  profile's `<remarks>`).

## Stage 2 — D1: geosphere.mantle layer + separated-crust interior view

**Layer plumbing:**
- `PresentationLayers.cs:59` — `MantleLayerId = "geosphere.mantle"`.
- `GlobeViewMode.cs:62` — `MantleInterior` enum value; resolver branch at `:115`
  (`"geosphere.mantle" => GlobeViewMode.MantleInterior`), active only at mobile-plate.
- `SphereRegimeScheduleDefaults.cs:112` — `geosphere.mantle` joins the mobile-plate `ActiveLayers`
  alongside `geosphere.plate` and `geosphere.crust`. The timeline track appears automatically
  (label auto-derived as "Mantle" by `FriendlyLayerLabel`); follows the exact Crust-track pattern.

**Presentation composition** (new focused helper, per the 2,400-LOC binder hazard flag):
- `project/plugins/App.Presentation/MantleInteriorViewComposer.cs` (~70 LOC) — assembles the
  composed Node3D tree: core sphere + four isosurfaces + separated crust slabs, **NO ghost shell**
  (the D1 distinction from the x-ray path). Draw order: opaque inner cores, then slabs, then
  translucent outer halos. Root scaled x2 (house globe scale).
- `PlanetPresentationBinder.cs`:
  - `_mantleLayerRoot` / `_mantleLayerActive` state (`:91-92`), reconciled on view-mode transition
    in `ApplyTimelineTick` (`:419-423`): entering `MantleInterior` calls `RebuildMantleLayer()`;
    leaving frees the root.
  - `RebuildMantleLayer()` (`:835`) samples the field, builds the separated slabs at
    `MantleLayerExplodeFactor` (0.4) via `BuildExplodedSolidCrust(factorOverride)`, then calls
    `MantleInteriorViewComposer.Compose(...)`.
  - `BuildExplodedSolidCrust` (`:636`) takes an optional `factorOverride` so the layer path uses
    0.4 without disturbing `render.exploded`'s agent knob.
  - Visibility (`:432`, `:434`, `:443`): plate surface hidden, boundary arcs restyled + shown as
    locators, opaque mantle sphere hidden — mirroring the x-ray gates via a shared
    `mantleLocatorActive = _mantleXrayActive || _mantleLayerActive`.
  - Cleanup (`:2371-2377`): `_mantleLayerRoot` freed on rebind/teardown alongside `_mantleXrayRoot`.
  - Selecting a different layer fully restores the previous presentation (verified in code paths:
    `RebuildMantleLayer` frees the composed root; visibility switches restore the plate surface,
    boundary arcs, and opaque mantle to their non-mantle state).
- `PresentationComposition.cs:25-31` — `render.mantle` doc now points to the `geosphere.mantle`
  layer as the user-reachable path; `render.mantle` stays as agent look-dev.

## Verification

`task test` from the worktree root — **all 810 tests green** across 10 projects (including the 9
`App.Architecture.Tests` constitution tests). No flake hit; no engine/carto-style worktree-path
flake encountered; no App.World.Tests transient encountered.

New/extended tests:
- `RadialSectionProfileTests` — 5 tests (ratio-lock pin, default-constants-match, core-radius,
  crust-fraction visibility, exaggeration linearity).
- `GlobeViewModeResolverTests` — +2 tests (mantle -> MantleInterior at mobile-plate; Inactive
  elsewhere). Total now 13 test cases (incl. the 3-case Theory).
- `PlateSolidBuilderTests` — +1 test (`Default_profile_thickness_exaggeration_yields_visible_slab_walls`
  pins wall depth >= 0.03R). Total now 8.

Build: 0 errors, 0 warnings in changed files.

## What the lead must eye-verify windowed

The agent cannot run the windowed app. The lead runs the windowed gate (`task run:exported`):

1. **Mantle track appears** on the Geosphere lane at the mobile-plate regime, labeled "Mantle",
   alongside Plate and Crust. Clicking it selects `geosphere.mantle`.
2. **Interior + detached slabs compose**: the dark core sphere at 0.55R, the four anomaly
   isosurfaces (cold/warm inner+outer), and the crust as separated thick slabs at 0.4 explode
   factor — the slabs still read as a sphere, just detached (Sketchfab reference). NO translucent
   ghost shell.
3. **Regular plate surface is hidden** in this mode; the separated slabs are the surface reference
   frame. Boundary arcs stay visible as locators (restyled thin filaments).
4. **Crust thickness reads**: 30 km x 8.0 / 6,371 km ~ 0.038R slab walls — clearly visible against
   the 0.45R mantle depth (the old ~0.0009R invisibility is gone).
5. **Layer switch restores**: selecting Crust / Plate / World (or deselecting) fully removes the
   composed mantle-interior tree and restores the regular presentation. Switch back and forth to
   confirm no node leaks or stuck state.
6. **Ratio lock holds**: the crust:mantle proportion reads as the declared ~0.084 (crust ~0.038R
   against mantle ~0.45R). If the lead tunes a knob, the pinned test will fail until the ratio is
   consciously re-declared.

Known follow-ups (out of scope for this packet):
- Per-tick field resampling while the mantle layer is active (the composed root rebuilds on
  transition, matching the x-ray toggle lifecycle; scrubbing evolves the field only on re-toggle).
- `BoundarySectionBuilder` / `CutawayStratumProfile` as consumers of `RadialSectionProfile`
  (their own exaggeration stays separate for now — documented in the profile remarks).
- Slab top coloring in the mantle layer uses plate-identity (falls out of `_lastViewMode`); the
  look loop may prefer terrain/continents coloring on the slab tops.
