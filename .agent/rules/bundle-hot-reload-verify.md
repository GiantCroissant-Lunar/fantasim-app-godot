<!-- agents-digest:start -->
## Verify via bundle hot-reload in the open windowed app
**fantasim-app-godot default (4-tier, bundle-oriented): keep the exported WINDOWED app open and hot-reload the changed bundle to verify a feature — build + re-run only when you must.**
- Loop: `task run:exported` stays open (windowed + console, **never headless**) → edit a collectible tier (App.Stage / App.Assist / App.Timeline) → `task bundle:<tier>` → `task bundle:install` → the App.Resource watcher hot-reloads the PCK → verify in-window AND confirm the `old ALC collected` log.
- Full build + re-run (`task build:godot:desktop` → `task run:exported`) ONLY for changes **outside a collectible ALC**: resident/host code, T1 contracts, T4 seams, the native iii bridge, or a new `collectible-bundles.json` registration.
- A hot-reload/unload claim is valid only in the WINDOWED app (not headless) AND with the `old ALC collected` line.
- If runtime verification is still ongoing, or the user explicitly wants to keep inspecting the app, leave the exported app running at handoff/final and report its PID + log path instead of closing it. If Codex exec-session cleanup is a concern, launch/detach the app so no tool session remains open; never kill the app merely to satisfy cleanup.
- Procedure + decision table: `.agent/skills/04-tooling/verify-windowed/SKILL.md`.
<!-- agents-digest:end -->

# Bundle Hot-Reload Verification

`fantasim-app-godot` is a **4-tier service architecture** (T1 contracts · T2 services ·
T3 orchestrators · T4 seams/hosts) with a **bundle-oriented** flow: feature work ships in
collectible PCK bundles (App.Stage, App.Assist, App.Timeline) loaded into hot-reloadable
`AssemblyLoadContext`s.

**The default verification path is hot-reload, not rebuild.** Keep the exported windowed
app open and reload the changed bundle into it; only do a full build + re-run when the
change lives outside a collectible ALC.

Do not close the exported app between verification iterations. If the user is continuing
the runtime session or asks to keep the app open, leave it running at handoff/final and
report the PID plus log path. Close it only when the verification session is explicitly
finished or the user asks you to stop it.

Codex-specific cleanup rule: do not reinterpret "do not end with an unmanaged exec session"
as permission to close the Godot app. If the app was started from an attached `exec_command`
session and needs to remain available, switch to a detached launch pattern before final
(`nohup ... > "$LOG" 2>&1 & echo $!` or an equivalent project task that returns), then
report the PID and log path. The running exported app is part of the verification state.

The step-by-step loop, the hot-reload-vs-build decision table, and the verification
evidence live in the [`verify-windowed`](.agent/skills/04-tooling/verify-windowed/SKILL.md)
skill — read it before verifying. (Companion bundle-safety invariants — Godot-type
containment, no leaked resident refs, the `old ALC collected` standard — are the
`alc-bundle-safety` rule.)
