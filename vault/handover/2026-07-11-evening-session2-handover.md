# 2026-07-11 evening session 2 handover — cleanup, flip, two designs, tunnel slice 1

**For the next session (fresh context). READ ORDER:** this doc → per-arc detail only as needed:
[`2026-07-11-session-close-handover.md`](2026-07-11-session-close-handover.md) (the four-arc
DAY session that preceded this one; its G31–G35 + standing rules all still bind) →
[`../plans/2026-07-11-tunnel-slice1-plan.md`](../plans/2026-07-11-tunnel-slice1-plan.md)
(the 12-task plan; tasks 1–12 ALL executed) →
[`../specs/2026-07-11-tunnel-timeline-design.md`](../specs/2026-07-11-tunnel-timeline-design.md) +
[`../specs/2026-07-11-surrealdb-persistence-slice1-design.md`](../specs/2026-07-11-surrealdb-persistence-slice1-design.md)
(both DRAFT; decision points resolved by user delegation to lead recommendations — not yet
re-stamped LOCKED; stamp after the eye sitting).

## 1. Session ledger (ALL pushed, main @ `271e9f9`, tree clean, suite green at every commit)

| Arc | Commits | One-liner |
|---|---|---|
| Cleanup pile | `c7530e4` `9509e53` `8f28cdd` `7f4c4be` `0b17dc9` | cliff.toml 2 real bugs + CHANGELOG regen; dead-code orphans; **double-full-bind FIXED** (PlanetSurfaceBindStamp content dedupe of the gen-completion chase — chase must stay, 105M class; gate: fresh-window seeks 1 bind + skip-line, was ×2); vault README delta; 22 worktrees pruned (dirty diffs archived `~/Work/lunar-horse/.agent/run/worktree-prune-archive-20260711/`); artifacts 6.2G→621M |
| Polarity flip | `9bda14f` | shared = 14 enumerated `*.Contracts` + floor (Common/Resource/Resource.Bundle.Seam permanent; Ecs ph-7; NodeGraph+Ui.NodeGraph ph-3); DynamicData was NOT dead → now bundle-local in world.pck; gate 5/5 ALC-collected 0 pinned |
| Design drafts | `3f3cd8f` `53bb5cf` | tunnel (Concept A, two-ring, dual-time = per-track cadence density on ONE canonical axis, 13 decision pts) + surrealdb (crust-only slice 1, 7 decision pts); sonnet-drafted, lead-reviewed; 4 key frames vendored |
| Cache keys | `a904de0` | crust key +Seed+SpinRate+GraphRevision (all 3 sites threaded real values); filmstrip plugin-local key +GraphRevision; **Seed on the filmstrip side = documented T1-blocked residue** |
| RocksDb spike | `9fad471` | **D-NOT-VIABLE as spiked**: plumbing passes (zero unify-storage changes) but SDK embedded engine holds native lock for process lifetime + NO native-dylib export packaging exists anywhere; 3 revisit conditions in `evidence/2026-07-11-rocksdb-spike/` |
| Persistence slice 1 | `84f3aa3` `7680be2` | LiteDB behind resident IDocumentStore (App.Common Bootstrap, `user://crust-cache.litedb` → `~/Library/Application Support/FantaSim/complete-app/`); payload = pipeline output only; SchemaVersion+MVID invalidation; fail-loud NoOpTruthEventStore sentinel; **gate: 12/12 cross-process warm restores 0–42 ms (cold 9–102 ms)** |
| Tunnel slice 1 | `a4ee222` `ccc95e3` `c17786a` `4d513a0` `271e9f9` | plan; tasks 1–5 (pure modules TDD 31 tests, TunnelCorridorLayout = FIRST TimeDomain.Rung consumer, IFilmstripFrameSink seam, binder ActiveRoot spike seam); task 6 checkpoint (policy +4 floor entries — **check-dual CAUGHT the transitive pull**: Timeline.Seam closure drags Command/Ui.Seam/Ui into bundle deps); tasks 7–11 (ITunnelPresentation, mount/rings/corridors, quad sink, ring scrub, `timeline.tunnel_view` + F9); Task-12 gate run, first render vendored |

Suite: **1,209/1,209** (18 projects) at `4d513a0`. Ollama Cloud GLM was quota-dead all session —
all agents were in-house sonnet subagents (user-authorized routing), lead-gated per packet.

## 2. FIRST THING NEXT SESSION — the user's eye-judgment sitting, FOUR items

An exported app may still be running (ingress :19292, tunnel build). If not:
`remote__enabled=true <repo>/build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/complete-app`
(fresh boot is FINE — crust cache warm-restores now; G32 fresh-boot-for-eye-work still applies).

1. **D8b scrub feel** — real mouse drag on the 2D timeline handle (carried from session 1).
2. **The rescaled 2 kb run** (`8f5f4d3`) — one-constant revert (`Service.MobilePlateWindowTicks`)
   or raise the intent (carried from session 1).
3. **Tunnel first look** — enable via `timeline.tunnel_view {"enabled":true}` or **F9** in-window.
   KNOWN STATE: geometry mounts/toggles/reloads clean (ALC 7/7, 0 pinned) but the stage camera
   sits INSIDE the placeholder-scale geometry (ThroatRadius=2.5, OuterRadius=18) — it reads as
   intersecting planes, NOT yet a legible tunnel (`evidence/2026-07-11-tunnel-slice1-gate/`).
   The eye-tune pass (constants + a dedicated camera pose) is hot-reload-iterable now
   (`task bundle:world` + `bundle:install` — NO more full rebuilds for tunnel visuals).
4. **Real-mouse ring scrub + F9** — only the user's mouse can claim these (D2); dragging the
   amber ring should drive the same D8b low-rung/climb pipeline as the 2D handle.

## 3. Frontier after the sitting (ranked)

1. **Tunnel eye-tune round** — falloff/radii constants, camera pose, colors; then stamp the
   tunnel spec LOCKED with the adjustments.
2. **Tunnel slice 2** — the two-ring rung-select widget; shared-globe hookup (binder
   `ActiveRoot` seam exists; spike verdict was inconclusive — needs the cheap windowed
   second-camera check); per-sphere local rings (dual time base §3.2); graph corridors beyond
   dimmed wedges; tunnel filmstrip `graphRevision=0` simplification → thread the real value.
3. **Filmstrip persistence (slice 2 of the surrealdb spec)** — disk budget/encoding decision
   points 4–5 still open.
4. **Phase 3+ bundle queue** (NodeGraph→ui bundle etc.) — note the floor now carries 4 tunnel
   entries whose phase tags say when they can drop.
5. D5/D7b compose-node arc (compose-json still correctly untriggered — tunnel didn't need it).

## 4. Small residues (each documented in code/spec, none blocking)

- Tunnel `IsEnabled` resets to hidden after every world-bundle reload (re-toggle during iteration).
- Tunnel filmstrip cache keys use `graphRevision=0` (no cheap access in the world-bundle binder).
- Filmstrip texture key lacks Seed (T1 contract boundary — documented in FilmstripTextureCacheKey).
- DynamicData now ships bundle-local in world.pck — FieldView's DynamicData paths have never been
  exercised in an export; first real use will be the true test.
- RocksDb option D revisit conditions (3) in the spike evidence README.

## 5. Gotcha ledger delta (G36–G40; G1–G35 stand)

- **G36** Ollama Cloud weekly quota exhaustion makes `opencode run` STALL SILENTLY (75 min, frozen
  logs, zero edits) — curl-probe `127.0.0.1:11434/api/generate` BEFORE dispatching; routing is the
  user's call (this session: in-house sonnet subagents).
- **G37** every NEW bundle→resident reference edge can drag a transitive closure into bundle deps —
  run `stage_bundle.py --all --no-build --check-dual` at every edge addition; promotions follow the
  flip's structural rule (host ProjectReference + bundle-referenced ⇒ shared exactMatch).
- **G38** SurrealDb.Net 0.10.2 embedded engines hold their native lock for the PROCESS lifetime —
  any future embedded-engine store must open once per process, never close-reopen.
- **G39** rtk-filtered output is NOT raw: `git diff > file` writes the filtered summary (invalid
  patch) and grep with parenthesized patterns can false-negative — use `rtk proxy git diff` for
  patches and corroborate surprising zero-match greps.
- **G40** agent worktrees can lag main at spawn — verify/fast-forward the worktree branch before
  building on it (the tasks-1-5 agent caught this itself).

## 6. Standing rules (restated, binding)

Provider routing per dispatch is the USER's call. Look changes are eye-judged by the USER.
Real-mouse doctrine for "user can X" claims. Canonical ticks + odometer everywhere; Ma/Ga only at
import bridges. Never create repos/projects/packages without asking (the RocksDb spike correctly
STOPPED at a would-be plate-projects change). External agents never commit — lead reviews by
artifacts, re-runs the suite (G23), verifies claims against source (G30 — caught the "polarity
flip precedent" mischaracterization and the DynamicData-is-dead falsehood this session).
