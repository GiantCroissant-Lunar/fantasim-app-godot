# Attempt-8 recovery — P0→P2 execution handover (session record, 2026-07-06)

**For the next session (fresh context).** This session ran the deadlock diagnosis → recovery
decision → P0/P1/P2 execution end-to-end. Read this FIRST, then the governing docs in §1.
**Standing user authorization:** "dispatch to agent cli using agent skill till everything is done,
and verify to evaluate if this recovery for attempt #8 really works" — dispatch phases per the
delegation skill, lead-verify BOTH gates per packet, keep the roadmap progress log current.
The session-goal-contract rule (workspace `.agent/rules/`) binds every session and packet.

## 1. Read order for a fresh session

1. THIS doc, then the roadmap **progress log**: `vault/plans/2026-07-06-attempt8-recovery-roadmap.md`
   (phases, O1–O5 outcomes, standing rules, per-phase evidence log).
2. `vault/architecture/planet-domain-station-map.md` — constitution: station route + two gates.
3. `vault/plans/2026-07-06-long-term-roadmap.md` — H1–H4 horizons (H1 = this recovery).
4. Context when needed: `2026-07-06-half-year-attempt-ledger.md` (7 prior eras + failure themes),
   `2026-07-06-rfc-salvage-index.md` (designs to salvage per phase), the circle-map + restart
   memories (agent memory dir), `vault/README.md` (authority index — every stale doc is bannered).

## 2. DONE this session (all committed & pushed)

**Repos/branches:** app `fantasim-app-godot` main (origin/main, through the commit carrying this
doc). Engine `fantasim-world` + `fantasim-cartography`: local mains push to **`attempt-8/main`**
(remote `main` holds a PREVIOUS attempt — unrelated histories; never force-push main).

| Phase | Commits (app unless noted) | What landed |
|---|---|---|
| Diagnosis | `1b998ba`…`7bde552` (pre-recovery) | Frozen-onset-frame root cause verified 3-way; M0 Continents motion channel; memories + ledger |
| P0 | — | Remotes wired; engine@`6e77f27→591a333`, carto pushed to attempt-8/main; app pushed |
| P1 | `2c53650` | `App.Architecture.Tests` C1–C5 LIVE (engine types/config banned in seam, tick-addressed products, proxy whitelist); fixed the parameterless `Rebind()` fetch |
| P2-E | engine `6e77f27` + `4a17fb9`(pack cfg) | `CrustPatchRecipe` organic per-cell seeding (defaults 0.25rad/0.2/0.05 — plan's 0.5 disproven, merged); engine packed **0.1.7** (pack include lists were stale — fixed); app pins bumped `a81be9c` |
| P2-A | `615b245` (packet+Lagrangian rewrite), `5258928` (boot fix), `b621d29` (subduction priority), `beb9489` (tight two-cap), `a934d44` (log) | Moving-frame continents through the stations; see §3 for the model |
| Engine plumbing | engine `591a333` | `CrustPipeline.RunAsync(patchRecipe)` param; engine packed **0.1.8**, app pinned 0.1.8 |
| Docs estate | `ea806a5`(app) `764a620`(engine) | Code-verified audit: 49 status banners, authority-index READMEs, 1 conflict resolved (planet-evolution-arc §2 → M0 wins) |
| Governance | workspace `.agent/rules/session-goal-contract.md` (+AGENTS.md index) | Falsifiable gate-bound session goals; conclusions incl. negative results mandatory |
| History | `92607ca`, `9c7ba8c` | Attempt ledger (Dec-2025→Jul-2026, 7 eras) + RFC salvage index + long-term roadmap H1–H4 |
| Evidence | this commit | `tools/gates/p2-windowed-gate.sh` (persisted; `GATE_OUT` env for output dir) + `vault/reference/2026-07-06-p2-continents-drift.gif` (21-frame sweep) |

## 3. The P2 model (what the code now does — precise)

- **Seeding (S1/S2):** per-cell `ContinentalFraction` from `CrustPatchRecipe` (engine
  `Geosphere.Crust/CrustInit.cs`) — 5 organic noise-edged patches, deterministic; recipe enters via
  `world.options` payload `continentalPatches` (`WorldCrustRunSpec.ReadRecipe`); patches are the DEFAULT.
- **Sampling (S3/S4):** `App.World/Crust/PlateFrameSampler.cs` — **Lagrangian**: material is
  carried by its ONSET plate; every onset cell center is rotated forward by its plate's Euler pole
  (delta = tick−onset); each target cell samples its **nearest forward image**, with TWO caps:
  gap-fill 1.5 mean cell spacings (beyond → **newly formed ocean floor**, fraction 0, age=delta)
  and subduction override 0.75 (a co-located continental image overrides an oceanic nearest —
  buoyancy; it must NOT be wider or land dilates ~7x). Elevations via `CellElevationSystem.Derive`
  (ECS station, hydrosphere Absent = waterless lock) + boundary contributions at the tick.
  Pre-onset (`arcTick < onset`): lid globe + empty crust (boot path — see gotcha G2).
- **View (S5):** Continents view colors per cell from `document.ContinentalFractionByCell`
  (land/ocean tones, `ContinentsPalette`), coastline = fraction-contour via cell adjacency;
  plate-membership coloring RETIRED; `ContinentalPlateIds` is `[Obsolete]` (cleanup pending).
- **Gates:** `MotionGateTests` = 11 cell-level gates (movement raw-Jaccard<0.7, shape vs
  rotated-expected ≥0.6 [freq-4 quantization ceiling ≈0.66], area conserved at endpoints AND
  mid-window, boot-tick no-throw, frontier displaced, determinism, patch count…) + the windowed
  script (`tools/gates/p2-windowed-gate.sh`). **Windowed verdict:** PASS on v2 criteria (land in
  all frames + ≥10% motion per step). NOTE: the script still contains the OLD per-frame
  landmass-count/area criteria that mis-fire (hemisphere projection + genuine merging) — see TODO T1.

## 4. NOT done — the open ledger (next session starts here)

**Immediate next action: write the P3 detailed plan** (roadmap says plan-after-P2 with real data),
then dispatch per the skill. P3 scope collected this session:
1. **Histogram tool first** (standing rule 6): measure the elevation distribution of the MOVING
   data before touching any ramp; derive band positions from it.
2. **Look pass:** relief/shading on land, ocean depth tone, coastline anti-jaggies (tessellation
   freq bump and/or contour smoothing; adaptive subdivision along fraction contours — carto
   feature-aware splits exist). Decide the DEFAULT-view question: the World view is still the
   locked terracotta ball — the user opens the app and SEES A BALL WITH STRIPS (their words,
   verbatim, after P2 passed). Re-aiming the default onto moving data is a user decision to
   surface in the P3 plan (the waterless lock stands unless explicitly unlocked).
3. **Smooth motion in the light path:** fractions currently update only at 5M-tick crust
   snapshots → visible 5-frame steps in the GIF. Per-tick fraction sampling in
   `RefreshContinentsMembership` (sampler on cached state, no crust re-materialization).
4. **Perf:** sampler nearest-lookup is O(n²) (~26M dots/refresh at freq 4) — spatial hash;
   also the per-seek triple-rebind (crust refresh + light refresh overlap).
5. Re-homed defects: **viewport/panel overlap** (graph panel + activity ledger crowd the globe);
   frame-budget test flake under parallel `task test`; ProvinceTint retirement review.
**P4:** rates too violent (near-full-circle sweeps in 200 Ma; supercontinent by mid-window) —
tune `DefaultAngularDriftPerMegaAnnum`/axis coherence with the diff gate as instrument; verify the
timeline **Play** button (never exercised!); ~15s window sweep demo.
**P5–P7:** per roadmap + salvage index (RFC-027/029+0200-xxx → P5; RFC-076+asthenosphere RFC → P5/P6;
mung-bean contract + RFC-0001/0002 → H4). P7 note: harden the ingress (see gotcha G5) + document
the agent API; the closing demo = unattended agent runs construct→scrub→amplify→gallery.

## 5. Gotchas learned (do not relearn these)

- **G1 — packets optimize mis-specified tests.** P2-A shipped a STATIC Eulerian field because the
  shape test compared raw cell-sets (a drifting mask fails that by definition). Specify tests
  against INTENT (rotated-expected comparison). Always eye-gate a packet's model claims.
- **G2 — cross-phase interactions bite at boot.** P1's tick-addressed `Rebind()` × P2's crust path
  at tick 0 threw and UNBOUND THE WHOLE PLANET (and pinned MaxTick=1, clamping all seeks). Run the
  windowed gate after ANY production change; the boot-tick unit gate now guards this one.
- **G3 — never republish the same package version** (NuGet caches by version): engine changes →
  bump (0.1.7→0.1.8) and re-pin. Engine pack include-lists live in `build/build.config.json`
  (were stale once already). Version via env: `GITVERSION_MAJORMINORPATCH=x.y.z task pack`.
- **G4 — packets may touch out-of-scope repos.** P2-A edited the ENGINE (CrustPipeline). Review
  on merits → commit in that repo → repack → re-pin. Check `git status` in SIBLING repos too.
- **G5 — macOS `seq` emits scientific notation** for big ints (1e+08) → ingress rejects
  ("requires numeric 'tick'") SILENTLY if the driver drops stderr. Use shell arithmetic loops.
  P7: make the ingress tolerant or the failure loud.
- **G6 — pixel metrics on a hemisphere lie:** projection foreshortening + genuine patch merging
  make per-frame landmass-count/area pixel checks meaningless. Count/area gates live at CELL
  level (unit suite); the windowed gate checks presence + motion + the human eye.
- **G7 — background agent plumbing:** a spawned agent's children notify the MAIN session, not the
  spawner (it waits forever); 529s kill agents mid-run (SendMessage with the agentId resumes with
  context intact); opencode summary-file + no-commit + lead-review remains the working pattern.
- **G8 — drive recipe:** launch export with `remote__enabled=true`; `python3 tools/fantasim-cmd.py
  cmd timeline.seek '{"tick":N}'` / `timeline.select_layer '{"sphereId":"geosphere","layerId":
  "geosphere.plate"}'` / `render.screenshot '{"path":"/abs.png"}'`; wait for the
  'Planet plate surface bound' log line before screenshotting after seeks that cross 5M
  snapshot boundaries. Layer ids are namespaced (`geosphere.crust` etc.).

## 6. TODOs left in the tree (small, non-blocking)

- T1: `tools/gates/p2-windowed-gate.sh` still prints the mis-firing landmass-count/area pixel
  criteria (v1); the v2 criteria (land present + ≥10% motion) ran ad hoc — fold v2 into the script.
- T2: `ContinentalPlateIds` `[Obsolete]` — remove after its last consumers (a MotionGate test +
  compat) are migrated.
- T3: `.agent/run/dispatch/` holds all packet prompts/summaries/reports (gitignored, LOCAL ONLY —
  they document this session's dispatches; harvest anything needed before cleaning).
- T4: harness task list state at session end: #1–#3 completed; #4 P3, #5 P4, #6 P5, #7 P6, #8 P7
  pending (recreate if the list doesn't carry over).

## 7. Standing locks (unchanged; unlock = explicit user approval only)

Waterless default look (`e3b84ef`) · plate count emergent · S2-indexing-only · Unify* math only ·
storage = SurrealDB via unify-storage · noosphere per-consumer (05-26) · no USD/DCC (05-28) ·
accretion/SPH parked → P6 as R-product · continents = `ContinentalFraction` ONLY (proxies banned
by C4).

**User's last word this session, verbatim, after P2's gates passed: "I still see a ball with
strips."** The motion channel is real; the LOOK is not there. P3 exists to close exactly that gap
— judged by their eye, not by our gates.
