# 2026-07-12 tunnel interior view gate — planet large at center + HUD ownership

**Result:** PASS  
**Design:** [rotating-tunnel design §4a amendment](../../2026-07-12-rotating-tunnel-two-ring-prototype-design.md)
(user eye verdict superseding the round-one spectator-oblique camera / far-throat globe)

Two coordinated changes, gated together in a FRESH export (the `ITimelineFace` contract change is
resident — hot-reload could not carry it):

1. **Interior framing (lead session, 3 hot-reload eye rounds before the rebuild):** camera moved
   inside the mouth on-axis (`TunnelCameraFraming`: pos `(0, 0.6, 2.2)`, target `(0, 0, -5)`,
   FOV 60); globe re-anchored from the far throat to the current-tick plane (`GlobePlaneZ = -5`,
   was `-TunnelDepth`); tunnel resized for interior legibility (`TunnelRadius` 8→5,
   `TunnelDepth` 14→20); dial rings pulled onto a plane between camera and globe
   (`RingPlaneZ = -3`, radii 1.7–2.9, was 8.15–10 on the mouth plane); ring hit-testing and
   pointer-angle projection moved to the same plane (`TryProjectToRingPlane*`).
2. **HUD ownership (sonnet subagent packet + lead contract change):** new
   `[CrossDelegate] ITimelineFace.SetHudVisible(bool)` implemented by the resident `TimelineFace`
   (thread-marshaled); `timeline.tunnel_view` handler hides/shows the 2D HUD from the tunnel's
   EFFECTIVE state; compose re-applies visibility on every world rebind; F9 now routes through
   the command (`RouteTunnelToggleThroughCommand`, App.Command contracts edge verified
   `--check-dual` clean: "no dual copies") with a logged direct-SetEnabled fallback.

## Evidence (fresh export, stdout captured)

- `interior-view-hud-hidden.png` — tunnel enabled: planet LARGE at center, both dial rings as
  full concentric circles, five track corridors radiating with content quads, focused-track
  readout at bottom-center, **2D HUD hidden**.
- `after-world-reload-hud-restored.png` — world bundle reloaded WHILE the tunnel was enabled:
  tunnel reset to hidden (known slice-1 residue), orbit camera restored, **2D HUD returned**
  via the compose re-apply path; ledger shows reload `ok: true`.
- ALC line captured verbatim from the fresh launch's stdout (missing from the two prior rounds
  because the old process's stdout was orphaned):

  ```
  info: BundleHost[0]
        Hot-reload: old ALC collected for bundle world
  ```

- Post-reload round-trip: enable → disable on the NEXT binder generation works; `camera.debug`
  shows `PCam_globe_default` active again after disable; HUD panel pixels present at the lane
  rows after restore.
- Suite: 18/18 test projects green (framing regression rewritten to pin the §4a interior
  contract: near-axial, planet ≥35% frame height centered, outer dial fully in frame,
  projected aspect ~1). One non-reproducing single-test blip in App.World.Tests during one
  aggregate run (566/566 on isolated rerun and on two subsequent aggregate runs; world tests
  do not consume the changed contracts).

## Still the user's eye

Ring dials currently render IN FRONT of the planet's limb (legibility bet); "fully behind" or
"HUD-like always-on-top" are one-constant alternatives. Corridor content density, colors, label
typography, and the stray mid-left label remain the eye-tune backlog.
