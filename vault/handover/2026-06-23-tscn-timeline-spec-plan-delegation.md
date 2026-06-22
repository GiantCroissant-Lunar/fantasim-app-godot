# Session record — .tscn-native timeline: design → spec → plan, via external-CLI delegation

> **Date:** 2026-06-23 · **Repos:** `fantasim-app-godot` (spec + plan + this handover), `fantasim-world` /
> `fantasim-cartography` (gitignore), workspace root (`AGENTS.md` / `CLAUDE.md`), `.agent/skills`
> (delegation skill) · **Result:** the `.tscn`-native time-advancement timeline is fully **DESIGNED**
> (spec on `main`) and **PLANNED** (verified 11-task TDD plan), and the external-agent-delegation
> workflow (`agy` / `kimi`) is wired + corrected. **No app code changed yet — the next session EXECUTES the plan.**

## TL;DR — the arc

Began as a re-orientation ("I'm completely lost") and became: build the *real* time-advancement timeline.
Brainstormed → a **Godot-native `.tscn` timeline** that replaces the `HSlider`, the trackless/"fake"
`AnimationPlayer`, and the Plan 5a boom-hud face, shipped in the hot-reloadable `timeline` bundle. Resolved
the deep design questions (cross-sphere conditioning, stream-id ↔ timeline, the canonical-time ladder,
variant/branch selector), then **wrote → citation-verified → committed → merged the spec**, and produced a
**verified 11-task implementation plan** — with the citation-check *and* the plan-drafting done by
**dispatching to the `agy` external CLI** (and the delegation skill corrected along the way).

## What shipped (all on `main`)

### `fantasim-app-godot`
- **Spec** — `vault/specs/2026-06-22-tscn-timeline-time-advancement-design.md` (merged @ `cfa37c4`). 13
  sections: native `.tscn` timeline in the `timeline` bundle; `AnimationPlayer` master **CT** value-track +
  `AnimationTree` (idle/playing/scrub); multi-lane model (**lane = sphere/`domain`, track = layer/`M`,
  section = regime**; a track's address = the truth-stream key `variant:branch:L:domain:model`); CT labels
  via the **odometer ladder** (`ka`/`kb`, never `Ma`/`Ga`) through `CanonicalDisplayFormatter`; cross-sphere
  **emergent** gate boundaries (Option A); controller-seam flip (bundle pushes ticks to resident
  `GlobeView`); colours; retire `HSlider` + boom-hud `TimelineViewSource` + `RegimeTimelineTransport` loop;
  variant/branch selector = documented fast-follow.
- **Plan** — `vault/plans/2026-06-23-tscn-timeline.md`. 11 TDD tasks, real C# + exact commands, **verified
  against the live code**: native-scene mount via the `stage` bundle precedent (`ISceneActivator` /
  `entryScene` / `scene-tier`); boom-hud retired (`git rm TimelineViewSource.cs`); `AddNode` `Vector2`
  positions; ASCII-only; real Taskfile targets (`bundle:timeline`, `bundle:install`, `run:exported`).

### workspace + tooling
- **`AGENTS.md` + `CLAUDE.md`** (workspace root) front-load the **external-agent-delegation** skill so it
  isn't missed. `CLAUDE.md` is **generated** from `AGENTS.md` via `.agent/scripts/sync.py` — edit `AGENTS.md`,
  not `CLAUDE.md`.
- **`.agent/skills/04-tooling/external-agent-delegation/SKILL.md`** — corrected + docs-backed kimi usage
  (and the `agy` template confirmed).

### gitignore (separate commits)
- `fantasim-world` `6fe1348` (ignore `.omo/`), `fantasim-cartography` `64cb100` (ignore `.nuke/temp/`).

## ⚠️ Durable findings — the external-CLI delegation workflow (the reusable bit)

- **READ `.agent/skills/04-tooling/external-agent-delegation/SKILL.md` BEFORE dispatching to any CLI.**
  Improvising hangs: a backgrounded `agy -p` with no `< /dev/null` and no `--model` pin **hung 6.5 h**.
- **`agy` (Antigravity / Gemini)** is the workhorse. Template:
  `agy -p "$(cat brief)" --model gemini-3.5-flash --dangerously-skip-permissions --print-timeout 30m > log 2>&1 < /dev/null`.
  Output lands in the conversation transcript / a file it writes — instruct it to **write to a known path**,
  don't trust the pipe.
- **`kimi` (0.18.0)** works headless via plain `kimi -p "..." --output-format text > log 2>&1 < /dev/null` —
  `-p` auto-runs tools under the `auto` policy; `--yolo`/`--auto` are interactive-only and **error** with
  `-p`; `--quiet`/`-w` are gone (use `--add-dir`). Docs: moonshotai.github.io/kimi-code.
- **Verify the CLI's output against ground truth — that is where the value is.** agy's citation pass caught
  3 real spec errors (`geosphere.plates`→`plate`, `atmosphere.genesis`→`bulk`/`coupled`, cross-repo doc
  links); agy's first plan draft wired the timeline through boom-hud — caught + corrected against the real
  `stage` bundle before it became the plan.

## Verification

- Spec citations independently verified by `agy` → 3 errors fixed; report at
  `.agent/logs/agy/spec-citation-findings.md`.
- Plan checked against live code (native-mount present, boom-hud retired, `AddNode` positions, ASCII,
  Taskfile targets all real).
- **No app code changed; nothing to build/test yet.**

## NEXT — start the new session here

1. **EXECUTE the plan** — `vault/plans/2026-06-23-tscn-timeline.md`. REQUIRED SUB-SKILL:
   `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans`. Task-by-task,
   commit per task, and **windowed-verify in the EXPORTED app** as the Godot gate (the only gate that
   exercises the seam + hot-reload). The parallel pure-C# tasks (e.g. the odometer-label unit test) are good
   `agy` delegation candidates — read the delegation skill first.
2. After it works: the deferred **render polish** (boundary-type terrain, magma glow) from Plan 5 still
   stands — that's the gap between "tectonics correct" and "looks like a world".

## Pointers

- Spec: `vault/specs/2026-06-22-tscn-timeline-time-advancement-design.md`
- Plan: `vault/plans/2026-06-23-tscn-timeline.md`
- Delegation skill: `.agent/skills/04-tooling/external-agent-delegation/SKILL.md`
- Engine doctrine: `fantasim-world/vault/architecture/planet-stack-model.md`, `canonical-foundation.md`
- Predecessor handover: `vault/handover/2026-06-22-timeline-face.md`
