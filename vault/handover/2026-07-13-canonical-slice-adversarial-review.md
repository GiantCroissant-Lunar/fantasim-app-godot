# Adversarial review — canonical world-history vertical slice (P9a/P9b)

**Date:** 2026-07-13 (lead-session review of the implementing agent's accepted slice)
**Scope:** app `2eae1fa..44102c5` (P9a: 789e380/96ff600/30c7fc9/694d8ac; P9b: 4cfd9ae/6611040/d99aa0f),
engine `9a49d3c..27ff851`.
**Method:** five independent fresh-context reviewers — engine truth/rotation, engine
tectonics/crust, app rotation authority, app crust/presentation, and an independent test/package
re-run — each verifying the spec
(`vault/specs/2026-07-13-canonical-world-history-and-dry-crust-design.md`) claim-by-claim
against the committed code.

## Verdict

**Accept stands.** The architecture landed as specified: the CAS prepare→plate→bound state
machine, bounded read-through-cursor with full hash re-verification, atomic two-state rotation
projection, single elevation authority, and the signed-morphology counterfactual gates are all
real and well-tested in their end state. No critical defect found. The review surfaces **three
fail-open defects worth fixing, one systematic test-falsifiability gap, one owed gate, and two
claims in the closure record that need correction.**

## Defects (ranked)

1. **MAJOR — unknown `rotationSourceKind` silently flips durable authority.**
   `App.World/Services/Service.cs:693-698` (`ReadRotationSourceFromParameters`): any kind not in
   `("imported","rot","gplates")` returns `RotationSourceRecipe.Default`; `RunGenerationAsync`
   then CAS-appends a durable `app.rotation-source-generated.v1` marker. A typo (`"gplate"`,
   `"import"`) silently deactivates a bound imported authority and the flip survives restart.
   The sibling `WorldCrustRunSpec.ReadRotationSourceRecipe` throws on unknown kinds — make this
   path do the same.

2. **MAJOR — engine materializer fails open on misaligned parent keyframes.**
   `fantasim-world Geosphere.Plate.Reconstruction/RotationModelMaterializer.cs:306-307`: when a
   parent-change plate has an authored keyframe at a time where an ancestor has no exact-time
   row, the ancestor's contribution is silently replaced by `Quaternion.Identity` instead of its
   interpolated rotation (GPlates semantics) or a rejection. Child keyed at 0/10 with parent
   keyed at 0/20 drops ~half the parent rotation at t=10 with no error and no covering test.
   Decide: interpolate the ancestor (parity) or fail closed — current behavior is neither.

3. **MINOR — numeric plate-id normalization is last-wins on collision.**
   `App.World/Crust/MaterializedRotationProvider.cs:45-57` + `TryNormalizePlateId`: authored ids
   `"1"` and `"001"` collide to integer 1; `_plateIdMap[numericId] = authoredId` keeps whichever
   enumerates last, silently dropping the other plate's motion. Found independently by two
   reviewers. Ambiguous mapping should reject at import.

## Systematic gap: co-axial-only test fixtures cannot falsify the algorithms they gate

Both flagship oracles use fixtures where every rotation shares the +Z axis:

- **Parent-change parity oracle** (`RotationModelParentChangeParityTests.cs:91`): for co-axial
  rotations, composition and SLERP commute, so the spec-forbidden interpolate-relatives-then-
  compose algorithm passes the entire oracle byte-identically. The spec's §7.2 mandate exists
  precisely because of non-commutativity, and no fixture exercises it. (The oracle was the lead
  session's authorship responsibility per §7.2, not the implementing agent's.)
- **Kinematics gate** (`MaterializedRotationProviderParityTests.cs:73-89`): single fixed-axis
  constant-rate plate — a swapped body-frame quaternion order and a central-vs-one-sided
  differencing regression both pass; stationary-outside-range is asserted only above the upper
  bound.

The implementations themselves were hand-verified correct (Hamilton convention checked against
`UnifyMaths.Generators/QuaternionTypesGenerator.cs:133-142`; world-frame delta at
`MaterializedRotationProvider.cs:122`; rad/canonical-tick at `:139`). The gates, not the code,
are the weakness. **Fix: one skew-axis parent-change fixture with independently derived
quaternion expectations, one axis-drifting kinematics fixture, and a below-lower-bound
stationary assert.** Also untested: the authored-discontinuity clause (crossover fixture is
continuous by construction) and the time-local cycle path (`Time_local_parent_cycle_is_rejected`
actually throws in `TopologicalPlateOrder`, not the cycle guard).

## Owed gate

Spec §8.1 requires "runtime logs prove a real imported append and materialization reached that
path before the exported visual gate is accepted," and P9a plan gate item 8 requires importing a
real fixture **in the exported app** with a durable backend, then rediscovering bound after a
PCK reload. What exists instead:

- The exported visual gate (`/tmp/fantasim-p9b-export-gate/runtime.log`) ran entirely under
  `RotationAuthorityDigest = generated:v1` — the imported path never executed in the exported app.
- The durable restart proof (`tools/verify-durable-rotation-restart.sh`) is genuine two-process
  SurrealDB/RocksDB recovery, but it drives the coordinator in a `dotnet test` host, stops the
  server with SIGINT (graceful, not crash), and its evidence dir was not retained
  (`keep_evidence` defaults to 0).

§8.4 of the design doc words around this instead of recording it as owed. **The exported-app
imported-rotation gate (import → diagnostics → PCK reload → bound rediscovery → ALC collection)
is still owed.**

## Corrections to the closure record

- "World: 629/629 in project-reference and package modes" — plausible but currently
  **unverifiable**: uncommitted workspace edits (another session's half-done removal of the
  `Cartography.Globe.Core` ProjectReference from `App.World.csproj` and
  `App.World.Rendering.csproj`) break the App.World.Rendering build
  (`GlobePlateSurfaces.cs`/`PlateSolidBuilder.cs` still `using` the removed namespaces).
  Engine **602/602 re-verified exactly**; package closure **0.1.12 verified** (27 nupkgs in
  `packages/nuget/`).
- "Concurrent queries cannot mix providers or cache identities" — true of the end state, but
  789e380 **alone** contained exactly the forbidden §7.4 mixing bug (per-phase re-resolve +
  generated fallback under an imported digest); 30c7fc9 fixed it in-packet. Cherry-pick hazard.
- Engine commit 447f2ba's "old callers behave identically" is false: legacy `RunAsync` features
  are now derived against per-tick reclassified topology instead of seed topology. That is the
  intended §7.3 fix, but it is a behavior change shipped under a "preserve" label, and the two
  intermediate commits each briefly broke the public surface (bisect hazard).
- b797710 bundles the trench-priority reorder (`CrustFeatures.cs:162-168`) into a truth-stream
  commit — spec-directed and test-locked, but hidden under a misleading message.

## Smaller findings (fix opportunistically)

- Control-envelope MessagePack encoding has no golden byte/digest vector — only round-trips
  (`RotationImportPayloadCodecTests.cs:114-131`); a `[Key(n)]` renumber would pass green while
  changing every future control-event hash. The three helper digests are properly vector-locked.
- No writer-before-store disposal **ordering** test (§8.3 names one); the code order is correct
  (`Service.cs:1542-1576`).
- SurrealDB-path *concurrent import* serialization is proven only against a probe store /
  in-memory; §8.1 says in-memory alone is insufficient.
- `RecordRotationSelection` blocks on durable I/O inside `_rotationStateGate`, stalling all
  rotation-dependent queries during a selection change (accepted-cost design; lands on render path).
- `ActorTruthEventWriter` fixed 5 s Ask timeout can report failure for a batch that lands
  (recovery converges on retry, but first result is a spurious error).
- Fresh-prepare accepts any pre-existing plate head as prior prefix
  (`RotationImportCoordinator.cs:107`) — only incidentally blocked by the non-decreasing-tick rule.
- Stale comments still describe pre-fix trench ridging (`PlateSurfaceReliefFabric.cs:49-50`,
  `PlateSurfaceReliefFabricTests.cs:143-144`) — regression bait.
- Dead public overload `BoundaryProfileContribution.Build(GeodesicSphereTessellation, …)` always
  yields all-zero profiles via `PlateId = -1` — the exact mechanism of the production bug this
  slice fixed; delete or fail loudly.
- Gap-filled transport cells get `CrustAgeTicks = delta` (maximally old) —
  inverted ridge bathymetry the moment hydrology is re-enabled (`PlateFrameSampler.cs:101-107`).
- `GlobePlateSurfaces.DefaultPeaks` (300 m) bypasses the 250 m residual cap on any future
  view/caller without a detail sampler; no test guards that path.
- No finalized-mesh build-twice determinism test (§8.2 clause); no unordered-dictionary
  dependence found by inspection.
- Spec-level: the 1/3 noise ratio anchors to the 800 m swell, but the net divergent-axis signal
  is 400 m and the ridge guarantee 300 m — 250 m noise can out-texture a ridge crest legitimately.
- `UseProjectReferences` still selects behavior in `App.Ecs` (compiles out
  `FieldComponents.cs`/`ReduceFieldsSystem.cs` in package mode); App.World itself is clean.
- Feature-derivation phase is uncancellable between ticks and recomputes `BoundaryCells.Compute`
  per snapshot (O(ticks × cells) redundant).
- Mis-ordered `(B,A)` provider pair keys yield a silently all-inactive world (documented, but a
  fail-silent seam in a fail-closed doctrine).
- `GeneratedEulerPoleRotationProvider.InstantaneousPoleAt` ignores pre-onset clamping — active
  boundaries reported on a frozen globe for ticks < onset (legacy-consistent).

## Outcome of the fix dispatch (2026-07-13 late, lead + opencode zai-coding-plan/glm-5.2)

The three fail-open defects and the oracle gaps were fixed the same evening; the exported-app
imported gate remains the only owed item.

- **Lead-owned oracles authored first** (independent pure-Python derivation, self-checked
  against the accepted co-axial values): engine
  `RotationModelSkewAxisParityTests.cs` (skew-axis parent change GREEN at authoring —
  production already implemented the mandated algorithm; two RED defect fixtures) and app
  `MaterializedRotationKinematicsOracleTests.cs` (drifting-axis pole, below-range clamp, SLERP
  quarter-point lock — GREEN at authoring). The RED pattern exposed a THIRD defect beyond the
  review: stable-chain plates interpolated relative links at query time and composed (the
  spec-forbidden algorithm), diverging ~2e-3 under a skew-axis moving parent.
- **Engine `e64fec3`** (GLM packet + lead compat fix): BuildModel resolves absolute samples for
  every plate at its authored keyframes in topological order; new
  `PlateCircuitNode.AbsoluteRotation` preferred by ReconstructOrientation; identity-fallback
  removed. GLM's first cut dropped `RelativeRotation` from materialized nodes, which the engine
  suite could not see but broke the app's finite-kinematics bounds walk (below-range stationary
  clamp lost, endpoint rates halved — caught by the app oracles in the integrated run, 638/640).
  Lead fix: authored relative samples stay on `RelativeRotation`. Gate: engine 24 projects
  0 failures (618 incl. 16 oracle), app 640/640 via project references. LESSON: engine packets
  that touch the reconstruction model MUST be gated with the app suite — the contract surface
  consumers walk (`RelativeRotation` as `FiniteRotationSamples`) is invisible to engine tests.
- **App branch `fix/p9a-review-followups`** (worktree `.agent/run/worktrees/fantasim-fix-p9a`,
  based on `c71c3e9`; commits `0f64d36` oracle + `e1f9a7e` fixes): unknown non-empty
  `rotationSourceKind` now throws before any durable append (absent/empty and explicit
  generated/gen/default still select generated); plate-id collision ("1" vs "001") throws at
  provider construction naming both ids. Tests prove a failed request leaves the prior imported
  authority active. Gate: 640/640 + 110/110. NOT on app main because main's working tree still
  fails to build under the unrelated uncommitted Cartography.Globe.Core csproj removals — merge
  once that migration is finished or reverted.
- **Still open:** exported-app imported-rotation gate (import → diagnostics → PCK reload → bound
  rediscovery → ALC collection); engine package closure republish (0.1.12 predates `e64fec3`,
  so package-mode consumers do not yet see the materializer fix); the remaining minor findings
  above.

## What was independently confirmed sound

CAS append path with no raw-store bypass; content-level (not count-level) orphan-batch
re-verification; full-preimage SHA-256 hash-chain verification with per-corruption tamper tests;
exactly-two-states projection constructor + onset-mismatch fail-closed + forced-interleaving
old-XOR-new concurrency tests; no reparse after commit (arch test + deleted provider);
length-framed injective selection encoding (no cache-key aliasing); GetFieldValues ALC test
(real collectible ALC, recursive type-graph walk, negative control, collection gate); all eight
P9b crust/presentation spec claims incl. ≥750 m/≥300 m counterfactuals through the finalized
mesh, trench-never-ridged, hole-free target-scans-source transport, Eulerian/Lagrangian frame
doctrine, thinner rings and independent zoom; d99aa0f strengthened (not weakened) assertions;
existing payload bytes/hashes unchanged; boundary coalescing matches §7.4 doctrine.
