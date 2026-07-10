# Layer→Track Registry — slice 1 implementation plan (2026-07-10)

Spec: [2026-07-10-layer-track-registry-design.md](../specs/2026-07-10-layer-track-registry-design.md).
TDD throughout (RED→GREEN→REFACTOR): every task writes its failing test first.
Implementer: subagent (no git operations — lead reviews, commits, runs the windowed gate).

## Task 1 — Contracts (T1, existing project `project/contracts/App.World`, namespace `FantaSim.App.World.Composition`)

New files under `project/contracts/App.World/Composition/`:
- `LayerTrackDescriptor.cs` — record per the spec sketch: `SphereId`, `LayerId`,
  `StreamId` (record: Variation, Branch, L, Domain, Model), `DisplayName`,
  `State` (string: declared|discovered|archived — string, not enum, per house schema-first rule),
  `TimeDomain` (StartTick long, EndTick long?, Rung string), `Content` (Type string, Source string?,
  CadenceTicks long?), `Capabilities` (IReadOnlyList<string>), `SourceRef` (string).
  All time fields canonical ticks. NO Ma/Ga vocabulary anywhere.
- `LayerTrackRegistrySnapshot.cs` — record: `Revision` (int), `Tracks` (IReadOnlyList<LayerTrackDescriptor>).
- `ILayerTrackRegistry.cs` — `LayerTrackRegistrySnapshot Current { get; }`,
  `event Action<LayerTrackRegistrySnapshot>? Changed`, `void SetArchived(string sphereId, string layerId, bool archived)`,
  `void Reload()`.
- JSON round-trip via System.Text.Json with JsonObject/JsonNode composition where dynamic —
  NEVER `JsonSerializer.Serialize(new {...})` anonymous types (ALC-pin house rule).

Tests FIRST (`project/tests/App.World.Composition.Tests/LayerTrackDescriptorTests.cs`):
serialize→deserialize round-trip preserves every field; unknown extra json fields are tolerated
on read (forward-compat); `State` accepts unknown strings without throwing.

## Task 2 — Pipeline json + registry builder (T3, `project/plugins/App.World.Composition`)

- `TrackPipelineDocument.cs` — minimal nodes+wires record (kind + params JsonObject), SchemaVersion, Revision.
- `LayerTrackRegistryBuilder.cs` — interprets a pipeline document. Slice-1 node kinds (each a
  small handler registered in a `TrackPipelineNodeCatalog`, mirroring WorldGenerationNodeCatalog's
  shape): `family-layers` (input: the WorldGenerationGraphFamilyDocument — layer-scope graphs →
  declared descriptors; sphereId parsed from layerId prefix), `declared-layers` (input: a json
  asset listing additional declared layers), `track-set` (output: merge inputs, apply archive
  overlay, stable sort by sphereId then layerId).
- `LayerTrackRegistryService.cs` — implements `ILayerTrackRegistry`; holds archive overlay
  (slice 1 persistence: a sidecar json under user:// — load on start, save on change);
  raises `Changed` on SetArchived/Reload; registered into the composition the same way existing
  App.World.Composition services register.
- Default pipeline json asset: reproduces today's view (geosphere layers from the family doc;
  atmosphere declared via `declared-layers` so the existing atmosphere lane keeps existing —
  atmosphere has no generation layers yet, which is exactly what declared-layers is for).
  Ship under `project/hosts/complete-app/config/track-pipeline.json` + a `declared-layers.json`
  beside it (contains the atmosphere sphere declaration).

Tests FIRST (`App.World.Composition.Tests`): builder with a fake family doc yields one declared
descriptor per layer-scope graph; declared-layers merge; archive overlay flips State and survives
rebuild; Changed fires exactly once per mutation; unknown node kind in pipeline json → clear error
naming the kind (no silent skip).

## Task 3 — Ingress commands (T3, `project/plugins/App.Timeline` — timeline category owns track UX)

- `timeline.set_track_archived` `{"sphereId","layerId","archived":bool}` → registry.SetArchived.
- `registry.reload` (category "world") → registry.Reload() (re-reads pipeline + declared-layers assets).
- JsonObject responses only. Update command Description strings.
Tests FIRST: payload parsing (mirror TimelineSeekOriginParsingTests style).

## Task 4 — TimelineFace consumes the registry (T4 seam, `project/plugins/App.Timeline.Seam/TimelineFace.cs`)

- Registry reaches the face via the resident-context proxy pattern (mirror
  `_generationGraphFamilyProvider`: provider field + BindResidentContext/ClearResidentContext +
  rebind-safe; NO direct static access; unsubscribe `Changed` on ClearResidentContext/_ExitTree —
  ALC discipline).
- Replace the hardcoded `PopulateLane(_ctl.GeosphereSchedule, ..., "geosphere")` /
  `PopulateLane(_ctl.AtmosphereSchedule, ..., "atmosphere")` pair with: group current snapshot
  by SphereId → one lane per sphere (regime band still comes from the sphere schedule where one
  exists — keep `_ctl.GeosphereSchedule`/`AtmosphereSchedule` lookup by sphereId for now, absent
  schedule → lane without regime band), one track row per descriptor.
- Content strip rendering goes through a small presenter lookup keyed by `Content.Type`:
  slice-1 entries wrap the EXISTING filmstrip path (`filmstrip`) and EXISTING chip/graph path
  (`graph`); any other type → generic labeled strip (`CompactStripLabel` + scrub still works).
  The lookup is a Dictionary<string, Func<...>> populated at face construction — data-keyed,
  no switch on layer ids anywhere.
- On `Changed`: rebuild lanes via the existing CallDeferred/coalesce discipline (reuse
  ViewRebuildCoalesceSeconds); archived tracks disappear, unarchived reappear; declared-empty
  tracks render dimmed (modulate) with their generic/declared strip.
- Grep gate: after this task, no literal `"geosphere"`/`"atmosphere"` LANE strings remain in
  TimelineFace lane-building code (schedule lookup table maps sphereId→schedule and may carry
  the two known ids in ONE table with a comment; the lane list itself must be registry-driven).

Headless-testable parts (pure grouping/ordering/dimming decisions) go in a small
`TrackLaneViewModelBuilder` (App.Timeline or Seam-internal static, Godot-free) with tests FIRST
in `App.Timeline.Tests`.

## Task 5 — Suite + handoff

- `dotnet build project/FantaSim.sln` clean; full `dotnet test` green.
- Do NOT commit. Do NOT run the windowed gate (lead does: full re-export — T1 contracts changed —
  then the spec's 5-point acceptance gate incl. hydrosphere.ocean add + archive toggle +
  `old ALC collected`).
- Leave `AGENT-SUMMARY.md` at repo root: files added/changed per task, test counts, any
  deviations from this plan with reasons, anything discovered mid-implementation the lead
  should know before gating.

## Constraints (hard)

- No new csproj/repo/package. No edits outside: contracts/App.World/Composition,
  plugins/App.World.Composition, plugins/App.Timeline, plugins/App.Timeline.Seam,
  hosts/complete-app/config (new assets only), tests projects named above.
- Respect Host._Ready invariant (don't touch Host.cs at all).
- Canonical ticks everywhere; no Ma/Ga identifiers, comments, or wire keys in new code.
- No smoke/demo assets: hydrosphere.ocean appears only in the GATE (lead-side), not shipped in
  the default declared-layers.json (which declares only the real current atmosphere sphere).
- Follow existing code style; match TimelinePlugin/TimelineFace patterns exactly.
