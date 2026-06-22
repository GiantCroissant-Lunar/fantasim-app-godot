# Session record — .tscn-native timeline: plan EXECUTED + merged (via agy/kimi CLI dispatch)

> **Date:** 2026-06-23 · **Repo:** `fantasim-app-godot` · **Result:** the 11-task tscn-timeline plan is
> **executed, reviewed, and merged to `main` @ `34dffab`** (fast-forward, 12 commits). Core behavior
> **windowed-verified** in the exported app (screenshot). **One runtime gate remains: interactive
> hot-reload + play/scrub confirmation.** Predecessor: `2026-06-23-tscn-timeline-spec-plan-delegation.md`.

## TL;DR

Executed `vault/plans/2026-06-23-tscn-timeline.md` task-by-task, **dispatching each task's
implementation to an external CLI** (kimi for transcription/edits; agy for the Task-3 surgical refactor
and the pure-C# odometer test) per the user's instruction, with the orchestrator verifying every task
(build/diff/test) and committing path-scoped. Full suite **126 tests / 0 fail**. Windowed-verify in the
exported app **caught a real ALC bug** (fixed). Final whole-branch review (opus) = clean/"with fixes";
the two review fixes landed. Merged to `main`.

## What shipped (`main` @ 34dffab, 12 commits 6aceb7b..34dffab)

The HSlider + fake trackless AnimationPlayer transport + Plan-5a boom-hud `TimelineViewSource` are
**retired**; replaced by a Godot-native `Timeline.tscn` in the hot-reloadable collectible `timeline`
bundle: `AnimationPlayer` CT value-track + `AnimationTree` (idle/playing/scrub); multi-lane
(lane=sphere, track=layer, section=regime); odometer-ladder CT labels via `CanonicalDisplayFormatter`;
seam flip so the bundle's `TimelineFace` pushes ticks to the resident `GlobeView`. `TimelineModel` kept
as the pure-C# layout calculator (only `TimelineViewSource` retired).

## ⚠️ Windowed-verify bug found + fixed (the gate's payoff)

`TimelineFace` silently did not attach: `warn: Resident script not resolved for bundle timeline`. Root
cause in `BundleHost.LoadCoreAsync` — the entry scene was instantiated (running the manifest
`residentScripts` binding via `BundleSceneHost.LoadResidentTypeScript`) **before** the bundle's plugin
assembly was loaded into its collectible ALC, so the bundle-local type `TimelineFace` was unresolvable.
**Fix (`0eda2a8`): load the plugin assembly (`AddGroupAsync`) BEFORE `InstantiateScene`.** Re-verified:
`info: Resident script bound for bundle timeline: . -> FantaSim.App.Timeline.TimelineFace`. (stage/assist
never hit it — script-less scenes. Reorder verified safe for them by the reviewer.)

## Durable build/ALC findings (reusable for any collectible Godot scene-tier bundle)

- A bundle that ships a `.tscn` with a C# script needs **`Godot.NET.Sdk`**. The SDK auto-injects a
  versioned `GodotSharp` ref that collides with central package management → set
  **`ManagePackageVersionsCentrally=false`** AND give every `PackageReference` an explicit `Version=`.
- Godot-SDK DLL output is **`.godot/mono/temp/bin/Debug/`**, not `bin/Debug/net8.0/` → Taskfile
  `bundle:timeline:build` cp path fixed (`fcf0e42`).
- Switching Microsoft.NET.Sdk → Godot.NET.Sdk leaves stale `obj/` (CS0579 dup-attr) → `rm -rf obj bin .godot`.
- Script attaches to a **script-less** `.tscn` root via manifest
  `residentScripts:[{nodePath:".",residentType:"FantaSim.App.Timeline.TimelineFace"}]` (avoids a
  cross-ALC `res://` script path). Value-track-driven C# property needs **`[Export]`**.
  `AnimationNodeStateMachinePlayback.Travel(name,bool)` — bool is positional (`Start` has `reset:`, `Travel` doesn't).
- Exported `.app` stdout captures **ILogger** but **not `GD.Print`**; for a visual use
  **`FANTASIM_GLOBE_CAPTURE=<png>`** (viewport readback at frame 15 then quit — no Screen Recording perm).
  Screenshot from this session: `.agent/logs/windowed-verify/timeline-capture.png`.

## NEXT — start the new session here

1. **Interactive windowed verify (the one remaining gate).** `task run:exported`, then confirm at the
   window: **Play** advances the playhead (the value-track runtime behavior — the riskiest unproven bit);
   drag-scrub seeks; click a regime band seeks; the `ka→kb` label rollover at onset; **regime bands lay
   out** (they position on the `Resized` pass — confirm they show, not just the layer tracks); and
   **hot-reload** per `.agent/rules/bundle-hot-reload-verify.md` — the review's open item:
   **confirm `old ALC collected`** after a `timeline` reload. `UnregisterPlayback` (the resident→collectible
   pin-breaker) runs on the deferred `_ExitTree` path after `RemoveGroupAsync`; if the ALC does NOT collect,
   the reviewer's fix is to move `UnregisterPlayback` to a synchronous pre-unload hook the SceneHost calls
   before `RemoveGroupAsync`.
2. **Deferred (still stands): Plan 5 render polish** — boundary-type terrain + magma glow (the gap between
   "tectonics correct" and "looks like a world"); the globe is the bare pre-plate sphere pre-onset.

## Notes

- Agent-resource sync populated repo `AGENTS.md` + deployed `.agent/.agents/.claude/CLAUDE.md` mid-session
  (not authored this session); left **uncommitted/untouched** per the user. Decide separately whether to commit.
- CLI delegation: **read `.agent/skills/04-tooling/external-agent-delegation/SKILL.md` before dispatching.**
  This session: kimi 0.18.0 + agy 1.0.10, both headless-verified; per-task prompts staged under
  `.agent/run/dispatch/`, logs under `.agent/logs/{kimi,agy}/`.

## Pointers

- Spec: `vault/specs/2026-06-22-tscn-timeline-time-advancement-design.md`
- Plan: `vault/plans/2026-06-23-tscn-timeline.md`
- SDD ledger (full per-task record incl. the 6 plan defects the verify gate caught): `.git/sdd/progress.md`
