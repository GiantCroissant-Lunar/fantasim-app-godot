# Session record — World-view lock, W3a cutaway, node inspector, review fixes, host-slim

> **Date:** 2026-07-03 (afternoon/evening; continues the same-day
> [world-view-arc handover](2026-07-03-world-view-arc-handover.md)) · **Repos:**
> `fantasim-app-godot` (most work), `fantasim-world`, `fantasim-cartography` ·
> **Result:** the world-view LOOK is user-locked; the cross-section cutaway (W3a) is live and
> windowed-verified; the node graph got compact cards + an inspector; all 5 external-review
> findings are fixed; the host is now a thin composition root; the ALC hot-reload pin is fixed
> (separate session); git is cleaned to main-only and the app repo is pushed.

## 1. TL;DR / repo tips

- `fantasim-app-godot` main `9709c45` (host-slim). **Pushed to origin** (behind 0, ahead 0).
  41 commits landed today. Full suite green (~600 tests across the App.* projects).
- `fantasim-world` main `5cc3118` (truth-stream identity validation). **NO REMOTE — local only.**
- `fantasim-cartography` main `743f8db` (projection selector honesty). **NO REMOTE — local only.**
- Git is clean: every repo is main-only, one worktree, zero uncommitted. All `agent/*` +
  legacy branches deleted (all were merged), all worktrees removed, orphan `.worktrees/` folders
  cleaned.

## 2. THE OPEN ITEM — where to start next session

The world-view look is LOCKED and the user is happy with it (Mars-like face, undulating limb,
albedo provinces). The immediate visual follow-up is small and cheap:

- **Cut-face brightness (W3a).** The cutaway cut faces read near-black in the windowed capture:
  the schematic lithosphere/mantle strata tones (`CutawayStratumProfile.LithosphereColor` 0.18/
  0.10/0.06, `MantleColor` 0.08/0.04/0.03) are too dark under the scene lighting, so the wedge
  reads as a void bite rather than solid strata. First tweak: brighten those tones, or render the
  cut faces **unshaded** (they are schematic, not lit surfaces). Drive it on captures via
  `render.cutaway` + `render.screenshot` (see §6). The crust band (the one TRUTH stratum) is fine.

After that, the roadmap (§5) resumes at **W3b** (slab geometry in the cutaway) or the user may
pick a different lane. Ask.

## 3. What landed this session (chronological, all on main)

### 3a. World-view look — FIXED then LOCKED (the morning handover's §2 open item)
- **Root cause found (not what we thought):** the base MANTLE SPHERE (teal, radius 0.96)
  OCCLUDED sunken terrain — elevations below ~-4000 m displaced beneath it, so the face-on disk
  WAS the mantle ball. Fix = `MantleSurfaceGate` (pure, contracts tier): mantle renders only when
  it owns the look (Inactive regime / plates hidden / no plate surface). At mobile-plate the
  watertight caps are the surface, mantle hidden.
- **Rank-equalized `WorldTerrainRamp`** (histogram equalization, ties = avg rank) so a low-heavy
  elevation distribution shows the full rust/ochre vocabulary instead of collapsing to near-black.
  `HypsometricTint` (crust diagnostic) stays value-linear on purpose.
- **Rim fresnel** pow 3→6 (`u_falloff` uniform): confines the atmosphere glow to the limb.
- **Dispatch round (3 agents):** `VertexColorEnvelope` (per-vertex color smoothing, kills the
  chunky per-cell "baseball seam"); `ProvinceTint` (low-freq continental albedo provinces,
  amplitude 0.12); **growth-diagnosis** (read-only) root-caused "terrain identical 105M vs 119M"
  = (a) `GenerationChanged→Rebind()` refetched at the parameterless default `PlateOnsetTick`,
  overwriting playhead terrain + resetting snapshot tracking; (b) service built snapshot series at
  500k-tick spacing ("5 Ma" misread) vs the trigger's 5M window → playheads selected
  never-generated ticks. Both fixed (`CrustSnapshotTickSeries.DefaultSpacingTicks` = 5M shared;
  `GenerationChanged` routes to `ScheduleRegimeRefresh` once a doc is bound).
- **Look-dev on references** (user's Reddit + mattkeeter.com/projects/planets links): produced two
  locked decisions —
  - **Non-linear height lens** (S1 doctrine AMENDMENT): world view displaces by
    `sign(h)·|h|^0.5 · 5e-4`. The truth field is ~±1,400 m interiors under 21,000+ m unbounded
    orogenic extremes — no LINEAR factor can render both. `BuildSurfaces(..., heightExponent=1.0)`
    default = old linear path exactly; the S2 indicator NAMES the profile
    (`vertical h^0.5 x5e-4 units`). **Diagnostic views stay strictly linear.**
  - **Everywhere-relief fabric** (user reframe, accepted): old waterless worlds are rough
    everywhere — flat interiors were MISSING PHYSICS (impacts/pre-onset orogeny/erosion), not
    honesty. `WorldPeaks` promoted from grid-hiding garnish to declared stand-in: freq 8, 6
    octaves, nominal amplitude 17,000.
  - **Calibration gotcha (documented upstream):** `NoiseRelief.Amplitude` is a BOUND, not a
    typical magnitude — measured std ≈ 0.15 × Amplitude, extremes ≈ ±0.45 ×. Size visible relief
    as `Amplitude ≈ target_std / 0.15`. Documented + characterization-tested in
    `fantasim-cartography` (consumed by project-reference, so it flows without repack).
  - Locks: app spec §5c-i (`vault/specs/2026-07-02-planet-evolution-arc-design.md`) + the S1
    amendment in `fantasim-world/vault/architecture/terminology-strata-scale-resolution.md`.

### 3b. W3a cutaway (agent/cutaway-mask + lead integration fixes)
- Pure `CutawayWedge` + `CutawayStratumProfile` (App.World.Composition). Crust thickness plumbed
  onto the presentation doc from the real `crust-thickness-m` field. `render.cutaway
  {"azimuthDeg":N,"widthDeg":N}` ingress command (width 0 clears).
- **Lead fixes the agent's unit tests couldn't catch (windowed-only):** (1) the wedge tested
  view-space `VERTEX` in `fragment()` → it followed the camera; fixed via a `vertex()` varying to
  capture model space. (2) diagnostic views must never clip → per-tick `GlobeViewMode.World` gate.
  (3) a regime/snapshot rebind replaced PlanetBody and dropped an active cutaway → `BindDocument`
  re-applies. (4) the Earth-radius metres→unit-globe anchor is a named const with the S3 upgrade
  path documented.

### 3c. Node inspector (agent/node-inspector)
- Compact node cards (title + one-line summary only; the giant property lists are gone) + a
  right-docked Inspector panel following the selected node. Pure `NodeInspectorFormatter` + tests;
  `GraphEdit.NodeSelected → source.Dispatch("select-node:…")`.

### 3d. External review — all 5 findings fixed (verified against source first)
- **Truth-stream identity** (fantasim-world, was ranked highest by doctrine): `DeserializeEvent`
  discarded the stored `TruthStreamIdentity` and re-badged with the caller's `expectedStream` — a
  cross-stream/corrupt read would surface as a valid-looking event whose identity ≠ its hash
  preimage. Now verified field-by-field, throws loudly. Agent confirmed the read path does NOT
  re-verify the hash chain, so this is the only guard.
- **`world.orchestrate` propagation** (app): inner failure used to serialize as the ResultJson of
  a SUCCESSFUL outer result. Now propagates via a typed `CommandFailedException`; two-level Ok
  semantics documented on `ExecuteAsync`.
- **ProductTick scoping** (app): the selected snapshot tick applied to EVERY layer's ProductTick
  while only crust's address was rewritten. Now scoped together.
- **glb_path JSON** (app): string interpolation → `JsonSerializer.Serialize`.
- **Projection selector** (cartography): recommended unimplemented projections → constrained to
  implemented ids, aspirational targets kept as docs, guard test added.

### 3e. Host-slim (user directive: "nothing but Host.cs-level composition in the host")
- **P1+P2 (agent/rendering-contracts):** render-shared types (GlobePlateSurfaces, the ramp/tint
  mappers, cutaway types, VerticalScaleLabel) → NEW `contracts/App.World.Rendering`. The host's
  `ProjectReference` into the collectible `App.World` is REMOVED (the dual-copy type-identity
  fragility). Shared closure expanded (Cartography.Globe.Core/.Contracts, Cartography.Shared.
  Contracts, UnifyMaths.Numerics, App.World.Rendering) in BOTH the Bootstrap SharedAssemblyPolicy
  AND the Taskfile `bundle:world:build` mirror.
- **Lead fix (windowed FileNotFoundException):** cutting that ProjectReference silently dropped
  `CrosscutFoundation.Persistence.Contracts` + `UnifySerialization.MessagePack.Runtime` from the
  host export (they only reached it transitively) → crust codec type-init threw. Host now pins
  both packages explicitly at 0.2.0. **LESSON: when cutting a ProjectReference, diff the export
  closure against the shared-policy promise.**
- **The move:** `World/*` → NEW `plugins/App.Presentation` (Godot SDK, namespace
  `FantaSim.App.Presentation`; types internal behind the public `IPlanetPresentation` +
  `PresentationComposition` seam). `SceneTierPckWatcher` → `App.Resource.Bundle.Seam` (public,
  gained the App.Command contract ref). `complete-app/` now holds ONLY `Host.cs`.
- **RESIDENT now, BUNDLE-READY:** App.Presentation's file layout is exactly what a collectible
  flip (P3–P5 mount protocol, alc-pin diagnosis §4) needs — no more file moves.

### 3f. Git cleanup + push (see §1)

## 4. ALC hot-reload pin — FIXED (separate spawned session; read its handover)
[2026-07-03-alc-pin-fix-handover.md](2026-07-03-alc-pin-fix-handover.md): the real root was
MessagePack's source-gen resolver static `AssemblyResolverCache` rooting bundle assemblies forever.
`BundleHost.UnloadCoreAsync` now evicts collectible-keyed entries on unload; **"old ALC collected"**
verified repeatedly for world AND timeline. Scene-tier pcks are now watched (`SceneTierPckWatcher`).
This UNBLOCKS the App.Presentation collectible-bundle flip (P3–P5) that would make planet look-dev
hot-reloadable.

## 5. Roadmap / open items (priority order)

1. **Cut-face brightness** (§2) — cheap look tweak, first thing.
2. **W3b cutaway** — slab geometry from the boundary network + polarity in the wedge (the next
   cutaway increment; W3a did the wedge mask + one truth stratum). User's textbook cross-section refs.
3. **A4 maturity (truth-side, would let the lens relax toward linear):** orogenic erosion/
   saturation (peaks honestly land ±9 km instead of 24 km spears), continents (ContinentalFraction
   growth → real albedo provinces), crust-age/impact roughness (the truth replacement for the
   sphere-fixed noise fabric so it drifts with plates).
4. **Sub-project B — native tscn timeline** (AnimationPlayer continuous playback + emergence-window
   zone + `CrustSnapshotTicks` cache strip; the 2026-06-22 spec). Now has real evolving terrain +
   working snapshot spacing to play back.
5. **Sub-project C — fracture emergence** (presentation-side AnimationTree crossfade).
6. **App.Presentation → collectible bundle (P3–P5)** — the structural payoff: makes look-dev
   hot-reloadable. Unblocked by the pin fix. Mount-protocol contracts + manifest, no file moves.
7. **Hydrosphere lane** — only after the waterless world fully reads; truth question still open
   (scalar ocean volume + derived shorelines vs per-cell water depth).
8. **Publish decision:** `fantasim-world` + `fantasim-cartography` have NO git remote — today's
   work there (truth-stream fix, projection fix) is local-only. User's call whether to create the
   GitHub repos + push.
9. Smaller: faceted flat-shading world-view variant; more fabric amplitude; per-world palette
   parameters; S3 world-radius anchor parameter (unblocks honest ×N in both VerticalScaleLabel and
   the cutaway anchor); flaky `CellReassignmentTests` perf budget test.

## 6. Operational knowledge (how to work on this)

- **Run/verify:** `task build` / `task test` from the app repo root. Export via
  `task build:godot:desktop` — **unify-build extracts to its OWN version dir** (grep the log for
  "Extracted runnable bundle ->", historically `_artifacts/0.1.2/`), while `task bundles` writes to
  the GitVersion dir — copy PCKs into `<app>.app/Contents/MacOS/bundles/` manually (the dir is
  wiped each export; recreate it: `mkdir -p …/bundles && cp <newest bundles dir>/*.pck …/bundles/`).
- **Launch windowed:** `remote__enabled=true FANTASIM_REMOTE_ENABLED=1 <app-binary>` (ABSOLUTE
  path — the shell cwd resets between tool calls). Drive via
  `python3 tools/fantasim-cmd.py cmd <command> '<json>'` — note the `cmd` subcommand:
  `timeline.seek {"tick":N}`, `render.cutaway {"azimuthDeg":N,"widthDeg":N}`, `render.screenshot`
  (returns an absolute PNG path; Read it / SendUserFile it — the user reasons visually). Mobile-plate
  onset ~100M, maxTick 120M; crust generates on regime entry (~10–15 s after first seek).
- **Hot-reload (the intended workflow, now working):** keep the exported app open; edit a bundle
  tier; `task bundle:<tier>` + copy the pck into the running app's `…/MacOS/bundles/`; the watcher
  reloads; confirm **"old ALC collected for bundle <x>"** in the app log. Full re-export ONLY for
  resident/host/T1-contract/T4-seam changes — but note the planet presentation is STILL resident
  (App.Presentation), so look tweaks still need a re-export until the P3–P5 collectible flip.
- **Delegation flake (recorded in memory [[delegation-model-cost]]):** headless `opencode run`
  through the oh-my-openagent "Sisyphus" orchestrator EXITED EARLY 4× today across providers
  (spawns sub-agents, then exits "waiting" for notifications that never resume headless). 6+ direct
  `ollama/glm-5.2:cloud` runs completed fine. ALWAYS verify by artifacts (`git log main..HEAD`,
  `git status`) — wrapper exit 0 means nothing. Retry once; if it dies twice, do it INLINE (the
  host-slim move was ~30 min inline after two dead dispatches).
- **The windowed app is the only real gate.** Every fix this session that headless missed —
  camera-relative wedge, mantle occlusion, the FileNotFoundException from the dropped transitive
  assemblies — surfaced only in the exported windowed run. Screenshot everything.

## 7. Next-session entry point

1. Read §2 (cut-face brightness) and §5 (roadmap).
2. Relaunch the exported app (`_artifacts/0.1.2/…/complete-app`, ABSOLUTE path, remote env), seek
   105M, `render.cutaway` to see the current cut-face darkness.
3. Brighten / unshade the cut faces, re-export (host-resident), capture, send.
4. Then pick a lane with the user: W3b cutaway, A4 truth-side maturity, sub-project B timeline, or
   the App.Presentation collectible flip.
