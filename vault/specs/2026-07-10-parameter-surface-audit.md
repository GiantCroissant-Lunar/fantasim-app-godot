# Parameter-surface audit — what may enter the v1 JSON schemas (2026-07-10)

Companion to [layer-track registry design](2026-07-10-layer-track-registry-design.md) §Open
questions. Motivation (user directive): fields that "sit there for a long time but are not used
properly" must not fossilize into the versioned data contracts. Every claim below was verified
against the actual read site (or its absence); the only reflective binding surface
(EmbeddedNodeGraphRenderer → BoomHudGraphEditBinder, nodegraph view-models only) was checked
before declaring timeline DTO fields dead.

## Headline risks

- **Placebo knobs**: `continentalPatches` is triple-broken (compiles to a JSON string instead of
  object; parser then rejects it; and even parsed, no RunAsync call site forwards it) — authoring
  it has NEVER done anything. **RESOLVED 2026-07-10 @4ad9827: wired end-to-end** (compiler parses
  "object" kind-hints; ReadPatchRecipe is null-unless-authored so the default path stays on
  recipe init; both RunAsync call sites forward spec.PatchRecipe). `spinRateRadiansPerMegaAnnum`
  only acts as a fallback when the OnsetRoster yields zero plates — i.e. never on the product path.
- **Two competing undeclared wire shapes for `rotationSource`** (nested object vs flat keys); the
  nested one is parsed-and-validated but has zero consumers; the flat one is consumed but never
  produced by the graph path.
- **`crust.generate` declares ZERO parameters in the catalog** while its executor reads ~20 keys —
  the node's real input surface is invisible to the graph editor.
- **7 Ma-named wire keys** (+ `plates[].rate` in rad/Ma) with canonical `*PerTick` alternates that
  already win when present.

## Schema gates for v1 (drop / rename / declare)

**DROP from v1 (dead or unreachable):** `DurationMegaAnnum` property; `durationMegaAnnum`/
`durationMa`/`targetTick`/`ticks` alias chain (unreachable on the default path — `canonicalTick`
always injected via sharedParams); spec fields `VerticalExaggeration`/`Seed`(post-ctor)/
`RotationSource`(nested) (`PatchRecipe` was on this list until wired 2026-07-10 @4ad9827 —
now live, keep it); `ContinentalPlateIds` (obsolete-marked,
zero readers); `CrustSnapshotTickState.Available` (permanently false — producer never set);
timeline DTO fields `TimelineBand.Variant`/`.IsActive`/`.EndTick`, `TimelineTrack.IsActive`,
`TimelineRulerMark.Tick` (face recomputes or ignores all five); filmstrip DTO
`RequestedTick`/`SourceFrequency`/`SourceKind` (unread); RUNR request keys `sphereHandoff*`,
`lifecycleKind`, `variant`, `branch`, `productCount`, `scheduleRevision` (no reader in this repo —
verify WorldRuntime once, then drop).

**RENAME to canonical:** `orogenicPerMegaAnnum`→`orogenicPerTick` (etc. for arc/islandArc/ridge
volcanism — the `*PerTick` forms already exist and win), `plates[].rate`→`plates[].ratePerTick`,
`spinRateRadiansPerMegaAnnum`→per-tick (AND plumb into OnsetRoster.Build, or drop as placebo).

**DECLARE (read-but-invisible knobs):** crust.generate's real surface (`canonicalTick`,
`snapshotTicks`, `rotationReferenceTick`, rate keys, `plates[]`, `continentalPlates[]`, `seed`,
`frequency`, ONE rotationSource shape); body-formation `retainedHeatJ`/`retainedVolatileMassKg`;
magma-ocean/stagnant-lid `canonicalTick`+`plateOnsetTick` (materially changes the lid layer).

**MARK run-scoped (playhead-supplied, not author knobs):** `canonicalTick` in authored graphs
(top-level sharedParams always beat options-port keys via MergeNestedOptions).

**UNIFY split-brain resolution:** the 13 boundary/render `world.options` knobs are honored by the
view path but ignored by `FromExecutionPayload` (hardcodes defaults) — v1 must define ONE
resolution path both executors share.

## Full findings table

| # | Surface | Field/Param | Status | Recommendation |
|---|---------|-------------|--------|----------------|
| 1 | world.options | `continentalPatches` | ~~set-but-ignored, triple-broken~~ RESOLVED 2026-07-10 @4ad9827: wired end-to-end, null-unless-authored keeps default path on recipe init | keep in v1 schema (now live) |
| 2 | crust.generate | `rotationSource` nested vs flat keys | nested parsed-never-consumed; flat consumed-never-produced; neither declared | one canonical shape, delete the other |
| 3 | world.options | `spinRateRadiansPerMegaAnnum` (+`spinRate`) | misnamed-Ma + placebo (fallback only when roster yields 0 plates) | rename per-tick + plumb into roster, or drop |
| 4 | SPEC wire keys | `durationMegaAnnum, durationMa, orogenicPerMegaAnnum, arcVolcanismPerMegaAnnum, islandArcVolcanismPerMegaAnnum, ridgeVolcanismPerMegaAnnum, plates[].rate` | misnamed-Ma (full set) | keep only `*PerTick` forms |
| 5 | crust.generate node | catalog declares zero params; executor reads ~20 | undeclared-but-read | declare real surface; drop aliases |
| 6 | world.options render knobs | 13 boundary keys + `verticalExaggeration`, `hydrosphereMode` | honored in view path, ignored by FromExecutionPayload | unify resolution path |
| 7 | WorldCrustRunSpec | `DurationMegaAnnum` | dead + misnamed | drop |
| 8 | WorldCrustRunSpec | `VerticalExaggeration`, `Seed`(post-ctor), `RotationSource`, `PatchRecipe`; `HydrosphereMode` reader test-only | dead record fields | keep off run-spec schema |
| 9 | LayerCompositionDecision | `TerrainRelief` | computed-never-consumed (binder reads DerivedViewMode/SurfaceColoring/MountMantleInterior only) | out of wire schema until realized |
| 10 | Presentation doc | `ContinentalPlateIds` | dead (obsolete-marked, constant) | drop |
| 11 | Presentation doc | `CrustSnapshotTickState.Available` | doubly dead (producer never sets — RUNR omits snapshotTicks; consumer reads .Tick only) | drop or fix producer + build the promised cache strip |
| 12 | Timeline DTOs | `TimelineBand.Variant/.IsActive/.EndTick`, `TimelineTrack.IsActive`, `TimelineRulerMark.Tick` | dead (face recomputes/ignores; VariantFor's boom-hud comment stale) | drop or consume |
| 13 | LayerFilmstripPreviewMap | `RequestedTick`, `SourceFrequency`, `SourceKind` | dead (set by all builders, read by none) | drop or mark diagnostic |
| 14 | world.body-formation | `retainedHeatJ`, `retainedVolatileMassKg`, `tick` alias | undeclared-but-read | declare |
| 15 | magma-ocean/stagnant-lid | `canonicalTick`/`tick`, `plateOnsetTick` | undeclared-but-read (lid crust-lerp end tick) | declare |
| 16 | default graphs | `canonicalTick` default 8000000 | always overridden by sharedParams at runtime | mark run-scoped |
| 17 | WorldCrustRunSpec | `DefaultDurationMegaAnnum=8.0` chain | drifted (100× off the catalog default) + unreachable | drop alias chain |
| 18 | layer-graph wiring | `layer` port into LayerSource/LayerNormalize (Required) | dead wire input (executors read scope params, not the wired object) | read the wire or make optional |
| 19 | timeline.select_layer | `sphereId`,`layerId` | parsed-but-undocumented | document; generate Descriptions from schema |
| 20 | world.generation_graph.run | `graph`,`sharedParams`,`executionScopeKey`,raw-doc fallback | parsed-but-undocumented | document; drop legacy fallback |
| 21 | RUNR request params | `sphereHandoff*`, `lifecycleKind`, `variant`, `branch`, `productCount`, `scheduleRevision` | set-but-never-read in this repo | verify WorldRuntime, then drop |
| 22 | world.layer-scope | `role` | verified live (round-trips into product metadata) | keep, document values |
| 23 | world.layer-source | `bodyId`,`datasetId`,`providerId`,`importFormat` | live but empty-string sentinels silently drop keys | model as optional/nullable |
| 24 | render options | `surfaceSubdivision`, `adaptiveSubdivision*` | verified live (view path) | keep, group as render section |
| 25 | Service.cs | `MobilePlateWindowTicks` "1 Gy (1M ticks/Ma)" comment | drifted-doc (canonical 100k ticks/Ma → window is 10 Gy-equivalent) | fix comment, express in rungs |

Evidence file:line for every row is in the audit transcript; key sites: WorldCrustRunSpec.cs
(:40-354 parse surface), WorldGenerationNodeCatalog.cs (:37-212 declarations),
WorldFunctionProvider.cs (:116-448 executors), WorldGenerationGraphCompiler.cs (:192-206
kind-hint gap), WorldGenerationGraphRunner.cs (:201-230 ToGenerationRequest),
GlobeViewMode.cs (:206-207), PresentationLayers.cs (:227, :278-280), TimelineDtos.cs (:32-41),
LayerFilmstripPreview.cs (:18-24), WorldPlugin.cs (:248-257 sharedParams injection).
