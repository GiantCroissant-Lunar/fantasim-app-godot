# Tunnel visual slice 1 — corridor headers, rings-on-tunnel, planet zoom

> **Status:** SHIPPED 2026-07-13 (commits `e93e6e7` A+C, `1b65021` B), unit-gated + live-verified on
> a fresh export; framing eye-tuned with the user over two rounds. Design dialogue is in the session
> transcript. Final Part B framing: camera `(-0.25, 0.25, 2.9)` → target `(0,0,-6)`, FOV 74, rings
> inner 1.70–1.90 / outer 2.20–2.45 (axis-centered, mount-parented), lens X 2.85. **Owed:** user
> real-mouse pass (ring gestures at the new positions; planet scroll-zoom feel) + final eye-judgment.

Iteration over the shipped asymmetric-cockpit tunnel. Five user directives were triaged; this slice
is the first three (independent visual wins). Deferred to later slices: **Slice 2** node-graph panel
on track-toggle (reuse the dormant `NodeGraphViewSource` standalone panel + thread the generation-
family provider across the world/timeline bundle seam); **Slice 3** stream branching as a **Y-fork to
an offset sub-tube** (data-model-first: needs a branch-lineage type with parent + fork-tick, synthetic
multi-branch emission, and tunnel keying that reads `StreamId.Branch` instead of collapsing on
`(SphereId, LayerId)`).

## Part A — corridor headers like the normal timeline track

- **Now:** each corridor has one billboard `Label3D` showing only `Descriptor.DisplayName`
  (`TunnelPresentationBinder.Corridors.cs:192`). The 2D track header is a chevron + name + active
  styling; it deliberately shows only the name.
- **Change:** structured per-corridor header = friendly name + a rung/state sub-line, with
  active/inactive conveyed by the header backing color (mirrors the 2D style-swap). Hold the chevron
  affordance for Slice 2 (it toggles the graph panel — no dead affordance now).
- **Pure/testable:** a `TunnelCorridorHeader` formatter (name, rung symbol, active label) in the
  seam, TDD'd against descriptor edge cases (null rung, archived-not-present, inactive). Rendering is
  Godot-side (hot-reload).

## Part B — outer/inner rings on the tunnel, not one track

- **Now:** rings are a sub-unit dial (radii 0.38–0.82) parented to `TunnelCamera` at camera-local
  `(-2.2, 0, -4)` — reads as one track's instrument (`TunnelPresentationBinder.Rings.cs:46`,
  `TunnelCameraFraming.cs:47-51`).
- **Change:** axis-concentric rings at ~tunnel radius, parented to the **mount**, on a ring plane in
  front of the interior camera, encircling the throat. Ring hit-testing + pointer angle move from the
  instrument plane to that mount-local ring plane (both angles then about the tunnel axis, same frame
  as the wall carousel).
- **Eye-tune (owed to user):** full-size rings may fight the near-axial interior camera; camera
  distance / FOV / ring-plane-Z are hot-reloadable knobs to frame both rings + the throat.
- **Guard:** must not regress the gated input hardening (wall-axis angle, planet occlusion,
  fail-closed relay, ALC-clean teardown).

## Part C — planet zoom in/out

- **Now:** planet radius (2.06) and camera FOV/pose are compile-time constants; `TryAlignToPlanetBody`
  only translates the mount, never scales; no zoom seam.
- **Change:** a planet-scale knob applied to the shared `PlanetBody`, **captured on enable and
  restored on disable/teardown** (same discipline as the camera capture/restore). Input: scroll-wheel
  (live) + a `timeline.tunnel_zoom`-style command (headless/testable). Scale clamped to a sane range.
- **Pure/testable:** the zoom-accumulate-and-clamp step in the seam, TDD'd (bounds, step, reset).

## Acceptance

- Unit: new seam helpers green; full suite stays green; input-hardening tests unchanged.
- Live (fresh export — Part A/C seam helpers are resident): headers legible per corridor; rings read
  as encircling the tunnel (user eye); planet scales smaller/larger via scroll + command and restores
  on disable; a live enabled world reload still collects the old world ALC with no pin.
- Real-mouse: ring gestures still own correctly (no globe-orbit leak), wall carousel unaffected.
