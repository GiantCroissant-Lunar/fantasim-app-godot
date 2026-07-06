# Timeline Ladder, Plate Focus, and Node Graph Polish Implementation Plan

> **AUDIT (2026-07-06, code-verified):** COMPLETED with drift — ladder zoom + PlateBoundaryFocusRenderer live but relocated to App.Presentation. _(See the authority index in `vault/README.md`.)_


> **For agentic workers:** Implement this plan task-by-task. Prefer bounded external-agent tasks; the lead session reviews and verifies the integrated result. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the visible world slice match the current product direction: timeline track zoom uses the canonical odometer ladder, plate remains the source of truth over unify-cell substrate, the focused plate layer shows boundary process visuals, and the node graph reads as an authored graph instead of raw boxes.

**Architecture:** Keep truth in world/plate data and use view state to choose presentation. Timeline zoom is a temporal viewport ladder (`jw`, `jx`, `jy`, `jz`, `ka`, `kb`, `kc`, `kd`, ...); unify-cell remains spatial substrate/LOD and is not the plate truth. Plate focus rendering derives convergent/divergent/transform overlays from real globe cells and plate motions in the resident presentation binder, without adding fake runtime data.

**Tech Stack:** Godot.NET.Sdk 4.7.0, .NET 8.0, BoomHud runtime 0.1.18, UnifyCell/UnifyGeometry/UnifyMaths 1.0.0, FantaSim world packages 0.1.5-0.1.6.

## Global Constraints

- Do not add smoke, fake, demo, placeholder, or proof-only runtime code.
- Do not reset, stash, checkout, or revert shared dirty worktree changes.
- Keep production changes backed by real domain data, real project assets, or real integrations.
- Prefer hot reload verification in the open exported app; use full build/re-run only for resident host or contract changes.
- No new per-UI or per-layer seam. Use generic contracts/view sources where possible.
- "Sea without sea" means exposed low terrain, basin, or young-crust color. Do not add a water surface shader in this slice.
- No TDD loop for this exploratory slice. Existing tests may be adjusted only when a real contract/model change requires it.

---

### Task 1: Timeline Odometer Ladder Zoom

**Files:**
- Modify: `project/plugins/App.Timeline/TimelineModel.cs`
- Modify: `project/plugins/App.Timeline/TimelineTimeFormatter.cs`
- Modify: `project/plugins/App.Timeline.Seam/TimelineFace.cs`

**Produces:**
- Zoom in/out steps through the canonical scale ladder instead of only multiplying the visible tick span.
- Ruler labels, zoom label, and regime/track widths reflect the selected temporal rung.

- [ ] Add a model helper that exposes ladder rungs and their tick spans from `BaselineScaleProfiles.GeospherePlateTimeV1`.
- [ ] Change timeline zoom buttons to select the next finer/coarser rung around the current tick.
- [ ] Keep fit behavior as full timeline range.
- [ ] Keep track selection and regime visibility behavior unchanged.
- [ ] Build the timeline bundle and report commands/results.

### Task 2: Plate Focus Boundary Renderer

**Files:**
- Modify: `project/hosts/complete-app/World/PlanetPresentationBinder.cs`
- Optional create: `project/hosts/complete-app/World/PlateBoundaryFocusRenderer.cs`

**Produces:**
- Selecting the plate layer in `mobile-plate` regime shows specific process visuals:
  - convergent: mountain/ridge/trench cue
  - divergent: exposed young-crust/rift cue
  - transform: strike-slip cue
- Pre-plate regimes hide plate boundary features.

- [ ] Derive boundary edges from adjacent `WorldGlobeSnapshot.Cells` with different `PlateId`.
- [ ] Classify each edge using real plate angular velocities from `WorldGlobeSnapshot.Plates`.
- [ ] Build Godot `ArrayMesh` or small batched meshes for overlays. Avoid many individual nodes.
- [ ] Use exposed low terrain / young crust colors, not water.
- [ ] Keep visuals deterministic from snapshot data.
- [ ] Build the host/export path and report commands/results.

### Task 3: Node Graph Visual Polish

**Files:**
- Modify: `project/plugins/App.Ui.NodeGraph/NodeGraphViewSource.cs`
- Modify: `project/plugins/App.Ui.Seam/MsaglGraphLayoutApplicator.cs`
- Optional modify: `project/plugins/App.Ui.Seam/ViewHost.cs`

**Produces:**
- Node cards show authored label, role, source/provider, parameters, and runtime state in a more scannable format.
- Layout spacing fits the left graph panel and avoids cramped overlapping cards.

- [ ] Improve the generic node graph data model enough for BoomHud/Godot renderer to distinguish title, category, source/provider, parameters, and runtime status.
- [ ] Keep executable function id in `typeId`; do not replace execution identity with display labels.
- [ ] Tune layout dimensions using actual content lengths and port counts.
- [ ] Do not create a domain-specific UI seam.
- [ ] Build the UI bundle and report commands/results.

### Lead Integration Verification

- [ ] Review each external agent diff before keeping it.
- [ ] Run `task test` or a narrower build if the whole suite is too slow.
- [ ] Run `GITVERSION_MAJORMINORPATCH=0.1.2 task build:godot:desktop bundle:timeline bundle:world bundle:install` after resident/host changes.
- [ ] Keep the exported windowed app open.
- [ ] Verify timeline ladder zoom, plate selected layer visual, node graph layout, world bundle reload, and command health.
- [ ] Capture a screenshot and report app PID, port, log path, and screenshot path.
