# Handover — A2UI payload JSON Schema (agent validation contract)

**Date:** 2026-07-12 · **Branch:** `main` (still shared with the tunnel-timeline agent) ·
**Follows:** [`2026-07-12-activity-agent-ui-pilot-handover.md`](2026-07-12-activity-agent-ui-pilot-handover.md)

## TL;DR

Delivered the first open follow-up from the agent-UI pilot: **published the constrained A2UI vocabulary
as a real JSON Schema** so an agent can validate a payload before emitting it. Faithful to the live
`RuntimeSurfaceCatalog` (schema-valid ⟹ normalizes **and** renders), drift-guarded by a test that
couples the schema to the catalog, and independently confirmed by a real draft-2020-12 engine.
Fully in the `App.Ui` lane — **zero overlap with the tunnel agent's files.**

## What shipped (one commit, App.Ui-only paths)

- **Schema:** `project/contracts/App.Ui/Presentation/Schemas/a2ui-surface.schema.json` — draft 2020-12,
  `$id: https://schemas.fantasim.local/ui/a2ui-surface.schema.json`. Encodes both contract layers from
  `RuntimeSurfaceCatalog.Basic`: `type` = hard enum of catalog types **minus `nodeGraph`** (unreachable
  `wires`); per-type property allow-list = `Properties ∪ BindableProperties` via `if/then` +
  `unevaluatedProperties:false`; per-type action events (`button`→`pressed`, `list`→`selected`);
  **`variant` = free string** (theme-resolved, not validator-enforced — a hard enum would over-reject).
- **Accessor:** `A2uiSurfaceSchema.Json` (embedded resource in `FantaSim.App.Ui.Contracts`) — a host can
  hand the schema to an agent at runtime.
- **Drift guard:** `project/tests/App.Ui.Tests/A2uiSurfaceSchemaTests.cs` — no JSON-Schema engine ships
  offline, so the tests couple the schema's declared vocabulary *directly to the catalog* (type enum,
  per-type props, per-type events) and run every schema `example` through the real normalize→validate
  pipeline. **102/102 `App.Ui.Tests` green** (78 prior + new).
- **Spec:** `vault/specs/2026-07-12-activity-surface-agent-ui-pilot-design.md` → new "Delivered" section
  (also answers the spec's open-question #1: the normalizer *is* the T4P adapter; agents keep the flat form).

## Verification

- **Unit:** 102/102 `App.Ui.Tests` (`dotnet test` scoped to that project — its graph excludes the
  tunnel-blocked `App.Presentation.Tests`, so it builds/runs independently).
- **Author-side semantic check** (real `jsonschema` 4.25.1, draft 2020-12; script in this session's
  scratchpad, *not* CI): meta-schema valid; both examples + 5 valid edge cases accepted; 12 should-fail
  payloads all rejected — incl. the subtle ones (`text` on a container, `enabled` on a spacer where the
  catalog bindable set is only `{visible}`, wrong event on a button, extra key in an action).
- **Not run:** the exported windowed app (no runtime surface changed — pure contract + test + schema; the
  app was already down, and relaunch would replace the tunnel agent's instance).

## Coordination state (unchanged from prior handover — still true)

- **Shared `main` tree** still holds the tunnel agent's 6 uncommitted files (`Host.cs`,
  `PlanetPresentationReloadGate.cs`, `App.Timeline/TimelinePlugin.cs`, `App.Resource/.../IService.cs`,
  two tunnel/timeline test files). **Untouched — I staged only App.Ui paths.**
- **Branch is ahead of origin/main by 26+** (both agents' work). Unpushed backlog remains a `push & sync`
  candidate whenever the user wants it.
- Full-solution `task build` is still blocked by the tunnel agent's in-flight red TDD test; the
  app-graph export path (`unify-build BuildGodotDesktop`) is unaffected.

## Open / next (same list, one item now done)

- ~~A2UI JSON Schema~~ ✅ **this session.**
- **Vocabulary extensions** — nested `list` items, images/icons, richer `panel` headers (may need
  boom-hud additions; would extend both the catalog and this schema in lockstep — the drift guard will
  enforce that).
- **Real domain flow** — wire an actual pipeline (e.g. world-gen) to emit its own A2UI detail cards
  instead of the demo command. Wants an app relaunch to verify (tunnel-agent coordination).
- **Undo/redo track** — separate approved spec; doubt-driven (truth-stream invariants); spec-before-code.
- Owed to the user (still): the eye-judgment sittings — D8b scrub feel, the 2 kb-MaxTick run, tunnel
  first look — need your eyes + the running app.
