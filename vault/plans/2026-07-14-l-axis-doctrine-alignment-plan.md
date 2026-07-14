# L-Axis Doctrine Alignment (L0 → L2) Implementation Plan

> **For agentic workers:** Implement task-by-task, TDD where a behavior changes; this is mostly
> a convention migration, so the load-bearing steps are the golden-repin discipline and the
> full-suite gates. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Align every world-scoped truth/control stream identity to the locked L doctrine
(`fantasim-world/vault/architecture/planet-stack-model.md`: L3 stellar, L2 world, L1 regional,
L0 local, negative micro): the shipped `L0` plates/imports/app streams become **L2**.

**Decision authority:** user, 2026-07-14 ("while we are still in the early development phase,
align to doc"). No durable production history holds L0 identities — the runtime truth backend
is in-memory and gate evidence is disposable — so this is a rename, not a data migration.

**Scope:** fantasim-app-godot (all convention sites) + the canonical spec already amended
(§7.5). The engine is convention-agnostic: its only non-test identity construction is
deserialization; engine TEST fixtures stay untouched (L is arbitrary data there) EXCEPT any
test that asserts the canonical convention string itself.

## Global Constraints

- Do NOT commit/stage/push; lead reviews and commits.
- Golden-vector policy: vectors that lock encodings over ARBITRARY fixtures stay untouched. A
  vector may be re-pinned ONLY when its fixture deliberately models the canonical convention
  and the L change flows into the bytes; every re-pin is listed in AGENT-SUMMARY.md with
  before/after values. Silent re-pins are a failure.
- The imported-authority digest (SHA-256 of the length-framed bound-cursor encoding) embeds the
  stream identity, so imported digests change. That is expected; nothing durable holds the old
  digests. Crust cache identity strings change likewise (fail-soft caches invalidate).
- Both repos' full suites green at the end; the durable restart proof script must pass after
  the change (`tools/verify-durable-rotation-restart.sh`).

---

### Task 1: App production convention sites → L2

**Files (verified sites; then sweep):**
- `project/plugins/App.World/History/RotationImportCoordinator.cs:327-328` — plates + imports
  streams: `LLevel` 0 → 2.
- `project/plugins/App.World/Services/WorldHistoryCoordinator.cs:91` — `_streamId`
  (`app:main:L0:world:default`) → `LLevel: 2`.
- Find the rotation-source SELECTION stream construction (grep `TruthStreamIdentity(` under
  `project/plugins/App.World` — it appends `app.rotation-source-*` markers) and align it to 2.
- `project/plugins/App.World.Composition/TrackPipelineNodeCatalog.cs:70,119` — both
  `TrackStreamId DefaultStreamId` literals `"L0"` → `"L2"`.
- `project/plugins/App.World/WorldPlugin.cs` — one `L0` site (read it; align the identity or
  the string it derives).
- Sweep: `grep -rn "TruthStreamIdentity(" project/plugins project/contracts --include=*.cs`
  and `grep -rn '"L0' project/plugins project/contracts project/hosts --include=*.cs
  --include=*.json` — every remaining world-scoped site aligns to 2 / "L2"; anything genuinely
  local-scoped stays and is listed in AGENT-SUMMARY.md with justification (expected: none).

- [ ] **Step 1:** Apply the changes.
- [ ] **Step 2:** Build: `dotnet build project/plugins/App.World/App.World.csproj --nologo -v minimal` → 0 errors.

### Task 1b: Fix the variant/branch transposition + centralize identity minting (audit additions, 2026-07-14)

The 2026-07-14 stack-model validity audit found two more mechanical-drift items; they land in
this migration because they touch the same lines.

- **Transposition fix:** `TrackPipelineNodeCatalog.cs:70,119` populate
  `LayerTrackStreamId("main", "default", ...)` — variation and branch are TRANSPOSED (doctrine
  and every other site: variant="default", branch="main"; compare `WorldPlugin.cs:170` which
  uses ("app","main",...) ordering). Fix both defaults to `("default", "main", "L2", "world",
  "default")` and update the tests that assert the vocabulary
  (`LayerTrackRegistryDefaultAssetsTests`, `LayerTrackRegistryBuilderTests`).
- **Vocabulary centralization (the drift guard):** create
  `project/contracts/App.World/Composition/WorldStreamVocabulary.cs` — a static class that is
  the ONLY production minting point for five-axis identities. Factories (exact set, derived
  from the sites in Task 1): `Plates(world, branch)`, `ImportsControl(world, branch)`,
  `RotationSelection()`, `Generation()`, plus `TrackDefault()` returning the
  `LayerTrackStreamId` default. Each factory hard-codes the doctrine-correct LLevel (2 for all
  of these) with a doc comment citing `planet-stack-model.md` §2. Include argument validation:
  a variant id that parses as an IP address or is empty/whitespace throws (the audit found the
  HTTP ingress path leaks caller IPs into WorldId — this guard makes that loud at mint time
  until the ingress mapping is redesigned).
- Rewrite every Task-1 production site to call the vocabulary instead of constructing
  identities inline.
- **Architecture guard test** (new file
  `project/tests/App.World.Tests/StreamVocabularyGuardTests.cs`): source-level scan (same
  technique as `RotationSourceSeamTests`' source assertions) proving no production file under
  `project/plugins/App.World`/`App.World.Composition` contains `new TruthStreamIdentity(` or
  `new LayerTrackStreamId(` outside `WorldStreamVocabulary.cs` and codec DECODE paths
  (`RotationSourceSelectionCodec.cs`, serializer deserialization) — list allowed files
  explicitly in the test. Plus doctrine tests: every vocabulary factory's LLevel equals 2, and
  `RotationSelection().ToStreamKey()` etc. match pinned expected strings.

- [ ] **Step 1:** Write the guard + doctrine tests RED (vocabulary absent).
- [ ] **Step 2:** Implement vocabulary, rewrite sites, fix transposition, tests GREEN.

### Task 2: App tests and assets

- Update the 8 app-test `TruthStreamIdentity(..., 0, ...)` fixtures and every `"L0"` string
  assertion (`WorldHistoryBuildModeContractTests`, `LayerTrackDescriptorTests`,
  `LayerTrackRegistryDefaultAssetsTests`, `LayerTrackRegistryBuilderTests`,
  `RotationSourceSeamTests`, recovery/consistency tests) to the L2 convention. A test that
  asserts the DEFAULT stream vocabulary must assert "L2" now.
- Check `project/hosts/complete-app/config/*.json` and `project/bundles/*/manifest.json` for
  literal stream-id strings (`grep -rn '"L0' project/hosts project/bundles --include=*.json`,
  ignoring `*.deps.json`); align any hits.
- Re-pin any app-side golden that embeds a convention-modeling identity (expected: the
  selection-codec golden if one exists; list every re-pin).

- [ ] **Step 1:** Apply.
- [ ] **Step 2:** `dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --nologo -v minimal` → 640/640.
- [ ] **Step 3:** `dotnet test project/tests/App.World.Composition.Tests/App.World.Composition.Tests.csproj --nologo -v minimal` → 110/110.
- [ ] **Step 4:** `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --nologo -v minimal && dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --nologo -v minimal` → 253 + 339.

### Task 3: Durable gate re-run

- [ ] **Step 1:** `tools/verify-durable-rotation-restart.sh` → both phases pass (the restart
  proof now commits and rediscovers L2 streams end-to-end).

### Task 4: Summary

- [ ] AGENT-SUMMARY.md: every changed site, every re-pinned golden (before/after), the sweep
  output proving no `L0` convention site remains (`grep` results), test totals, confirmation
  nothing was committed.

## Out of scope

Engine repo code/test changes (convention-agnostic; the slice-2a branches stream is L2-native
via its own spec); renaming any COMMITTED evidence artifacts; the exported-app re-gate (lead
runs it after commit alongside the next windowed sitting).
