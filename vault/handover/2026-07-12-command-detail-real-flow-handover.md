# Handover — real domain flow: command pipeline emits its own A2UI detail card

**Date:** 2026-07-12 · **Branch:** `main` (still shared with the tunnel-timeline agent) ·
**Follows:** [`2026-07-12-a2ui-schema-handover.md`](2026-07-12-a2ui-schema-handover.md)

## TL;DR

Second open follow-up from the agent-UI pilot: replaced the **demo** `activity.emit_detail` command
with a **real, always-on producer**. Every command result now emits its own A2UI detail card, built
from real execution data, guaranteed to render (it dogfoods the schema shipped earlier today). All in
the `App.Command` + `App.Ui.Activity` lane — **no overlap with the tunnel agent's files.**

## What shipped (one commit, App.Command + App.Ui.Activity + tests + docs)

- **Pure builder:** `project/plugins/App.Command/Services/CommandActivityDetail.cs` —
  `BuildResultDetail(...)` composes a context line (descriptor description → title → command id), an
  actor + category badge row, a compact `corr …/cause …` lineage line, and — **only on failure** — an
  `Error` panel. Only catalog component types + documented variants; text truncated at 200. Produces
  plain A2UI JSON (no normalizer/renderer dependency).
- **Real producer wired:** `project/plugins/App.Command/Services/Service.cs` → `RecordCommandResult`
  now sets `DetailDocumentJson` from the real `CommandRequest`/`CommandDescriptor`/`CommandResult`.
- **Auto-expand refined:** `project/plugins/App.Ui.Activity/ActivityViewSource.cs` → `ShouldAutoExpand`
  expands a **failure** or an **explicit emission** (non-result kind); routine `CommandResult`/
  `DomainCommand` entries stay collapsed but expandable. (Old rule expanded any detail-doc entry, which
  would now wall the ledger.) The `activity.emit_detail` demo — kind `UiOperation` — still auto-expands.
- **Tests:** `App.Command.Tests/CommandActivityDetailTests.cs` (builder → real normalize→validate:
  success / failure+panel / null-descriptor fallback / truncation) + `App.Ui.Tests/ActivityViewSourceTests.cs`
  (failure expands, routine success collapses, explicit emission expands; added a minimal `FakeBus`).

## Verification

- **Unit:** App.Command.Tests **19/19**, App.Ui.Tests **104/104** (`dotnet test` scoped to each project —
  both graphs exclude the tunnel-blocked `App.Presentation.Tests`). Production code compiles (the test
  projects reference the changed plugins). The builder's card is validated through the *same*
  normalize→validate pipeline the runtime uses, so a producer can't emit a card the renderer rejects.
- **In-window: NOT run, deliberately.** A relaunch would compile the tunnel agent's uncommitted WIP
  into the app (the app-graph export builds the working tree). Deferred to a clean tree / explicit OK.
  `App.Command` is resident (needs rebuild + relaunch); `App.Ui.Activity` is a collectible bundle
  (hot-reload) — so the card *content* is resident, the *rendering* hot-reloads.

## Coordination state (unchanged)

- Shared `main` tree still holds the tunnel agent's **6 uncommitted files** — untouched; I staged only
  `App.Command` / `App.Ui.Activity` / their tests / vault docs.
- Branch is ahead of origin/main by 28+. Unpushed backlog remains a `push & sync` candidate.
- Full-solution `task build` still blocked by the tunnel agent's in-flight red TDD test.

## Open / next (two pilot items now done)

- ~~A2UI JSON Schema~~ ✅ · ~~Real domain flow~~ ✅ (this).
- **In-window visual pass** for the command-result cards (gated on a clean relaunch) — the one honest
  gap. Look: routine results collapsed, failures auto-expanded with the red `Error` panel.
- **Vocabulary extensions** — nested `list` items, images/icons, richer `panel` headers (needs boom-hud
  + Godot-renderer changes; the schema's drift guard will force the schema to track any catalog change).
- **Undo/redo track** — separate approved spec; doubt-driven (truth-stream invariants); spec-before-code.
- Owed to the user (still): eye-judgment sittings — D8b scrub feel, 2 kb-MaxTick run, tunnel first look.
