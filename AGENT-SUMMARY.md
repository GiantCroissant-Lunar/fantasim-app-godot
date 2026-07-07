# AGENT SUMMARY

## Directive

Implemented D6 timeline input behavior in `App.Timeline.Seam`:

- The vertical playhead line now has an 8px grab margin on either side through the full lanes
  height. A left press in that zone is intercepted from `TimelineFace._Input`, starts
  `_scrubDragging`, accepts the event, and routes through the existing scrub/`SeekTo` echo path
  before lane buttons can consume it.
- Mouse wheel over the ruler or lanes zooms the time scale. Wheel up zooms to the next finer
  ladder span; wheel down zooms to the next coarser span.
- Wheel zoom is centered on the cursor tick by preserving the cursor's fractional position in
  the visible window, clamped to `[MinViewSpanTicks, MaxTick]`.
- Pinch magnify gestures use the same cursor-centered zoom helper when Godot emits
  `InputEventMagnifyGesture`.
- Existing +/-/Fit buttons remain wired; +/- now share the same clamped zoom-window arithmetic.

## Files Changed

- `AGENT-SUMMARY.md`
- `project/plugins/App.Timeline.Seam/TimelineFace.cs`
- `project/plugins/App.Timeline.Seam/TimelineScrubMapper.cs`
- `project/tests/App.Timeline.Tests/TimelineScrubMapperTests.cs`

## Verification Performed

- `dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore --filter TimelineScrubMapperTests` passed.
- `dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore` passed.
- `git diff --check` passed.
- `dotnet build project/plugins/App.Timeline.Seam/App.Timeline.Seam.csproj --no-restore` failed because the Godot seam assets file is missing:
  - `NETSDK1004: Assets file 'project/plugins/App.Timeline.Seam/.godot/mono/temp/obj/project.assets.json' not found. Run a NuGet package restore to generate this file.`
- `dotnet build project/plugins/App.Timeline.Seam/App.Timeline.Seam.csproj` was attempted. It remained at `Determining projects to restore...` for five minutes, then exited with:
  - `Build FAILED.`
  - `0 Warning(s)`
  - `0 Error(s)`
- Windowed app verification was not run per directive constraint.
- Conventional commit was attempted, but staging failed because the sandbox cannot create the
  shared worktree git lock:
  - `fatal: Unable to create '.../.git/worktrees/app-w6-timeline/index.lock': Operation not permitted`

## Lead Manual Acceptance Checklist

1. In the windowed app, grab the playhead line in the middle of a lane, over a regime/track button,
   and drag left/right. Confirm it scrubs and the button underneath does not fire.
2. Wheel up over the ruler at a known tick. Confirm the view zooms in and that tick stays under the
   cursor.
3. Wheel down over the lanes at a known tick. Confirm the view zooms out and that tick stays under
   the cursor unless clamped at the timeline edge.
4. Confirm ruler click/drag scrub still works.
5. Confirm lane scrub and band seek still work away from the playhead grab zone.
6. Confirm track buttons still select layers.
7. Confirm Play, Fit, +, and - buttons still work.
8. Confirm label/playhead/handle echo updates immediately after scrubbing/seeking.
