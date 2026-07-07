# AGENT SUMMARY

## Scope

Implemented D7c first slice: geosphere layer tracks now treat the track content area as that layer's read-only node graph.

## Changed

- Added design doc: `vault/specs/2026-07-08-track-embedded-layer-graphs-design.md`.
- Added pure graph projection in `App.Timeline`: resolves a layer graph from `WorldGenerationGraphFamilyDocument` by `sphereId + layerId + regimeId` and computes stable pipeline order for compact strips.
- Added pure track layout arithmetic in `App.Timeline`: compact rows remain 26px; expanded rows are 200px.
- Added `App.Ui.Seam.EmbeddedNodeGraphRenderer` to reuse the existing BoomHud `GraphEdit` binder plus `GraphNodeVisualEnhancer`, `MsaglGraphLayoutApplicator`, and annotation enhancer.
- Updated `TimelineFace`:
  - Each track row has a separate chevron and label toggle.
  - Compact geosphere tracks show read-only node chips and edge hints in the content area.
  - Expanded geosphere tracks host a read-only `GraphEdit` for that layer graph.
  - Compact graph strips use `MouseFilter.Ignore`; expanded graphs use `MouseFilter.Stop`.
  - New dynamic chevron/toggle signal callables and expanded graph binders are disconnected/disposed before lane rebuild and `_ExitTree`.
- Wired graph family lookup through the resident registry per call, avoiding a static capture of the world service instance.

## Deferred

- Editing graphs from embedded track content.
- Layer presentation composition/compose nodes.
- Atmosphere lane graphs.
- Windowed/hot-reload verification in this environment.

## Verification Run

- `dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore` passed.
- `git diff --check` passed.

## Verification Blocked

- `dotnet build project/plugins/App.Timeline.Seam/App.Timeline.Seam.csproj --no-restore` and `dotnet build project/plugins/App.Ui.Seam/App.Ui.Seam.csproj --no-restore` could not compile because Godot SDK asset files were absent under `.godot/mono/temp/obj/project.assets.json`.
- Retrying seam builds with `--ignore-failed-sources` hung during restore resolution in the sandbox, so I stopped relying on restore-backed builds.
- The windowed app cannot be run from this task per directive.

## Lead Real-Mouse Checks

1. Launch the already-exported windowed app.
2. Hot-reload/install the changed timeline bundle if using the normal bundle loop.
3. Expand the geosphere Mantle/Magma Ocean/Stagnant Lid/Plate/Crust track that is visible at the current playhead.
4. Confirm the compact row changes from node chips to an expanded read-only graph area.
5. Confirm the expanded graph pans but does not allow editing nodes/wires.
6. Collapse the same track and confirm the row returns to 26px compact chip content.
7. Scrub over compact track content and confirm playhead/world time still moves.
8. Grab the playhead line over both compact and expanded lane heights and confirm drag-scrub still works.
9. Use mouse wheel over the timeline and confirm time zoom remains cursor-centered.
10. Click layer labels and confirm D5 active toggles/multi-highlight behavior is unchanged.
11. Click regime bands and confirm band seek still works.
12. Use Play, Fit, `+`, and `-` and confirm behavior is unchanged.
13. After hot-reload, confirm the log reports `old ALC collected`.
