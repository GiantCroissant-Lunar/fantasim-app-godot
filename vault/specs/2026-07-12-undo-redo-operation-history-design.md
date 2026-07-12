# Undo / redo — reversible operation history (concept lock)

**Status:** concept-lock DRAFT — **NOT ACCEPTED; revision required.** 2026-07-12. Scope pre-approved
(activity/agent-UI arc follow-up), but the fresh-context adversarial review below (doubt-driven gate)
found the two-axis *framing* sound and the *concretization* unbuildable against the real command
surface. **Do not write a plan or code against this draft** — see "Doubt-driven review outcome". The
body from here to that section is the reviewed draft, kept for the record with its flaws intact.

**Grounding (read):** [`planet-domain-station-map`](../architecture/planet-domain-station-map.md) (the
truth-stream constitution), `project/contracts/App.Command/CommandTypes.cs` (command records),
`project/plugins/App.Command/HostComposition/CommandComposition.cs` (registration),
`project/contracts/App.Activity/ActivityEntry.cs` (ledger causation model).

## Why now

The agent-UI pilot made the activity ledger the workspace's operation record. The user-approved next
step: let the user **undo/redo their operations** — UI state changes and reversible domain commands —
without disturbing the simulation, whose own navigation is the timeline scrub.

## The load-bearing decision: two independent axes

The single most important thing this design gets right — everything else follows from it:

```
   OPERATION HISTORY  (undo / redo)          SIMULATION TIME  (timeline scrub)
   discrete, reversible user operations      continuous, tick-addressed
   UI ops + input/param-changing commands    a deterministic FOLD over the S1 truth stream
   inverse per operation                     navigated by seeking a CanonicalTick
   in-memory, session-scoped                 already re-derivable; append-only truth
```

Per the constitution (S1): **truth is event-sourced and tick-addressed; simulation state is a
deterministic fold over the truth stream.** You do not "undo" a fold — you *navigate* it with the scrub.
Therefore:

- **Undo/redo NEVER reverses the truth stream or simulation state.** It reverses discrete operations
  that change *inputs* (generation parameters, recipes, layer selection, spin rate) or *UI state*
  (which views are shown, expanded, selected).
- **Simulation time stays on the timeline scrub** (the approved constraint). Seeking a tick is
  navigation, not a mutating operation — it is **not** on the undo stack (§ Timeline-scrub boundary).

This separation is what keeps undo safe: a reversible operation only changes a declared input that
**re-derives deterministically**, so restoring the prior input restores the prior observable state.

## Reversibility taxonomy (checkable)

Every command is exactly one of:

| Class | Meaning | Undo stack |
|---|---|---|
| **NonMutating** | Query/observe; no state change (`command.status`, `render.screenshot`, `activity.recent`). | never touches the stack |
| **Reversible** | Changes a declared input / UI state and can produce a faithful inverse (`world.setSpinRate`, `ui.selectLayer`, `view.show/hide`). | pushed as an undoable op |
| **Irreversible** | Appends truth, does external I/O, or otherwise has no faithful inverse (`world.commit`, `bundle.install`, anything that advances/writes the S1 stream). | inserts a **barrier** |

**Checkable invariant (enforced like a station contract):** a **Reversible** command MUST NOT write to
any S1 truth stream or perform external I/O — it may only set inputs/recipes/UI state that re-derive.
A command that cannot meet this is **Irreversible** by definition. *(This is the same shape as the P1
conformance gate: a source/dependency scan can assert reversible handlers don't reference the truth-write
seams.)*

## Mechanism: self-inverting reversible commands

`CommandDescriptor` has no reversibility today. Add it minimally and keep the forward handler shape.

1. **Descriptor gains a class:** `CommandReversibility Reversibility` (default `NonMutating` so existing
   commands are unaffected and never accidentally undoable). Purely declarative — the ingress/UI can show
   it; the mechanism is the registration below.
2. **Reversible registration produces an inverse at execution time** (capturing pre-state *before* the
   forward mutation):
   ```csharp
   // executes forward AND returns the request that will undo THIS execution
   commands.RegisterReversible(descriptor, execute);
   // execute: (payloadJson, ct) => Task<ReversibleOutcome>
   // ReversibleOutcome { string? ResultJson; CommandRequest Inverse; }
   ```
   Example: `world.setSpinRate {to: 0.02}` reads the current rate (`0.005`), applies `0.02`, and returns
   `Inverse = world.setSpinRate {to: 0.005}`. The inverse is an ordinary command — dispatching it undoes
   the op **and itself yields its own inverse**, which is the *redo*. Undo/redo is this symmetry; there
   is no separate "un-apply" path to keep correct.
3. **Irreversible / NonMutating** commands keep the existing `Register(descriptor, handler)`.

*(PROPOSED — review)* Capturing the inverse as a full `CommandRequest` (not a closure) makes it
serializable and auditable, and routes undo through the **same dispatcher** as any command — no parallel
mutation path.

## The undo/redo model

- **Authority *(PROPOSED — review, open-question #4)*: an `App.Command`-owned undo/redo stack is the
  authority; the ledger *reflects* it.** Undo/redo is a live interaction cursor with well-understood stack
  semantics (push on op, pop on undo, clear-redo-on-new-op). The **ledger** (append-only audit) is the
  wrong structure to *be* that cursor — but its `CausationId`/`CorrelationId` chains are the **linkage
  substrate**: each stack op references the ledger `EntryId` of the operation it inverts, so audit and
  undo stay correlated. (The alternative — ledger-as-authority, deriving the cursor by folding the audit
  log — is the fork to kill in review.)
- **Semantics:**
  - Reversible op executes → push its inverse on **undo**; **clear redo**.
  - **Undo** → pop **undo** top, dispatch the inverse (performs the reverse, yields *its* inverse), push
    that on **redo**. Recorded in the ledger with `CausationId` = the undone op's entry.
  - **Redo** → pop **redo** top, dispatch, push its inverse back on **undo**.
  - Irreversible op → clear **redo**, push a **barrier** sentinel on **undo**; undo refuses to cross it
    with a clear message ("can't undo past *world.commit*"). Entries below the barrier stay for audit.
- **Compound operations** (the ledger already chains these via `CausationId`): a compound's inverse is the
  **reverse-ordered composite** of its children's inverses, applied atomically — undo of a compound undoes
  the whole chain or nothing. The causation chain is what lets undo find the children.
- **New surface:** `commands.undo` / `commands.redo` commands (dispatchable via the ingress), plus an
  `IUndoService` exposing `CanUndo/CanRedo` + top-of-stack labels for a future UI affordance.

## Truth-stream invariants this design must not violate (checkable)

1. A **Reversible** handler writes no S1 truth stream and does no external I/O (taxonomy contract above).
2. Simulation time (`CanonicalTick`) is navigated **only** by the timeline scrub; it is never on the
   undo stack.
3. Undo/redo dispatches go through the **same** main-thread, serialized command dispatcher
   (`ImmediateMainThreadDispatcher`) as forward commands — no re-entrant/parallel mutation.
4. A Reversible op's inverse **fully restores observable state** because it re-applies a declared input a
   deterministic fold consumes (§ two axes). If restoration isn't total, the command is Irreversible.
5. The undo stack holds only Reversible ops; Irreversible ops insert a barrier; NonMutating never touch it.
6. Undo/redo is **session-scoped in-memory**; the persisted ledger (LiteDB) is **audit-only** and is
   never replayed as a cross-session undo.

## Timeline-scrub boundary

Seeking a tick is continuous navigation (like scrolling), not a discrete mutating operation. **Scrub is
excluded from the undo stack** *(PROPOSED — review)*. Rationale: it honors "simulation stays on the
timeline scrub," avoids polluting the op history with every drag frame, and the scrub already offers its
own back-navigation (seek to the prior tick). If users later want "jump back to where I was scrubbing," a
*named bookmark* is the right tool, not undo.

## Session scope & persistence

In-memory, cleared on restart. Cross-session undo (replaying the persisted ledger backwards) is a much
harder, invariant-dangerous problem (the world/inputs at restart may differ) and is a **non-goal**.

## Phasing (slices)

- **Slice 1 — UI-op undo (lowest risk, no truth contact).** `IUndoService` + `commands.undo/redo` +
  make 2–3 pure UI ops reversible (`view.show/hide`, `ui.selectLayer`, activity `toggle`). Proves the
  self-inverting mechanism and the stack with zero truth-stream risk. TDD; unit-testable headless.
- **Slice 2 — reversible domain input commands.** One real input command made reversible end-to-end
  (candidate: `world.setSpinRate` — a single property, already shipped; a clean first inverse). Adds the
  taxonomy conformance check (reversible handler references no truth-write seam).
- **Slice 3 — barriers + compound.** Irreversible barrier behavior; compound-op inverse via the causation
  chain; the UI affordance (undo/redo buttons + top-of-stack labels).

## Doubt-driven review — failure modes & the decisions that must survive it

Run an adversarial, fresh-context review (ideally **cross-model** via external-agent-delegation) on these
BEFORE Slice 1 code:

- **FM1 — a "reversible" op that doesn't fully restore state** (hidden state, non-determinism, a side
  effect). Corrupts silently. *Mitigation:* invariant #4 + "when in doubt, Irreversible"; Slice 1 picks
  ops that are provably pure.
- **FM2 — undo leaking into truth mutation.** *Mitigation:* taxonomy contract #1, enforced by a
  station-style dependency scan; truth-appending commands are Irreversible by construction.
- **FM3 — authority fork** (command-stack vs ledger-as-authority). The decision most worth an independent
  challenge — an append-only audit log folded into a live redo cursor is subtly wrong (branching, undo-of-
  undo); confirm the command-owned stack is right.
- **FM4 — scrub/undo confusion.** Confirm scrub-excluded is the intuitive contract (vs. "undo my last
  seek").
- **FM5 — async re-entrancy** (undo issued mid-forward-command). *Mitigation:* single serialized
  dispatcher; the stack mutates on the same thread as dispatch.
- **FM6 — redo invalidation** across an intervening irreversible op. Standard clear-redo + barrier;
  confirm the message/UX.
- **FM7 — compound atomicity.** A partially-applied compound inverse leaves inconsistent state; confirm
  the reverse-composite is atomic (all-or-nothing) and that the causation chain reliably enumerates
  children.

## Non-goals

- Rewinding the simulation / truth stream (that is the timeline scrub + re-derivation).
- Cross-session or persisted undo.
- Undoing external side effects (published messages, files written, bundles installed) — those are
  Irreversible barriers.

## Doubt-driven review outcome (2026-07-12) — revise before any plan

A fresh-context adversarial review, grounded in the **real** registered-command surface, found the
two-axis *framing* sound but the *concretization* unbuildable. Load-bearing corrections (with the
anchors the review cited):

- **The "reversible input command" class is essentially empty today.** The draft's flagship examples are
  fictional or not reversible: `world.setSpinRate` **doesn't exist** — spin rate is a generation option
  (`WorldGenerationRenderOptions.SpinRateRadiansPerMegaAnnum`) that only changes via **full regeneration**
  (Irreversible; invalidates `_globeReconstructorKey`, `App.World/Services/Service.cs:1294`);
  `view.show/hide` and activity `toggle` are **not commands** (direct `IViewService.ShowAsync`
  / local dispatch + bus publish, never through the dispatcher); `timeline.select_layer` is **tick-gated**
  (throws if the layer isn't active at `controller.Tick`, `App.Timeline/TimelinePlugin.cs:328`) — so undo
  and the scrub are **coupled, not independent**, for that op. → **Slice 1 as written is not buildable.**
- **Invariant #3 is factually wrong — the dispatcher does not serialize.** `ImmediateMainThreadDispatcher`
  runs `action()` inline (`App.Command/Providers/IMainThreadDispatcher.cs:13`); the real ingress runs
  handlers concurrently (HttpTransport threadpool → `RemoteBridgeNode`, ≤16 fire-and-forget async
  handlers/frame). Async handlers interleave → check-then-act races on the stack and **stale pre-state
  across `await`**. Needs a genuine serialization / re-entrancy guard.
- **Reversibility is not statically checkable.** Handlers acquire effects via runtime `registry.TryGet<T>()`
  (a "reversible" handler can `TryGet<IMessageBus>().Publish(...)`; the scan sees only `TryGet<T>`).
  Enforcement must be **runtime capability confinement** (a restricted registry facade that denies
  truth-write/bus/IO resolution and throws if touched), not a P1-style static scan.
- **Silent-corruption path.** Command results use **two-level `Ok`** (transport `Ok=true` = "didn't
  throw"; domain success lives in `ResultJson`). A failed inverse (e.g. "binder not mounted — world bundle
  unloaded") reads as success → undo pops the stack without restoring state. View/camera targets are
  **write-only** (`Action<…>`, no getter) → no pre-state to capture. The reversible contract needs a typed
  success signal **and** read-back APIs.
- **Ledger-linkage gap + multi-actor.** `CommandRequest` has **no `CausationId`** field
  (`App.Command/CommandTypes.cs:12`), so an external undo service can't say "I invert entry X"; every
  command emits **two** ledger entries (request+result). CLI + UI + agent share one ledger/stack — a
  global stack means the user's Undo silently reverses the **agent's** last action. Decide per-actor vs
  global; add `CommandRequest.CausationId` or drop "the ledger reflects the stack".
- **Compound self-contradiction.** "Any irreversible child ⇒ barrier" contradicts "reverse-composite
  atomicity" for a mixed compound; once `resource.reload_bundle`'s irreversible cascade (ALC unload +
  SceneFlow Exit/Enter) has run, no inverse can retract it and captured delegates go stale. → **Any
  irreversible child ⇒ barrier, full stop**; delete the atomic-reverse-composite claim. No compound
  transaction primitive exists (`CausationId` is audit metadata, not a boundary).
- **The natural undo candidates are exactly what the taxonomy forbids.** `timeline.set_track_archived`
  (archive/restore — the most intuitively-undoable op) does **synchronous `File.WriteAllText`** and
  persists across restart (`App.World.Composition/LayerTrackRegistryService.cs:70`) → Irreversible by
  invariant #1. `camera.orbit` / globe drag is the same continuous-navigation class as the scrub the spec
  carves out. The "reversible = no I/O" line cuts through the middle of the natural undo set.
- **Unanswered UX (blocks the value).** Undo dispatches its inverse **as a command**, which appends its
  own request+result entries to the **persisted, user-visible** ledger — undo **grows** the visible
  history instead of retracting the undone op. *What does the user see when they undo?* Must be answered
  before building.
- **Kept:** inverse-as-**data** (`CommandRequest`, not a closure) is right — it respects the ALC-pin
  discipline (closures over bundle types pin collectible ALCs). Add a bundle-generation guard so a stale
  inverse (command unregistered across a reload) is invalidated, not silently re-run.

**Verdict:** two-axis separation survives; the concretization does not. There is no cheap first slice —
shipping undo means first building a **reversible-command substrate** (serialization/re-entrancy guard,
runtime capability confinement, read-back + typed success, `CommandRequest.CausationId`, ledger-visibility
UX). This is a much larger lift than the approved scope assumed. **Strategic fork is the user's** —
(A) build the substrate (multi-slice; slice 1 *builds the reversible path*, not a retrofit), (B) narrow
hard to a single purpose-built tick-independent reversible toggle, or (C) defer until the command surface
grows real reversible inputs and spend the session on higher-ROI work.
