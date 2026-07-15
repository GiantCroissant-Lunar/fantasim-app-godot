<!-- agents-digest:start -->
## Verify the exact exported Godot app, then hot-reload bundles
**Runtime verification has two mandatory gates: exact process identity and fresh visible usability. Logs alone never prove that the intended exported app works.**
- Before launch or UI control, bind the check to the intended repository/worktree and record its root, `HEAD`, absolute `.app`/executable path, bundle identifier, PID, and log path. Prove the PID's executable with `lsof -a -p "$PID" -d txt -Fn` (or an equivalent process-to-path check).
- If multiple worktrees/exports share the same app name or bundle identifier and the authoritative target is not explicit in the user request or active plan, **stop and ask**. Never choose the newest-looking export, the first search result, or an already-open window by title.
- Launch and control the app by its absolute bundle/executable path. A display name such as `complete-app` is not an identity. A bundle identifier is sufficient only after proving exactly one matching process/export exists. Before every screenshot or interaction, confirm the foreground/UI tool PID is the recorded PID.
- Keep that exported **windowed** app open. For a changed collectible plugin/bundle: `task bundle:<tier>` → `task bundle:install` → wait for App.Resource to reload it → verify the feature in-window **and** confirm the `old ALC collected` log.
- Full build + exact-path relaunch (`task build:godot:desktop` → exact export) is required for bootstrap/host code, shared contracts or policy assemblies, T4 seams, the native iii bridge, or a new `collectible-bundles.json` registration.
- Success requires both: (1) expected lifecycle/error-free logs, and (2) a fresh screenshot after startup/reload plus a representative in-scope interaction in the stable product scene from that same PID. A splash screen, logo/glyph, gray/blank window, crash dialog, or log-only run is failure/inconclusive—not proof.
- If verification is still ongoing, leave the verified app open at handoff and report repository root, `HEAD`, absolute executable, PID, log path, screenshot evidence, and the interaction performed. Never kill a pre-existing user process because its title collided with the target.
- Full procedure: `.agent/skills/04-tooling/verify-windowed/SKILL.md`.
<!-- agents-digest:end -->

# Exported Godot Runtime Verification

`fantasim-app-godot` is a four-tier, bundle-oriented application. Collectible feature
plugins ship as PCK bundles loaded into hot-reloadable `AssemblyLoadContext`s. The normal
feature loop therefore keeps one exported windowed app open and installs a rebuilt bundle
into that same process.

The running window is evidence only after its identity is bound to the requested checkout.
This workspace can contain several worktrees whose exports all appear as `complete-app` and
can share `com.giantcroissant.fantasim`. Neither the visible app name nor the bundle ID alone
distinguishes them. Starting or inspecting whichever match appears first can test the wrong
binary while producing plausible logs and screenshots.

Before any runtime claim:

1. Determine the authoritative repository/worktree from the user's request and active plan.
2. Record its root and `git rev-parse HEAD`.
3. Resolve the export and executable to absolute physical paths.
4. Launch that exact path, record PID and log, and prove the PID maps back to that executable.
5. Before UI observation, prove the UI tool/foreground application PID is that recorded PID.
6. Wait until startup/loading has yielded to the stable product scene. Capture a fresh
   screenshot and execute the acceptance interaction named by the change or plan.

If step 1 is ambiguous because more than one candidate checkout/export exists, do not infer
authority from timestamps, current focus, process age, or apparent visual similarity. Ask the
user which checkout/export is authoritative.

## Evidence is conjunctive

Lifecycle logs prove startup, reload, and ALC collection. A screenshot and interaction prove
the product remained visibly usable. Neither evidence class substitutes for the other:

- a clean log with a blank, gray, splash-only, or frozen window fails the visual gate;
- a plausible window from an unproven PID fails the identity gate;
- a correct startup screenshot taken before the changed bundle reload fails the reload gate;
- an `old ALC collected` line without exercising the changed behavior fails the feature gate.

Use a full build and exact-path relaunch when the change is outside a collectible ALC: host or
bootstrap code, shared contracts/policy, resident seams, native bridge code, or bundle-registry
composition. Use bundle hot-reload only for a plugin already registered as collectible.

Do not close the verified app between iterations. If the user is continuing the runtime
session, leave it open and detach it from transient shell sessions if necessary. Close only an
agent-launched process when verification is explicitly finished or the user requests closure;
never terminate an unrelated pre-existing process based on a matching display name.

The commands, decision table, evidence record, and failure paths live in the
[`verify-windowed`](.agent/skills/04-tooling/verify-windowed/SKILL.md) skill. Companion ALC
containment and unload invariants remain in the `alc-bundle-safety` rule.
