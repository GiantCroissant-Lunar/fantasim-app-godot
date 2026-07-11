# D4.2 world-scope unit sweep — implementation plan (2026-07-11)

> **For the implementing agent:** execute task-by-task. NO git write operations; NO windowed
> gate — the lead reviews by artifacts, commits (Tasks 1–3 and Task 4 SEPARATELY), and gates.

**Spec grounding:** D4.2 directive (2026-07-07 directives doc) — canonical ticks + odometer
vocabulary everywhere in World scope; Ma/Ga only at import bridges. Audit:
[`../specs/2026-07-10-parameter-surface-audit.md`](../specs/2026-07-10-parameter-surface-audit.md)
findings #4, #7, #17, #25. Review 2026-07-10: ~77 Ma/Ga leaks in 10 files (WorldCrustRunSpec ≈47,
incl. wire keys) + tick constants authored under a WRONG 1M-ticks/Ma assumption (canonical is
**100k ticks/Ma**): `MobilePlateWindowTicks` ("1 Gy" comment, value = 10 Gy-equivalent) and
`PlateFeatureFadeInTicks` ("5 Ma" comment, value = 50 Ma-equivalent). User directive:
**re-derive values from stated intent, don't relabel comments to bless the drift.**

## Global constraints (hard)

- Edits ONLY under `project/plugins/App.World/`, `project/plugins/App.World.Composition/`,
  `project/contracts/App.World/`, and `project/tests/App.World.Tests/` +
  `project/tests/App.World.Composition.Tests/`. Nothing else.
- **EXEMPT — do NOT touch:** `spinRateRadiansPerMegaAnnum` and OnsetRoster's
  `DefaultAngularDriftPerMegaAnnum`/rad-Ma authoring vocabulary (user decision 2026-07-11:
  rad/Ma IS the authoring unit there, one declared conversion inside OnsetRoster.Build);
  `UnitConverter` itself (it IS the bridge); anything under `Import`/importer namespaces
  (import bridges legitimately speak Ma/Ga); `tools/rates/` docs.
- Suite baseline 1116 green BEFORE Task 1 (verify; if red STOP). Full build + full suite after
  EVERY task. Prefix every shell command with
  `cd /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot && `.
- Anything that doesn't fit a classification rule below goes on the FLAGGED list in
  AGENT-SUMMARY-d42.md — unchanged. Never guess.

### Task 0 — enumerate

`grep -rn --include="*.cs" -iE "megaannum|\bMa\b|\bGa\b|\bGy\b" project/plugins/App.World/ project/plugins/App.World.Composition/ project/contracts/App.World/ | grep -v obj`
(and the same over test projects). Classify EVERY hit into: (1) wire-key drop/rename,
(2) identifier rename, (3) comment re-derivation, (4) the two value re-derivations,
(5) EXEMPT, (6) FLAGGED. Record the full classified table in AGENT-SUMMARY-d42.md — this
table is a deliverable even where no edit results.

### Task 1 — wire-key cleanup (audit #4, #7, #17; mechanical)

Before deleting any parse alias, grep the SHIPPED config/graph jsons
(`project/hosts/complete-app/config/`, any `*.json` graph assets) and test fixtures for the
old keys; report usage in the summary. Then:
- Drop the misnamed-Ma parse aliases where the `*PerTick` form already exists and wins:
  `orogenicPerMegaAnnum`, `arcVolcanismPerMegaAnnum`, `islandArcVolcanismPerMegaAnnum`,
  `ridgeVolcanismPerMegaAnnum`; rename `plates[].rate` → `plates[].ratePerTick` (keep a
  parse-time hard ERROR on the old key naming the new one — silent ignore is forbidden).
- Drop the dead `DurationMegaAnnum` property + `durationMegaAnnum`/`durationMa`/`targetTick`/
  `ticks` alias chain + `DefaultDurationMegaAnnum=8.0` chain (audit: unreachable on the default
  path — `canonicalTick` always injected via sharedParams). TDD: tests first asserting the old
  keys now fail loudly and `*PerTick` forms parse; update existing fixture tests.

### Task 2 — identifier renames (mechanical, compile-checked)

Category (2) from Task 0: locals/properties/methods named `*MegaAnnum`/`*Ma` whose VALUE is
actually ticks (or which merely pass through tick values) get canonical names (`*Ticks`,
`*PerTick`). Do NOT rename anything whose value genuinely IS rad/Ma or Ma at an exempt bridge.
No behavior change — rename-only refactors, suite green.

### Task 3 — comment re-derivations (behavior-neutral)

Category (3): every comment stating a wall-clock equivalence recomputed at 100k ticks/Ma and
expressed in odometer rungs (ka/kb) with the Ma-equivalent in parentheses where helpful.
Includes audit #25's `MobilePlateWindowTicks` comment ONLY IF Task 4 is skipped for it —
otherwise Task 4 rewrites both value and comment together.

### Task 4 — the two value re-derivations (ISOLATED; lead commits separately)

Re-derive from stated intent at canonical 100k ticks/Ma:
- `MobilePlateWindowTicks` (Service.cs): stated intent "1 Gy" ⇒ 1000 Ma × 100,000 = **100,000,000
  ticks**. Current value is presumably 1,000,000,000 (verify; report actual).
- `PlateFeatureFadeInTicks`: stated intent "5 Ma" ⇒ **500,000 ticks** (verify current, report).
TDD: failing tests first that pin the re-derived values via UnitConverter math (not magic
numbers). Update every dependent test/fixture that hardcoded the old span. Rewrite both
comments in rung vocabulary. **Keep Task 4's diff isolated to the minimal files** — the lead
commits it separately and gates it with before/after screenshots (the run span visibly
rescales ~10×: MaxTick = onset + MobilePlateWindowTicks feeds the timeline ruler, snapshot
series, filmstrips). List every observable consequence you can identify in the summary.

### Task 5 — handoff

Final full build + suite; `AGENT-SUMMARY-d42.md` at repo root (NOT AGENT-SUMMARY.md — another
packet may be in flight): the Task-0 classified table, per-task file/test counts, FLAGGED list,
Task-4 consequence list, deviations with reasons. No commits.

## Lead acceptance gate (lead-run)

Tasks 1–3 commit: suite green; world-bundle hot-reload → `old ALC collected for bundle world`;
one seek + screenshot renders unchanged. Task 4 commit (separate): fresh boot; timeline ruler
span reflects the re-derived window; regime bands/filmstrips sane; before/after screenshots
vendored; the user's eye gets the final verdict on the rescaled run.
