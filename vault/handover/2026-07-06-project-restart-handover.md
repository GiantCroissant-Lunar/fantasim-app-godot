# FantaSim — project restart handover

**Date:** 2026-07-06 · **Trigger:** user is considering a project restart ("we are in a deadlock now")
after 2+ months of planet look/motion work that never reached the target feel.
**Audience:** whoever (human or agent) picks this up next — with or without the existing repos.
This document is self-contained; read it before touching anything.

---

## 1. The target (unchanged, and still right)

One line: **a stylized, readable planet whose continents visibly drift, collide, and merge over
hundreds of millions of years** — the feel of the Scotese-style "How Earth Will Look In 250
Million Years" animation. Legibility over realism (north-star spec,
`vault/specs/2026-07-05-planet-look-north-star.md`).

The gate that defines success (never changed, repeatedly skipped): a viewer looking at a windowed
screenshot/recording names, unaided — (a) land vs ocean by tone, (b) mountain chains as lines,
(c) a round planet, and (d) **sees the land masses MOVE** across the sphere.

## 2. What exists and is VERIFIED working (assets a restart should not burn)

| Asset | Where | Status |
|---|---|---|
| Event-sourced world engine: convection, lid fracture, plate kinematics | `fantasim-world` (⚠ **LOCAL-ONLY, no git remote**) | Motion verified HEALTHY 2026-07-06: pole rates 0.9–2.0e-2 rad/Ma; 100% cell membership change across the 200 Ma window |
| Canonical time | engine `UnitConverter` | 100,000 ticks/Ma; plate onset = tick 100M = 1000 Ma; window onset+20M ticks = 200 Ma |
| Watertight per-plate caps + feature-aware adaptive subdivision | `fantasim-cartography` (⚠ **LOCAL-ONLY, no git remote**) | Reviewed clean 2026-07-04 |
| Crust pipeline with per-cell `ContinentalFraction` → **bimodal elevation formula** | engine `Geosphere.Crust` + app `CellElevationSystem.Derive` (App.Ecs) | BUILT and wired; dry mode = `fraction × ContinentalAmp`, wet mode adds sea level + ocean-age deepening |
| Motion delivery channel (M0): cached reconstructor light path, per-playhead-move refresh | app `IService.GetGlobeSnapshotAt` / `GetGlobeBoundaryCellsAt`, binder `RefreshContinentsMembership` | Landed 2026-07-06 (`4ff4a77`, `5f598df`); windowed gate PASS (23–37% pixel change per 5-Ma step) |
| Motion regression gate | `MotionGateTests` (App.World.Tests) | In CI; ≥30% membership floor |
| Remote drive + scripted eye gate | App.Remote ingress :19292, `tools/fantasim-cmd.py`, gate script pattern | Proven this session: seek → select layer → screenshot → pixel-diff, fully scriptable |
| Hot-reload bundle/ALC infrastructure | app (stage/assist/timeline/world/activity/iii PCKs) | Works; ALC pin fixed 2026-07-03 — but see §4 on its iteration tax |
| External delegation roster | `.agent/skills/04-tooling/external-agent-delegation` | opencode (ollama glm-5.2 / kimi-k2.7-code) reliable; used successfully for trace/review/implementation this session |

**⚠ Before any restart action:** the local `fantasim-world` and `fantasim-cartography` clones have
no remote CONFIGURED, but GitHub remotes exist with a PREVIOUS attempt's version
(`git@github.com:GiantCroissant-Lunar/fantasim-world.git`, `…/fantasim-cartography.git`).
Wire the remotes and push the current local state (as a new branch if the old version must be
preserved) before any restart action — the current engine state exists only on this disk.

**⚠ Context correction (user, 2026-07-06): this is the 8TH ATTEMPT in the past half year** — the
GitHub remotes hold an earlier one. Seven restarts did not fix the outcome ("a ball with some
strips instead of a planet" every time). That is decisive evidence about §5: restarting alone
does not change the result; §6 (and the architecture-conformance addendum below) is what must
change.

**⚠ Recommendation revision (user correction, 2026-07-06):** §5's option B said "no bundles until
the planet reads." The user rejects that framing: **the bundle-oriented approach, 4-tier service
architecture, ECS, Akka.NET, and node graph are FEATURES of the app — the product — not
infrastructure to defer.** The persistent failure mode is precisely that agents either (a) do
architecture work and never touch the look, or (b) chase the look by wiring proxies AROUND the
architectural stations (Host-direct, binder-direct — e.g. ProvinceTint, M0 membership coloring).
Any next step must make the planet read THROUGH the architecture, with conformance mechanically
enforced — see the station-map idea in the session notes. Option B as written is retracted.

## 3. Why it never looked right — the verified diagnosis (2026-07-06)

Full evidence: `vault/handover/2026-07-06-motion-diagnosis-and-m0-continents.md`.

1. **Frozen onset frame.** `Service.BuildPlanetPresentationRuntime` computes every user-visible
   channel (elevations, crust features, boundary topography, sections) against the ONSET globe
   (`globeAtOnset`/`arcsAtOnset`; `WorldCrustRunSpec.RotationReferenceTick = onset`). The engine's
   healthy motion reaches the document's `GlobeSnapshot` but has no visible expression in the
   World/Hypso views. Proof: PlateIdentity view changes 70.2% of globe pixels across the window;
   crust view 4.9% (belts brighten in place, nothing moves).
2. **The continents circle.** Four render-layer continent PROXIES were built across the months —
   biome colors (Jun), ProvinceTint noise albedo (`ecd22b2`), bimodal ramp tuning (`cc548ce`),
   M0 plate-membership coloring (`5f598df`) — while the CANONICAL truth field
   (`ContinentalFraction`) sat mis-seeded, frozen, and uncalibrated. Each session rediscovered
   "we need continents/bimodality" and added another proxy. (Also re-litigated: water — settled
   by the `e3b84ef` lock "waterless worlds are worlds".)
3. **The three real defects in the existing chain** (all checkable, none require new concepts):
   - **Seeding:** `CrustInitRecipe.Continental(0, 1)` = one contiguous 2-plate block → a single
     blob whose coastline is a plate frontier. Needs several organic sub-plate patches.
   - **Frame:** the crust chain must be evaluated in the moving plate frame (advect / sample in
     plate-local coordinates) so land RIDES plates with stable shapes.
   - **Calibration:** the "muddy" verdicts came from eye-tuning the ramp against an elevation
     histogram that was never measured. Measure first; the bands are numbers.
4. **Process failures that made it a deadlock:**
   - Three look arcs committed WITHOUT the windowed eye test (verified: the last export was never
     launched). The gate existed on paper only.
   - No prior-attempt audit: sessions proposed what previous laps had already built.
   - Iteration tax: 4-tier + bundles + export ≈ minutes per look change; references (Lague,
     tectonics.js) iterate in seconds. The architecture made the wrong thing cheap (reload
     machinery) and the right thing expensive (seeing a change).
   - Two planet pipelines accumulated (`PlanetPresentationBinder` live; `GlobeView`/
     `WorldViewComposition` DEAD — zero call sites — but its comments misled diagnosis).

## 4. The deadlock, named precisely

It is not a code deadlock. The engine simulates; the caps render; the motion channel works; the
bimodal formula exists. It is a **process deadlock**: each lap adds a proxy at the render layer,
increases codebase mass, skips the gate, and makes the next lap heavier and more confusing —
four continent representations, two pipelines, locked decisions that keep getting re-argued.
A restart that changes the code but not the process will reproduce the circle in new clothes.

## 5. Restart options (honest assessment)

**A. Full greenfield.** Maximum psychological reset; loses the verified engine + cartography +
   two months of hard-won diagnosis. The circle was never caused by the engine. NOT recommended.

**B. Keep the engine + cartography; restart the app/presentation thin.** One Godot project, ONE
   planet pipeline, no bundles/ALC until the planet reads (add them back when the look is locked —
   they are orthogonal and already proven). Wire: engine truth → moving-frame crust sampling →
   caps → screen, with the scripted gate from day 1. This keeps every verified asset and discards
   exactly the accumulated presentation mass where the circle lives. **Recommended.**

**C. No restart; surgical repair in place.** Retire the proxies (ProvinceTint, M0 membership
   coloring, dead GlobeView pipeline), fix the three defects on the existing chain. Cheapest in
   code; carries the full existing mass and the "we've been here before" weight. Viable if the
   deadlock feels code-shaped; wrong if it feels process-shaped.

**My recommendation: B**, with the §6 rules as hard constraints. The engine and cartography are
the two components that were independently verified GOOD this session; the app layer is where all
four proxy laps and both pipelines live.

## 6. Non-negotiables for whatever comes next (the actual circle-breakers)

1. **The eye gate runs on every look/motion change.** It is scripted and cheap now (launch →
   drive → capture → diff). An arc without gate evidence is failed by definition.
2. **Prior-attempt audit before proposing.** Read this doc + the circle-map memory + `git log
   --grep` before any continents/ocean/look proposal. Re-proposing a settled decision (e.g. the
   waterless lock) requires an explicit user unlock, not a fresh argument.
3. **One canonical continent representation:** `ContinentalFraction` (or its successor), seeded
   organically, advected in the plate frame. Render-layer continent proxies are banned.
4. **Never build features on frozen topology** — this was already locked doctrine (2026-06-21)
   and was violated silently by the presentation layer. Any new pipeline must demonstrate motion
   in its FIRST windowed screenshot pair, before any feature work.
5. **Iteration budget:** a look change must be visible on screen in under ~2 minutes. Defer any
   infrastructure that breaks this until the look is locked.
6. **Calibration by measurement:** print the elevation histogram before tuning any ramp.

## 7. Pointers

- **Specs:** `2026-07-05-planet-look-north-star.md` (look target),
  `2026-07-06-m0-visible-drifting-continents.md` (motion slice, gates pattern).
- **Handovers:** `2026-07-06-motion-diagnosis-and-m0-continents.md` (evidence chain);
  `2026-07-03-cutaway-hostslim-review-handover.md` (architecture state before this session).
- **Key commits (app):** `e3b84ef` water lock · `ecd22b2` ProvinceTint · `cc548ce` bimodal ramp ·
  `9e36306` first reassignment fix · `1b998ba`→`7bde552` today's diagnosis+M0 arc.
- **Key code:** `Service.BuildPlanetPresentationRuntime` (the frozen-frame loss point),
  `CellElevationSystem.Derive` (the bimodal formula), `GlobeReconstructor.ReassignCellsAt`
  (healthy motion), `PlanetPresentationBinder` (the one live pipeline).
- **Agent memories (Claude, `~/.claude/projects/-Users-apprenticegc-Work-lunar-horse/memory/`):**
  `fantasim-continents-circle-map` (anti-circling rules), `fantasim-frozen-topology-diagnosis`
  (diagnosis + drive recipe), `fantasim-planet-look-gate-closed` (gate recipe).
- **Dispatch reports (untracked):** `.agent/run/dispatch/motion-death-trace-REPORT.md`,
  `frozen-topology-refute-REVIEW.md`, `m0-packet1-SUMMARY.md`.

## 8. Open decisions for the restart

1. Scope: A / B / C above (recommendation: B).
2. Fate of the M0 Continents view: keep as the motion-channel scaffold, or retire its membership
   coloring immediately once fraction-driven coloring flows.
3. The two LOCAL-ONLY repos: create remotes before anything else.
4. NuGet feed pins / Unify package versions: the local feed is the fuller one (see
   nuget-feed-sync memory); a fresh app repo must pin against it.
5. What "restart" means for the vault: carry `vault/` forward wholesale (recommended — it is the
   institutional memory), or start a fresh vault seeded with this doc + the north star + the
   circle map.
