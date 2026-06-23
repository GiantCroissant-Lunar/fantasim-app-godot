# Session record — post-timeline: window size, versioning fix, artifacts cleanup, vault doc org

> **Date:** 2026-06-23 (continues [2026-06-23-tscn-timeline-executed-merged.md](2026-06-23-tscn-timeline-executed-merged.md)) ·
> **Repos:** `fantasim-app-godot` (window + versioning + cleanup + vault), `fantasim-world` / `fantasim-cartography` (vault) ·
> **Result:** tscn-timeline is merged; this session added a larger exported window, fixed the GitVersion version explosion, cleaned ~4.3 GB of stale build artifacts, and standardized docs across all three vaults.

## Current state (all on `main`)

| Repo | `main` | What landed this session |
|---|---|---|
| `fantasim-app-godot` | `7010ef3` (+ tag **`v0.1.0`**) | tscn-timeline (merged earlier) · larger window (`100945e`) · versioning fix (`6f02d46`) · vault README+AGENTS (`740e62c`,`7010ef3`) |
| `fantasim-world` | `bc5f9c6` | vault consolidation + README + AGENTS note |
| `fantasim-cartography` | `5122525` | vault taxonomy established + README + AGENTS note |

## What shipped

### 1. Larger exported window (`100945e`)
`project/hosts/complete-app/project.godot` had **no `[display]` section** → default ~1152×648. Added one mirroring `ref-projects/fantasim-app-godot`: viewport **1920×1080**, window override **2400×1350**, `resizable`, `stretch=canvas_items`/`expand`. Verified by screenshot (2400×1350; timeline + globe layout intact).

### 2. Versioning fix (`6f02d46`, tag `v0.1.0`)
Artifacts were landing in `build/_artifacts/57.1.0|58.0.0|…` instead of `0.1.x`. **Root cause:** `GitVersion.yml`'s `major-version-bump-message` regex matched **every** conventional-commit type with the breaking `!` made optional → every commit bumped MAJOR → major tracked the commit count. **Fix:** corrected the regex (require `!`), then per the chosen pre-1.0 policy **disabled the `feat→minor` / `breaking→major` auto-bumps** (commits bump PATCH only) and **tagged `v0.1.0`** as the baseline. Version now resolves to **`0.1.0`**; future commits → `0.1.1`, `0.1.2`, … (with a preview suffix in dev).
- ⚠️ The **`v0.1.0` tag is LOCAL** (this repo has no remote) — push it when a remote is added so CI resolves the same baseline.
- To leave 0.1 later: tag `v1.0.0` or re-enable the commented bump lines in `GitVersion.yml` (the file documents this).

### 3. `build/_artifacts` cleanup
Removed 9 stale version dirs (`4.0.0`…`58.0.0`, plus a fallback `0.1.0`) = **~4.3 GB freed**; kept the `generated` codegen cache. The dir is now clean; **the next build re-exports into `build/_artifacts/0.1.0/`**.

### 4. Vault doc organization (all three repos)
Standardized one taxonomy everywhere: **`architecture/` · `specs/` · `plans/` · `handover/`**, each repo with a `vault/README.md` map-of-content and an `AGENTS.md` "Docs live in `vault/`" pointer.
- `fantasim-world`: consolidated the stray `docs/superpowers/{specs,plans}` into `vault/` via `git mv` (history preserved); `docs/` removed.
- `fantasim-cartography`: empty vault → 4 folders + README (design refs remain read-only in `ref-projects/fantasim-cartography/docs/`).
- Convention: **write new specs/plans/handovers under `vault/`, NOT `docs/superpowers/`.** `AGENTS.md` is hand-maintained (humans + agents); `CLAUDE.md` is the generated pointer (gitignored).

## NEXT — start the new session here

1. **Interactive windowed verify of the timeline (the one open runtime gate).** The exported app was deleted in the cleanup, so first **re-export the full cycle at the current version**: `task build:godot:desktop && task bundles && task bundle:install && task run:exported` (run them together so app + bundles share the `0.1.0` artifact dir — mixing versions caused a transient `bundle:install` miss last time). Then confirm at the window: **Play** advances the playhead (the value-track behaviour — riskiest unproven bit), drag-scrub, click-a-regime seeks, the `ka→kb` label rollover at onset, the regime **bands** lay out, and **hot-reload → `old ALC collected`** (per `.agent/rules/bundle-hot-reload-verify.md`). If the ALC does NOT collect, the code-review's contingency: move `TimelineFace`'s `UnregisterPlayback` off the deferred `_ExitTree` path to a synchronous pre-unload hook the SceneHost calls before `RemoveGroupAsync`.
2. **Plan 5 render polish** (still deferred): boundary-type terrain + magma glow — the gap between "tectonics correct" and "looks like a world" (the globe is the bare pre-plate sphere pre-onset).
3. **Commit `.agent/` in `fantasim-app-godot`** (currently untracked): the committed `AGENTS.md` links to `.agent/rules/…` + `.agent/skills/…`, so those links dangle until `.agent/` is committed. (Part of your agent-resource deployment — `.agents/`/`.claude/`/`CLAUDE.md` are now gitignored as sync output.)
4. **Optional (scoped out this session):** port `ref-projects`' richer design docs (ref `fantasim-app-godot/vault/architecture` ~30 docs; ref `fantasim-cartography/docs` rfcs/roadmap) into the working vaults — each `vault/README.md` flags this.

## Pointers
- Timeline: `vault/specs/2026-06-22-tscn-timeline-time-advancement-design.md` · `vault/plans/2026-06-23-tscn-timeline.md` · `vault/handover/2026-06-23-tscn-timeline-executed-merged.md`
- Vault index (each repo): `vault/README.md` · taxonomy + AGENTS note
- Versioning: `GitVersion.yml` (pinned 0.1.x; tag `v0.1.0`)
- SDD ledger (full per-task record + the 7 plan defects the verify gate caught): `.git/sdd/progress.md`
- External-CLI delegation (kimi/agy): `.agent/skills/04-tooling/external-agent-delegation/SKILL.md` — read before dispatching.
