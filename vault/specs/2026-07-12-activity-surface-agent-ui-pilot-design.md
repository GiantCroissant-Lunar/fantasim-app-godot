# Activity surface as the agent-UI (a2-ui / ag-ui) pilot — concept lock

**Status:** DRAFT concept-lock (2026-07-12). Refines
[hot-reloadable-ui-runtime-and-scoped-bindings.md](../architecture/hot-reloadable-ui-runtime-and-scoped-bindings.md),
which already names `a2-ui` and `ag-ui` as target presentation-document families and slates the
`activity` bundle to migrate from presentation-*code* to presentation-*data + bindings*. Builds on the
list-item card work shipped `9ec5197` (2026-07-12), which converted the activity ledger from a flat
label stack to BoomHud `panel`/`badge` cards.

## Why now

The activity ledger should stop being a passive *log* and become an interactive **agent-interaction
surface**: entries are structured (schema-conformant) so an AI can emit them and the surface renders
*proper* detail; items collapse/expand for detail; and it hosts action affordances (starting with
undo/redo, spec'd separately). `ActivityActor.Kind` already admits `"agent"` as a first-class actor,
so the ledger was designed anticipating this.

## Grounding: the protocol split (verified against the specs)

- **AG-UI** (Agent-User Interaction Protocol) = *transport*. A stream of ~17 typed JSON events
  (`TEXT_MESSAGE_*`, `TOOL_CALL_*`, `STATE_SNAPSHOT`, `STATE_DELTA`, …), agent→frontend, over SSE /
  WebSocket / HTTP.
- **A2UI** (Agent-to-UI, v1.0) = *payload*. A declarative JSON UI definition — a flat **adjacency
  list** of components with ID references ("LLM-friendly"), separating UI structure from application
  state — carried inside an AG-UI event (e.g. a `TOOL_CALL`).

Fantasim already has both halves in embryo:

| Protocol role | Fantasim analogue |
|---|---|
| AG-UI transport / event stream | `App.Remote` HTTP command ingress (`:19292`) + the activity entry stream (`ActivityEntry` with `CausationId` / `CorrelationId`, request/result pairs) |
| A2UI payload / declarative UI tree | BoomHud `RuntimeSurfaceDocument` (catalog-validated component tree; the activity card builder already emits it) |

## Decisions

1. **BoomHud `RuntimeSurfaceDocument` is the canonical intermediate `PresentationDocument`.** Per the
   parent arch doc ("all formats normalize into one intermediate model"), A2UI and AG-UI JSON
   *normalize into* the BoomHud runtime-surface tree; we do not fork a second renderer. Note the shape
   gap to reconcile in the adapter: A2UI is an **adjacency list**, BoomHud is a **nested tree** —
   normalization flattens/expands between them.
2. **Activity is the pilot surface** for the data-driven, agent-emittable runtime (the arch doc's
   stated migration target for `activity`).
3. **Sequencing (user, 2026-07-12):** build the pilot surface (collapsible + schema-driven detail)
   first; undo/redo is a *separate* spec.
4. **Undo/redo scope (user, 2026-07-12), for the separate spec:** UI operations + reversible domain
   commands only. Reversible commands declare an inverse or capture a prior value on the **command
   descriptor**; irreversible commands (regenerate, reload_bundle, screenshot) are flagged
   non-undoable; **simulation time-travel stays on the existing timeline scrub** (a different axis —
   not command-undo). The activity ledger's causation/correlation IDs are the undo-stack *substrate*;
   whether the ledger is the undo *authority* or merely *reflects* an `App.Command`-owned stack is an
   open question for that spec. This track is doubt-driven (touches truth-stream invariants) and gets
   its own design review before code.

## Phase A slices (this spec)

- **A1 — scroll + collapsible items.** Add a `scroll` primitive to the BoomHud catalog + renderer
  (→ Godot `ScrollContainer`); give each activity card a chevron button dispatching `toggle:{entryId}`;
  the `ActivityViewSource` tracks an expanded-id set and re-renders (recreate-and-rebind) the expanded
  card with its detail rows inline (today's tooltip content). Removes the current newest-15 cap.
- **A2 — schema-driven per-entry detail.** Define a per-entry *detail schema*; render the expanded
  card's detail from a presentation subtree derived from the entry's `PayloadJson` + command
  descriptor, instead of the hardcoded field list. This is the normalization adapter's first real job.
- **A3 — agent-emitted detail.** Let an entry carry (or reference) an A2UI/AG-UI detail payload the
  agent produced; the surface renders it through the same normalize→BoomHud path. Closes the loop:
  "the AI follows a JSON schema to create activity UI showing proper detail."

## Key open decision — how to get scroll

A1 needs vertical room, which the pure-BoomHud render path lacks today (scroll currently exists only
in hand-authored shell `.tscn`s; see [[fantasim-boomhud-card-ui-pattern]]).

- **(recommended) Add a `scroll` container to BoomHud** (`plate-projects/boom-hud`): ~1 catalog entry
  + 1 renderer case → `ScrollContainer`. Small, additive, benefits *every* BoomHud surface, keeps the
  activity bundle pure-data. Cost: a shared house-library change (boom-hud build+pack), not bundle-only.
- (alt) Extend the resident `PresentationShellBinder` to emit cards inside a shell `.tscn` scroll —
  keeps scroll without a boom-hud change, but is resident (rebuild+relaunch) and re-couples activity to
  a hand-authored scene we just removed.

## Constraints carried from the card work

- Resident `ViewRenderer.NormalizeLabels` forces `AutowrapMode.Off` + `ClipText=false` + light-gray
  font on all labels post-mount → truncate long text at build, full text in tooltip/expanded detail;
  per-variant badge *text* color is overridden (bg/border variant survives). Revisit if A2/A3 need
  richer inline text.
- Presentation JSON is an embedded resource in the bundle dll; changing it needs `task bundle:activity`
  + targeted `activity.pck` install + hot-reload; gate = `old ALC collected for bundle activity`.

## Open questions

1. A2UI adjacency-list ↔ BoomHud tree: normalize in a T4P adapter, or extend BoomHud to accept an
   adjacency-list document directly?
2. Detail schema ownership: per-command-descriptor detail templates, or a generic
   payload-shape → presentation mapper, or agent-supplied payload (A3) as the default?
3. Does A1's `scroll` primitive belong in `boomhud.runtime.basic.v1`, or a new catalog version?
4. Undo authority: ledger-as-authority vs `App.Command`-owned stack the ledger reflects (deferred to
   the undo spec).

## Delivered — A2UI payload schema (2026-07-12)

The constrained A2UI vocabulary is now published as a real JSON Schema so an agent can validate a
payload **before** emitting it (the first handover follow-up):

- **File:** `project/contracts/App.Ui/Presentation/Schemas/a2ui-surface.schema.json` (draft 2020-12,
  `$id: https://schemas.fantasim.local/ui/a2ui-surface.schema.json`). Embedded in the
  `FantaSim.App.Ui.Contracts` assembly and retrievable at runtime via
  `A2uiSurfaceSchema.Json` — a host can hand it to an agent on request.
- **Fidelity (schema-valid ⟹ normalizes AND renders).** The schema encodes *both* layers of the real
  contract, taken from the live `RuntimeSurfaceCatalog.Basic`, not a hand-copied list:
  - **Component `type`** — hard `enum` of the catalog types **minus `nodeGraph`** (its essential
    `wires` data can't be expressed in the flat form, so an emitted one is degenerate — build node
    graphs through the canonical surface). Answers open-question #1: the normalizer *is* the T4P
    adapter; agents keep the flat form.
  - **Properties per type** — hard per-type allow-list = the catalog's `Properties ∪ BindableProperties`
    (mirrors `RuntimeSurfaceValidator`, which accepts a static property if it is in either set),
    enforced with `if/then` + `unevaluatedProperties: false`.
  - **Action events per type** — hard (`button` → `pressed`, `list` → `selected`); other types forbid
    `actions`.
  - **`variant` values — intentionally a free string, not an enum.** Variants are theme-resolved at
    render time; an unknown one falls back to default styling and does **not** fail validation, so a
    hard enum would wrongly reject renderable payloads. The recognized values are documented on the
    `variant` `$def`.
- **Drift guard (`A2uiSurfaceSchemaTests`, in `App.Ui.Tests`).** No JSON-Schema engine ships offline,
  so instead of evaluating the schema the tests couple its declared vocabulary *directly to the live
  catalog* (type enum, per-type property sets, per-type event enums) and push every schema `example`
  through the real normalize→validate pipeline. Catalog gains/loses a type, property, or event without a
  schema update ⇒ a test fails. **102/102 `App.Ui.Tests` green.**
- **Author-side semantic check.** A one-off `jsonschema` (draft 2020-12) run confirmed the schema
  evaluates as intended — meta-schema valid; both examples + 5 valid edge cases accepted; 12 should-fail
  payloads all rejected (incl. `text` on a container, `enabled` on a spacer, wrong event on a button,
  extra key in an action). Script kept in this session's scratchpad (not CI — CI is .NET/offline).
