# 2026-07-12 asymmetric cockpit tunnel — gate evidence (vendored)

**Result:** PASS
**Design:** [asymmetric cockpit tunnel](../../2026-07-12-asymmetric-cockpit-tunnel-design.md)
**Plan:** [asymmetric cockpit tunnel implementation](../../../plans/2026-07-12-asymmetric-cockpit-tunnel-plan.md)

> Vendored after the fact during the 2026-07-12 review pass — the arc shipped and gated live but its
> evidence had never been copied into the vault (the two prior tunnel arcs were). This record closes
> that durability gap. The **full raw bundle** (23 numbered restore/test/export/reload logs, both
> aspect-ratio screenshots, bundle sha256 checksums, status JSONs) lives untracked in the gate
> worktree `/private/tmp/fantasim-asymmetric-cockpit-gate-src-29cfce5/vault/specs/evidence/2026-07-12-asymmetric-cockpit-tunnel-gate/`;
> only the small high-value proof artifacts are vendored here.

## What shipped

The asymmetric cockpit refinement over the interior-view tunnel: left-of-center focused track,
right-third current planet large at frame center, honest 3D snapshot spheres, exactly two
camera-relative dials, canonical outer time, presentation-only fine inspection, and fail-closed
HUD/F9/reload behavior. Landed on `main` (commits `c83c7ab`…`2827a1a`, plus the follow-on reload
lifecycle fences `f4d3671`, `f4e2c76`, `2c47a01`).

## Evidence (vendored files in this dir)

- `cockpit-16x9.jpg` — tunnel enabled: planet large at center on the current-time plane, both dial
  rings as concentric circles, five track corridors radiating (several honestly labeled
  "preview unavailable"), left-of-center focused-track readout ("Magma Ocean | ka | 0 ticks —
  active at current time"), outer readout "tick 0 | kb", 2D timeline HUD hidden while the activity
  ledger rides along at right.
- `tunnel-enable.json` / `enable-before-world-reload.json` — the enable command results.
- `status-enabled-16x9.json` — full app status snapshot with the tunnel enabled.
- `world-reload-enabled.log` — a live world reload while the tunnel was enabled.
- `world-reload-alc.log` — the ALC lines extracted from the full reload run: it ends with
  `Bundle unloaded: world` → `Bundle loaded: world …` → `Hot-reload: old ALC collected for bundle
  world`, i.e. the enabled-tunnel binder unloaded cleanly with no pin.

Across the whole gate run the bundles collected their old ALCs with **zero `still pinned` lines**:
world ×5, timeline ×3, assist ×2, stage ×1, activity ×1. The five `world` collections cover the
live enabled-tunnel world reloads.

## Known residue at gate time (the eye-tune backlog)

Visible in `cockpit-16x9.jpg` and unchanged by this gate: most corridors read "preview unavailable"
(filmstrip content density), the dial rings render in front of the planet limb (a deliberate
legibility bet), and stray floating `+0.68 kb` / `+0.22 kb` readouts sit mid-frame. These are the
user's eye-judgment items, not gate failures.
