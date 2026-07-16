# 2026-07-16 look current-state renders — born-rough export, pre-re-derivation baseline

The FIRST windowed renders of the born-rough crust (`b12ed79`), captured the same evening,
as the factual baseline for the north-star re-derivation session. NOT a pass/fail gate —
amplitudes are still the OLD (pre-re-derivation) budgets and the user's eye has not judged.
Judge against the binding registry: `vault/reference/README.md`.

## Identity (verify-windowed)

- Repo `fantasim-app-godot`, HEAD `06a0c61` (docs commit atop born-rough `b12ed79`).
- Export: `build/_artifacts/0.1.2/godot/osx/complete-app.app`, rebuilt 21:51 (post-b12ed79);
  full pipeline `task build` → `build:godot:desktop` → `bundles` → `bundle:install`.
- Launched with `remote__enabled=true`; PID 66844 verified owning the exact exe via
  `lsof -p … -d txt`; log `/tmp/fantasim-windowed-1784210016.log`, 0 fatal/unhandled;
  ingress `/health` ok, 19 commands. A stale pre-rebuild instance (PID 49401, old code in
  memory, holding :19292) was closed first.
- Driven via `tools/fantasim-cmd.py`: `timeline.seek` (incl. `origin:scrubCommit`),
  `timeline.select_layer`, `timeline.tunnel_view/zoom`, `camera.orbit`, `render.screenshot`.

## Shots

| File | State | Reading |
|---|---|---|
| `01-boot-tunnel-tick0.png` | boot, tick 0, magma-ocean, tunnel default-on | Molten convection surface renders; labels + activity ledger live. |
| `03-world-assembled-late-tick.png` | tick 100M, world view, seek WITHOUT commit | **Gotcha exhibit:** HUD reads `scrubbing : mobile-plate : 1 kb` — an uncommitted seek leaves the 1-kb scrub-preview product on screen (smeared ball). Any look judgment on an uncommitted seek is invalid; use `origin:scrubCommit`. |
| `06-crust-whole-globe.png` | tick 100M committed, crust layer, distance 7 | **Born-rough visible:** chunky faceted relief, banded belts — Keeter-family GEOMETRY reading at last. Silhouette still a perfect circle (0.5%R clamp still law). |
| `07-tunnel-timeline-wide.png` | tick 100M, tunnel view, zoomed out | The tunnel timeline product shot: planet centered, lanes (Coupled Climate/Crust/Magma Ocean/Mantle/Plate) with ka badges, playhead rings, vertical-exaggeration readout. |
| `08-young-world-tick20M.png` | tick 20M committed, stagnant-lid | **The open failure, proven live:** stagnant-lid still renders the smooth eggshell — no crust surface materializes pre-onset, so born-rough cannot show. The user's "bulk everywhere at EVERY tick" criterion fails in this regime today. |

## Deltas vs the acceptance criterion (registry §criterion)

1. Lumpy silhouette: NOT met anywhere — silhouette clamp still in force (re-derivation item).
2. Bulk everywhere (late mobile-plate): geometry now reads chunky/faceted; amplitude re-tune
   against `mattkeeter-planets-biomes.png` still owed.
3. Every tick / every regime: FAILS at stagnant-lid (no crust surface) — decide the pre-onset
   surface story in the re-derivation.
