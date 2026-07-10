# Layer→Track Registry — design (2026-07-10)

**Status:** DIRECTION LOCKED for slice 1; compose-json section is PROTOTYPE-VALIDATED-PENDING
(user explicitly unsure — revisit after slice 1 evidence). Companion arcs: D5/D7b (directives
spec ADDENDA), tunnel timeline (separate future spec), SurrealDB persistence (separate slice).

## Problem

Tracks/lanes in the timeline are hardcoded (TimelineFace `PopulateLane` — literal `"geosphere"`,
`"atmosphere"`); layer composition rules are a C# table (`LayerCompositionDecision`); world
generation is stacked layers across spheres, but layer add/remove is invisible to every view.
A future Unity consumer must be able to add layers/tracks that round-trip back into this app.

## User decisions (2026-07-10, dialogue)

1. **Layer truth = hybrid**: generation family json declares generation layers; truth-stream
   discovery contributes non-generation tracks (observations, events, saved views). One merged
   registry feeds every view.
2. **Composition home = own json, per sphere** (beside the family json, NOT inside it —
   composition is an R/M presentation product, not causal truth).
3. **Add/remove = declared-always + archive**: declared layers always have tracks (dimmed when
   empty/pre-onset); removing a declared layer = family-json edit; discovered tracks appear on
   first content, "remove" = archive flag; truth-stream data is never destroyed.
4. **No hard code**: pipelines and policies are data (jsons); code exists only as catalogs of
   small registered handlers (node executors, content presenters). Views are generic interpreters.
5. **Data-oriented end state**: eventually the generation family defaults also move from
   code-built (`WorldGenerationGraphDefaults.BuildFamily`) to shipped JSON assets. Not slice 1.
6. **Units**: every time field in every json is canonical ticks (+ rung labels for display).
   Ma/Ga is structurally excluded from all new schemas; existing Ma-named wire keys get renamed
   at the boundary (parameter-surface audit addendum below governs what enters v1 schemas).
7. Terminology: these artifacts are called **jsons** (versioned record trees; serialized JSON),
   not "documents".

## The three jsons

Same structural family as the existing generation graphs: nodes (kind string + JSON-schema'd
params) + wires + `SchemaVersion` + `Revision`.

1. **Generation family json** (exists) — causal authority: spheres, layers, generation pipelines.
2. **Pipeline json** (new, one) — how tracks materialize: sources (`family-layers`,
   `declared-layers` (a json asset of declared-but-not-yet-generating layers — the
   declared-always decision makes these first-class), `stream-discovery`, `compose-refs`) →
   merge/filter → `track-set` output. The hybrid decision is wiring here, not code. Default
   asset reproduces today's lanes.
3. **Compose json** (new, per sphere) — domain compose nodes (`geometry-stack`,
   `coloring-priority`, `exaggeration-ratio`, `visibility-weight`) replacing
   `LayerCompositionDecision`. Re-evaluated on active-set toggle / json edit / regime boundary;
   output object is applied (not decided) by the presentation binder. **DEFERRED past slice 1.**

At rest: slice 1 = JSON assets loaded beside the app; later = unify-storage/SurrealDB documents
(same jsons, different shelf; gives revision history + cross-session identity).

## Track descriptor (v1 sketch — schema-first, extensible)

```json
{
  "sphereId": "geosphere",
  "layerId": "geosphere.crust",
  "streamId": { "variation": "main", "branch": "default", "l": "L0", "domain": "world", "model": "default" },
  "displayName": "Crust",
  "state": "declared | discovered | archived",
  "timeDomain": { "startTick": 100000000, "endTick": null, "rung": "ka" },
  "content": { "type": "filmstrip", "source": "<stream address>", "cadenceTicks": 5000000 },
  "capabilities": ["scrub", "toggle", "expand-graph"],
  "sourceRef": "<family-json layer-scope graph id | stream address>"
}
```

- `content.type` is an extensible string; seed vocabulary from the tunnel-design legend:
  `world-context`, `filmstrip` (frames), `series`, `graph`, `observations`, `events`.
- Unknown `content.type` → generic strip (label + scrub). This is the Unity round-trip
  degradation guarantee: never invisible, never a crash, richer only when a presenter exists.

## Architecture (slice 1 scope marked ✔)

- ✔ **Contracts** (existing `contracts/App.World/Composition` namespace — no new project):
  `LayerTrackDescriptor`, `LayerTrackRegistrySnapshot`, `ILayerTrackRegistry`
  (snapshot + `Changed` event + `SetArchived`). Canonical-tick fields only.
- ✔ **Registry builder** (T3, App.World.Composition plugin): interprets the pipeline json.
  Slice 1 interpreter supports `family-layers` source → `track-set` output (+ archive state).
  Stream-discovery source lands in slice 2; the json shape already declares it.
- ✔ **TimelineFace consumes the registry** (via the resident-context proxy pattern, rebind-safe
  like `_generationGraphFamilyProvider`): one lane per distinct `sphereId`, one track per
  descriptor. The hardcoded two-lane build is DELETED. Content strips render through a presenter
  lookup keyed by `content.type` (slice 1 presenters: filmstrip + graph = existing code behind
  the lookup; generic fallback for unknown types).
- ✔ **Ingress**: `timeline.set_track_archived {sphereId, layerId, archived}` for the gate + a
  path that adds a declared layer (family revision) at runtime.
- ✔ Node-graph track content keeps working (existing D7c chip/expand path reads descriptors).
- ✘ Compose json + evaluators (deferred; `LayerCompositionDecision` stays behind the registry).
- ✘ Stream-discovery source, SurrealDB shelf, tunnel view, family-defaults-to-JSON migration.

## Slice-1 acceptance gate (falsifiable, windowed)

In the exported windowed app: (1) adding a REAL declared layer (e.g. `hydrosphere.ocean`) to
the `declared-layers` json asset + `registry.reload` ingress makes a new lane+track appear
reactively — no restart (family-json runtime editing waits for the data-migration slice); (2) `timeline.set_track_archived` on a real layer removes/restores its track live;
(3) grep gate: no literal `"geosphere"`/`"atmosphere"` lane strings remain in TimelineFace;
(4) full suite green; (5) hot-reload gate (`old ALC collected`) for the timeline bundle.
Contract additions are T1 → full re-export before the gate (verify-windowed decision table).

## Open questions (carried forward, not blockers)

- Compose json node vocabulary + whether its evaluation output fully subsumes GlobeViewMode
  (user unsure — decide with slice-1 evidence in hand).
- Ring control (vs bar) for huge time scaling in the tunnel view — implementation-time detail.
- Where archive state persists before SurrealDB lands (slice 1: app-local json sidecar).
- Parameter-surface audit: **COMPLETE** — see
  [2026-07-10-parameter-surface-audit.md](2026-07-10-parameter-surface-audit.md) (25 findings).
  Its drop/rename/declare gates govern v1 schemas. Headlines: `continentalPatches` and
  `spinRateRadiansPerMegaAnnum` are placebo knobs (must be wired or dropped, never frozen);
  `rotationSource` has two competing undeclared wire shapes; crust.generate's ~20 real inputs
  are invisible to the catalog; 7 Ma-named wire keys have already-winning `*PerTick` alternates.
