# 2026-07-11 tunnel framing round 1 — dedicated face-on camera

First framing pass on the slice-1 tunnel (vault/plans/2026-07-11-tunnel-slice1-plan.md,
"eye-tune round" = frontier #1 in the evening-session2 handover). Implementation dispatched
to `opencode run --model zai-coding-plan/glm-5.2` (prompt: `.agent/run/dispatch/
tunnel-framing-prompt.txt`); lead-reviewed, lead-gated.

## Change

- New `TunnelPresentationBinder.Camera.cs`: binder-owned `TunnelCamera` (child of the mount at
  local `(0,0,TunnelCameraDistance)`, face-on down -Z), `MakeCurrent()` on enable, captured
  previous camera restored on disable AND in `ClearMount` before the mount is freed (ALC-safe).
- Constants: `OuterRadius` 18 -> 10; new `TunnelCameraDistance = 16`, `TunnelCameraFovDeg = 55`,
  all in the single tunable block in `TunnelPresentationBinder.cs`.
- Zero new references, zero contract changes — fully bundle-local, hot-reload-iterable.

## Evidence (live exported app, hot-reload, no restart)

- `before-camera-inside-geometry.png` — pre-change: orbit camera (~6.5 units) inside the
  18-unit annulus; reads as intersecting planes.
- `tunnel-framed-round1.png` — post-reload (ledger 19:55:12 `ok:true` + rebind): globe centered
  in the throat, corridor wedges with filmstrip quads, rung rings labeled `1 kb`/`2 kb`.

## Gate results

- World bundle hot-reloaded into the RUNNING app (PID 58410, no restart) — new framing live.
- Restore path verified behaviorally: after `timeline.tunnel_view {"enabled":false}`,
  `camera.debug` shows the rig camera current again at the original orbit position with
  `PCam_globe_default` active.
- PhantomCamera host does NOT reassert `Current` against `MakeCurrent()` — the open risk from
  the packet review is empirically closed.
- Suite: 18/18 projects, 0 failures, post-change.
- Caveat: the host logs to stdout owned by the original launch terminal, so the literal
  `old ALC collected` line was not capturable this round; reload `ok:true` + next-generation
  binder driving the new camera is the behavioral evidence. The next fresh launch should
  re-capture the line per the bundle-runtime-verification rule.

## Still open (user's eye)

Framing constants are a first pass — the USER judges the look (composition, how much annulus
the 2D timeline overlay may cover, colors, falloff). Iterate via `task bundle:world` +
`task bundle:install`, re-enable with F9 or `timeline.tunnel_view` (enabled state still resets
to hidden on every world reload — known slice-1 residue, deliberately out of this packet).
