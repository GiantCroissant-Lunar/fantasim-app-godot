# Agent Summary - D8c Track Filmstrip

Branch: `wt/2026-07-08-filmstrip`

## Implemented

- Added design addendum: `vault/specs/2026-07-08-track-filmstrip-design.md`.
- Replaced compact timeline track content with low-res image filmstrip frames.
- Kept expanded track content as the embedded `GraphEdit` node graph.
- Added pure filmstrip planning/cache-key helpers in `App.Timeline`.
- Added a read-only low-resolution world preview DTO/API:
  - crust uses low-frequency materialization and frame-tick sampling from the governing snapshot;
  - plate uses low-frequency globe snapshot plate identity;
  - magma-ocean, stagnant-lid, mantle, and atmosphere use procedural placeholders for this slice.
- Made crust product cache frequency-aware so preview frequency 2/3 cannot collide with full presentation products.
- Added async thumbnail generation with neutral placeholders and main-thread Godot `ImageTexture` creation.
- Disposes cached textures and clears resident filmstrip provider on `_ExitTree`.

## Verification

- Attempted required no-restore runs:
  - `dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore -v minimal --nologo`
  - `dotnet build project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore -v minimal --nologo`
  - `dotnet build project/contracts/App.World/App.World.csproj --no-restore -v minimal --nologo`
  - `dotnet build project/plugins/App.World/App.World.csproj --no-restore -v minimal --nologo`
  - `dotnet build project/plugins/App.Timeline.Seam/App.Timeline.Seam.csproj --no-restore -v minimal --nologo`
- Local no-restore builds are blocked by missing `project.assets.json` files in this worktree. The lead should rebuild with network/restore available.

## Lead real-mouse checks

1. Start the already-open exported windowed app or run `task run:exported`.
2. Build/install the affected bundles, then confirm hot reload and `old ALC collected`.
3. In compact mode, verify each timeline track shows a strip of image thumbnails instead of graph node chips.
4. Wheel zoom over the timeline and verify thumbnail count/content refreshes for the new visible range.
5. Drag the playhead/ruler/line and verify scrub remains responsive and the thumbnails do not steal input.
6. Toggle D5 layer buttons and verify selected/inactive styling still updates.
7. Expand a track with the chevron and verify the embedded node graph is unchanged from the prior D7c expanded content.
8. Collapse the track and verify the filmstrip returns.
