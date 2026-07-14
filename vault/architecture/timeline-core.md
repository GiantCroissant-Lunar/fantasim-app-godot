---
source: project/contracts/App.World/Composition/{ILayerTrackRegistry,LayerTrackRegistrySnapshot,LayerTrackDescriptor,TimelineTickOrigin,ITimelineController}.cs, project/plugins/App.World.Composition/{LayerTrackRegistryService,LayerTrackRegistryBuilder,TrackPipelineNodeCatalog,WorldStreamVocabulary}.cs, project/plugins/App.World/WorldPlugin.cs, project/plugins/App.Timeline.Seam/{TimelineFace,TimelineFace.Lanes,TimelineFace.Input,TrackLaneViewModelBuilder}.cs, project/plugins/App.Timeline/{Services/Service,TimelinePlugin}.cs, project/contracts/App.Timeline/{TimelineModel,TimelineScrubCoalescer}.cs, project/plugins/App.Presentation/{PlanetTimelineController,PlanetPresentationBinder,ScrubRefreshCoordinator}.cs; specs/plans: vault/specs/2026-07-10-layer-track-registry-design.md, vault/plans/2026-07-10-layer-track-registry-slice{1,2}-plan.md, vault/plans/2026-07-11-timelineface-split-plan.md, vault/plans/2026-07-14-l-axis-doctrine-alignment-plan.md (authored 2026-07-14)
source-status: current-code documentation
distilled: 2026-07-14
divergence: >
  Filmstrip texture production (FilmstripPreviewController render pipeline), HUD-mode
  epochs/tunnel-loss safety, and the expanded per-track GraphEdit view are named but not
  described in depth; boom-hud and node-graph docs own those. D8b rung-ladder refresh is
  described only as far as the timeline drives it.
---

# Timeline core — layer-track registry, TimelineFace, playback/scrub

The registry-driven timeline is the app's time cockpit: a merged track list every view reads
instead of hardcoding lane names, a resident Godot face that renders it, and a playback/scrub
pipeline whose values always come from the world fold. The 3D tunnel view
([[tunnel-presentation]]) consumes the same registry and the same scrub-origin pipeline.

## Doctrine (what must hold — cited, not restated)

- **Truth vs view** — hub `fantasim-hub/vault/architecture/planet-stack-model.md` §8: the
  animation is a projection; the world at tick T is `fold(events ≤ T)`; the playhead drives
  `RequestTick → fold`; the value at a tick comes from the kernel fold, never from Godot
  interpolating keyframes. Godot supplies transport, track/section UI, and state-machine blend
  only, and appears only in the render seam. §8 also records the resolved build choice: the
  native `.tscn` AnimationPlayer timeline (multi-lane, stream-addressed, odometer-ladder
  labels) IS the shipped form.
- **L axis** — hub `lrm-axis-model.md`: world-scoped streams are L2;
  `WorldStreamVocabulary` is the sole production minting point for five-axis identities.
- **Variant × Branch** — hub `variant-and-branch.md`: identity axis order is
  variant/variation first, then branch. Doctrine values for an unaddressed default track:
  variation=`"default"`, branch=`"main"`.
- **Display units** — odometer-ladder rungs only (anchor `ka`), never Ma/Ga; ladder mechanics
  in engine `fantasim-world/vault/architecture/canonical-foundation.md`.

## Built (code-verified 2026-07-14)

### Registry contract (T1, `contracts/App.World/Composition/`)

`ILayerTrackRegistry` — `Current` (never null), `Changed` event, `SetArchived`, `Reload`.
`LayerTrackRegistrySnapshot(Revision, Tracks)`; `Revision` increments on every rebuild.
`LayerTrackDescriptor` rows carry everything a view needs without knowing the domain concept:
`SphereId`, `LayerId`, `StreamId`, `DisplayName`, `State`, `TimeDomain`, `Content`,
`Capabilities`, `SourceRef`. Wire keys are pinned camelCase via `JsonPropertyName` (a
cross-process/cross-tool contract, not an in-process DTO).

- `LayerTrackStreamId(Variation, Branch, L, Domain, Model)` — the five-axis product address.
- `LayerTrackTimeDomain(StartTick, EndTick?, Rung)` — canonical ticks; `Rung` is an
  odometer-ladder symbol (e.g. `"ka"`), never Ma/Ga; `EndTick` null = open-ended.
- `LayerTrackContent(Type, Source?, CadenceTicks?)` — `Type` is an extensible **string**
  (`LayerTrackContentTypes`: world-context, filmstrip, series, graph, observations, events,
  declared-empty), and `LayerTrackStates` (declared/discovered/archived) is likewise strings,
  per the house schema-first rule: unknown values round-trip, never throw.

### Track pipeline (T3, `plugins/App.World.Composition/`)

`LayerTrackRegistryBuilder.Build` is a pure interpreter (no I/O, no Godot, no service state):
it walks a `TrackPipelineDocument`'s nodes in declared order, resolves each kind through
`TrackPipelineNodeCatalog.Find` (throws on unknown kind — no silent skip), and returns the
track-set node's output. Registered handlers:

| Node kind | Handler | Produces |
|---|---|---|
| family-layers | `FamilyLayersNodeHandler` | one **declared** filmstrip track per `WorldLayerGraphBinding` in the generation family json; sphereId derived from the layerId dot-prefix |
| declared-layers | `DeclaredLayersNodeHandler` | one **declared** track per declared-layers json entry (content type defaults to `declared-empty`) |
| stream-discovery | `StreamDiscoveryNodeHandler` | one **discovered** track per `DiscoveredTrackRecord` from the injected provider seam |
| track-set | `TrackSetNodeHandler` | sink: merges wired sources, applies the archive overlay, stable-sorts by sphereId/layerId with an optional `laneOrder` param (JSON array of sphereIds) overriding sphere precedence |

`LayerTrackRegistryService` owns the impure half: reads `config/track-pipeline.json` +
`config/declared-layers.json`, asks providers for the family document and discovered tracks
(both fail soft to empty with a warning), and persists the archive overlay to a sidecar json
(`data/layer-track-archive.json`) so user-hidden tracks survive restart. Archiving only flips
`State`; underlying data is never deleted.

**Composition:** `WorldPlugin.ComposeLayerTrackRegistry` (in `plugins/App.World/WorldPlugin.cs`)
constructs and registers it (`OwnerId "app.world"`), resolving asset paths against both
`AppContext.BaseDirectory` and the exe dir (macOS export splits them). The discovery provider
is `WorldPlugin.DiscoverTruthStreamTracks`: today it surfaces exactly one real discoverable —
the append-only world truth stream (`world.truth-events`, content type `events`) once
`WorldOverview.IsDirty` shows a head. No engine stream-enumeration API exists; discovery is
app code that already knows which streams exist.

### Stream-identity convention on tracks

`WorldStreamVocabulary` (`plugins/App.World.Composition/` — deliberately NOT T1 contracts, for
ALC type-identity) is the only production minting point. `TrackDefault()` returns
`("default", "main", "L2", "world", "default")` — **variation first, branch second**. A
variation/branch transposition (`("main", "default", …)`) in the pipeline node handlers was
found by the 2026-07-14 stack-model audit and fixed the same day
(`vault/plans/2026-07-14-l-axis-doctrine-alignment-plan.md` Task 1b).
`StreamVocabularyGuardTests` (tests/App.World.Tests) source-scans production files so no
inline `new TruthStreamIdentity(`/`new LayerTrackStreamId(` can reappear outside the
vocabulary and codec decode paths; IP-shaped world ids throw at mint time (ingress leak guard).

### Lanes and presenters (`plugins/App.Timeline.Seam/`)

`TrackLaneViewModelBuilder.BuildLanes` (Godot-free, linked into App.Timeline.Tests) groups a
snapshot into one lane per distinct sphereId in first-seen order, drops archived tracks, and
resolves each track's `TrackContentPresenterKind` from `content.Type`: filmstrip → Filmstrip,
graph → Graph, **anything else → Generic**. `TimelineFace` dispatches through a data-keyed
presenter dictionary (`_trackContentPresenters`) — never a switch on layer/sphere ids.
`RenderGenericTrackContent` is the degradation guarantee: an unknown content type renders as a
labeled, dimmed strip — never invisible, never a crash (`declared-empty` deliberately has no
presenter so pre-onset declared tracks fall through to exactly this).

### TimelineFace and the native AnimationPlayer (`plugins/App.Timeline.Seam/TimelineFace*.cs`)

`TimelineFace` is a resident `Control` split into partials (`.Lanes`, `.Input` — split plan
2026-07-11). It binds collectible services through the resident-context proxy (never a
static from the bundle side), and re-registers playback on controller swap so hot-reload
never leaves Play/Pause/Seek unwired. ALC discipline is explicit: registry `Changed` is
unsubscribed in both `ClearResidentContext` and `_ExitTree`, and in-flight filmstrip renders
are cancelled so threadpool stacks cannot root an outgoing bundle ALC.

`SetupAnimationSystem` builds the native pieces if the scene lacks them: an `AnimationPlayer`,
an `AnimationTree` whose root is an `AnimationNodeStateMachine` with three states
(`idle`/`playing`/`scrub`, all-pairs transitions, 0.12 s crossfade — the §8 "regimes as
states" vocabulary applied to transport), and a `playing` Animation holding a single value
track on `.:InternalTick` keyed 0 → MaxTick over `MaxTick / ticksPerSecond` seconds. While
playing, Godot advances `InternalTick`; the property setter pushes each new tick into
`ITimelineController.PushTick` — Godot is the transport, the tick values are consumed by the
fold-driven side below, never interpolated into world state.

Ruler/lane labels come from `TimelineModel` (`contracts/App.Timeline/`): ladder rungs built
from `BaselineScaleProfiles.GeospherePlateTime` (anchor `ka`), `SelectRungForSpan` picks the
display rung from the current view span, and zoom steps rungs via
`TryGetFinerRung`/`TryGetCoarserRung` (wheel/magnify/buttons).

### Playback and scrub-origin flow

- `App.Timeline.Services.Service` (engine-agnostic, no Godot) owns the
  Idle/Playing/Scrubbing state machine and builds `TimelineViewSnapshot`s from the injected
  regime schedules; `AcceptTickFromFace` advances it only while Playing (scrub echoes are
  already known from its own `SeekAsync`).
- `PlanetTimelineController` (`plugins/App.Presentation/`) is the resident hinge:
  `PushTick(tick, origin)` clamps, applies via the binder callback, and suppresses
  `TickChanged` for `ScrubPreview` ticks so previews never fan out as real tick changes.
- `TimelineScrubCoalescer` (`contracts/App.Timeline/`) makes drags cheap: Press applies
  immediately as `ScrubPreview`, Motion only stores a pending tick, `ConsumeFrame` (drained in
  `TimelineFace._Process`, and by the tunnel's `TunnelGestureCoordinator` per frame) applies
  the latest pending as `ScrubPreview`, Release applies as `ScrubCommit`. The tunnel wraps
  the same coalescer — one scrub-origin vocabulary (`TimelineTickOrigin`) across 2D and 3D.
- `PlanetPresentationBinder.ApplyTimelineTick` decides whether the playhead crossed content
  (regime transition, crust-snapshot boundary) and hands `(tick, origin, heavy)` to
  `ScrubRefreshCoordinator`: previews fire an immediate low-rung refresh (LowRung=2), rest
  starts the D8b climb (MidRung=3 then full), commit/standard cancel and request full. Its
  refresh echo is suppressed so a refresh's own `UpdateFrom → PushTick` cannot cancel the
  climb that requested it.
- **The fold:** every refresh terminates in
  `WorldService.GetPlanetPresentationAsync(_timeline.Tick[, frequencyOverride])` — the world
  service materializes the presentation document at the playhead tick from truth/products.
  That call is this repo's realization of doctrine `RequestTick → fold`; nothing upstream of
  it invents values.

Tests: `LayerTrackRegistry{Builder,Service,DefaultAssets}Tests`,
`WorldPluginLayerTrackRegistryTests`, `TrackLaneViewModelBuilderTests`, `TimelineModelTests`,
`TimelinePlaybackFlowTests`, `PlanetTimelineControllerScrubOriginTests`,
`StreamVocabularyGuardTests`.

## Not built / open

- **Per-track StreamId vs "only the generator face binds truth"** — audit-flagged
  reconciliation still owed (app `vault/handover/2026-07-14-branch-arc-l2-doctrine-session-handover.md`,
  owed-doc list): every registry track carries a `LayerTrackStreamId`, but doctrine says only
  the generator face binds truth. Whether track StreamIds are addresses (view-side pointers)
  or bindings needs an explicit statement; until then treat them as display/addressing
  metadata only.
- **Time domains are placeholders.** All three source handlers emit
  `StartTick=0, EndTick=null, Rung="ka"`; precise per-track time-domain derivation is
  deferred to the compose-json arc (registry design doc, decision recorded in the handlers).
- **Discovery is one hardcoded-known stream.** Real variant/branch-aware enumeration needs an
  engine API or a richer app-side census; today only the world truth stream is discoverable.
- **Variant plumbing absent** (hub `variant-and-branch.md` restoration status): track
  `Variation` is always `"default"`/`"app"`; the variant-recipe slice is the named next arc.
- **Stale in-code remark:** `LayerTrackRegistryService`'s doc comment says it is "composed and
  owned by `FantaSim.App.Timeline.TimelinePlugin`", but the only production construction site
  is `WorldPlugin.CreateLayerTrackRegistry` (OwnerId `app.world`). The timeline plugin only
  consumes it via the registry (e.g. `timeline.set_track_archived`). Fix the comment or move
  ownership; do not trust the remark.
- **Tunnel junctions** (slice 3, next arc) extend this registry/lane model into branch
  topology — see [[tunnel-presentation]].

Lineage: specs/plans of 2026-06-22 (tscn timeline), 2026-07-10 (registry slices),
2026-07-11 (face split, D8b), 2026-07-14 (L-axis alignment + transposition fix).
