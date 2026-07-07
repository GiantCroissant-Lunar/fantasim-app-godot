# D4.1 — Remove the Ma/MegaAnnum unit leak from world-generation function output

**Branch:** `wt/2026-07-07b-ma-leak`
**Directive:** D4.1 (`vault/specs/2026-07-07-layer-presentation-input-parity-canonical-units-directives.md`)
**Doctrine:** the app displays time in ODOMETER LADDER vocabulary (rung symbols `jy`/`jz`/`ka`/`kb`/..., anchor rung `ka` = 100_000 canonical ticks), NEVER real-world units (`Ma`/`Ga`).

## The leak that was fixed

`project/plugins/App.World/WorldFunctionProvider.cs` — the `crust.generate` summary `JsonObject` (the `Summarize` method) emitted three leaking fields in user/agent-facing command output:

```jsonc
// BEFORE (leaked physical units)
["durationMegaAnnum"] = 8.0,
["ticksPerMegaAnnum"] = 100000,
["timeScale"] = { "unit": "Ma", "tickScaleNumerator": 100000, "tickScaleDenominator": 1 }
```

## New canonical shape

```jsonc
// AFTER (canonical vocabulary only)
["ticks"]        = 800000,            // unchanged
["canonicalTick"]= 800000,            // unchanged
["durationTicks"]= 800000,            // duration as canonical ticks (long)
["durationLabel"]= "8 ka",            // odometer label, same formatter/profile as CanonicalTimeLabel
["timeScale"]    = { "rung": "ka", "ticksPerRung": 100000 }  // ladder anchor rung + scale
```

`durationLabel` is produced by `CanonicalTimeLabel.ForTick(durationTicks, UnitConverter.TicksPerMegaAnnum)` — the **same** `CanonicalDisplayFormatter` + `BaselineScaleProfiles.GeospherePlateTimeV1` profile used by `CanonicalTimeLabel` (Globe) and `TimelineTimeFormatter` (Timeline). Examples: `8 ka`, `1.25 ka`, `123.45 jz`.

The `timeScale.rung` value `"ka"` is **read from the profile API** (`BaselineScaleProfiles.GeospherePlateTime.AnchorScaleSymbol`), not hardcoded. The anchor rung `ka` is numerically 100_000 ticks == the legacy `Ma` anchor, so any consumer that divided by the old `ticksPerMegaAnnum` keeps the identical scale factor when dividing by `timeScale.ticksPerRung`.

No `"Ma"`, `"Ga"`, `"MegaAnnum"`, or `"annum"` substrings remain anywhere in the emitted JSON (keys or values) — asserted by the new guard test.

## Files changed

1. **`project/plugins/App.World/WorldFunctionProvider.cs`** — the fix:
   - `+using FantaSim.World.Contracts.Quantities;` (for `BaselineScaleProfiles`).
   - `GenerateCrustAsync` call site (`:334`): passes `spec.EndTick` (duration in ticks) instead of `spec.DurationMegaAnnum`.
   - `Summarize` signature (`:346`): `double durationMegaAnnum` → `long durationTicks`.
   - `JsonObject` body (`:412-421`): leaking keys replaced with `durationTicks` / `durationLabel` / canonical `timeScale`.
2. **`project/tests/App.World.Tests/WorldFunctionProviderTests.cs`** — output-consumer assertions updated + new guard test added.

## Consumers audited and outcome

Full-repo grep for `durationMegaAnnum | ticksPerMegaAnnum | tickScaleNumerator | tickScaleDenominator | timeScale | "Ma"` across `cs`, `py`, `md`, `json`, and all other file types.

### Output consumers (read the emitted JSON) — UPDATED

| File:line (new) | Was | Now |
|---|---|---|
| `WorldFunctionProviderTests.cs:63` | `result["durationMegaAnnum"]` == 8.0 | `result["durationTicks"]` == `MegaAnnumToTickDelta(8.0)` |
| `WorldFunctionProviderTests.cs:98` | `summary["durationMegaAnnum"]` == 1.25 | `summary["durationTicks"]` == `MegaAnnumToTickDelta(1.25)` |
| `WorldFunctionProviderTests.cs:99-102` | `summary["ticksPerMegaAnnum"]` | `summary["timeScale"].rung` == `"ka"` ∧ `.ticksPerRung` == `TicksPerMegaAnnum` |
| `WorldFunctionProviderTests.cs:115` | `result["durationMegaAnnum"]` == `TickDeltaToMegaAnnum(12345)` | `result["durationTicks"]` == `12345` |

These were the **only** output consumers in the repo. No Python tooling, no `.json` schema, no docs enumerate the output keys as a contract.

### New guard test (follows `CanonicalTimeLabelTests` pattern)

`WorldFunctionProviderTests.Crust_generate_result_emits_no_MegaAnnum_and_uses_canonical_fields` — serializes the whole `crust.generate` result JSON and asserts:
- `DoesNotContain("annum", json, OrdinalIgnoreCase)` — catches `durationMegaAnnum`/`ticksPerMegaAnnum`/`Megaannum`.
- `DoesNotContain("\"Ma\"", json, Ordinal)` — catches any `"unit":"Ma"` value.
- Positive shape: `durationTicks` present, `durationLabel` non-empty and Ma-free, `timeScale.rung == "ka"`, `timeScale.ticksPerRung == TicksPerMegaAnnum`.

(A bare case-sensitive `"Ma"` substring check on the whole JSON would be fragile — `productAddress` carries lowercase `main`, feature enum names like `Mountain` start `Mo` — so the guard targets the specific leak vocabulary instead.)

### INPUT-authoring vocabulary — INTENTIONALLY LEFT (D4.2 territory, not D4.1)

These read/write the **input** parameter `durationMegaAnnum` (an authoring bridge that converts physical Ma to canonical ticks at the boundary). They are not consumers of the emitted output JSON, so they are out of scope for the D4.1 output-leak fix. Per doctrine D4.2 they are a legitimate input bridge (`Ma/Ga permitted ONLY at import/export bridges`) and their rename is the separate vocabulary-sweep packet:

- `project/plugins/App.World/Crust/WorldCrustRunSpec.cs:317-318` — input parser (`durationMegaAnnum` → ticks).
- `project/plugins/App.World/Recipes/CrustGenerationGraph.cs:24,29,35` — recipe input param.
- `project/plugins/App.World/GenerationGraph/WorldGenerationNodeCatalog.cs:38` — catalog param `spinRateRadiansPerMegaAnnum`.
- `project/tests/App.World.Tests/WorldCrustRunSpecTests.cs:57` — input-payload test.
- `project/tests/App.World.Tests/WorldFunctionProviderTests.cs:94` — input-payload test (the provider still accepts `durationMegaAnnum` as input; only its output assertions changed).

The `UnitConverter.TicksPerMegaAnnum` identifier itself is the single sanctioned Ma↔tick authoring boundary (per the 2026-06-21 handover) and is intentionally retained as the numeric source for `timeScale.ticksPerRung`. Renaming that identifier engine-wide is D4.2.

### Vault docs — INTENTIONALLY LEFT (append-only)

`vault/specs/2026-07-07-...directives.md:110` (the doctrine describing the leak), `vault/handover/2026-06-20-*.md` (historical session records). Vault handovers are append-only and never edited for currency.

## Test results

```
task test   # dotnet build (0 warnings, 0 errors) → dotnet test --no-build
```

All projects green. `App.World.Tests` = **488/488 passed** (verified across 3 consecutive runs). `WorldFunctionProviderTests` = 7/7 (includes the new guard). The new guard test was also run by name and passes.

One transient single-test failure appeared in one intermediate `task test` invocation (487/488), but did not reproduce in the authoritative run or in 3 subsequent targeted re-runs — consistent with the known worktree path-resolution flake the task brief mentions (affects engine/carto suites in worktree checkouts; the lead re-verifies in the main checkout). It is unrelated to this change: the failing test was not in `WorldFunctionProviderTests`, and my change only touches the `crust.generate` output shape consumed solely by `WorldFunctionProviderTests` (all green).

## Verification of the new output (reasoned)

For the default 8 Ma run (800_000 ticks): `durationTicks = 800000`, `durationLabel = "8 ka"` (anchor amount 8.0 → largest rung with magnitude ≥ 1 is `ka`). For a 12_345-tick run: `durationLabel = "123.45 jz"`. Neither contains `Ma`. The anchor `ka` == 100_000 ticks == the legacy `Ma` anchor, preserving the consumer scale factor exactly.
