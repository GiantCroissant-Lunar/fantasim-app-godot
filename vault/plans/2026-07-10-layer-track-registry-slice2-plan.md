# Layer→Track Registry — slice 2 implementation plan (2026-07-10)

Spec: [2026-07-10-layer-track-registry-design.md](../specs/2026-07-10-layer-track-registry-design.md)
(slice-1 gate PASSED; compose-json direction locked but NOT in scope here).
Slice-1 plan (structure to mirror): [2026-07-10-layer-track-registry-slice1-plan.md](2026-07-10-layer-track-registry-slice1-plan.md).
TDD throughout (RED→GREEN→REFACTOR). Implementer: subagent (no git operations — lead reviews,
commits, runs the windowed gate).

Scope (handover §3.1): (a) archive-restore returns a track to its SOURCE state, not hardcoded
"declared"; (b) `stream-discovery` source node + provider seam, completing the hybrid model;
(c) `laneOrder` pipeline param on `track-set` (restores geosphere-first lane order).
**No T1 contract changes** — `LayerTrackStates.Discovered` already exists. Everything lands in
the world bundle (App.World.Composition + WorldPlugin wiring) + config assets + tests.

## Grounding facts (verified against code, do not re-derive)

- Engine `ITruthEventStore` (fantasim-world contracts) has NO stream-enumeration API — only
  Append/Read/GetHead against a known `TruthStreamIdentity`. Discovery therefore enters the
  registry through an injected provider seam (mirror of `_familyDocumentProvider`), NOT an
  engine query. Engine-side enumeration arrives with the SurrealDB slice, if ever.
- The one real discoverable today: the world truth stream WorldRuntime writes
  (`TruthStreamIdentity("app","main",0,"world","default")`), surfaced app-side via
  `FantaSim.App.World.IService.GetOverviewAsync()` → `WorldOverview.WorldId` (stream key) +
  `IsDirty` (head exists ⇒ events appended).
- Lane order today = snapshot order = `TrackSetNodeHandler` sort (sphereId ordinal →
  alphabetical: atmosphere before geosphere). `TrackLaneViewModelBuilder.BuildLanes` groups in
  first-seen order — it needs NO change; ordering flows from the sort in track-set.

## Task 1 — archive-restore preserves source state (App.World.Composition)

`TrackSetNodeHandler.ApplyArchiveOverlay` (TrackPipelineNodeCatalog.cs:162-173): restoring from
archive currently hardcodes `LayerTrackStates.Declared` (the comment marks this as slice-2
scope). Change: when NOT in the archive overlay, return the descriptor with the state the
source node produced (i.e. leave `track.State` untouched); only flip TO `Archived` when
overlaid. Delete the stale comment.

Tests FIRST (`App.World.Composition.Tests`, extend the existing builder/handler test file):
a source-produced `discovered` track that is archived then un-archived comes back `discovered`;
a `declared` track round-trips to `declared`.

## Task 2 — stream-discovery source node + provider seam

- `DiscoveredTrackRecord` (new file, App.World.Composition — NOT contracts): record with
  `SphereId`, `LayerId`, `StreamId` (`LayerTrackStreamId`), `DisplayName`,
  `ContentType` (string), `ContentSource` (string?), `CadenceTicks` (long?),
  `Capabilities` (IReadOnlyList<string>?), `SourceRef` (string?).
- `TrackPipelineBuildContext` gains `IReadOnlyList<DiscoveredTrackRecord> DiscoveredTracks`
  (init, default empty list).
- New kind: `TrackPipelineNodeKinds.StreamDiscovery = "stream-discovery"`; handler
  `StreamDiscoveryNodeHandler.Execute` maps each record → `LayerTrackDescriptor` with
  `State = LayerTrackStates.Discovered`, `TimeDomain` = (0, null, "ka") like the other sources,
  defaulting `Capabilities` to `["scrub","toggle"]` and `SourceRef` to `ContentSource ?? "stream-discovery"`.
  Register in `TrackPipelineNodeCatalog.Handlers`.
- `LayerTrackRegistryBuilder.Build` + `LayerTrackRegistryService`: thread an optional
  `Func<IReadOnlyList<DiscoveredTrackRecord>>? discoveredTracksProvider = null` (appended ctor
  param, backward compatible); service invokes it inside `BuildSnapshotLocked()` with the same
  try/log-warn/empty-fallback discipline as the family provider.
- `WorldPlugin.CreateLayerTrackRegistry` wires the real provider: resolve
  `_registry?.TryGet<FantaSim.App.World.IService>()`, call `GetOverviewAsync()`; if null or
  `!IsDirty` → empty list; else ONE record: SphereId `"world"`, LayerId `"world.truth-events"`,
  StreamId `new("app","main","L0","world","default")` (matches WorldRuntime's identity),
  DisplayName `"Truth Events"`, ContentType `"events"` (renders via the generic presenter
  fallback by design — no new presenter in this slice), ContentSource = `overview.WorldId`,
  CadenceTicks null. This is REAL data (the append-only truth stream), not a demo asset —
  no-smoke rule satisfied; do not invent any other records.
- Shipped `project/hosts/complete-app/config/track-pipeline.json`: add node
  `{ "nodeId": "discovery", "kind": "stream-discovery", "params": {} }` + wire
  `discovery → trackSet`; bump `revision` to 2 and extend the comment.

Tests FIRST: handler maps records → discovered descriptors (field-by-field); empty/absent
provider → no discovered tracks; provider throwing → logged, empty, build still succeeds;
merged track-set output contains discovered + declared with archive overlay applying to both.

## Task 3 — `laneOrder` param on track-set

- `TrackSetNodeHandler.Execute`: read optional `laneOrder` (JSON array of sphereId strings)
  from `node.Params` via JsonNode/JsonObject access (NEVER anonymous-type serialization —
  ALC-pin house rule). Sort: sphere precedence = index in `laneOrder`; spheres not listed sort
  AFTER all listed ones, ordinal among themselves; within a sphere, layerId ordinal (unchanged).
  Missing/empty/malformed-entry param ⇒ exactly today's behavior (sphereId ordinal).
- Shipped `track-pipeline.json`: `"params": { "laneOrder": ["geosphere", "atmosphere"] }` on
  the trackSet node (restores the pre-slice-1 geosphere-first order; "world" intentionally
  unlisted → discovered lane lands last).

Tests FIRST: laneOrder reorders spheres; unlisted spheres follow listed ones; empty/missing
param preserves alphabetical; layer order within a sphere unaffected.

## Task 4 — suite + handoff

- `dotnet build project/FantaSim.sln` clean; full `dotnet test` green (baseline 1075).
- Do NOT commit. Do NOT run the windowed gate (lead does: world-bundle hot-reload + config
  re-provision, then the acceptance gate below).
- Leave `AGENT-SUMMARY.md` at repo root: files added/changed per task, test counts, deviations
  with reasons, anything discovered mid-implementation the lead should know before gating.

## Slice-2 acceptance gate — **RESULT: 4/5 PASS, point 5 FAILS on a PRE-EXISTING defect (2026-07-10)**

Evidence: `../specs/evidence/2026-07-10-track-registry-slice2-gate/`. (1) PASS — lanes render
geosphere→atmosphere (screenshot; was alphabetical). (2) PASS — clean boot baseline 7; one
`timeline.seek` fires the crust trigger → real generation appends a truth event → `IsDirty`
→ `registry.reload` reports 8; World lane renders LAST with the Truth Events track through the
generic presenter (screenshot; degradation guarantee held). (3) PASS — archive removed the
discovered track live, restore brought it back (revisions 3→5; trackCount stayed 8 while
archived — data never destroyed); restore-to-`discovered` semantics pinned by unit tests.
(4) PASS — suite 1091/1091 (lead-run); grep gate holds (only the allowed sphereId→schedule
lookup remains, TimelineFace.cs:837-838). (5) **FAIL — pre-existing**: `old ALC still pinned
for bundle world after unload (reload degraded)`. NOT a slice-2 regression: the first pinned
unload was tearing down the slice-1-era world ALC before any slice-2 binary had loaded, and it
reproduces new→new. TimelinePlugin's sever-on-RuntimeChanging and WorldPlugin's shutdown
(command unregister → registration dispose → instance dispose) are both correct by reading —
the pin is elsewhere; needs the ClrMD pin-hunter recipe
([[fantasim-alc-shared-type-identity]] memory). Slice 1's gate only ever hot-reloaded the
TIMELINE bundle; the world-bundle reload path was likely never exercised clean. Behaviors were
re-verified on a clean single-ALC boot after the degraded-reload session.

Original gate definition (for reference):

1. Fresh registry.reload baseline: lanes ordered geosphere → atmosphere (laneOrder proof —
   was alphabetical).
2. After world generation has appended truth events (boot or explicit generation command):
   `registry.reload` → trackCount includes `world.truth-events` with state `discovered`, lane
   "world" renders last with the generic presenter. If nothing appends truth events in the
   current export, REPORT that honestly (mechanism still unit-proven) — do not fabricate a
   stream.
3. `timeline.set_track_archived` on the discovered track: archive removes it live; restore
   returns state `discovered` NOT `declared` (Task 1 proof, observable via registry snapshot).
4. Full suite green; grep gate still holds (no lane literals in TimelineFace).
5. `old ALC collected for bundle world` after hot-install (timeline bundle untouched this slice).

## Constraints (hard)

- No new csproj/repo/package. No T1 contract edits (contracts/App.World/** untouched).
- Edits ONLY under: `project/plugins/App.World.Composition/`, `project/plugins/App.World/`
  (WorldPlugin wiring only — do not touch Services/*), `project/hosts/complete-app/config/`
  (the pipeline json only), `project/tests/App.World.Composition.Tests/`.
- Do not touch Host.cs, TimelineFace, TrackLaneViewModelBuilder, or anything in App.Timeline*.
- Canonical ticks everywhere; no Ma/Ga identifiers, comments, or wire keys.
- Match existing code style exactly (file-scoped namespaces, doc comments citing vault paths).
