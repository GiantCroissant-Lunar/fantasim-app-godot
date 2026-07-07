# 2026-07-07 session handover — GPlates arc, mantle look-loop, calibration (waves 1–3)

**For the next session (fresh context). READ ORDER:** this doc → the two governing docs:
`vault/plans/2026-07-07-gplates-truth-playback-and-viewport-systems.md` (all packets P1–P10,
waves 1–3) and `vault/specs/2026-07-07-mantle-xray-exploded-crust-references.md` (the three
visual references, the METHOD-LOCKED v2 volumetric field, and the NORTH-STAR EYE GATE).
Yesterday's context if needed: `vault/handover/2026-07-06-attempt8-p0-p2-execution-handover.md`.

**Standing user rules (verbatim intent):** provider routing is the USER's call per dispatch
("don't get cute") — "agent cli zai" = opencode `zai-coding-plan/glm-5.2` (5-h rolling quota;
mid-window exhaustion = silent multi-hour hang: watch log mtime, kill by EXACT exe path,
salvage); "codex cli gpt 5.5 high" = `codex exec -m gpt-5.5 -c 'model_reasoning_effort="high"'
-s workspace-write` (sandbox has NO network/sockets — it cannot run tests; lead verifies);
"sub agent (opus)" = in-house Agent tool. **The mantle view matching reference 2 is FIRST
PRIORITY, judged by eyes — MINE first as demanding critic, the user's as final gate.**

## 1. What landed today (all pushed)

| Where | Commits (headline) | What |
|---|---|---|
| engine `attempt-8/main` | `657bc18`, `4be11ab`, incl-list fix, `5f6382d` | P1 rot-importer→stream + GPML polygon import/rasterizer; P2 basal `PlateHistoryForcingSource`; **`MantleAnomalyField`** volumetric T'(dir,r,tick) (opus); packed **0.1.9 then 0.1.10** |
| carto `attempt-8/main` | `da8e254` | P7 NaN-height guard (was the "holes in globe" defect); app consumes via ProjectReference — live |
| app `main` | `b7fe0f6` … `292936b` (~15 commits) | P3 rotation-source seam + per-tick light path; P4 graph gate + ViewMountLayout; P4b regime layer-gen nodes (parity-tested); P5 timeline Play fix; P6 camera composed + P8 lazy-bind + P9 pending-configure + camera-steal fixes; M-B exploded crust (`render.exploded`, windowed-verified); M-A mantle x-ray (`render.mantle`) + look iterations (ghost shell, thin-sheet field 0.03R, **grid 88**, L1 quiet filament arcs + cold halo, L2 `camera.orbit` command); all-ocean fix `4bcdd27`; **1 Gy window** (`MobilePlateWindowTicks`); PhantomCameraManager autoload; **P10 rate-calibration tools + report** `292936b` |

Windowed-verified today: exploded crust (3 factors, screenshots), mantle x-ray front view
(user saw iteration 2 + final L1 frame), per-tick smooth drift (3.4%/0.2 Ma), 1 Gy sweep GIF
(41 frames, delivered), Play-fix chain headless+suite.

## 2. IMMEDIATE NEXT ACTIONS (both fully diagnosed, small)

1. **Apply the rate calibration.** `project/plugins/App.World.Composition/OnsetRoster.cs:29`
   `DefaultAngularDriftPerMegaAnnum: 0.02 → 0.0035` rad/Ma (real-plate median; current value
   = real p90 → ~5.7–7× too fast as default; keep ~0.017 as "lively upper" option). Evidence:
   `tools/rates/2026-07-07-rate-calibration-report.md` (quaternion stage analysis of the real
   Cao 2024 rot files; selftest exact). Then re-run the 1 Gy sweep (recipe §4) for the
   calibrated GIF — expect stately drift, no taffy-smearing. NOTE: engine consumes the derived
   AngularDriftPerTick in `ConvectionCenters.cs:69`; MotionGate tests may have rate
   assumptions — run `task test` and re-eye the drift GIF.
2. **Fix the last camera link.** State proven by log+pixels: follow ENGAGES, rig `Camera3D`
   IS Current — but it sits at the ORIGIN (screenshot renders from inside the orange basal
   blanket). The phantom camera never moves the real camera. Fix in
   `project/plugins/App.Camera.Seam/CameraRig.cs` `EnsureViewportRig`: the phantom-camera
   addon requires **PhantomCameraHost to be a CHILD of the Camera3D it drives** — currently
   the host node is parented elsewhere (rigRoot). Re-parent host under the camera; verify the
   pcam is visible to that host and per-frame follow updates run. Then windowed:
   `camera.orbit {"yawDeg":80,"pitchDeg":-15}` MUST change pixels; then the **edge-on
   curtain-vs-lobe verdict** (eye-gate criterion 1) finally happens.

## 3. The open ledger after those two

- Mantle look remaining (spec eye-gate): palette pass (hotter plume cores), slab curtain
  verdict → possible field tune round 2, criterion 6 (time sweep with mantle active — slabs
  must visibly deepen; never tested), composed money shot (`render.exploded` over the mantle
  interior — inner-sphere reveal).
- Track B (motion real): thread `rotationSource` through
  `WorldGenerationGraphRunner.ToGenerationRequest` (P3 seam is unit-complete, not
  app-reachable) → then REAL Cao playback in-app (rot files already in
  `tools/rates/data/`, gitignored). v2 supersampled rasterizer for fractional coastlines.
- M-B polish: wall lighting + thickness-exaggeration knob. M-C traction feedback (own gated
  slice; contract fields exist). Default-view decision (World view still the static locked
  ball). Ingress loud-failures (silent select_layer/seek rejections bit us twice). Crosscut
  console has NO log-level knob (floor Information — P8's debug diagnostics were bumped to
  info: consider downgrade + knob). `PlanetPresentationBinder.cs` ~2000 LOC — split before it
  becomes the next hazard.

## 4. Drive recipes (proven today)

- Launch: `cd fantasim-app-godot && remote__enabled=true build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/complete-app &`
- **Seek INTO the window FIRST** (`timeline.seek {"tick":107000000}`), THEN
  `timeline.select_layer` — selecting an inactive layer FAILS SILENTLY (this artifact faked
  the "inert light path" defect for a whole gate cycle).
- `render.mantle {"enabled":true}` → wait ~60 s at grid 88 → `render.screenshot`.
  `render.exploded {"factor":0..1}`. `camera.orbit {"yawDeg","pitchDeg","distance"}`.
- Kill ONLY by exact path: `pkill -f "MacOS/complete-app"` (a bare pattern once killed a
  codex agent whose PROMPT contained the string).
- Full re-export after resident changes: `task build:godot:desktop && task bundles && task
  bundle:install`. Bundle-only change (App.World*, App.World.Composition):
  `task bundle:world && task bundle:install` + relaunch.
- 1 Gy sweep GIF: seek 100M→1100M step 25M, screenshot each, `magick ... -crop
  1000x1000+1600+460 +repage -resize 640x640 -set delay 18 -loop 0 out.gif`.

## 5. Gotchas (new this session — the 2026-07-06 G1–G8 still stand)

- **G9 cwd drift:** background/subsequent shells land in stale dirs (workspace root or the
  LAST worktree cd'd into). Two ops landed inside the codex worktree (an edit + a patch
  apply). USE ABSOLUTE PATHS for every repo op; verify `pwd` before task/git.
- **G10 `grep -c` exits 1 on zero matches** → kills `&&` chains after a CLEAN build. Don't
  gate chains on it.
- **G11 keep-both conflict merges:** naive ours+theirs concatenation on `git apply --3way`
  conflicts SPLICES METHODS (three chimeras hand-repaired in the binder/RenderComposition).
  Resolve conflict-by-conflict reading both originals; verify with a brace-depth scan +
  build before testing.
- **G12 sequential `git apply --3way` in one shell loop** silently rolls back some patches —
  apply ONE per invocation, check `git status` between.
- **G13 marching-cubes lattice floor:** an isosurface cannot show features thinner than the
  sampling cell (0.036R cells blurred 0.03R sheets into lobes). Grid 88 ≈ 0.023R now; if
  curtains still lobe at edge-on, tune field vs grid TOGETHER.
- **G14 Godot `GeometryInstance3D.Transparency`** does not reliably ghost custom-shader
  surfaces in the export — hide + dedicated ghost shell instead.
- **G15 vendored-addon autoloads:** enabling a plugin by hand-editing `project.godot` does
  NOT register its autoload (editor does that) — PhantomCameraManager had to be added
  manually to `[autoload]`.
- **G16 worktree hygiene:** engine/carto test suites have a path-resolution flake INSIDE
  worktrees (CrustViz walks up for a dir literally named `fantasim-world`) — final
  verification runs in main checkouts. ~13 worktrees exist under `yokan-projects/.worktrees/`
  — all integrated; safe to `git worktree remove` after harvesting any AGENT-SUMMARY.md you
  want (P10's report is already harvested into `tools/rates/`).

## 6. Dispatch state

No agents running at session end. All seven wave-1 packets + M-A/M-B + P8/P9/P10 + L1/L2
integrated and pushed. `.agent/run/dispatch/` holds all prompts (gitignored, session-local);
`.agent/logs/{opencode,codex}/` holds run logs. Memory file
`fantasim-gplates-packet-arc` (agent memory dir) carries the same resume state condensed.
