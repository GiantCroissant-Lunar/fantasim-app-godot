# Growth Diagnosis Report

**Date:** 2026-07-03
**Branch:** agent/growth-diagnosis (read-only worktree)
**Symptom:** Crust terrain renders pixel-identical at t=105,000,000 and t=119,000,000 despite crust snapshots existing and the snapshot-crossing refresh logic firing.

---

## 1. VERDICT

The root cause is a **Rebind-overwrite race**: the `GenerationChanged` event emitted by the crust trigger's async pipeline completion causes `PlanetPresentationBinder.Rebind()` to re-fetch the presentation document at the **parameterless** overload, which defaults to `PlateOnsetTick` (100,000,000) — not the current playhead tick. This 100M-tick document overwrites whatever terrain was correctly rendered for the playhead, and the subsequent `ApplyTimelineTick` inside `BindDocument` cannot detect a snapshot crossing because `ResetRegimeTracking` just cleared the snapshot-tracking state and re-initialized it to the current selection. The net effect: every time the trigger fires for a new window (105M, 110M, 115M), the user's correctly-refreshed terrain is silently replaced by the **100M snapshot**, and it stays there until the next manual seek crosses a snapshot boundary — at which point the same cycle repeats. So seeking between 105M and 119M shows the same 100M terrain because both windows trigger the race.

A secondary co-contributor (spacing mismatch) makes the symptom worse: the trigger policy uses `CrustGenerationWindowTicks = 5,000,000` as spacing (`WorldPlugin.cs:28`), while the service's `BuildCrustSnapshotTickStates` uses `UnitConverter.TicksPerMegaAnnum * 5 = 500,000` (`Service.cs:301`). The trigger produces 5 snapshots; the binder's crossing detector watches 41 snapshot boundaries. This does not cause the identical-terrain bug directly, but it means the trigger fires on fewer windows than the binder expects crossings, so the race window is wider.

---

## 2. EVIDENCE

### H1. Snapshot coverage — **PARTIAL (KILLED as root cause, but spacing mismatch confirmed)**

The trigger policy produces 5 snapshots spanning the FULL mobile-plate regime, not just backward from the trigger tick.

- `CrustGenerationTriggerPolicy.Evaluate` (`CrustGenerationTriggerPolicy.cs:73-82`): `windowIndex = tick / _windowSize`, `canonicalTick = windowIndex * _windowSize`, and `SnapshotTicks = CrustSnapshotTickSeries.ForRegime(regime, _windowSize, _maxTick)`.
- `_windowSize = CrustGenerationWindowTicks = 5_000_000` (`WorldPlugin.cs:28`, `WorldPlugin.cs:177`).
- `CrustSnapshotTickSeries.ForRegime` (`WorldGenerationGraph.cs:177-203`): `start = max(0, regime.StartTick)`, `endExclusive = maxTick + 1` (for OpenEnd). `firstWindowStart` is the first spacing-multiple `>= start`. Loop: `for tick in [firstWindowStart, endExclusive) step spacing`.
- Regime: `mobile-plate`, `StartTick = PlateOnsetTick = 100_000_000` (`SphereRegimeScheduleDefaults.cs:51`, tests confirm `100_000_000L`), `EndTick = OpenEnd`, `maxTick = 120_000_000`.

Numerical reproduction (verified with a throwaway script):
```
spacing=5M, onset=100M, maxTick=120M -> snapshots = [100M, 105M, 110M, 115M, 120M]  (count=5, matches log)
SelectSnapshotForPlayhead(105M) = 105M
SelectSnapshotForPlayhead(119M) = 115M
```
The 5 snapshots span 100M..120M. A playhead at 119M selects snapshot 115M, not 105M. So the snapshots are NOT all below 105M — **H1 as stated is KILLED.** The selection logic DOES return different snapshots for 105M and 119M.

**Spacing mismatch (confirmed, secondary issue):** `Service.cs:301` builds the binder's crossing-detector series with `UnitConverter.TicksPerMegaAnnum * 5 = 100_000 * 5 = 500_000` spacing (`UnitConverter.TicksPerMegaAnnum = 100_000`, `UnitConverter.cs:11`). This produces 41 snapshot ticks at 500K intervals. The binder's `_boundCrustSnapshotTicks` (`PlanetPresentationBinder.cs:260`) takes ALL 41 ticks (ignoring the `Available` flag on `CrustSnapshotTickState`), so it detects crossings at 500K boundaries while the trigger only generates products at 5M boundaries. The binder's `RefreshPresentationForRegime` fetches at the raw playhead (not a pre-generated snapshot), so this mismatch does not directly cause identical terrain — but it means the binder triggers more refreshes than the trigger generates snapshots, widening the race window.

### H2. Pipeline span — **KILLED**

`GlobeReconstructor.RunCrustSnapshot` (`GlobeReconstructor.cs:666-709`) calls `CrustPipeline.RunAsync(..., startTick: 0, endTick: endTick, snapshotTicks: activeTicks, rates: DefaultRates())`. The `endTick` is the max requested tick. The pipeline accumulates from tick 0 to `endTick`.

- `CrustPipeline.RunAsync` (`CrustPipeline.cs:53-123`): calls `CrustEvolutionOperator.EvolveAtSnapshots(..., startTick, endTick, ticks, rates, ...)`.
- `EvolveAtSnapshots` (`CrustEvolution.cs:167-267`): for each snapshot tick, computes `tickCount = snapshotTick - segmentStart + 1` (line 223) and emits integrated deltas of magnitude `rate * tickCount` (lines 290, 300, 325).
- `CrustStateFolder.FoldAt` (`CrustState.cs:36-92`): sums all additive deltas with `tick <= snapshotTick` (line 51, 58). State at tick T = sum of deltas over 0..T.

So state at 119M has ~14M more ticks of accumulation than at 105M. The pipeline does NOT ignore or clamp the tick. **H2 is KILLED.**

### H3. Rate magnitudes — **KILLED (rates produce visible deltas)**

Rates (app `GlobeReconstructor.DefaultRates()`, `GlobeReconstructor.cs:717-725`):
```
OrogenicPerTick = 1.0 / TicksPerMegaAnnum = 1.0 / 100_000 = 1e-5
ArcVolcanismPerTick = 0.6 / 100_000 = 6e-6
RidgeVolcanismPerTick = 0.5 / 100_000 = 5e-6
```

Elevation formula (`CellElevationSystem.Derive`, `CellElevationSystem.cs:62-77`):
```
base = (ContinentalFraction - 0.5) * 1000
uplift = OrogenicPressure * 20.0
volcano = VolcanicActivity * 4.0
elevation = base + uplift + volcano + oceanDepth
```

For a convergent continental-continental boundary cell (continental-fraction=1.0, orogenic pressure accumulates at `1e-5 * T`):

| Tick | OrogenicPressure | Elevation (m) | Delta from 105M (m) |
|------|-----------------|--------------|---------------------|
| 100M | 1000 | 20500 | -1000 |
| 105M | 1050 | 21500 | 0 |
| 110M | 1100 | 22500 | +1000 |
| 115M | 1150 | 23500 | +2000 |
| 119M | 1190 | 24300 | +2800 |
| 120M | 1200 | 24500 | +3000 |

Displacement: `VerticalExaggeration = 1e-5` (`WorldGenerationRenderOptions.cs:49`), `elevation * 1e-5` on unit sphere.
- 105M to 119M: delta = 2800m, displacement = 0.028 unit-sphere. Cell spacing ~0.03 (freq 4, 5120 cells). Displacement/cell = 0.93 — **nearly one full cell width, clearly visible geometrically.**

Color (rank equalization): `WorldTerrainRamp.ComputeColors` (`WorldTerrainRamp.cs:42-70`) uses **percentile rank** (histogram equalization). A uniform additive offset to ALL cells preserves rank order and would be invisible in color. But orogenic accumulation is **non-uniform**: only boundary cells at active convergent margins accumulate; interior cells do not. So ranks of boundary cells rise relative to interior cells, and colors change. **Rank equalization does NOT mask this non-uniform growth.** The `HypsometricTint` (`HypsometricTint.cs:57-82`) uses percentile-clamp (not rank), so it is also sensitive to non-uniform changes.

**H3 is KILLED** — rates produce visible elevation, displacement, and color deltas.

### H4. Other causes — **CONFIRMED (Rebind-overwrite race is the root cause)**

The root cause is the `Rebind` path triggered by `GenerationChanged` fetching at the wrong tick.

**The race sequence (confirmed from source):**

1. User seeks to a tick in window 21 (105M) or window 23 (115M).
2. `CrustGenerationTrigger.OnTickChanged(tick)` (`CrustGenerationTrigger.cs:71-119`) evaluates the policy. If the window is new (not in `_completed`), it launches `RunAsync` which calls `_execute` → `ExecuteCrustGenerationAsync` (`WorldPlugin.cs:218-263`).
3. `ExecuteCrustGenerationAsync` runs the generation graph, then `world.RunGenerationAsync(...)` (`WorldPlugin.cs:260`).
4. `Service.RunGenerationAsync` (`Service.cs:189-197`): on success, calls `EmitGenerationChanged(...)` (line 195).
5. The binder subscribed via `SubscribeGenerationChanged` (`PlanetPresentationBinder.cs:194-199`): the callback calls `Callable.From(Rebind).CallDeferred()`.
6. **`Rebind()` (`PlanetPresentationBinder.cs:110-141`): calls `world.GetPlanetPresentationAsync()` — the PARAMETERLESS overload (line 127).**
7. **`Service.GetPlanetPresentationAsync()` (`Service.cs:136-137`): `=> GetPlanetPresentationAsync(SphereRegimeScheduleDefaults.PlateOnsetTick)` — defaults to `PlateOnsetTick = 100_000_000`.**
8. The document is built at tick=100M: `BuildPlanetPresentationRuntime(family, 100M)` → `BuildCrustSurfaceData(reconstructor, 100M, ...)` → `RunCrustSnapshot(new[]{100_000_000})` → state at 100M.
9. `Rebind` calls `ResetRegimeTracking()` (line 137) which clears `_boundCrustSnapshotTick = null` and `_boundCrustSnapshotTicks = empty` (`PlanetPresentationBinder.cs:102-108`).
10. `_timeline.UpdateFrom(document)` (line 138) → `PushTick(_tick)` → `ApplyTimelineTick(_tick)`. But `_boundCrustSnapshotTicks.Count == 0` (just reset), so the snapshot-crossing branch (`PlanetPresentationBinder.cs:294-310`) is **skipped** — no refresh.
11. `BindDocument(document)` (deferred, line 140 → 202-272): rebuilds the plate surface mesh with the **100M elevations** (line 249, 534-577). Sets `_boundCrustSnapshotTicks` (500K series, line 260) and `_boundCrustSnapshotTick = SelectSnapshotForPlayhead(_timeline.Tick)` (line 261-262). Calls `ApplyTimelineTick(_timeline.Tick)` (line 263). Since `_boundCrustSnapshotTick` was JUST set to the current selection, `selectedSnapshot == _boundCrustSnapshotTick` → **no crossing → no refresh**.

**Net effect:** The correctly-rendered playhead terrain is overwritten by the 100M snapshot, and the binder's crossing detector cannot recover because its tracking was just re-initialized.

**Key citations:**
- `PlanetPresentationBinder.cs:127` — `Rebind` calls parameterless `GetPlanetPresentationAsync()`.
- `Service.cs:136-137` — parameterless overload defaults to `PlateOnsetTick` (100M).
- `PlanetPresentationBinder.cs:194-199` — `GenerationChanged` callback triggers `Rebind`.
- `PlanetPresentationBinder.cs:137` — `ResetRegimeTracking()` clears snapshot tracking.
- `PlanetPresentationBinder.cs:294-310` — snapshot-crossing detection (skipped when `_boundCrustSnapshotTicks` is empty).
- `PlanetPresentationBinder.cs:260-263` — `BindDocument` re-initializes tracking to current selection, so no crossing is detected.
- `Service.cs:189-197` — `RunGenerationAsync` emits `GenerationChanged` on success.
- `WorldPlugin.cs:260` — trigger's execute calls `world.RunGenerationAsync`.

**Why the symptom says "refresh fires correctly":** The `Crust snapshot transition` log (`PlanetPresentationBinder.cs:306-310`) DOES fire when the user manually seeks across a 500K snapshot boundary. But the subsequent `RefreshPresentationForRegime` fetches at the playhead and renders correct terrain — only for that to be overwritten moments later by the `Rebind` from the trigger's `GenerationChanged` event (if the trigger fires for that window). The log the user saw (`<none> -> 105,000,000`) is from the INITIAL `BindDocument` after the first trigger fire — it logs the transition from `null` (unset) to the first selected snapshot. Subsequent seeks log further transitions, but the terrain is always overwritten back to 100M.

---

## 3. FIX SKETCH

**Minimal fix:** Make `Rebind()` (or the `GenerationChanged` callback) fetch at the current playhead tick instead of the parameterless (100M) overload.

- **`PlanetPresentationBinder.cs:127`**: Change `world.GetPlanetPresentationAsync()` to `world.GetPlanetPresentationAsync(_timeline.Tick)`. This ensures the post-trigger rebind renders terrain at the current playhead, not 100M.
- Alternatively, change the `GenerationChanged` callback (`PlanetPresentationBinder.cs:194-199`) to call `RefreshPresentationForRegime()` (which already fetches at `_timeline.Tick`, line 412) instead of `Rebind()`, when a snapshot series is already bound. `Rebind` should only be used for the initial mount or world-service re-registration.

**Secondary fix (spacing mismatch):** Align the trigger's window spacing with the service's snapshot-tick series spacing. Either:
- Change `Service.cs:301` to use `CrustGenerationWindowTicks` (5M) as the series spacing (so the binder detects crossings at 5M boundaries, matching the trigger's snapshot generation), or
- Change `WorldPlugin.cs:28` `CrustGenerationWindowTicks` to `UnitConverter.TicksPerMegaAnnum * 5` (500K) so the trigger generates snapshots at the same 500K spacing the binder expects.

The first option is safer (fewer trigger fires, matching the original design intent of 5 snapshots over the mobile-plate era).

**Tertiary fix (robustness):** In `BindDocument` (`PlanetPresentationBinder.cs:263`), after initializing `_boundCrustSnapshotTick` to the current selection, check whether the current playhead's selected snapshot differs from the document's `ReferenceTick` / the elevations' source tick. If so, schedule a refresh. This prevents any future rebind from silently leaving stale terrain.

**Doctrine risk (truth-stream invariants):**
- The fix must NOT change the truth-stream commit logic — the trigger's pipeline run still commits to the truth stream at the correct canonical tick. The fix only changes which tick the PRESENTATION fetches after the trigger completes.
- The `RefreshPresentationForRegime` path already fetches at the playhead (`PlanetPresentationBinder.cs:412`), so the fix aligns `Rebind` with this existing behavior.
- The truth-stream's hash-chained determinism is unaffected: `RunCrustSnapshot(new[]{tick})` is a pure read that materializes the stream up to `tick`; it does not mutate the stream.
- The spacing fix must preserve the invariant that snapshot ticks are spacing-multiples aligned to the regime start (the `ForRegime` first-tick alignment, `WorldGenerationGraph.cs:192-194`).

---

## 4. OPEN QUESTIONS

1. **Is the `GenerationChanged` → `Rebind` race timing-dependent?** The analysis assumes the trigger's async completion always fires `GenerationChanged` after the user's seek-refresh. If the trigger completes BEFORE the user seeks, the 100M terrain is already in place and the seek-refresh would correctly overwrite it (only to be overwritten by the NEXT trigger fire). The symptom (identical at 105M and 119M) implies the trigger fires for both windows 21 and 23, and each fire's `GenerationChanged` overwrites the playhead terrain. This could be confirmed by adding a log in `Rebind` showing the fetch tick and in `BindDocument` showing the document's `ReferenceTick`.

2. **Does `RefreshPresentationForRegime` actually run before `Rebind` overwrites it?** Both are deferred via `Callable.From(...).CallDeferred()`. The order depends on Godot's deferred-call queue. If `Rebind` (from `GenerationChanged`) is queued AFTER `RefreshPresentationForRegime` (from the seek's `ApplyTimelineTick`), the 100M overwrite happens last. If queued before, the refresh happens last and the user briefly sees correct terrain before the next trigger fire. A runtime log check would confirm.

3. **What does the user's "pixel-identical" comparison measure?** If comparing screenshots taken AFTER the trigger completed for both windows, both would show 100M terrain (identical). If comparing during active seeking (mid-flight), the terrain might briefly differ. The symptom says "despite the snapshot-crossing refresh logic firing correctly" — which matches the analysis: the refresh fires, renders correct terrain, then gets overwritten.

4. **Why does `BuildCrustSnapshotTickStates` use 500K spacing while the trigger uses 5M?** This appears to be an oversight — the service's spacing was likely set independently of the trigger's window size. The `CrustSnapshotTickState.Available` flag was intended to distinguish generated from ungenerated snapshots, but the binder ignores it (`PlanetPresentationBinder.cs:260` selects `.Tick` only). Confirm with the commit history (the task brief cites commits 2fc518b, 5c1f8c7, 9e36306, 8771c42, 97d4f2b).

5. **Does the engine's `CellReconstructor` use raw ticks vs. the app's onset-relative delta?** The engine's `CellReconstructor.ReconstructCellCenters` (`CellReconstructor.cs:44`) uses `targetTick.Value` directly as the rotation time, while the app's `GlobeReconstructor.RotationDelta` (`GlobeReconstructor.cs:322`) uses `tick - _onsetTick`. This means the engine classifies boundaries at a different rotation angle than the app renders. This mismatch affects which boundary TYPE is deposited at each snapshot but does not affect whether accumulation happens. It may cause visual mismatches between boundary types and cell membership but is not the root cause of identical terrain. Worth a separate investigation.
---

## 5. RESOLUTION — Open Question 5 (2026-07-03, follow-up session)

**Confirmed real and fixed.** The mismatch was not in `CellReconstructor` itself (its contract is
delta-based: "tick 0 = identity") but in what the callers feed it: the app's crust runs
(`GlobeReconstructor.RunCrustFeatures/RunCrustEvolution/RunCrustSnapshot`) and the generation-graph
path (`WorldPlugin` → `WorldFunctionProvider.crust.generate`) pass ABSOLUTE snapshot ticks into
`CrustPipeline.RunAsync`, whose internal `BoundaryTypesAt` fed the raw tick to
`ClassifyBoundariesAt` — while all rendering (`RotationDelta`, `ReassignCellsAt`,
`BuildBoundaryArcsAt`) rotates at `tick − onset`.

**Quantified** (app default seed, 0.02 rad/Ma, onset 100M ticks = 1000 Ma): constant angular offset
`rate × onset` = 20 rad ≡ **1.1504 rad ≈ 65.9°** (mod 2π). Comparing raw vs onset-delta
classification on the 4-plate seed at freq 4: **1–5 of 6 plate-pair boundary types differ at every
snapshot tick** in 100M–120M (e.g. at 105M the raw path classifies the rendered 2|3 mid-ocean ridge
as Convergent and the colliding 0|1 pair as Divergent).

**Fix** (rotation convention only; deposit magnitudes/accumulation window unchanged):
- engine `fantasim-world`: `CrustPipeline.RunAsync` gained `rotationReferenceTick` (default 0 =
  historical behavior); `BoundaryTypesAt` classifies at `snapshotTick − rotationReferenceTick`.
  TDD-proven by `CrustEvolutionTests.RunAsync_rotationReferenceTick_classifies_at_delta_from_reference`.
- app: all three `GlobeReconstructor` crust runs pass `rotationReferenceTick: _onsetTick`;
  `WorldPlugin.ExecuteCrustGenerationAsync` adds `rotationReferenceTick = PlateOnsetTick` to
  sharedParams, read by `WorldFunctionProvider.GenerateCrustAsync`.

Windowed-verified via world-bundle hot-reload into the running exported app: crust generation
re-ran clean at 105M/115M, snapshot-transition refresh fired, terrain differs across snapshots with
boundary-anchored feature accents. Known pre-existing caveat: the world bundle reload logged
`old ALC still pinned … reload degraded` (the scene-tier pin issue tracked separately); the new
assemblies are live regardless (fresh extract dir, new trigger runs).
