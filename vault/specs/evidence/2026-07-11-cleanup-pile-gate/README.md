# Cleanup-pile gate evidence — 2026-07-11 (evening session)

Windowed gate for the G34 double-full-bind dedupe (`8f28cdd`), run against the live
exported app (ingress :19292) via world-bundle hot-reload (`task bundle:world` +
`task bundle:install`).

## Repro (pre-fix, same boot)

`timeline.seek` into a fresh window (t=180M, window 36): **2 identical binds**
(`triangles=11616` ×2) — the generation-completion chase re-bound an identical surface.
Seek into an already-generated window (t=120M): 1 bind (no chase — no generation).

## Post-fix probe

- seek t=185M (window 37): binds delta **1**, plus
  `Planet surface re-bind skipped at t=185000000: content stamp unchanged (generation-completion echo).`
- seek t=190M (window 38): binds delta **1**, plus the same skip line at t=190000000.
- `old ALC collected` count went 7 → 14 across the hot-reload install — no pin.
- Timeline metadata (UpdateFrom, snapshot-lane states) still applies on skipped refreshes.

Full suite after the change: 1,146 passed / 0 failed (18 test projects).

See `gate-log-excerpt.txt` for the raw lines.
