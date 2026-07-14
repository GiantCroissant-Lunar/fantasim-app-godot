---
source: fantasim-app-godot/project/contracts/App.World/Composition/{Fields,FieldValues}.cs,
  plugins/App.World.Composition/{FieldComposer,FieldValueResolver,GeosphereFieldCatalog,
  AtmosphereFieldCatalog,GeospherePlateLayer,SyntheticCrustLayer}.cs,
  plugins/App.World/{WorldFunctionProvider.cs,Crust/WorldCrustMaterializer.cs,
  GenerationGraph/WorldGenerationRegimeFieldSampler.cs},
  plugins/App.World/Services/WorldHistoryCoordinator.cs, plugins/App.World.FieldView/Services/
  FieldViewService.cs, plugins/App.Ecs/{Systems/ReduceFieldsSystem.cs,Fields/FieldComponents.cs}
  + App.Ecs.csproj/App.Ecs.Tests.csproj + fantasim-world/vault/architecture/fields-concept.md
  (authored 2026-07-14)
source-status: current-code documentation
distilled: 2026-07-14
divergence: >
  Documents the App.World.Composition layer-stack composer as the primary subject (per task); the
  engine's separate multi-contributor reducer system (World.Fields.Core) and its app-side ECS
  realization (App.Ecs.ReduceFieldsSystem) are covered only in "Not built / open" because — despite
  sharing vocabulary ("field", "resolve") with the composer — they are a DIFFERENT mechanism (see
  below) that is not the live rendering path. Does not restate node-graph or timeline paradigms.
---

# Field composition runtime (FieldComposer)

**Doctrine:** [`planet-stack-model.md`](../../../fantasim-hub/vault/architecture/planet-stack-model.md)
§4 (Layers + Fields — the composition stack). This doc is the app-side **Built** account of that
doctrine's produce/consume DAG, opinion-strength, and loud-error rules — read §4 first; this doc
does not redefine them.

## Doctrine (what must hold)

- A Layer declares what it **produces** and **consumes**; composition resolves a producer→consumer
  DAG (§4).
- **Unresolved reference** — a consumed Field with no producer is a compose-time error, surfaced by
  id, never a silent zero. A Field may declare a fallback.
- **Opinion strength** — if two Layers produce the same Field, the one higher in the stack (added
  later) wins; non-destructive override.
- **Stack order ≠ execution order** — stack order decides the winner; execution order is the
  topological producer→consumer order the scheduler runs.

## Built (code-cited)

### Core contract types

`contracts/App.World/Composition/Fields.cs`: `FieldId`/`LayerId` (readonly record structs over a
string), `FieldDescriptor(FieldId, FieldDomain, FieldValueKind)` (schema: Cell/Globe/Feature domain
× Scalar/Vector/Categorical/Mask kind), `FieldConsumption(FieldId, Required = true, Default =
null)`, `LayerFieldBinding(LayerId, Produces, Consumes)` (a layer's declared contract),
`FieldCompositionResult(ExecutionOrder, WinningProducers, UnsatisfiedOptionalFields, Errors)` with
`IsValid => Errors.Count == 0`, and `FieldCompositionErrorKind` (`UnknownField`,
`UnresolvedRequiredField`, `Cycle`, `DuplicateLayer`).

### `FieldComposer` — the DAG resolver (`App.World.Composition/FieldComposer.cs`)

Pure, side-effect-free class: `DeclareField(FieldDescriptor)` registers schema; `AddLayer
(LayerFieldBinding)` appends to an ordered stack; `Compose()` returns a `FieldCompositionResult`
and never throws for declaration problems — every error is collected and returned, not raised.
`Compose()`'s steps, in order:

1. **Duplicate-layer guard** — the same `LayerId` twice → `DuplicateLayer` error.
2. **Unknown-field guard** — any produced/consumed `FieldId` with no `DeclareField`'d descriptor →
   `UnknownField` error.
3. **Opinion-strength resolution** — for each field, collect every layer index that produces it;
   the **winner is the highest stack index** (`producerList[^1]`), recorded in `WinningProducers`
   keyed by `FieldId → LayerId` (and internally by index, so a duplicate `LayerId` can't create an
   ambiguous re-resolution).
4. **Unresolved/fallback check** — a `Required` consumption with no winning producer →
   `UnresolvedRequiredField`; an optional consumption with no producer and no `Default` → added to
   `UnsatisfiedOptionalFields` (not an error); an optional consumption with a `Default` is silently
   satisfied by the fallback.
5. **Dependency graph** — one edge `producer → consumer` per field whose winning producer differs
   from the consumer (self-production/self-consumption creates no edge); multiple shared fields
   between the same pair collapse to one edge.
6. **Topological sort** — Kahn's algorithm over the edge set, using a `SortedSet<int>` frontier so
   ties break by **ascending stack index** — the execution order is deterministic for a given
   stack, independent of dictionary iteration order.
7. **Cycle detection** — if the sort doesn't consume every layer, one `Cycle` error is added.

### `FieldValueResolver` — the executor (`App.World.Composition/FieldValueResolver.cs`)

`Resolve(FieldCompositionResult, layers, geometry, tick, sphereHandoff?)` throws immediately if
`composition.IsValid` is false (never resolves an invalid graph). For a valid composition:

- **Default materialization** — every optional consumption with no winning producer but a declared
  `Default` is broadcast as a uniform per-cell array before any producer runs.
- **Execution** — walks `composition.ExecutionOrder`; for each `IFieldProducer` layer, computes
  which of its declared `Produces` it actually **owns** (won), builds an `allowedReads` set (its
  declared `Consumes` plus its own owned outputs), then calls `Produce(context)`.
- **Loud invariants**, enforced by the internal `ComputeContext` (`IFieldHandoffComputeContext`):
  a read of a field not in `allowedReads` throws (`"Producer read undeclared field"`); a read of a
  declared-but-not-yet-produced field throws (`"...was read before it was produced"`); a write to a
  field not in the producer's declared `Produces` throws (`"...wrote undeclared field"`); a
  per-cell array whose length ≠ `CellCount` throws; after `Produce` returns, every field the
  producer **owns** must have been written or the resolver throws
  (`"...did not write its declared field"`).
- **Winner-only storage** — a producer's write to a field it declares but does not win is silently
  ignored (`_ownedWrites.Contains(field)` gate in `SetScalar`), so the winning value is authoritative
  regardless of run order.

### Field catalogs (schema-driven registration)

`GeosphereFieldCatalog` and `AtmosphereFieldCatalog` (same plugin) are static classes: a
`FieldId`/`FieldDescriptor` pair per field, an `All` list, and `DeclareInto(FieldComposer)` that
registers every descriptor. Geosphere: `PlateBoundaryDistance`, `Elevation`, `CrustThickness`,
`SurfaceTemperature`, `MeltFraction`, `HeatFlow` (all `Cell`/`Scalar`). Atmosphere:
`AtmosphereGreenhouse`, `AtmosphereHydration`, `AtmospherePressure`, `AtmosphereSurfaceTemp`. A
code comment on `GeosphereFieldCatalog` notes these are **app-local literal strings**: the
engine-side `GeosphereFieldIds` constants it should eventually promote to
(`FantaSim.World.Contracts.Fields`) do not exist in the current `fantasim-world` yet.

### Producer layers and the one real DAG edge in production

`IFieldProducer : ILayer { void Produce(IFieldComputeContext); }`. Shipped producers:
`GeosphereMagmaOceanLayer`, `GeosphereStagnantLidLayer`, `GeospherePlateLayer`,
`SyntheticCrustLayer`, `AtmosphereBulkLayer`, `AtmosphereCoupledLayer`. Only **one** production
`Consumes` declaration exists in the whole app: `SyntheticCrustLayer` consumes
`GeosphereFieldCatalog.PlateBoundaryDistance` with `Required: true`
(`SyntheticCrustLayer.Fields`) — produced by `GeospherePlateLayer`. Every other shipped layer
consumes nothing (`Array.Empty<FieldConsumption>()`), so every other production composition today
is a single-layer, edge-free "stack."

### Production wiring — where `FieldComposer`/`FieldValueResolver` actually run

- **`WorldCrustMaterializer.BuildCrustThickness`** (`App.World/Crust/WorldCrustMaterializer.cs`)
  — "the source-of-truth implementation for the cutaway path": composes `GeospherePlateLayer` then
  `SyntheticCrustLayer` (the one real producer→consumer edge above), resolves, and reads
  `GeosphereFieldCatalog.CrustThickness`. Feeds `PlanetPresentationDocument.CellCrustThickness`
  (`contracts/App.World/PresentationLayers.cs`), which reaches the cutaway/mantle-x-ray
  rendering in `App.Presentation/PlanetPresentationBinder.cs`.
- **`WorldFunctionProvider.ResolveRegimeLayerFields`** (`App.World/WorldFunctionProvider.cs`)
  — composes a single `GeosphereMagmaOceanLayer` or `GeosphereStagnantLidLayer`, backing the
  `geosphere.magma-ocean.generate` / `geosphere.stagnant-lid.generate` node functions
  (world-generation-graph). The doc comment states the intent explicitly: the graph-generated
  layer product must equal the composition-generated one ("P4b parity"). Output is serialized to
  JSON (`fields[scalar.Field.Value] = [...]`) under a `WorldGenerationProductAddress`, which
  `Service.GetGenerationProductsAsync()` surfaces and `Service.ToPlanetLayer` turns into a
  `PlanetPresentationLayer` for rendering.
- **`WorldGenerationRegimeFieldSampler.ResolveMagmaOceanSurfaceTemperatureK`**
  (`App.World/GenerationGraph/WorldGenerationRegimeFieldSampler.cs`) — the doc comment calls this
  "the bridge from graph products into the existing layer field runtime": composes+resolves a
  magma-ocean layer and reduces to one plain `double` (cell average) so T4 render seams never need
  to understand field-composition internals.

## Not built / open

- **A same-vocabulary, different-mechanism field system also exists and is only partially wired.**
  The engine's `fields-concept.md` (`fantasim-world/vault/architecture/`) documents `World.Fields` /
  `World.Fields.Core` — a **multi-contributor reducer** system (`FieldContribution` +
  `IFieldReducer` + `CompositeFieldCatalog` + `FieldReducerRegistry`, e.g. `weighted-average`,
  `exclusive-writer`), conceptually unrelated to the single-winner `FieldComposer` this doc
  documents. `WorldHistoryCoordinator` (`App.World/Services/WorldHistoryCoordinator.cs`)
  constructs and **validates** that catalog+registry at startup (`CatalogValidator.Validate`,
  fail-fast), but its read path is a stub: `GetScalarFieldValues` returns `0f` for every field
  with a code comment, *"No ECS contributions yet; Task 7 feeds real reduced values"*
  (`WorldHistoryCoordinator.GetScalarFieldValues`). `App.World.FieldView/Services/FieldViewService.cs` is a
  real, live T3 UI projection (`ObservableCollection<FieldValueViewModel>`, R3-driven) bound to
  exactly this stubbed path via `IService.GetFieldValuesAsync`/`GetScalarFieldValuesAsync` — so
  today it projects placeholder values, not composed field truth.
- **The engine-fields-concept's ECS reduce system is built and tested but not registered into any
  live tick loop.** `App.Ecs/Systems/ReduceFieldsSystem.cs` + `App.Ecs/Fields/FieldComponents.cs`
  (`FieldSubject`, `FieldContributionBuffer`, `ResolvedFields`) implement exactly the
  `ReduceFieldsSystem = FieldValueResolver` mapping the engine doc describes, including the
  canonical `(Tick, ProducerId)` sort for determinism. `App.Ecs.Tests/ReduceFieldsSystemTests.cs`
  is an explicit **"Determinism + cross-path proof"** (shuffled-order determinism, canonical-sort
  necessity, and direct-reduce-equals-truth-stream-materialize-then-reduce). But both the system
  and its test only compile under `-p:UseProjectReferences=true`
  (`App.Ecs.csproj`, `App.Ecs.Tests.csproj` — the dev-mode project-reference toggle);
  grepping the whole app for `ReduceFieldsSystem` finds no registration into `EcsWorldActor`'s
  `ArchSystemRunner` — it is compiled and correctness-proven, but dormant.
- **Opinion-strength override and `Default` fallback are algorithm-complete but production-unused.**
  No shipped layer stack has two layers producing the same field, and no `FieldConsumption` in
  production code sets `Required: false` with a non-null `Default`. Both are exercised only in
  `FieldComposer`'s own logic and would need a first real user before their behavior is proven
  outside the algorithm itself.
- **No dedicated `FieldComposer`/`FieldValueResolver` unit-test file exists.** The DAG/opinion-
  strength/topological-sort/cycle-detection logic is exercised only incidentally, through
  `App.World.Composition.Tests/FieldHandoffContextTests.cs` (a single-layer magma-ocean handoff
  test) and through the production call sites above — there is no `FieldComposerTests.cs` pinning
  the composer's own guarantees (duplicate-layer, unknown-field, cycle, multi-producer override) in
  isolation.
- **"`ResolvedFieldComponents`" (as named in the originating task) is not a type in
  `App.World.Composition`.** The closest real name is `ResolvedFields`/`FieldValue`, and it lives
  in the dormant `App.Ecs` reducer path above, not in the live `FieldComposer`/`FieldValueResolver`
  path this doc documents — a likely source of future naming confusion between the two systems.
