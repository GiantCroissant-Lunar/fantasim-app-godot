# Handover — Activity surface → agent-UI (a2-ui/ag-ui) pilot

**Date:** 2026-07-12 · **Branch:** `main` (shared with a concurrent tunnel-timeline agent) ·
**fantasim HEAD:** `6cd2f9e` · **boom-hud HEAD:** `d5fb077` (pushed to origin/main)

## TL;DR

Turned the activity ledger from a flat text log into the workspace's **agent-UI pilot surface**.
An agent can now emit a UI document (A2UI adjacency-list, constrained to the BoomHud vocabulary) and
it renders as an activity card's detail. Delivered end-to-end across five commits, verified in the
exported windowed app. Design is concept-locked in
[`vault/specs/2026-07-12-activity-surface-agent-ui-pilot-design.md`](../specs/2026-07-12-activity-surface-agent-ui-pilot-design.md),
which sits under [`architecture/hot-reloadable-ui-runtime-and-scoped-bindings.md`](../architecture/hot-reloadable-ui-runtime-and-scoped-bindings.md)
(the doc that already named a2-ui/ag-ui as target formats).

## What shipped (all on `main`, activity-only commits)

| Commit | Slice | Summary |
|---|---|---|
| `9ec5197` | (pre-pilot) | Flat labels → BoomHud `panel`/`badge` cards (kind badge, name, time, meta pills, tooltip) |
| `85cb1f5` | **A1** | Added a `scroll` primitive to boom-hud (→ `0.1.47`); wrapped the card list in it; collapsible cards (chevron dispatches `toggle:{EntryId}`); raised renderer `MaxNodeCount` 512→4096 |
| `12bb36e` | **A2** | Generic payload→typed detail (drops the hardcoded key whitelist); chevron fix (▸/▾ are TOFU in the app font → ASCII `+`/`-` with a `neutral`-variant pill) |
| `4430c98` | **A2-full** | `A2uiPresentationNormalizer` (adjacency-list → BoomHud tree, safe-by-construction) + `ActivityEntry.DetailDocumentJson` contract field + builder wiring |
| `6cd2f9e` | **A3** | `activity.emit_detail` remote command (agent emission path); entries with a detail doc auto-expand |

boom-hud `d5fb077` (the `scroll` primitive) is **pushed** to origin/main.

## Protocol mapping (verified against the specs)

- **AG-UI** = transport (agent→UI event stream) → fantasim's `App.Remote` command ingress (`:19292`) + the `ActivityEntry` stream.
- **A2UI** = payload (declarative UI, flat adjacency-list of components by id, LLM-friendly) → normalized into the **BoomHud `RuntimeComponentNode` tree**, which stays canonical (the renderer/validator target).

## Code map

- **Normalizer** (reusable, resident): `project/contracts/App.Ui/Presentation/A2uiPresentationNormalizer.cs` — `Normalize(a2uiJson, idPrefix) → JsonObject?`. Returns null on any structural problem (missing root, dangling/cyclic ref, missing type, over-limit) → caller falls back. Emits only generic nodes, so `RuntimeSurfaceValidator` still guards. Ids are prefixed to avoid collisions.
- **Contract**: `project/contracts/App.Activity/ActivityEntry.cs` — added optional `string? DetailDocumentJson`.
- **Card builder** (bundle): `project/plugins/App.Ui.Activity/ActivityPresentationDocumentBuilder.cs` — `AppendExpandedDetail` renders the normalized A2UI doc when present, else the generic payload walker. Chevron constants + `ToggleButton` (neutral variant) here. Generic detail = `FormatPayloadDetailParts` (walks all payload keys, typed) + `SkipDetailKeys` + `LabelFor`.
- **View source** (bundle): `project/plugins/App.Ui.Activity/ActivityViewSource.cs` — `_expanded` set (lock-guarded), `OnEntry` auto-expands detail-doc entries, `Dispatch` handles `toggle:`.
- **Emission command** (resident): `project/plugins/App.Command/HostComposition/CommandComposition.cs` — `activity.emit_detail`; publishes an `ActivityEntry` on the crosscut bus (records via App.Activity's bus-subscription `Append` AND refreshes the view in one call — `Append` does NOT republish, so `bus.Publish` is the path). `App.Command.csproj` now refs `GiantCroissant.CrosscutFoundation.Messaging.Contracts`.
- **Renderer options** (resident): `project/plugins/App.Ui.Seam/ViewRenderer.cs` — `ValidatorOptions.MaxNodeCount = 4096`.
- **Presentation scaffold** (bundle, embedded in dll): `project/bundles/activity/activity.presentation.json` — root container with a `scroll` wrapping the rows slot.
- **Tests**: `project/tests/App.Ui.Tests/A2uiPresentationNormalizerTests.cs` (13, incl. a normalizer→validator round-trip) + `ActivityViewSourceTests.cs` (card structure, generic detail, toggle, A2UI-detail wiring). **78/78 green.**

## The emission API (how an agent drives it)

Dispatch through the ingress (`tools/fantasim-cmd.py cmd activity.emit_detail '<payload>'`):
```json
{ "name": "agent.world.analysis", "category": "agent", "outcome": "ready",
  "detail": { "root": "d", "components": {
      "d":   {"type":"container","layout":"vertical","children":["hdr","meta"]},
      "hdr": {"type":"label","text":"Tectonic analysis complete","variant":"title"},
      "meta":{"type":"container","layout":"horizontal","children":["p1"]},
      "p1":  {"type":"badge","text":"plates: 3","variant":"info"} } } }
```
Allowed component types = the BoomHud catalog: `container`, `panel`, `label`, `badge`, `button`,
`progressBar`, `list`, `scroll`, `spacer`. Variants = the theme's `title`/`sectionTitle`/`muted`/`item`/
`section`/`surface`/`info`/`success`/`warning`/`danger`/`neutral`. A demo payload is at
`scratchpad/a2ui-payload.json` (this session's scratchpad).

## ⚠️ Build/verify operational knowledge (READ before rebuilding)

1. **Full-solution `task build` is BLOCKED** by the tunnel agent's in-flight red TDD test
   (`App.Presentation.Tests/TunnelRuntimeChangeThreadGateTests.cs` references an unimplemented type).
   The **export doesn't need test projects** — export the app graph directly:
   `dotnet tool run unify-build -- BuildGodotDesktop` (its internal `Compile` builds only the app graph).
2. **boom-hud is NuGet-consumed** (separate repo `plate-projects/boom-hud`, local feed
   `packages/nuget`). Changing it = edit → `task pack` → copy nupkgs to the feed → bump
   `project/Directory.Packages.props` (+ any inline pins) → resident rebuild + **relaunch** (NOT
   hot-reloadable). macOS quirks hit this session: `tools/gitversion.sh` is Windows-only (use global
   `dotnet-gitversion`); `unify-build PackProjects` needed `--root .` (no `.nuke` marker);
   `syncLocalNugetFeed:false` so nupkgs were copied to the feed by hand. Current version: **0.1.47**.
3. **Full re-export + relaunch sequence** (used this session, resident/contract changes need it):
   ```
   kill <app pid>
   dotnet tool run unify-build -- BuildGodotDesktop
   task bundle:common
   python3 tools/bundles/strip_common_from_export.py --app <APP> --manifest project/bundles/common/manifest.json --assembly complete-app --common-pck build/_artifacts/0.1.2/godot/bundles/common.pck
   mkdir -p <APP>/Contents/MacOS/config && cp project/hosts/complete-app/config/{track-pipeline,declared-layers,app}.json <APP>/Contents/MacOS/config/
   task bundle:activity          # rebuild activity.pck against the new contract
   task bundle:install
   remote__enabled=true remote__bind=127.0.0.1:19292 graph__show=false graph__autoRun=false activity__show=true nohup <APP>/Contents/MacOS/complete-app > <log> 2>&1 &
   ```
   (`<APP>` = `build/_artifacts/0.1.2/godot/osx/complete-app.app`.) The **contract change makes bundle
   hot-reload impossible** — a new-contract `activity.pck` in an old-contract process mismatches; must relaunch.
4. **Bundle-only changes** (builder/json, no contract change) DO hot-reload: `task bundle:activity` →
   copy just `activity.pck` next to the running exe → file-watcher reloads. Gate: `old ALC collected for bundle activity`.
5. **App currently running: PID 74893** (may be gone by next session). Log in the prior session's scratchpad.

## Verification state (honest)

- **Unit:** 78/78 `App.Ui.Tests` (normalizer incl. validator round-trip; card structure; generic detail; toggle; A2UI-detail wiring).
- **In-window:** A1 scroll renders (bounded panel, `ScrollContainer` in the tree); A3 emission → **auto-expanded agent card rendered the emitted A2UI** (title, muted desc, info/warning/success badges, confidence progressBar), clean ALC, zero render errors.
- **Not exercised in-window (low-risk, noted):** a live chevron *click* (toggle is unit-tested + uses the same dispatch path as the working refresh/hide buttons); scroll *overflow* (couldn't inflate the ledger with synthetic commands on a fresh boot). The chevron `+` glyph is a bit faint — the pill carries the affordance; worth a polish pass.

## Coordination context (IMPORTANT for the next session)

- **Shared `main` working tree** with a concurrent tunnel-timeline agent. The working tree currently
  holds THEIR uncommitted files (`App.Resource/Services/IService.cs`, `Host.cs`,
  `PlanetPresentationReloadGate.cs`, `TimelinePlugin.cs`, tunnel tests) — **do NOT commit or revert those.**
  All my commits staged only activity files. Do the same: stage explicit paths, guard against
  `tunnel|timeline`. Do NOT branch (a shared worktree checkout switch would yank their work).
- Two background sessions the user started (`task_692f28ac`, `task_5755bbb8`) to fix activity tests are
  **redundant** — those tests are green + committed here. They'll conflict on the same files; cancel them.
- Relaunching the exported app replaces the tunnel agent's running instance — the user has been
  authorizing this, but confirm before relaunching.

## Open / next (whenever the user wants)

- **A2UI JSON Schema** — publish the constrained-vocabulary schema as an actual JSON Schema so agents validate before emitting.
- **Vocabulary extensions** — nested `list` items, images/icons, richer `panel` headers (may need boom-hud additions).
- **Real domain flow** — wire an actual pipeline (e.g. world-gen) to emit its own A2UI detail cards instead of the demo command.
- **Streaming** — A2UI supports incremental streaming; deferred (one-shot is fine for card detail).
- **Undo/redo track** (separate spec, user-approved scope earlier: UI ops + reversible domain commands via a command-descriptor inverse; ledger causation IDs as the undo-stack substrate; irreversible commands flagged; simulation stays on the timeline scrub). This is doubt-driven territory (truth-stream invariants) — spec before code.

## Gotchas learned

- Resident `ViewRenderer.NormalizeLabels` forces `AutowrapMode.Off` + `ClipText=false` + light-gray on
  every label post-mount → truncate long text (full text in tooltip/expanded rows); badge *bg/border*
  variant survives but *text* color is overridden.
- `▸`/`▾` (and geometric shapes generally) render as tofu in the app font — ASCII only for glyphs.
- Adding a param to the `ActivityEntry` positional record is source-compatible for named-arg callers but
  a **binary** break for bundles that *construct* it against the old ctor — only `App.Ui.Activity`
  (bundle) constructs it, so only `activity.pck` needed rebuilding; other bundles just read it (fine).
