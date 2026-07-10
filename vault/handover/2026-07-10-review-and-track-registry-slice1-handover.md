# 2026-07-10 session handover — full-repo review → fix round → layer-track registry slice 1

**For the next session (fresh context). READ ORDER:** this doc →
`vault/specs/2026-07-10-layer-track-registry-design.md` (dialogue-locked decisions + gate
results + the compose-json OPEN question) → `vault/specs/2026-07-10-parameter-surface-audit.md`
(25 findings gating the v1 json schemas) → `vault/plans/2026-07-10-layer-track-registry-slice1-plan.md`
(executed; slice-2 shape follows it). Prior context:
`vault/handover/2026-07-08-directives-waves4-10-handover.md` (D-frontier),
`2026-07-08-phase25-common-resident-layer-handover.md` (bundle mechanics).

**Standing user rules (restated, still binding):** provider routing per dispatch is the USER's
call ("zai" = opencode `zai-coding-plan/glm-5.2`, "ollama cloud" = `ollama/glm-5.2:cloud`,
"sub agent (sonnet 5)" = in-house Agent tool model sonnet; codex last resort). Look changes are
eye-judged. Real-mouse doctrine for "user can X" claims. Canonical units everywhere; Ma/Ga only
at import bridges. Never create repos/projects/packages without asking. External agents never
commit — lead reviews by artifacts, commits, runs every gate.

## 1. What landed (ALL pushed, app main through `92f32a2`; suite green at every commit)

| Arc | Commits | What |
|---|---|---|
| Review fixes (4 CLI packets: zai ×2, ollama ×2, disjoint files) | `5364631..82b97cf` (6) | Scrub-origin threading (`timeline.seek` optional `origin` → PushTick; Continents membership refresh gated off ScrubPreview; windowed-proven: 6-preview+1-commit sweep = 1 crust gen + 2 binds, was 7 heavies). TimelineFace: JsonObject payload (ALC-pin), 512-cap FIFO filmstrip texture cache, dead chip helpers. Host: sever-before-dispose fixed, missing `collectible-bundles.json` now boot-fatal. Dead code: `PendingConfigurationById`, `TODO(cache)` cluster. **Flake FIXED**: BoundaryProfileLodTests was the ~1-in-3 transient (only timing test never hardened) — best-of-batches, 5/5 green. `.gitignore`: `ref-projects/` (user's claude-design export lives at `ref-projects/fantasim-app-godot/` — tunnel wireframes + screenshots, user-managed, never committed). |
| User decisions executed | `c572c4f`, `506697d` | **App.World.Seam DELETED** (user go; 991 lines: WorldViewComposition zero callers, GlobeView frozen-onset rigid-cap, feature grid never reached onset — never functioned; sln entry + stale comments cleaned; post-deletion export boot gate PASS). **Spin-rate alignment** (user: "node-graph path is the product path"): WorldCrustRunSpec fallback + node-catalog UI default 0.02→0.0035 w/ provenance; GlobeReconstructor legacy 0.02 annotated intentionally-uncalibrated. |
| Parameter audit | `7ddc201` | 25 findings, vault/specs/2026-07-10-parameter-surface-audit.md — placebo knobs, dual rotationSource shapes, crust.generate's invisible ~20-key surface, 7 Ma wire keys, dead DTO fields. Drop/rename/declare gates for v1 schemas. |
| continentalPatches | `4ad9827`, `c04ccce` | Audit #1 fixed via spawned chip session: compiler "object" kind-hint, null-unless-authored ReadPatchRecipe, both RunAsync sites forward PatchRecipe. Audit doc marked resolved. |
| **Track registry slice 1** | `a3b3876`, `92f32a2` | Registry-driven timeline lanes. T1 contracts (`LayerTrackDescriptor`/`Snapshot`/`ILayerTrackRegistry`, canonical-tick-only, camelCase, forward-tolerant); pipeline-json interpreter (`TrackPipelineNodeCatalog`: family-layers/declared-layers/track-set); `LayerTrackRegistryService` w/ archive-overlay sidecar; **WorldPlugin owns the instance + `registry.reload`**; timeline consumes contract only; TimelineFace N-lane build (`TrackLaneViewModelBuilder`, Godot-free) + content presenters keyed by content.type (filmstrip/graph/generic-dimmed fallback); `timeline.set_track_archived`; assets `hosts/complete-app/config/track-pipeline.json` + `declared-layers.json` reproduce today's exact 7 tracks/2 lanes; `build:godot:desktop` provisions them exe-adjacent. Implemented by sonnet subagent (2 rounds, strict TDD, +41 tests → 1071 total); lead fixed path resolution + ownership. **ALL 5 GATE POINTS PASS** (evidence `vault/specs/evidence/2026-07-10-track-registry-gate/`): declared-layer add → trackCount 7→8 + Hydrosphere lane live; archive removes/restores live (whole-lane disappearance works); grep gate; suite; `old ALC collected for bundle timeline`. |

## 2. Dialogue-locked design decisions (user, this session — bind future arcs)

1. **Layer truth = hybrid**: family json declares generation layers; stream discovery adds
   non-generation tracks (slice 2); merged registry feeds every view.
2. **Compose = own json per sphere** beside the family json — but **USER IS UNSURE** about the
   compose story ("this looks odd" → corrected to domain node vocabulary: geometry-stack,
   coloring-priority, exaggeration-ratio, visibility-weight; NOT AnimationTree blend/add/filter
   verbatim). Explicitly deferred: decide WITH slice-1 evidence in hand. Do not implement
   compose-json without a fresh user conversation.
3. **Add/remove = declared-always + archive**; truth-stream data never destroyed.
4. **No hard code**: pipelines/policies as jsons; code only as catalogs of small registered
   handlers; views are generic interpreters. Call the artifacts "jsons", not "documents".
5. **Data-oriented end state**: family defaults (WorldGenerationGraphDefaults.BuildFamily)
   eventually migrate code→JSON assets. Not slice 1/2 unless user re-prioritizes.
6. **Tunnel timeline** (claude-design export, 3 wireframes + Time Scale Loupe + Sphere Tracks
   Dual Time Base): user corrections — the wireframes' Ma/Ga display is WRONG, enforce canonical
   tick + odometer; prefer a RING control over the bar for huge time scaling (implementation-time
   detail); tunnel = a second skin over the same registry, AFTER the TimelineFace/view-model
   separation matures. Vendor key frames into vault when specced.
7. Track-kind seed vocabulary = the export legend: world-context, frames(filmstrip), series,
   node-graph, observations, events.

## 3. IMMEDIATE NEXT (proposed order; user picks)

1. **Slice 2 of the registry**: stream-discovery source node (completes hybrid), lane-order
   pipeline param (cosmetic regression: lanes now sort alphabetically — atmosphere above
   geosphere; was geosphere-first), `"discovered"` state restore path (code comment marks the
   spot in TrackSetNodeHandler).
2. **Compose-json decision conversation** (user has slice-1 evidence now).
3. Or resume the pre-existing frontier: **D8b progressive-resolution scrub** (scrub-origin fix
   cleared its runway; residual: `RegisterPlayback` onSeek is still `Action<long>` — widen it in
   D8b), **PlanetPresentationBinder split** (7 seams mapped in review; do BEFORE D8b/D5 code
   lands in it), **polarity flip** (6 of 7 assembly decisions remain), **D4.2 world-scope unit
   sweep** (77 Ma leaks in 10 files + the 10×-wrong tick constants — re-derive from intent;
   audit table has the exact key list).
4. **app.json ghost** (NEW, pre-existing): in exports AppContext.BaseDirectory = per-arch data
   dir (`Contents/Resources/data_*`); Bootstrap's `config/app.json` (optional:true) has NEVER
   loaded in an export — env vars carry everything. Decide: provision app.json exe-adjacent +
   probe (like the registry now does), or bless env-only and delete the json path.
5. Review backlog remainder: mixed-frame residue (Service.cs thickness/sections at snapshot
   frame vs playhead — intent decision), vault README index refresh (frozen at 07-06; the whole
   07-07..07-10 doc wave unindexed), CHANGELOG (286+ commits stale), 26 merged worktrees under
   `yokan-projects/.worktrees/`, 6.2GB/39 artifact versions, pre-commit format covers host only,
   TimelineFace split (FilmstripPreviewController first), SurrealDB first slice (crust+filmstrip
   cache persistence; crust cache also mis-keyed — omits Seed).

## 4. Gotchas NEW this session (G26+; prior G1–G25 all still stand)

- **G26 AppContext.BaseDirectory in exports = per-arch data dir with NO config/** — any
  BaseDirectory+config pattern silently no-ops in exports (app.json precedent). Probe
  exe-adjacent (`Environment.ProcessPath` dir) too, and provision via Taskfile
  (build:godot:desktop now copies registry jsons next to common-resident-expected.json).
- **G27 plugin-assembly ProjectReference between collectible bundles dual-copies the dep
  closure** — timeline→App.World.Composition pulled 8 Unify dlls into both pcks. ALWAYS run
  `python3 tools/bundles/stage_bundle.py --check-dual` after touching any plugin csproj. Fix
  pattern: owner-side composition + T1-contract-only consumption.
- **G28 cwd drift is CHRONIC in this harness** (bit 4×: git ops, app launch exit 127, ugrep
  no-such-file, background export) — prefix EVERY repo op with `cd /abs/path &&`, use absolute
  binary paths for background launches, and `pwd` as the first line of long background scripts.
- **G29 external GLM agents each left one out-of-scope .gitignore edit** — otherwise faithful;
  keep the "do NOT touch files outside scope" clause and always `git status` before commit.
- **G30 don't trust "mirrors existing pattern X" in agent reports** — X itself may be broken
  (app.json). Verify the mirrored pattern works in the EXPORT, not just in tests.
- The in-house sonnet subagent pattern worked well: strict-TDD plan → implement+unit-test only →
  lead gates; resume the SAME agent (SendMessage) for refactor rounds — it kept full context.

## 5. Drive recipes (current, verified this session)

- Launch: `remote__enabled=true <repo>/build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/complete-app` (ABSOLUTE path).
- Registry: `python3 tools/fantasim-cmd.py cmd registry.reload '{}'` → `{ok,revision,trackCount}`
  (expect 7 baseline); `timeline.set_track_archived '{"sphereId":..,"layerId":..,"archived":bool}'`;
  gate fixture = append hydrosphere.ocean to `<exe>/config/declared-layers.json` + reload
  (fixture is GATE-ONLY, never shipped — the shipped json's comment says so).
- Screenshots: `render.screenshot '{"path":"<abs>.png"}'` (viewport PNG; lanes at window bottom;
  to see below-fold lanes, archive tracks above to free space).
- Scrub gate: seeks with `"origin":"scrubPreview"` burst + one `"scrubCommit"`; heavies counted
  via log signatures `Crust generation triggered` / `Planet plate surface bound`.
- ALC gate: `task bundle:timeline && task bundle:install` → grep app log for
  `old ALC collected for bundle timeline`.
- Full re-export (T1/resident changes): `task build:godot:desktop && task bundles && task bundle:install`
  (desktop export now also provisions the registry config jsons).

## 6. State at session end

App main pushed through `92f32a2`; working tree clean. Exported app RUNNING (fresh export, all
bundles installed, baseline 7 tracks restored, ingress :19292). 1071/1071 tests green.
Memory files updated: `fantasim-review-backlog-2026-07-10` (resume pointer for the backlog +
this handover), `fantasim-basedirectory-export-verified` (G26 evidence), gplates-arc flake note
corrected. Delegation skill drift NOTED, unfixed: `.agent/delegation/<cli>/adapter.yaml` +
`.agent/delegation/README.md` referenced by the skill do NOT exist anywhere in the workspace.
Pending user answers carried forward: compose-json direction (§2.2), app.json ghost (§3.4),
slice-2 vs other-frontier priority (§3).
