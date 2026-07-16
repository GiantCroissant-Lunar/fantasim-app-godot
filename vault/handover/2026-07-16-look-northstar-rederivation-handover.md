# 2026-07-16 handover — the look misunderstanding, named; north-star re-derivation owed

**For the next session. READ THIS FIRST, before any planet-look proposal or fix.** The user
ended the 2026-07-16 session with a reference image and this statement (verbatim):

> "I expect to see the world like attached image (no biome, no hydrology) all the time, but
> I keep seeing egg shell. I think agents all misunderstood me for months."

This document is the discussion foundation the user asked for. It contains: (1) what the
user actually wants, precisely; (2) the locked decision that collides with it and must be
re-derived WITH the user; (3) why months of agents kept producing eggshell — the five
stacked causes; (4) everything that shipped 2026-07-16 and what is in flight; (5) the
proposed reconciliation and next-session agenda.

---

## 1. The acceptance criterion, captured at last

**The reference image (attached in the 2026-07-16 session chat):** a stylized procedural
planet ("cartoon mode" screenshot from a procedural-planet-generator tool; UI sliders:
Scale_factor 1.1, Height shift 1, Mesh Flatness 0.7, smoothing normals 21%, water level,
cartoon/realistic shader toggle). Its properties — these ARE the acceptance criterion:

- **Bulk everywhere.** Every part of the surface carries chunky, visible relief. No smooth
  regions. The terrain has physical presence at planet scale (the Keeter reference from the
  same day is the same aesthetic family — as is kenny.wtf, referenced in the ORIGINAL
  waterless-worlds lock e3b84ef).
- **Lumpy silhouette.** Mountains visibly break the limb. The limb is NOT a circle. Eyeballed
  relief is on the order of a few percent of radius — far above the currently-clamped 0.5%.
- **Chunky legible geometry.** Low-poly faceted reading; features are solid masses, not
  surface decoration on a ball.
- **EXPLICITLY EXCLUDED by the user: biome and hydrology.** The image's green/blue/white
  coloring is NOT the ask (waterless lock stands; biomes deferred). The ask is the SHAPE.
- **"ALL THE TIME":** this look at every tick, every regime — a magma-ocean world, a
  stagnant-lid world 7 Ma post-onset, and a 100 Ma mobile-plate world must ALL read as rocky
  bodies with bulk. Never an eggshell at any point on the timeline.

## 2. The collision: the 2026-07-05 planet-look north-star encoded the misunderstanding

`vault/specs/2026-07-05-planet-look-north-star.md` (@a69087b) locked, after the user
"rejected the crust look AGAIN":

- silhouette <= 0.5%R, enforced by a UNIT-TESTED clamp (active since @8e86bec, fitted lens);
- "planets are <=0.15% relief = circular limb" as the diagnosis of what was wrong;
- interior fabric <= 0.15x belts — "chains not crumple", calm interiors;
- the rejected look was diagnosed as "Astroneer rocky body" drift, and lumpy silhouette +
  everywhere-crumple were classified as ASTEROID CUES to eliminate.

**The 2026-07-16 reference image has a lumpy silhouette, everywhere-texture, and >0.5%R
relief.** The user now states agents misunderstood for months. The most consistent reading:
the 07-05 rejections were rejections of SPECIFIC bad renders (uniform fBm crumple with no
structure — noise WITHOUT form), but the correction over-rotated to "circular limb + calm
interiors," which the user never wanted either. The user's consistent references (kenny.wtf
in the July-2 lock, Keeter and the cartoon planet on 07-16) all show CHUNKY BULK EVERYWHERE.
What the user rejects is structureless noise AND smooth eggshells; what they want is FORMED
bulk everywhere.

**Doctrine handling:** the north-star is a user-locked decision → it requires an EXPLICIT
user unlock/re-derivation (settled-decisions rule). Next session's first agenda item is that
conversation, against the reference image, clause by clause: silhouette budget (0.5%R → a
few %R?), interior fabric budget (0.15x belts → parity with belts?), smooth normals
(→ faceted?), and the fitted-lens policy. Nothing else should be attempted before that
re-derivation — every look fix downstream tunes to whatever the new numbers are.

## 3. Why agents produced eggshell for months — five stacked causes (all now identified)

1. **Presentation suppressors, deliberately installed:** the silhouette clamp (0.5%R,
   unit-tested); the interior amplitude multipliers (0.15x); the 250 m residual cap
   (TectonicDetailSampler.MaxResidualAmplitudeMetres — confirmed 2026-07-16 morning as
   CURRENT INTENDED design per the 07-05 north-star, deliberately below the 800 m boundary
   signal); the budget-fitted height lens (~2.8e-7/m at world view). Each was a rational
   consequence of the 07-05 lock. Together they guarantee an eggshell.
2. **Truth starts smooth:** crust is born with near-zero relief in truth (CellElevations
   accumulates from collisions only — at 107M/7 Ma post-onset, hundreds of metres). There is
   NO birth-roughness component anywhere in truth or in honest derived form. (The in-flight
   born-rough slice addresses the derived half; see §5.)
3. **The one interior texture that existed is sphere-fixed:** WorldPeaks fabric does not
   ride plates (documented defect) — it is wallpaper, not crust, and it is capped tiny by (1).
4. **Built-vs-wired reporting:** machinery (adaptive subdivision, relief fabric, detail
   samplers) kept being reported as delivered while the rendered product stayed uniform/flat
   — no falsifiable gates. 2026-07-16 fixed this pattern for LOD (density-ratio test + wireframe
   toggle; the gate FAILS on uniform output) — the same discipline must apply to the look
   re-derivation (define the acceptance render BEFORE tuning).
5. **Narrowing diagnosis loop:** every look complaint got translated into a narrower
   parameter fix inside the locked frame, instead of re-asking "what should the planet look
   like at ANY random tick?" The user's one-sentence criterion (§1) was never captured as
   the gate until now.

## 4. What shipped 2026-07-16 (all local commits, NOTHING pushed — user's call)

App repo (fantasim-app-godot main): `7c5a02f`+`57d3074`+`bf934fd`+`06bce87` (directive spec
rounds 1-3 + plans), `9a68527`+`de2d27b`+`5d29924`+`f67b639` (tunnel default arc — boots into
tunnel; 3 boot defects found via 5 windowed gate rounds), `e4a217e` (x-ray residue retired
into geosphere.mantle layer path; loud deprecated alias), `3c43cd8` (VISIBLE adaptive LOD:
boundary-band density 4.52x interior, budgeted, deterministic, wireframe via
`render.lod {"mode":"wireframe"}`; lead closed two wiring gaps — production adoption in
LayerProjectionProfileResolver + fallback renderer), `2854008` (slab formed relief:
SlabTopReliefComposer, lit strata walls, ratio-locked to RadialSectionProfile).

Windowed-verified (export built 2026-07-16, verify-windowed identity gates, screenshots in
app_userdata/complete-app/screenshots/): boot→tunnel chain; wireframe nonuniform tessellation
tracking boundaries (120627); slab mountains at tick 200M vs faithful-flat at 107M (121451 vs
120707) — which triggered the user's final clarification.

Hub vault: research deposit `2026-07-16-terrain-diffusion-evaluation.md` (@2ac80df2 + later
amendments) — verdict, corrections ledger, adopted invariants (conditioning on causal
context, deterministic identity, no query-history). plate-projects: UnifyMaths.Numerics noise
primitive @ae6bb90 (bit-exact NoiseRelief port, 105k-comparison parity), unify-topology S2
children/range/cover @ae8c9c6, pcg-rng-port cross-runtime goldens @e2909cc.

Key context docs: `vault/specs/2026-07-16-layer-first-presentation-directives.md` (directives
1-4 + rounds 2-3: tunnel persistent-apparatus 1c; no-flat-plates 3b; motion-in-slab 3c; LOD
4b; born-rough 3d; ABSOLUTE SCALE LOCKED), the three plan docs of the same date, and hub
`vault/research/2512.08309v4.pdf` (the terrain-diffusion paper the user cites as the standing
motivation for detail-as-conditioned-field).

## 5. In flight at session end

- **born-rough crust slice** (plan `vault/plans/2026-07-16-born-rough-crust-plan.md`,
  worktree `yokan-projects/.worktrees/lf-born-rough`): plate-anchored deterministic birth
  roughness, age-conditioned, derived-only. First zai dispatch DIED to the known Sisyphus
  early-exit flake (zero work); retry running with anti-fanout instruction (log:
  `.agent/logs/opencode/lf-born-rough-retry-20260716.log`). Lead flow on completion: review
  diff → apply to main → suite → full export → windowed gate (rocky body at stagnant-lid
  tick; no onset pop; texture rides drifting plates). NOTE: its amplitude numbers were
  written under the OLD north-star budgets — after the §2 re-derivation they will need
  re-tuning upward; the architecture (plate-anchored, conditioned, deterministic) is
  independent of the budget numbers and stays.
- Two background task chips from the morning (relief-fabric doc reconciliation — user
  started it; unify-topology CMake Windows-path fix — pending).
- App instance PID 49401 may still be open at tick 200M (mantle slab view).

## 6. Proposed reconciliation (for the discussion, not pre-decided)

The three user decisions of 2026-07-16 — ABSOLUTE scale, BORN-ROUGH crust, look-like-the-image
ALL THE TIME — are mutually consistent on one condition: **birth roughness must be BIG in the
(derived) elevation field itself** — kilometre-scale from formation (planets form violently;
geologically defensible), not millimetre texture. Then: young worlds already read chunky
(image-look from tick 0), orogeny grows visibly on top (absolute scale preserved), and the
lens can be a single honest declared amplification (a few %R at max relief, lumpy limb
allowed) instead of a fitted clamp. Concretely, the re-derivation would move ~4 numbers:
silhouette budget, interior/belt ratio, birth-roughness amplitude ramp, lens scale — all
declared parameters as of today's shipped work, so the tuning loop is short once the numbers
are agreed. The 250 m detail cap then needs a decision: keep (detail = garnish) with bulk
carried by the elevation field, or lift per the new budget.

## 7. Next-session agenda (in order)

1. Re-derive the look north-star with the user against the reference image (unlock + new
   clause-by-clause numbers; write the successor spec; supersede 07-05 explicitly).
2. Land/verify the born-rough slice under the NEW budgets (architecture in flight is
   budget-independent).
3. One windowed look-iteration loop (knobs are now declared; move any contracts-tier
   defaults to hot-reloadable tiers first — the common-layer full-export trap is documented).
4. USER EYE GATE against the image: three screenshots — magma-ocean tick, stagnant-lid tick,
   late mobile-plate tick — ALL must read as rocky bodies with bulk; the last must show
   grown mountain belts on top. That triple-screenshot IS the falsifiable acceptance.
5. Then resume the queued arcs: 3c motion-in-slab, persistent tunnel (1c), chunked/tiled
   design session, relief-fabric doc reconciliation integration.

**Standing constraints that survive re-derivation:** waterless lock (no hydrology/biome —
the user re-affirmed the exclusion TODAY); truth doctrine untouched (this is presentation +
derived fields); absolute scale (locked today); motion-spine rule #1; the terrain-diffusion
invariants (conditioned, deterministic, no query-history, plate-anchored).
