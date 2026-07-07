# AGENT SUMMARY

## D8 smooth scrub implementation

- TimelineFace now treats mouse-drag motion as coalesced scrub preview input: press applies the first tick immediately, motion stores the latest pending tick, `_Process` applies at most one latest tick per frame, and release commits the final tick immediately.
- TimelineFace splits cheap local echo from controller application. The label, playhead line, handle, and AnimationPlayer position update before the world apply for each preview frame.
- Timeline tick origin is plumbed through `ITimelineController.PushTick` with `TimelineTickOrigin.Standard`, `ScrubPreview`, and `ScrubCommit`.
- PlanetPresentationBinder now runs the existing light `ApplyTimelineTick` path on every applied scrub preview, including continent/fraction refresh whenever the active surface-coloring owner is Continents, but defers heavy presentation refreshes during scrub previews through `ScrubApplyScheduler`.
- Scrub-preview ticks do not publish `TickChanged`, so listeners such as automatic crust generation do not run for every mouse-motion frame. Commit and standard seeks still publish `TickChanged`.
- Heavy refreshes caused by regime/crust-snapshot transitions run after roughly 300 ms of scrub rest, or immediately on scrub release. Ingress `timeline.seek` remains a standard/full apply path.

## Windowed lead feel

In the exported windowed app, the lead should feel: drag the timeline and the playhead stays glued to the cursor; the planet updates through the light animation path under the drag; expensive crust/presentation refresh lands after about 0.3 seconds of resting or on mouse release; ingress `timeline.seek` behavior remains deterministic/full-apply.

## Verification

- `dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore --verbosity normal`
- `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --no-restore --verbosity normal`
- `dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --no-restore --verbosity normal`

The direct Godot SDK project builds with `--no-restore` could not run because `.godot/mono/temp/obj/project.assets.json` is absent for those projects in this worktree.
