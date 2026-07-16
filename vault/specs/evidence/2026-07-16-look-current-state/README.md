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

## Boot-default OS-level test (added same evening, after user challenge)

The user challenged "the tunnel timeline is live and default" — correctly: the capture
sequence had run `timeline.tunnel_view {"enabled":false}` for shot 08 and left it off, so the
real window showed the 2D timeline while the summary claimed tunnel. OS-level verification
(computer-use screenshots of the actual macOS window, not viewport captures) then established:

1. **Claim-time state: user was right.** OS screenshot showed the 2D HUD (Play/Fit,
   `scrubbing : stagnant-lid : 200 ka`).
2. **Tunnel works when enabled**: after `tunnel_view {"enabled":true}`, the OS screenshot
   shows the full tunnel reading (rings, lanes, faceted planet centered).
3. **Fresh relaunch with ZERO tunnel commands** (PID 68442, exe-verified, log
   `/tmp/fantasim-boot-default-test-1784211317.log`): boot log shows THREE failed asserts —
   `Tunnel default-on assert: effective=False, failureReason='tunnel mount unavailable'` —
   then `Tunnel pending default-enable applied at preparation completion: effective=True`.
   So default-on lands only after world preparation, via the out-of-band path.
4. **BUT the boot framing does not READ as a tunnel.** At default zoom the planet fills the
   entire window; rings/lanes are off-screen (only floating ka badges hint at them). The
   tunnel reads as the hero mockup only after ~2× `tunnel_zoom` out. **Defect for the tunnel
   arc: boot framing should present the tunnel reading, not a full-frame planet close-up.**

Discipline this encodes: an "X is on screen" claim is valid only against an OS-level
screenshot taken at claim time with no state changes after it; in-app viewport captures
prove renderability, not what the user sees. Restore any state you toggled for captures.

## Deltas vs the acceptance criterion (registry §criterion)

1. Lumpy silhouette: NOT met anywhere — silhouette clamp still in force (re-derivation item).
2. Bulk everywhere (late mobile-plate): geometry now reads chunky/faceted; amplitude re-tune
   against `mattkeeter-planets-biomes.png` still owed.
3. Every tick / every regime: FAILS at stagnant-lid (no crust surface) — decide the pre-onset
   surface story in the re-derivation.
