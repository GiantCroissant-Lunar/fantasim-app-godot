# AGENTS.md — fantasim-app-godot agent rules

Entry point for agent rules in this project. `CLAUDE.md` is a thin `@AGENTS.md` pointer;
other CLI agents read `AGENTS.md` directly. This file is **hand-maintained — the source of
truth** — and points to the rules and skills under `.agent/` instead of restating them.

## Docs — `vault/`

Project documentation lives under `vault/` — see [`vault/README.md`](vault/README.md) for the
taxonomy: `architecture/` (evergreen design) · `specs/` (dated concept-lock) · `plans/`
(implementation) · `handover/` (session records). Write new specs/plans/handovers there;
superpowers `writing-plans` / spec output should target `vault/plans` and `vault/specs`
(not `docs/superpowers/`).

## Rules — `.agent/rules/`

- [bundle-hot-reload-verify](.agent/rules/bundle-hot-reload-verify.md) — 4-tier, bundle-oriented:
  verify a feature by **hot-reloading the changed bundle in the already-open exported windowed
  app** (`task run:exported` → edit a tier → `task bundle:<tier>` → `task bundle:install` →
  watch-reload → confirm `old ALC collected`). Full build + re-run only for changes **outside a
  collectible ALC** (resident/host code, T1 contracts, T4 seams, the native iii bridge, a new
  `collectible-bundles.json` registration).
- [no-smoke-or-fake-production-code](.agent/rules/no-smoke-or-fake-production-code.md) — keep
  smoke checks, fake/demo assets, and verification-only harnesses out of production runtime
  composition; use tests/tools/harnesses for proofs, and keep app startup/config tied to real
  product concepts.

## Skills — `.agent/skills/`

Deployed into `.claude/skills/` and `.agents/skills/` by the sync (`task agent:sync:repo --
yokan-projects/fantasim-app-godot` from the workspace root). Use the one matching your task.

- [verify-windowed](.agent/skills/04-tooling/verify-windowed/SKILL.md) — the full hot-reload
  verification loop, the hot-reload-vs-build decision table, and the verification evidence
  (incl. the `old ALC collected` gate).
