# Bundle-oriented maximalism — everything collectible except the loading floor

**Status:** DECIDED (2026-07-08). User directive, options resolved interactively same day.
Supersedes the "hold for now" hedges in
[service-scope-ownership.md](../architecture/service-scope-ownership.md) (its decision rule,
reference-direction invariant, and worked world example remain the authority for *how* to
move; this spec decides *what* moves — everything). Companions:
[cross-alc-rules.md](../architecture/cross-alc-rules.md),
[service-tier-architecture.md](../architecture/service-tier-architecture.md).

## The directive (user's words)

> "I would like to be bundle oriented as much as possible. even for presentation, ui,
> whatever, except the functionality that loads bundle, it has to be resident."

There is no "hold" list. If a subsystem is not part of the resident floor below, it moves to
a collectible bundle — the only variables are grouping, ordering, and per-move risk gates.
Never argue a subsystem back to resident for convenience.

## Resident floor (exhaustive)

1. **Thin Host entry** (`hosts/complete-app/Host.cs`) — boot kernel, load bundle catalog,
   enter root scenes. Target: ~50 lines. Everything else in today's 780-line Host.cs
   (timeline rebind machinery, `ShowIiiGraph`, presentation wiring) leaves with its subsystem.
2. **App.Common kernel** — `IRegistry`, config, logging, MessagePipe, Akka `ActorSystem`,
   `IPluginHost` + `SharedAssemblyPolicy`. Every bundle binds against it.
3. **Bundle machinery** — App.Resource + App.Resource.Bundle.Seam, App.SceneFlow,
   ReloadPolicy/SceneTierPckWatcher.
4. **Cross-bundle rendezvous** — App.Command router (bundles register command families into
   it), `IBundleSceneRegistry`, a minimal resident view-anchor node bundle views mount into.
5. **All T1 contract assemblies** (`[PluginSharedContract]`) — type identity across ALC
   boundaries. T1 never moves.
6. **Native code** — the godot-rust gdext binary (native libs never unload). Only the
   *managed* App.Iii wrapper is potentially movable (phase 8, callback audit).

## Decisions (resolved 2026-07-08)

| Question | Decision |
|---|---|
| Bundle granularity | **Domain-grouped.** Presentation joins the existing `world` bundle; NodeGraph + Ui views form a `ui` bundle; Gpu/Render/Audio form a `platform` bundle. Coherent reload units that change together. |
| Sequencing vs locked frontier (D8b scrub, D5/D7b graph arc) | **Phases 0–2 first, then frontier.** Policy inversion + staging tooling + Presentation→world + Timeline T3 land first so the eye-judged look work iterates on hot-reload; phases 3+ interleave with frontier work afterward. |
| App.Remote | **Bundled, fully maximal — loads FIRST.** The `remote` bundle enters before all others to minimize the window where a bundle failure kills the agents' drive channel. Accepted cost: a broken remote bundle means keyboard-only recovery. |

## Two systemic changes (phase 0 — before any migration)

1. **Invert the SharedAssemblyPolicy polarity.** Today
   [App.Common/Bootstrap.cs:110](../../project/plugins/App.Common/Bootstrap.cs) shares the
   `"FantaSim.App."` prefix (resident by default; collectible only by exclusion in
   `collectible-bundles.json`). Target: share only `*.Contracts` / `[PluginSharedContract]`
   assemblies plus the kernel closure; everything else defaults to the bundle's own ALC.
   Kills the exclusion-list bookkeeping and the forgotten-exclusion bug class.
2. **Generate bundle staging from `collectible-bundles.json`.** The world bundle already
   stages from its real `deps.json` filtered through the policy (Taskfile jq pipeline);
   other bundles are hand-copied DLL lists. One generic `bundle:stage` task, driven by the
   registry, replaces per-bundle Taskfile mirrors. At ~10 bundles, hand mirrors are the top
   gotcha source (see the MessagePack two-place-mirror entries in the G-list /
   memory `fantasim-alc-shared-type-identity`).

## Target bundle topology (domain-grouped)

```
RESIDENT: Host(thin) + App.Common + Resource(+Seam) + SceneFlow + Command + contracts + gdext
bundles/
  remote     — App.Remote + App.Remote.Seam            (loads FIRST, ops channel)
  stage      — (exists) scene root
  world      — (exists) App.World* closure + App.Presentation (+ its Godot-mount seam)
  timeline   — (exists) + App.Timeline T3 (+ Timeline.Seam)  → deletes Host rebind machinery
  assist     — (exists)
  activity   — (exists, view) + App.Activity T3 + UnifyStorage LiteDb (goes bundle-local)
  ui         — App.Ui T3 + App.Ui.Seam + App.NodeGraph + App.Ui.NodeGraph + iii-graph view
  platform   — App.GpuCompute(+Seam) + App.GpuShader(+Seam) + App.Render(+Seam) + App.Audio
  camera     — App.Camera + App.Camera.Seam (phantom-camera addon stays in host PCK; audit)
  ecs        — App.Ecs (rebindable frame-pump handle in Host; Akka actor teardown on unload)
  iii        — managed App.Iii(+Seam) ONLY if native-callback audit passes; else stays floor
```

## Phase queue (each gated: windowed `old ALC collected` + doubt-driven review)

| Phase | Move | Note |
|---|---|---|
| 0 | ✅ SHIPPED 2026-07-08 (`ff9a872`, `5d78a2c`) — policy externalized to `shared-assembly-policy.json` + generic stager `tools/bundles/stage_bundle.py` | polarity FLIP still pending (post-phase-2 gated edit to the json) |
| 1 | ✅ SHIPPED 2026-07-08 (`f633d84`…`9714ed7`, windowed-gated: `old ALC collected for bundle world`) — Presentation ships inside world.pck | closure shrink still pending (post-flip); residual: first-reload-after-boot ALC pin (chip filed); see handover/2026-07-08-bundle-maximalism-phase0-1-handover.md |
| 2 | Timeline T3 → timeline bundle | deletes Host.cs:121–205 rebind machinery + the dual-copy smell (host ProjectReference AND bundle-excluded assembly) |
| 2.5 | **Common resident-layer bundle (`common.pck`)** — DECIDED 2026-07-08, spec: [2026-07-08-common-resident-layer-bundle.md](2026-07-08-common-resident-layer-bundle.md) | plate-projects foundation libs load once at boot into the DEFAULT context (packaging granularity, not reload); exe shrinks to the loading floor; kills the bundle∩bundle duplication class; prerequisite shaping for the polarity flip (post-flip shared = contracts + common layer) |
| — | **frontier resumes (D8b, D5/D7b)**; phases below interleave | |
| 3 | NodeGraph + Ui.NodeGraph + evict `ShowIiiGraph` from Host → ui bundle | Host demo is a ~200-line resident consumer; must move anyway |
| 4 | Ui T3 + Ui.Seam → ui bundle | after 3, consumers are all bundles; resident keeps only the anchor. **Verify first:** Godot-derived types (Node subclasses, ScriptManagerBridge source-gen registration) in a collectible ALC — source-driven check against Godot 4.7 .NET, not memory |
| 5 | Camera, Render, GpuCompute, GpuShader, Audio → camera/platform bundles | thin couplings; worn-in template |
| 6 | Activity T3 (+LiteDb bundle-local), Remote (+load-first ordering) | drops UnifyStorage shared exact-matches |
| 7 | Ecs | frame-pump rebind handle + actor teardown; the scope doc's Akka gotcha is live |
| 8 | Managed Iii | only if native side holds no GCHandles into managed collectible code |

## Standing risks

- **Resident→collectible delegates** (render cutaway/exploded/mantle targets, camera orbit
  target, ecs pump): every one becomes a registry-mediated, cleared-on-unload binding.
  The unregister discipline in Host `_Notification` is the pattern; each move extends it.
- **Per-bundle dep-closure audits** stay mandatory (world bundle's 18-name closure is the
  precedent; the MessagePack type-identity class of failure returns any time a shared
  assembly slips into a bundle).
- **Boot ordering** becomes an explicit DAG in `EnterInitialScenes` (remote → stage → world
  → timeline-compose → assist → timeline → …). Keep it in one place, sequenced, logged.

## Next actions

1. Write the phase-0/1 implementation plan via writing-plans → `vault/plans/`.
2. Phase 0 tooling PR (policy inversion behind a config flag first, flip after phase 1 proves).
3. Phase 1 (Presentation→world) with the windowed gate; then frontier resumes.
