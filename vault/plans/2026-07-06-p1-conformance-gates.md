# P1 — mechanical architecture-conformance gates (detailed plan)

**Parent:** `2026-07-06-attempt8-recovery-roadmap.md` · **Station map:** `vault/architecture/planet-domain-station-map.md`
**Goal:** the station contracts fail the BUILD when violated, so "stick with the architecture"
stops being doctrine and becomes a compile/test fact — for every agent, every session.
**Scope:** one new test project + rules C1–C5 below. Pure .NET; no Godot; no behavior changes to
production code EXCEPT the single legalization fix in C3 (see below).

## New project

`project/tests/App.Architecture.Tests/App.Architecture.Tests.csproj`
- net8.0, xunit, same package versions as sibling test projects (copy App.World.Tests.csproj style).
- ProjectReference: `plugins/App.Presentation` (for assembly-level checks). Source scans need no references.
- Add to `project/FantaSim.sln` AND verify it runs under `task test` (Taskfile globs test projects —
  check `Taskfile.yml`'s test target; if it enumerates explicitly, add it).

## Rules (each = one test class, TDD: write the test, run RED where a live violation exists, fix or whitelist explicitly)

**C1 — Seam references no engine assemblies.**
`SeamAssemblyReferencesTests`: load `FantaSim.App.Presentation` assembly, assert
`GetReferencedAssemblies()` contains NO name starting with `FantaSim.Geosphere`,
`FantaSim.World.` (engine namespaces; NOT the app's `FantaSim.App.World` contracts).
Also assert no reference to `CrosscutFoundation.Config` (contract 2).

**C2 — Seam constructs no engine/domain-runtime types (source scan).**
`SeamSourceScanTests`: enumerate `project/plugins/App.Presentation/**/*.cs` (path anchored via
a `[repo-root]/project` probe — walk up from `AppContext.BaseDirectory` until `FantaSim.sln` is
found), assert NO match for the banned regexes:
`new\s+GlobeReconstructor`, `OnsetRoster\s*\.`, `WorldCrustRunSpec`, `WorldCrustMaterializer`,
`CrustInitRecipe`, `LidFractureAtOnset`. Failure message names file:line.

**C3 — Tick-addressed products only (source scan).**
`TickAddressedProductsTests`: in `project/plugins/App.Presentation/**/*.cs`, ban
`GetPlanetPresentationAsync\(\)` (parameterless). **Known live violation:**
`PlanetPresentationBinder.Rebind()` line ~135 calls the parameterless overload (this is exactly
the frozen-onset-frame entry point). Fix in this packet (the one production change):
`Rebind()` uses `world.GetPlanetPresentationAsync(_timeline.Tick)`. Run `task test` — the
existing App.Presentation.Tests must stay green; if any test pinned the parameterless call,
update it to the tick-addressed form.

**C4 — No new render-layer continent proxies (source scan).**
`ContinentProxyBanTests`: in `project/plugins/App.Presentation` and
`project/contracts/App.World.Rendering`, assert the only palette types are the whitelisted set
`{ HypsometricTint, CrustAccentMapper, PlateIdentityPalette, ContinentsPalette, WorldTerrainRamp, ProvinceTint }`
(scan for `class \w*Palette|class \w*Tint|class \w*Ramp` declarations vs whitelist), AND assert
`ProvinceTint` usage does not spread: it may appear only in its own file, its test, and the single
existing binder call site (count occurrences; document why: legacy lap-2 proxy, scheduled for
review in P3 — the whitelist SHRINKS, never grows, without a station-map amendment).

**C5 — Config reads stay out of the seam (source scan).**
`SeamConfigBanTests`: ban `CrosscutFoundation.Config` and `_config\.` in
`project/plugins/App.Presentation/**/*.cs` (host plumbs plain values; recipes ride graph payloads).

## Deliverables & verification

1. New test project with C1–C5, all green after the C3 fix.
2. The C3 production fix (one line + any test updates).
3. `task test` green end-to-end; report test counts.
4. Summary at `.agent/run/dispatch/p1-SUMMARY.md`: rules implemented, violations found, what was
   fixed vs whitelisted (whitelists must be EXPLICIT in code comments referencing the station map).

## Out of scope

Roslyn analyzers (source-scan tests are sufficient and dependency-free); iii/timeline/boom-hud
conformance (planet domain only); any look/behavior change beyond C3.
