# Plan: tunnel timeline becomes the default time surface (directive 1)

**Source directive:** `vault/specs/2026-07-16-layer-first-presentation-directives.md` §1
(user, 2026-07-16): tunnel timeline view is the DEFAULT; the lane/animation timeline is
hidden; "no matter what time we are, we should use tunnel timeline."

**Design decisions (locked for this slice):**
1. The tunnel presentation is ENABLED BY DEFAULT at startup — the app boots into the tunnel
   time surface at the current tick, with no command required, in every regime.
2. The lane timeline face is NOT deleted. It already hides whenever the tunnel is effective
   (`TimelinePlugin.cs:177-178` couples `TimelineHudState` to `!tunnelEffective`); with
   tunnel default-on it is therefore hidden by default. It remains reachable by explicitly
   disabling the tunnel (`timeline.tunnel_view {"enabled":false}`) — that is the developer
   escape; no separate config gate in this slice.
3. Flying-is-scrubbing stays the primary time interaction (spline-tunnel doctrine). This
   slice adds NO new transport UI. If the tunnel HUD currently exposes no Play control, that
   is acceptable for this slice: `timeline.play` via ingress and the lane face (when tunnel
   explicitly disabled) remain the transport paths. Record what exists in AGENT-SUMMARY.md.
4. Every existing `timeline.*` ingress command keeps working unchanged (drive recipes and
   remote procedures depend on them).

**Known trap (must be tested):** `TimelinePlugin.cs:225` — world bundle reloads reset the
tunnel binder to disabled (slice-1 residue). Default-on must SURVIVE a world reload: after a
reload the tunnel must re-assert enabled without user action. Write the failing test first.

**Code anchors (verified 2026-07-16):**
- `project/plugins/App.Timeline/TimelinePlugin.cs` — owns `timeline.tunnel_view`
  (`TunnelViewCommandId`, :33), computes `tunnelEffective` + `TimelineHudState` (:177-178),
  reload-reset residue comment (:225).
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.*` — tunnel binder.
- `project/plugins/App.Timeline.Seam/TimelineFace.cs` (+ `.Lanes.cs`) — the lane face.
- Existing tests around these live in the corresponding `project/tests/*` projects — match
  their style; the tunnel slice-1 plan (`vault/plans/2026-07-11-tunnel-slice1-plan.md`) shows
  the prior task structure.

**TDD order:**
1. Failing test: fresh plugin composition → tunnel presentation effective by default; HUD
   state hidden (lane face suppressed) without any command.
2. Failing test: after simulated world-reload/compose cycle (the :225 path), tunnel remains
   (or returns to) effective without an explicit command.
3. Failing test: `timeline.tunnel_view {"enabled":false}` still disables (escape hatch) and
   the HUD state flips visible; re-enable restores.
4. Implement the minimal default-on (initial state + reload re-assert), make tests green.
5. Full suite for the affected test projects green.

**Out of scope:** new transport/HUD controls; tunnel visual changes; corridor/frame work;
branch/fork geometry; any engine or contract (T1) change; `project.godot` changes.

**Acceptance (agent-verifiable):** all new + existing tests green via `dotnet test` on the
affected solution/projects; `git status` shows only intended source/test files changed.
**Acceptance (lead-only, after review):** fresh export boots directly into the tunnel view at
the current tick (windowed, verify-windowed procedure); `timeline.seek` + `timeline.play`
still function via ingress; disabling tunnel shows the lane face.

**Agent operating constraints:** work ONLY in the assigned worktree; NO commits, NO pushes;
do NOT run export/bundle/install tasks (a user session may hold the exported app open); do
NOT modify `project.godot`, export artifacts, or anything under `vault/`; leave findings +
file list in `AGENT-SUMMARY.md` at the worktree root.
