---
name: verify-windowed
description: Verify a fantasim-app-godot feature by hot-reloading the changed bundle in the already-open exported WINDOWED Godot app — the only gate that exercises the Godot seam and ALC collection. Build + re-run only for changes outside a collectible bundle (resident/host code, T1 contracts, T4 seams, the native iii bridge, a new bundle registration). Use after any change to a collectible tier (App.Stage / App.Assist / App.Timeline), or whenever you are about to claim a feature works.
category: 04-tooling
layer: tooling
related_skills:
  - "@unify-build"
  - "@doubt-driven-development"
---

# Verify in the Windowed App (Bundle Hot-Reload)

## Overview

`fantasim-app-godot` is a 4-tier (T1 contracts · T2 services · T3 orchestrators · T4
seams/hosts), bundle-oriented app. Collectible feature bundles — **App.Stage**,
**App.Assist**, **App.Timeline** — ship as PCKs loaded into hot-reloadable
`AssemblyLoadContext`s. Because they hot-reload, you verify a feature by **updating the
bundle in an already-running windowed app**, not by rebuilding and relaunching every time.

Headless proves wiring only. The **exported windowed app is the only gate** that exercises
the Godot seam, renders, and shows whether the collectible ALC actually unloaded.

## When to Use

- After any change to a collectible tier (`App.Stage` / `App.Assist` / `App.Timeline`).
- Before claiming a feature works, renders, or that a bundle hot-reloads/unloads.
- Any time you would otherwise reach for a full rebuild to check a one-tier change.

**When NOT to use (these need a full build + re-run instead — see the table below):** changes
to resident/host code, T1 contracts, T4 seams, the native iii bridge, or the bundle registry.

## The Hot-Reload Loop

Keep the windowed app open the whole time. Per change, only steps 2–5 repeat.

```
1. task run:exported          # launch the WINDOWED app (console attached) — leave it open. NEVER --headless.
2. <edit a collectible tier>  # App.Stage / App.Assist / App.Timeline
3. task bundle:<tier>         # bundle:stage | bundle:assist | bundle:timeline
                              #   (builds the tier DLL → stages it → exports the PCK)
4. task bundle:install        # copies the exported PCK(s) next to the running app
5. <observe>                  # App.Resource's file-watcher detects the PCK change and
                              #   hot-reloads it (debounced). Verify the feature in-window
                              #   AND confirm the `old ALC collected` log line.
```

`task bundles` re-exports all three tiers at once when you touched more than one.

## Hot-Reload vs. Full Build — the decision

**Hot-reload** (steps above) when the change is **inside a collectible ALC** — the bundle
DLL, its scene, or its manifest for App.Stage / App.Assist / App.Timeline.

**Full build + re-run** — `task build:godot:desktop` → `task run:exported` — only when the
change is **outside a collectible ALC**:

| Change | Why hot-reload can't cover it |
|---|---|
| Resident / host code (`complete-app` Host, `App.Common`) | Loaded at app startup, not in a collectible ALC |
| T1 contracts (`project/contracts/`) | Shared interfaces — consumers must recompile |
| T4 seam projects (`App.*.Seam`) | Resident; Godot types live here and aren't reloaded |
| Native iii bridge | `task bridge:build`, then relaunch — the gdext dylib loads at startup |
| New bundle registration | The host reads `collectible-bundles.json` at startup |

When in doubt, prefer hot-reload first; if the change clearly isn't picked up, it's probably
one of the rows above — then build.

## Red Flags

- Rebuilding the whole app for a single-tier change (slow; defeats the bundle design).
- Verifying **headless** — a clean headless log proves wiring, never rendering or ALC collection.
- Claiming hot-reload/unload **without** the windowed app AND the `old ALC collected` line.
- Adding a new collectible bundle but skipping its `collectible-bundles.json` entry (the
  `BundleHost` load-time lint will throw — by design; never weaken it).

## Verification

A feature/bundle claim is valid only when:

- [ ] The change was exercised in the **exported windowed app** (`task run:exported`), not headless.
- [ ] For an in-bundle change: the new PCK was hot-reloaded via `task bundle:<tier>` + `task bundle:install`, and the behavior was observed in-window.
- [ ] For a hot-reload/unload claim: the **`old ALC collected`** log line appeared after the reload cycle.
- [ ] For an out-of-ALC change: a full `task build:godot:desktop` → `task run:exported` was run (hot-reload would silently not apply).
