# Hot-reloadable UI runtime and scoped bindings

**Status:** DRAFT (2026-06-25). This refines the horizontal scope model in
[service-scope-ownership.md](service-scope-ownership.md), the vertical T1-T4 model in
[service-tier-architecture.md](service-tier-architecture.md), and the scene/scope mechanism in
[multi-scene-di-scoping-review.md](multi-scene-di-scoping-review.md).

## Decision

The app should use a **Common-scoped UI runtime** with **scope-owned binding sessions**.

`App.Common` owns the renderer, presentation loader, binding engine, action/event router, hot-reload
watch/adopt path, and the Godot surface host. Domain scopes (`stage`, `assist`, future child scopes)
own their view models, action handlers, presentation registrations, and binding sessions.

This lets UI presentation data hot-reload without pretending that feature-specific Godot-derived UI
assemblies are collectible or worth preserving as the target.

"Common owns" is a lifetime and service-boundary statement. In the current repo, feature-specific
resident seams such as `App.Ui.Seam` and `App.Timeline.Seam` still exist. They are transitional
migration debt, not the target. The target is a data-first UI runtime: a generic resident Godot
adapter/runtime shell creates and binds UI from presentation data. Feature-specific Godot-derived C#
UI classes are removed as their surfaces move onto that runtime.

## Current verified slice

The first implemented slice proves the data-first binding mechanism, not the final data-only loading
path:

- `App.Ui.Contracts` now exposes a generic `PresentationTemplateBinder` that turns JSON presentation
  templates plus placeholder/slot bindings into `RuntimeSurfaceDocument`.
- `App.Ui.Activity` moved from hardcoded document construction to an `activity.presentation.json`
  template and Activity-owned placeholder/row binding.
- The Activity template lives in the Activity bundle source as `activity.presentation.json`, is
  embedded as a fallback resource, is packed into the Activity PCK, and is extracted beside the
  collectible DLL for pure-C# loading.
- Bundle extraction uses a visitor-based policy (`IBundleEntryVisitor` via `BundleExtractor`),
  segregating managed assemblies (`ManagedAssemblyExtractionVisitor`) from presentation and other
  data (`BundleDataExtractionVisitor`) without keeping the old DLL-only extractor as the policy owner.
- Presentation loading is generic and domain-neutral via `PresentationDocumentLoader`, looking up loose
  extracted files beside the assembly before falling back to assembly embedded resources.
- `task bundle:activity:data` can re-export the Activity PCK from the staged DLL and current
  presentation data without rebuilding the Activity assembly.
- The exported app can load external bundle PCKs through `FANTASIM_BUNDLE_DIRS`, which is required for
  verifying rebuilt bundles outside the embedded app PCK.
- The Activity UI is now the runtime audit surface for reload verification. It records user/system
  operations, command request/result pairs, graph runtime events, scene entry, and reload results.
- The Activity bundle was verified in the exported windowed app with five consecutive
  `resource.reload_bundle` calls in one process. Each reload unmounted the Activity view, unloaded the
  Activity bundle, extracted a fresh Activity bundle instance, loaded it, logged old ALC collection,
  and remounted the Activity view. The Activity ledger showed ten command rows for the five request /
  result pairs with `failures 0`.
- Window sizing was verified on macOS Retina by treating `window:initialWidth` /
  `window:initialHeight` as logical desktop size and converting through
  `DisplayServer.ScreenGetScale()` before applying Godot's pixel-sized `Window.Size`.

That last point is still incomplete. Changing Activity layout no longer requires rewriting C#
document construction or embedding the data in the DLL, but it still rides the Activity PCK rebuild
and reload. A broader data-only hot reload path should provide a bundle-relative presentation
resolver contract so any scope-owned view source can request `res://bundles/<bundle>/...` or an
extracted bundle data path without taking a direct Godot dependency.

## The problem this solves

We want all practical parts of the app to be hot-reloadable:

- T3 service implementations should reload when their owning scene scope reloads.
- Presentation data should reload while keeping the current scope and view model alive.
- Godot UI layouts authored as `.tscn` / `.tres` should be editable like prefabs.
- JSON-first UI dialects such as BoomHud, a2-ui, ag-ui, or app-specific schema documents should flow
  through the same binding and action model.
- The app should keep the Unity multi-scene/VContainer mental model: scene = scope, scopes have
  parent-child lifetime and dependency visibility.

The difficult boundary is T4:

- Some T4 code derives from Godot types (`Node`, `Control`, `GodotObject`).
- Godot can cache script classes and native-side references outside normal .NET ownership.
- Loading those Godot-derived assemblies in collectible ALCs is fragile.

Therefore the answer is not "make every T4 assembly hot-reloadable." The answer is:

1. remove feature-specific Godot-derived UI seam assemblies;
2. make the presentation data and binding documents hot-reloadable;
3. make T3 implementations reload with their owning scope;
4. keep only a generic resident Godot adapter/runtime shell for engine interop;
5. bind new UI data to the currently active scope-owned view model/action context.

## Vocabulary

| Term | Meaning |
|---|---|
| **UI runtime** | Common-scoped service that loads presentation data, instantiates/reconciles Godot nodes, binds state, and routes events/actions. |
| **Presentation document** | Hot-reloadable UI description: `.tscn`, `.tres`, BoomHud JSON, a2-ui JSON, ag-ui JSON, or an app schema. |
| **Renderer adapter** | T4G/T4P component that turns one presentation format into a Godot node tree or updates an existing tree. It must stay generic, not feature-specific. |
| **Binding context** | Scope-owned object exposing state snapshots, property paths, action handlers, and event streams for one surface. |
| **Binding session** | Disposable bridge between a Common-owned UI surface and a scope-owned binding context. Disposing it releases all strong references to the scope. |
| **Surface** | A mounted visual root identified by a stable id, such as `timeline`, `world.graph`, `assist.panel`. |
| **T4G** | Generic resident Godot adapter/runtime shell. Contains Godot-derived C# for surface roots, node factory/rendering, resource loading, event capture, and converters. No feature-specific behavior. |
| **T4P** | Pure C# presentation/format adapter code. May be resident today, or collectible if packaged as presentation-code and kept free of Godot pins. Feature-specific T4P should migrate toward data. |
| **T4D** | T4 data. Presentation documents and assets; hot-reloadable. |
| **Legacy feature seam** | Feature-specific Godot-derived C# UI such as `TimelineFace`. Transitional debt; remove as the surface moves to T4D plus scope-owned bindings. |

## Scope topology

The target topology is:

```text
App.Common scope (resident)
  owns:
    - Resource / bundle loader
    - SceneFlow
    - Command / remote ingress
    - UiRuntime / UiService
    - renderer adapters for supported UI document formats
    - action/event routing infrastructure
    - generic resident Godot adapter/runtime shell

  App.Stage scope (reloadable)
    owns:
      - stage-level services
      - Stage-owned Timeline/World/NodeGraph registrations
      - stage view models
      - stage action handlers
      - stage presentation registrations
      - stage binding sessions

    App.World scope (reloadable child, current/future)
      owns:
        - World T3 when delivered as a domain bundle
        - world-specific binding contexts

    App.Timeline scope (reloadable child, current transitional shape)
      owns:
        - Timeline T3 while the existing scene-tier bundle remains intact
        - Timeline scene activator while compatibility scene remains

    App.Assist scope (reloadable child)
      owns:
        - Assist T3
        - assist view models
        - assist action handlers
        - assist presentation registrations
        - assist binding sessions
```

`App.Common` stays alive for the full process. Child scopes may unload and reload. A parent scope
reload disposes child scopes first.

`world` and `timeline` are shown because the current repo already has `world` and `timeline` bundle
entries. The target direction may fold Timeline T3 registration into Stage while keeping Timeline
presentation data separate. Until that split lands, Timeline remains a transitional scene-scope bundle.

## Scope resolution and ServiceArchi

T1 contracts and T2 proxies stay shared, but T2 resolution cannot remain purely flat if scene scopes
own T3 implementations. Service resolution should be contextual:

```text
T2 proxy call
  -> active/current scope provider
  -> parent provider chain
  -> resident/global registry fallback
```

This preserves the Unity/VContainer-style hierarchy: a child scope can see parent services, but a
parent does not directly hold child implementation instances. The existing global `IRegistry`
pattern can remain for resident process services, but scene-owned services should be resolved through
the active scope first. Otherwise a Stage-owned Timeline service registered globally would leak across
scope boundaries, while a purely scope-local Timeline service would be invisible to generated proxies.

## Tier and reload matrix

| Layer | Example | Owner | Reload mode |
|---|---|---|---|
| T1 contract | `App.Timeline.IService`, UI binding DTOs | Common/shared ALC | Not reloaded; shared type identity. |
| T2 proxy | ServiceArchi generated proxy | Common/shared ALC | Not reloaded; follows T1. |
| T3 service instance | Timeline model/service, World, NodeGraph | Owning scene scope | Dispose/recreate instance inside a live scope when assembly stays resident/shared. |
| T3 service bundle | Timeline/World implementation in collectible ALC | Owning scene scope or child scope | Exit owning scope, dispose bindings/services/actors, unload ALC, load new bundle, re-enter. |
| T4P presentation/format code | Pure C# format adapter or legacy view source | Common/resident or presentation-code bundle | Generic adapters may stay; feature-specific view sources should migrate to data. |
| T4G runtime shell | Generic Godot `Control`, node factory, renderer, surface host, event capture, converters | Common/resident | Restart required for adapter changes. |
| T4D data | `.tscn`, `.tres`, JSON presentation/binding docs | Scope registration or bundle catalog | Hot-reload data; rebind/reconcile UI. |
| Legacy feature seam | Feature-specific Godot `Control` / renderer | Host-composed resident today | Transitional debt; remove by moving the surface to data-driven UI runtime. |

The important rule:

```text
Common owns UI mechanism.
Scene scopes own domain state and actions.
Presentation data is hot-reloadable.
Only generic engine-adapter Godot C# is resident.
```

Do not collapse these rows during implementation. A `ui-view` bundle containing pure C# presentation
code is not the same reload operation as a `.json` or `.tscn` data-only presentation bundle.

## Common-scoped UI runtime responsibilities

The Common UI runtime should own the mechanisms that must be stable across scope reloads:

- surface mounting and unmounting;
- loading presentation documents by address/version;
- selecting a renderer adapter by presentation format;
- instantiating or reconciling Godot node trees;
- binding UI elements to state paths and action ids;
- routing UI events into the current binding context;
- applying view-model snapshots back to UI elements;
- reacting to presentation-data changes and rebinding the surface;
- releasing binding sessions when their owning scope exits.

It should not own domain behavior. For example, Common may know that button `playPause` dispatches
action id `timeline.togglePlayback`, but it does not implement playback. The active Stage-owned
Timeline binding context implements that action.

Common also should not keep direct references to collectible view models, legacy `IViewSource`
implementations, actor instances, or generated cross-service targets after their owning scope exits.
Any resident-to-scope bridge must be visible as a disposable binding/session object.

## Scope-owned binding context

Each domain scope contributes binding contexts to the Common UI runtime.

For example, Stage might register:

```text
surface: timeline
state source:
  timeline.tick
  timeline.maxTick
  timeline.playbackState
  timeline.currentRegime
actions:
  timeline.play
  timeline.pause
  timeline.togglePlayback
  timeline.seek
events:
  timeline.viewChanged
```

This registration is owned by Stage. When Stage exits:

1. the binding session disposes;
2. Common releases all strong references to the binding context;
3. UI events stop routing to Stage action handlers;
4. the surface is removed or shown as unbound;
5. the Stage ALC can collect if no other resident strong reference remains.

This avoids the resident-to-collectible pin problem. Common may store a `BindingSession`, but that
session must be scope-disposable and must clear every delegate/reference on dispose.

This model replaces the current `DeferredTimelineFace` / `[CrossService]` bridge instead of adding a
parallel bridge. Today, `TimelineFace._ExitTree` manually clears static resident fields and unbinds
the generated target. In the target architecture, the UI runtime binds presentation elements directly
to state/actions through a binding session, and feature-specific classes such as `TimelineFace` and
their resident proxies are retired.

## Presentation documents

Presentation documents are data. They define structure and bindable ids, not domain behavior.

Supported document families can include:

- `.tscn` / `.tres` Godot prefab-style layouts;
- BoomHud runtime documents;
- a2-ui JSON;
- ag-ui JSON;
- app-specific JSON for precise binding metadata;
- future formats if they can map to the same binding model.

All formats should normalize into one intermediate presentation model:

```text
PresentationDocument
  surfaceId
  version
  root element
  elements:
    id
    type
    properties
    style/classes
    children
    bindings
    events
```

The renderer adapter consumes this normalized model or directly handles the source format while
emitting the same binding points.

Presentation documents should use engine-agnostic DTOs for cross-scope values such as color, length,
size, font token, icon id, and alignment. Resident renderer adapters convert those DTOs into Godot
types. Collectible assemblies should not need `using Godot;` just to describe visual state.

## Binding model

Bindings should be explicit and stable. The model needs four primitives:

1. **Element id** - stable identity for a UI node.
2. **Property binding** - state path -> UI property.
3. **Event binding** - UI event -> action id.
4. **Collection/template binding** - optional repeated UI over a state collection.

Example normalized binding:

```json
{
  "surface": "timeline",
  "elements": {
    "playPause": {
      "type": "button",
      "properties": {
        "text": { "bind": "timeline.playPauseLabel" },
        "disabled": { "bind": "timeline.isBusy" }
      },
      "events": {
        "pressed": { "action": "timeline.togglePlayback" }
      }
    },
    "tickLabel": {
      "type": "label",
      "properties": {
        "text": { "bind": "timeline.currentTickLabel" }
      }
    }
  }
}
```

For `.tscn`, bindings can be expressed in one of two ways:

- node metadata/custom resources inside the `.tscn`;
- a sidecar document such as `Timeline.bindings.json`.

Sidecar bindings are preferable as the canonical format because the same binding model can apply to
`.tscn`, BoomHud, a2-ui, ag-ui, and app-specific JSON. `.tscn` node names or metadata can provide
element ids, while the sidecar owns event/property mapping.

This sidecar binding layer is different from the existing `residentScripts` / `residentType`
manifest mechanism. `residentScripts` says which resident Godot C# class drives a node in a scene.
Binding documents say which UI element property maps to which state path and which UI event maps to
which action id. They can compose, but they should not be treated as the same mechanism.

## `.tscn` as a prefab/layout source

Godot `.tscn` should be treated as a prefab/layout source, not as the service architecture boundary.

Allowed in hot-reloadable `.tscn`:

- normal Godot nodes (`Button`, `Label`, `PanelContainer`, etc.);
- node names or metadata used as binding ids;
- resources/styles/themes;
- layout hierarchy;
- simple declarative data.

Avoid in hot-reloadable `.tscn`:

- feature-specific C# scripts deriving from Godot classes, such as `TimelineFace`;
- domain behavior implemented as Godot script code;
- references that require a specific collectible T3 assembly to be resident in Godot's script cache.

The resident renderer can load the `.tscn`, attach it under a Common-owned surface root, scan ids,
then apply the current binding session.

Reloading `.tscn` / `.tres` must bypass stale Godot resource cache entries. The presentation loader
should use `ResourceLoader.CacheMode.ReplaceDeep` where applicable, and bundle delivery should prefer
versioned or hash-suffixed virtual paths for rebuilt PCK content. A plain cached load can report
success while the UI still shows the old tree.

## Hot reload flows

### Windowed reload-loop verification

Do not call a hot-reload slice verified after one reload or after a headless-only run. For this repo,
the reliable acceptance check is the exported windowed app plus repeated reloads in the same process.

Build and export first:

```bash
dotnet build project/hosts/complete-app/complete-app.csproj -c Debug -v q -nologo
task bundles
dotnet tool restore
dotnet unify-build BuildGodotDesktop --configuration Debug
```

Launch the exported app with external bundle PCKs and Activity visible. Disable graph autorun unless
the test is specifically about graph/worker activity. If `BuildGodotDesktop` writes a newer artifact
version, replace `0.1.1-118` with the current exported app directory under `build/_artifacts`:

```bash
FANTASIM_BUNDLE_DIRS=/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/build/_artifacts/0.1.0/godot/bundles \
remote__enabled=true \
remote__bind=127.0.0.1:19292 \
graph__show=false \
graph__autoRun=false \
activity__show=true \
/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/build/_artifacts/0.1.1-118/godot/osx/complete-app.app/Contents/MacOS/complete-app
```

Drive several reloads through the remote command path:

```bash
for i in 1 2 3 4 5; do
  curl -fsS -X POST http://127.0.0.1:19292/command \
    -H 'Content-Type: application/json' \
    -d "{\"command\":\"resource.reload_bundle\",\"payloadJson\":\"{\\\"bundleId\\\":\\\"activity\\\"}\",\"correlationId\":\"reload-activity-loop-$i\"}"
  printf '\n'
  sleep 0.5
done
```

Each response must be `ok: true`:

```json
{"id":"reload-activity-loop-1","ok":true,"resultJson":"{\"ok\":true,\"bundleId\":\"activity\"}","error":null}
```

The console log must show a complete cycle for every reload:

```text
Dispatching command resource.reload_bundle
View unmounted: activity
Bundle unloaded: activity
Bundle plugin extracted: activity ... /activity/<next>
Activity view source registered; matching view sources=1
Bundle loaded: activity from .../activity.pck
Hot-reload: old ALC collected for bundle activity
resource.reload_bundle: reloaded 'activity'.
View renderer bound to activity.
View mounted: activity
```

The Activity UI must remain visible after the final reload and show the same evidence at the surface
level:

- title count includes `commands 10` for five request/result pairs;
- `failures 0`;
- repeated `[cmd] resource.reload_bundle` rows with `bundle: activity` and `outcome: requested`;
- repeated `[res] resource.reload_bundle.result` rows with `bundle: activity` and `outcome: ok`.

If any command succeeds but the console lacks `Hot-reload: old ALC collected`, the reload is degraded:
something still pins the old collectible context. If the console succeeds but Activity does not show
the command/result rows, the reload path may work but the audit surface is broken and must be fixed
before claiming user-visible verification.

For graph/worker verification, launch `iii`, start the real workers, enable `graph__show=true` and
`graph__autoRun=true`, then require Activity rows for `ui.graph.run`, `graph.run.started`,
`graph.node.started`, `graph.node.completed`, and `graph.run.completed`. Do not use the echo worker
as proof for the real graph path.

### Presentation-data reload

Use when `.tscn`, `.tres`, JSON, style, or binding docs change.

```text
change notification / command
  -> Resource resolves new presentation document version
  -> UiRuntime loads/parses document
  -> renderer reconciles or recreates the surface node tree
  -> BindingEngine reattaches element ids to the existing binding context
  -> current state snapshot is replayed into the UI
```

Expected result: the user sees added/removed buttons, labels, layout, or styles without restarting
and without recreating the Stage T3 services.

This flow assumes a safe reload trigger, such as the `resource.reload_bundle` command path described
in [bundle-delivery-and-loading.md](bundle-delivery-and-loading.md). The current watcher-based path is
legacy and should not be treated as the final trigger contract.

### T3 instance reload

Use when a service instance can be recreated without unloading an assembly. This applies to
resident/shared T3 implementations whose lifetime is scoped even though their code identity is not
collectible.

```text
reload service instance in owning scope
  -> dispose binding sessions
  -> dispose T3 services / unregister handlers
  -> recreate scope services
  -> register fresh binding contexts
  -> UiRuntime rebinds surfaces
```

Expected result: runtime state is recreated and UI rebinds, but code changes are not picked up unless
the assembly was already updated through another process.

### T3 bundle reload

Use when pure domain/service code lives in a collectible bundle.

```text
reload owning service bundle
  -> dispose leaf scope or service scope that owns the bundle
  -> dispose binding sessions
  -> dispose T3 services / unregister handlers
  -> stop and await scope-owned actors/tasks
  -> unload collectible ALC
  -> verify old ALC collection
  -> load new T3 bundle
  -> recreate scope services
  -> register fresh binding contexts
  -> UiRuntime rebinds surfaces
```

Expected result: domain behavior code updates. UI may be briefly unbound, then rebinds to the fresh
context.

### Scope reload

Use when a scene scope is replaced.

```text
reload stage
  -> dispose assist and other child scopes
  -> dispose stage binding sessions
  -> dispose stage services
  -> unload stage-owned bundles
  -> load fresh stage-owned bundles
  -> recreate stage scope
  -> recreate child scopes if policy says to restore them
  -> rebind Common-hosted UI surfaces
```

Expected result: all Stage-owned services and bindings are fresh. Common UI runtime survives.

For v1, prefer leaf-scope reloads (`assist`, `timeline`, similar child scopes). Reloading a parent
such as `stage` is a subtree operation: children must exit first, and restoring them requires an
explicit policy for which state/surfaces come back. Do not silently treat parent reload as the same
operation as leaf reload.

### T4G engine adapter change

Use when the generic Godot-derived engine adapter changes.

```text
developer changes resident generic engine adapter
  -> restart app
```

Expected result: restart is needed only for generic adapter changes. Feature-specific UI changes
should be T4D data changes and hot-reload in-process.

## Bundle classification

The architecture needs bundle categories, not one generic "bundle" concept.

| Bundle kind | Contains | Scope | Reload path |
|---|---|---|---|
| Contract bundle | T1/T2 shared contracts | Common/shared | Process restart or app startup only. |
| Service bundle | T3 pure implementation | Owning scene scope | T3 instance reload or T3 bundle reload, depending on ALC packaging. |
| Presentation-data bundle | `.tscn`, `.tres`, JSON, styles, binding docs | Common UI runtime, bound to scope context | Presentation-data reload; no ALC unload. |
| Presentation-code bundle | Pure C# format adapter or legacy presentation builder, no Godot-derived types | Owning scope or Common UI runtime | Generic adapters may remain; feature-specific builders migrate to presentation data. |
| Scene-scope bundle | `ISceneActivator` + scope registration | SceneFlow | Exit/re-enter scope. |
| Resident runtime shell | T4G generic Godot adapter | Common/resident | Restart. |
| Feature seam | Feature-specific Godot C# | Host-composed resident today | Migration debt; remove rather than extend. |

This avoids conflating `Timeline service`, `Timeline surface`, and `Timeline scene`.

Current manifest tags map only loosely to these categories:

| Current `bundleType` | Current meaning | Target interpretation |
|---|---|---|
| `domain-bundle` | Pure domain/service bundle, such as `world`. | Service bundle. |
| `ui-view` | Current `activity` shape: collectible pure C# UI/view code. | Presentation-code bundle today; target is presentation-data if the surface can be expressed as JSON/tscn plus bindings. |
| `scene-tier` | Scene activator bundle, currently also carrying service/presentation for `timeline`. | Scene-scope bundle only after service and presentation are split out. |

## Timeline bundle split

The current Timeline bundle is the exact conflation this design is trying to remove. Today it acts as:

- a scene-scope bundle (`scene-tier` with an activator);
- a T3 service bundle (`FantaSim.App.Timeline.dll`);
- a presentation-data bundle (`scenes/Timeline.tscn`);
- a legacy feature seam attachment path through `residentScripts` / `TimelineFace`.

The desired end state is likely:

```text
App.Common:
  UiRuntime
  Generic resident Godot adapter/runtime shell

App.Stage:
  Timeline T3 service
  Timeline binding context/action handlers

Timeline presentation-data bundle:
  timeline.tscn or timeline.json
  timeline.bindings.json

Optional Timeline scene-scope bundle:
  only if Timeline remains a separate enter/exit lifecycle under Stage
```

The Timeline presentation can reload independently from the Timeline T3 service. The Timeline T3
service reloads with Stage or with a Stage-owned service bundle. `TimelineFace` is not part of the
target end state.

The migration should explicitly decide whether Timeline T3 is Stage-owned directly or remains a
Timeline child scope for v1. Both can work, but they imply different reload commands and different
ALC ownership.

## Action and event routing

Actions should be addressable by stable ids, not direct delegates stored forever in Common.

```text
UI event
  -> UiRuntime receives (surfaceId, elementId, eventName, payload)
  -> BindingSession maps to action id
  -> current BindingSession action table handles it
  -> optional parent-scope router handles it if local table does not
  -> handler updates view model/service
  -> view model emits snapshot/event
  -> UiRuntime applies changes to bound elements
```

A binding session may use delegates internally, but it must be disposable and must unregister every
handler on scope exit. Prefer routing through a scope-owned action table:

```text
BindingSession
  surfaceId
  scopeId
  action table
  state provider
  subscriptions
```

On dispose, the table and subscriptions are cleared.

Avoid a flat global action router. Two active scopes may both define local actions such as `close`,
`select`, or `play`; collisions should be resolved by the active surface/session first, with explicit
bubbling to parent scope only when intended.

## Reconciliation versus recreate

The UI runtime can start with recreate-on-change:

1. remove old surface root;
2. instantiate the new document;
3. rebind all element ids;
4. replay state.

Later it can optimize to reconciliation:

- preserve matching nodes by stable id;
- update changed properties/styles;
- add/remove changed nodes;
- keep focus/scroll/selection where ids still match.

The architecture should not require reconciliation in phase 1. Correctness first: recreate and
rebind is enough if disposal is clean and visible state is replayed.

## Error behavior

Presentation-data reload should be non-destructive:

- parse/validate new document before replacing the current surface;
- if loading fails, keep the old surface mounted;
- report validation errors through Activity/Command logs and optional dev UI;
- do not dispose the current binding session on presentation parse failure.

Binding failures should be visible but isolated:

- unknown action id -> disable event route or log error for that element;
- unknown state path -> leave property unchanged or show placeholder in dev mode;
- missing element id -> log binding warning;
- duplicate element id -> reject document or choose deterministic first with error.

Scope reload failure should follow the bundle reload policy:

- stage new bundle first;
- dispose old scope only after the new bundle is available if possible;
- on failure, keep old scope or restore last-known-good where supported;
- prove old ALC collection with `WeakReference`, not filesystem deletion.

If ALC collection fails, the failure should be observable as a reload failure with diagnostics. Common
resident references, R3 subscriptions, event delegates, and resident actor-system handles are the first
suspects.

## Required invariants

1. **T1/T2 shared identity.** Contracts live in shared assemblies so every scope sees the same
   interface types.
2. **No resident strong refs to collectible implementations after dispose.** Common must clear
   binding sessions and action handlers on scope exit.
3. **Only generic Godot-derived runtime code stays resident.** Hot reload applies to data and pure C#
   scope services. Feature-specific Godot UI classes are migration debt to retire.
4. **Presentation docs contain ids and bindings, not domain behavior.**
5. **Scopes own behavior.** Stage owns Timeline/World/NodeGraph behavior; Assist owns Assist
   behavior; Common owns UI mechanics.
6. **Reload mode is explicit.** Presentation-data reload, T3 instance reload, T3 bundle reload, scope
   reload, and app restart are distinct operations.
7. **Parent scope can see parent services; child scope can consume parent services.** Parent should
   not directly store child implementation instances outside disposable sessions.
8. **Every binding session is disposable.** Dispose must unregister events, clear delegates, and
   detach UI callbacks.
9. **Presentation reload uses cache replacement.** `.tscn` / `.tres` loads must use
   `ResourceLoader.CacheMode.ReplaceDeep` or equivalent versioned paths so stale Godot resources do
   not masquerade as successful reloads.
10. **ALC collection is verified.** Collectible bundle reload must keep a `WeakReference` to the old
    ALC and force the verification window before reporting reload success.
11. **Presentation parse failure is non-destructive.** A bad document must not dispose the existing
    binding session or unmount the last good surface.
12. **Scope-owned actors/tasks are torn down.** Collectible scopes must stop actors, timers, tasks,
    and subscriptions they created before ALC unload.
13. **T2 resolution is scope-aware.** Generated proxies should resolve from the active scope and
    parent chain before falling back to resident/global services.

## Migration sketch

This is intentionally not an implementation plan. It sketches direction only.

1. Define common UI presentation/binding contracts:
   - `IUiRuntime` / `IUiService`;
   - `PresentationDocument`;
   - `BindingDocument`;
   - `IBindingContext`;
   - `IBindingSession`;
   - scope-owned action table;
   - engine-agnostic UI DTOs and resident converters.
2. Reconcile current bridges:
   - treat `DeferredTimelineFace` / `[CrossService]` as incumbent migration debt, not a model to
     preserve;
   - treat `TimelineFace._ExitTree` cleanup as the manual version of binding-session disposal;
   - identify `ViewHost` / `ViewRenderer` / `IViewSource` references that currently pin UI sources.
3. Add one renderer adapter for a simple JSON document.
4. Add a `.tscn` renderer path that uses node names/metadata plus sidecar bindings and
   `CacheMode.ReplaceDeep`.
5. Move one pilot surface onto the Common UI runtime:
   - Stage owns Timeline binding context;
   - the generic runtime shell owns the surface host;
   - presentation reload updates the visual tree.
6. Add reload command variants:
   - `ui.reload_surface`;
   - `resource.reload_bundle`;
   - `scene.reload_scope`.
7. Split the existing Timeline scene-tier bundle:
   - move Timeline service ownership to Stage or keep it as an explicit Timeline child scope for v1;
   - move `Timeline.tscn` and bindings into a presentation-data bundle;
   - keep `TimelineFace` resident only as temporary migration debt while the generic renderer learns
     to render the surface from data;
   - delete `TimelineFace` and retire `DeferredTimelineFace` once `IBindingSession` owns the bridge.
8. Reclassify `activity` and `NodeGraph`:
   - `activity` is a current `ui-view` presentation-code bundle, not just data, but the target is
     presentation data plus bindings where feasible;
   - `App.Ui.NodeGraph` is feature-specific resident pure C# presentation code today. The target is
     NodeGraph presentation data plus a scope-owned binding context, rendered by the generic UI
     runtime.
9. Repeat for Assist surfaces after the pilot proves scope disposal, cache replacement, and ALC
   collection verification.

## Expected developer experience

When editing Timeline UI:

```text
Change Timeline.tscn:
  add a button named "jumpToOnset"

Change Timeline.bindings.json:
  bind jumpToOnset.pressed -> timeline.seekOnset

Save/build/publish presentation data:
  app reloads presentation document
  button appears
  current Timeline binding context handles the action
```

No app restart. No Stage service reload if only UI data changed.

When editing Timeline T3 code:

```text
Change App.Timeline service logic:
  reload Stage-owned Timeline service bundle or Stage scope
  binding session disposes and re-registers
  Common UI runtime rebinds existing/new presentation to fresh service
```

When editing the generic Godot engine adapter:

```text
Change resident engine adapter C#:
  restart app
```

Editing feature-specific UI should not require this path in the target. It should be expressed as
presentation-data changes plus binding declarations. If a surface still requires editing
`TimelineFace` or another feature seam, that surface has not finished migrating.

## Open questions

1. Should sidecar binding documents be mandatory for `.tscn`, or should `.tscn` metadata be allowed
   as the primary binding source?
2. Should Common UI expose one unified `IUiService` contract or separate `IPresentationService`,
   `IBindingService`, and `IActionRouter` contracts?
3. Should presentation reload be driven through `App.Resource` catalog addresses, or through a
   dedicated UI presentation catalog?
4. What is the first pilot surface: Timeline, NodeGraph, or Assist?
5. Should Stage reload automatically restore child scopes and their surfaces, or should that be an
   explicit policy per scene?
6. Which current feature-specific pure C# view sources can migrate fully to data-only JSON/tscn, and
   which need a temporary generic T4P format adapter first?
7. Should Timeline T3 be Stage-owned directly, or remain a Timeline child scope during the first
   reloadable UI migration?

## References

- [service-tier-architecture.md](service-tier-architecture.md)
- [service-scope-ownership.md](service-scope-ownership.md)
- [multi-scene-di-scoping-review.md](multi-scene-di-scoping-review.md)
- [bundle-delivery-and-loading.md](bundle-delivery-and-loading.md)
- [cross-alc-rules.md](cross-alc-rules.md)
