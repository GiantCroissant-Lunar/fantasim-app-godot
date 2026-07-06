# 2026-07-06 — motion-death diagnosis + M0 drifting continents (session record)

**Arc:** "why does fantasim feel stuck" → verified root cause → M0 spec → implementation → gates PASS.

## 1. Diagnosis (verified, three-way cross-checked)

Question: two months of look/motion work never produced the Scotese-style feel ("Earth in 250 My").
Method: external dispatch per external-agent-delegation (kimi-k2.7-code code trace + glm-5.2
adversarial refutation, reports under `.agent/run/dispatch/`) + lead-session runtime verification
(probe test + log-gated screenshot A/Bs in the exported app). Neither agent's verdict survived the
runtime check alone — the probe + live A/Bs settled it.

**Root cause:** engine motion is HEALTHY (OnsetRoster pole rates 0.9–2.0e-2 rad/Ma; 100% of 5120
cells change plate across the 200 Ma window) and the playhead reaches the document
(`Service.cs` builds `GlobeSnapshot = BuildGlobeAt(arcTick)`), but **all user-visible channels
(CellElevations/CellFeatures/sections/boundary topography) are computed against the frozen ONSET
frame** (`Service.BuildPlanetPresentationRuntime` globeAtOnset/arcsAtOnset;
`WorldCrustRunSpec` RotationReferenceTick=onset). World/Hypso paint per-cell heights+colors →
motion has no visible expression. Proof: PlateIdentity view changed 70.2% of globe pixels
100M→120M in the live app; crust view 4.9% (belts brighten in place).

Corrections to earlier beliefs: `TicksPerMegaAnnum = 100_000` (onset = 1000 Ma; window
onset+20M ticks = **200 Ma**, not 20); `WorldViewComposition`/`GlobeView` is DEAD code (0 call
sites; its "frozen topology" comment misled the first diagnosis). Secondary finds: typed boundary
arcs diverge from membership frontiers at later ticks (F1); refresh quantized to 5M-tick crust
snapshots.

## 2. M0 — visible drifting continents (spec + implementation, all landed on main)

Spec: `vault/specs/2026-07-06-m0-visible-drifting-continents.md` (D1–D4 user-approved).

- `1b998ba` docs(vault): M0 spec.
- `e7d4a9a` test(world): motion probe (100% reassignment evidence).
- `4ff4a77` feat(world): packet 1 — `GlobeViewMode.Continents` (+ resolver w/ `globe:plateView`
  override), `PlanetPresentationDocument.ContinentalPlateIds` (single-sourced, default {0,1}),
  `IService.GetGlobeSnapshotAt` with cached OnsetRoster/GlobeReconstructor per (seed, freq).
  Implemented via opencode/kimi dispatch, lead-reviewed. `MotionGateTests` (≥30% floor).
- `5f598df` feat(presentation): packet 2 — `ContinentsPalette` + `PlateCapMeshBuilder.
  BuildContinents` (two-tone + frontier tint from `IService.GetGlobeBoundaryCellsAt` = same
  reassigned topology, D4), binder `RefreshContinentsMembership` per playhead move (document
  `with { GlobeSnapshot }` + in-place cap rebuild, no crust materialization), host knob plumb.
  TDD; full `task test` green (449 App.World).

## 3. Gates (spec §4)

1. Unit: PASS (`MotionGateTests`, in CI).
2. Windowed scripted: **PASS** — Continents view, captures at 100/105/110/115/120M (bind-gated),
   consecutive diffs 25.3 / 32.5 / 36.6 / 22.9 % (threshold 10%). Drive script:
   session scratchpad `m0-windowed-gate.sh` (remote :19292 → seek → select geosphere.plate →
   render.screenshot; recipe also in agent memory).
3. Eye test: land/ocean unambiguous; a coherent continent visibly sweeps across the sphere
   across the window. User is final judge.

## 4. Open items

- **Play sweep (§3.3, D3 second half):** TimelineFace play machinery exists but was not touched;
  verify/enable a ~15 s onset→maxTick sweep as the demo channel.
- Per-seek triple rebind observed (crust-snapshot refresh + light refresh overlap) — cheap perf
  cleanup: skip the heavy snapshot refresh path while in Continents mode.
- F1 typed-arc/membership alignment; F2 motion-character tuning (rates/axis coherence — now
  measurable via the diff gate); F3 age-anchored time labels (user: raw t=0 is wrong); F4
  plate-frame crust accumulation (the World/Hypso fix — next spec).
- Parked by user: accretion/SPH (Lague Planetary Fluid Sim), blob/cutaway content, exploded view.
