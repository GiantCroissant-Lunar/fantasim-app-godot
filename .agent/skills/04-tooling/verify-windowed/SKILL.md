---
name: verify-windowed
description: Verify the intended fantasim-app-godot export by binding UI evidence to an exact worktree, commit, executable path, and PID, then exercising the stable windowed product scene. For collectible plugins, hot-reload the changed PCK and require both visual/interaction proof and the old-ALC-collected log. Use before claiming an exported app works, renders, reloads, or remains usable.
---

# Verify the Exact Windowed Export

## Outcome

A runtime claim is valid only when all three facts refer to the same process:

1. **Identity:** the process executable belongs to the intended repository/worktree and commit.
2. **Lifecycle:** startup/reload logs contain the expected evidence and no fatal error.
3. **Usability:** a fresh post-startup/post-reload screenshot and representative interaction
   succeed in the stable product scene.

Godot's splash/logo, a gray or blank window, and clean console output are intermediate signals.
They do not establish usability.

Use the workspace `unify-build` procedure for builds. Use doubt-driven review when runtime
identity, plugin boundaries, or unload evidence remain uncertain.

## Target Identity Gate — before launch or computer control

This workspace may contain multiple `complete-app.app` exports with the same display name and
bundle identifier. Resolve authority from the user request and active vault plan, never from
window focus, timestamps, search order, or visual similarity.

1. Record the intended checkout and commit:

   ```bash
   REPO="$(git rev-parse --show-toplevel)"
   HEAD="$(git rev-parse HEAD)"
   git worktree list --porcelain
   printf 'repo=%s\nhead=%s\n' "$REPO" "$HEAD"
   ```

2. Resolve the exact built export from this checkout's task configuration; do not copy a path
   or version from another worktree:

   ```bash
   ARTIFACTS_VERSION="$(cd "$REPO" && task --silent version:artifacts)"
   APP="$(cd "$REPO/build/_artifacts/$ARTIFACTS_VERSION/godot/osx/complete-app.app" && pwd -P)"
   EXE="$APP/Contents/MacOS/complete-app"
   BUNDLE_ID="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP/Contents/Info.plist")"
   printf 'artifacts_version=%s\napp=%s\nexe=%s\nbundle_id=%s\n' \
     "$ARTIFACTS_VERSION" "$APP" "$EXE" "$BUNDLE_ID"
   ```

   A missing directory means this checkout has no export at its configured version; build it or
   resolve the intended artifact version explicitly. Do not fall back to another directory.

3. Check candidate processes/exports. If more than one worktree/export matches and the requested
   authority remains unclear, stop and ask the user which checkout is the target. Do not proceed
   with a best guess.

4. Launch the exact executable detached, retaining its PID and log:

   ```bash
   LOG="/tmp/fantasim-windowed-$(date +%s).log"
   nohup "$EXE" >"$LOG" 2>&1 &
   PID=$!
   printf 'pid=%s\nlog=%s\n' "$PID" "$LOG"
   ```

   Add only environment/configuration explicitly required by the active acceptance plan. Do not
   silently change modes (for example, remote versus local) merely to make startup succeed.

5. Prove the PID owns the intended executable before observing its window:

   ```bash
   ACTUAL_EXE="$(lsof -a -p "$PID" -d txt -Fn | sed -n 's/^n//p' | head -n 1)"
   test "$ACTUAL_EXE" = "$EXE"
   ```

   If the path does not match, stop. Close only the wrong process that this agent launched;
   never kill a pre-existing user process because its title or bundle ID matches.

6. Target UI/computer control by the absolute `.app` path where supported. Never target only
   `complete-app`. A bundle ID is acceptable only after proving there is exactly one matching
   process/export. Before every screenshot or interaction, compare the UI tool's reported PID
   with `$PID`; if it cannot report one, confirm the foreground PID independently, for example:

   ```bash
   FRONT_PID="$(osascript -e 'tell application "System Events" to unix id of first application process whose frontmost is true')"
   test "$FRONT_PID" = "$PID"
   ```

Do not continue to lifecycle or visual verification until this identity gate passes.

## Choose Reload or Relaunch

Use the repository's `unify-build` procedure for builds. The verification choice is:

| Change | Required runtime path |
|---|---|
| Existing collectible plugin/bundle: its DLL, scene, manifest, or assets | Keep exact app open; build/install that bundle; verify reload |
| Host/bootstrap composition | Full exported build; relaunch exact executable |
| Shared contracts or shared policy/common assembly | Full exported build; relaunch exact executable |
| T4 seam or Godot-resident integration | Full exported build; relaunch exact executable |
| Native iii bridge | Build bridge and exported app as required; relaunch exact executable |
| New/changed `collectible-bundles.json` registration | Full exported build; relaunch exact executable |

The source of truth for collectible plugins is `collectible-bundles.json`. Do not assume the
only plugins are Stage, Assist, or Timeline, and do not relabel a plugin as resident merely to
avoid the reload path.

## Collectible-Plugin Reload Loop

Keep the identity-proven windowed app open. Repeat steps 2–5 for each change:

```text
1. Exact exported app is running; identity record is complete.
2. Edit an already-registered collectible plugin.
3. Run the repository's task bundle:<tier> target for that plugin.
4. Run task bundle:install so App.Resource observes the PCK change.
5. Verify lifecycle logs and fresh in-window behavior from the same PID.
```

The lifecycle gate requires the expected reload sequence, the `old ALC collected` line for the
superseded context, and no fatal/unhandled error attributable to startup or reload.

## Visual and Interaction Gate

After startup and again after the changed bundle reloads:

1. Bring the identity-proven PID to the foreground and re-check the foreground/UI PID.
2. Wait for splash/loading/transitional visuals to yield to the stable product scene.
3. Take a fresh screenshot and retain its path or tool evidence.
4. Perform the acceptance interaction stated by the active plan or changed feature.
5. Confirm the resulting visible state is usable and expected; capture a second screenshot when
   the interaction changes visible state.
6. Re-check logs for fatal errors produced during the interaction.

For infrastructure work with no new UI, use a harmless, already-documented product interaction
that crosses the affected seam. Simply watching the window remain open is not an interaction.
If no valid interaction is documented or discoverable, report the visual check as inconclusive
and obtain the missing acceptance action; do not claim the app works.

Immediate failures/inconclusive evidence include:

- only a Godot splash, logo/glyph, gray window, blank window, spinner, or crash dialog;
- a screenshot whose PID cannot be tied to the recorded executable;
- an interaction performed before the reload or in a different app instance;
- clean logs without a stable product scene and successful interaction;
- a stable-looking scene with fatal startup/reload/interaction errors in the log.

## Handoff Record

If verification is ongoing or the user wants to inspect the result, leave the verified app open.
Detach it from transient shell sessions if needed. Report all of:

- repository/worktree root and `HEAD`;
- absolute `.app` and executable paths plus bundle identifier;
- verified PID and process-to-executable check;
- log path and relevant lifecycle evidence;
- fresh screenshot evidence after startup/reload;
- representative interaction and observed result;
- whether the process remains open.

Close the app only when verification is explicitly finished or the user asks. State which PID
was closed and why. Never close an unrelated process selected by display-name collision.

## Claim Checklist

- [ ] The authoritative repository/worktree was explicit; ambiguity among duplicate exports was resolved by the user/plan.
- [ ] Repository root, `HEAD`, absolute app/executable path, bundle ID, PID, and log path were recorded.
- [ ] The PID-to-executable check passed, and every UI observation used that same PID.
- [ ] The selected reload/relaunch path matches the changed code's actual plugin/host boundary.
- [ ] Startup/loading completed to the stable product scene in the exported windowed app.
- [ ] A fresh post-startup/post-reload screenshot was captured.
- [ ] A representative in-scope interaction succeeded visibly.
- [ ] Logs contain the expected lifecycle evidence and no fatal error.
- [ ] For hot reload, the changed behavior was exercised and `old ALC collected` appeared.
- [ ] The handoff reports identity, lifecycle, and visual/interaction evidence; the app stays open when inspection continues.
