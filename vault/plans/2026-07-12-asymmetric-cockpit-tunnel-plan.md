# Asymmetric Cockpit Tunnel Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the
> `external-agent-delegation` skill); otherwise execute inline with a review checkpoint per task.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved asymmetric cockpit-tunnel slice through the named exported gate: a
left-center active track, right-third current planet, honest 3D snapshot spheres, exactly two
camera-relative dials, canonical outer time, presentation-only fine inspection, and fail-closed
HUD/F9/reload behavior.

**Architecture:** Put time, source, cache, framing, focus, scheduling, and lifecycle decisions in
Godot-free policies. Keep `TimelinePlugin` as the sole mode/HUD command owner, keep
`TunnelPresentationBinder` as the effective geometry/camera/input owner, and pass only T1 contract
values across collectible boundaries. Ordinary filmstrip work remains bounded FIFO; fine inspection
uses an independent actively-cancelled latest-wins lane.

**Tech Stack:** .NET 8, C# 12, Godot.NET 4.7, xUnit, Taskfile, UnifyBuild, collectible plugin ALCs,
generated `[CrossDelegate]` face forwarding, and the existing HTTP command/evidence tools.

## Global Constraints

- Design authority is `vault/specs/2026-07-12-asymmetric-cockpit-tunnel-design.md`; implementation
  may tune permitted visual seeds, but may not relax authority, provenance, lifecycle, cancellation,
  or evidence requirements.
- Follow RED -> observe the intended failure -> minimal GREEN -> focused pass -> relevant full suite
  -> path-scoped Conventional Commit for every behavioral task.
- Use CodeGraph before direct source search. Before Godot/.NET API edits, follow
  `source-driven-development` and record the official source consulted in the task log.
- `ITimelineController.Tick` remains the only authoritative time. Inner/fine paths never call
  `PushTick`, persist a layer offset, or bind another world/presentation document.
- Exactly two interactive dial meshes exist. The current-plane slice, labels, chevrons, tethers,
  lens, and unavailable sectors are non-interactive and may not read as a third ring.
- Never retain a bundle-defined service, delegate, callback, task continuation, face context, or
  material sink from a resident/static owner after sever. Resolve cross-bundle services lazily.
- All deferred Godot callbacks and async completions compare captured lifecycle/mode/mount epochs.
- A timeline-only reload preserves a still-effective tunnel. World or stage loss forces safe 2D and
  never auto-restores the tunnel.
- The final gate uses one fresh full export because `App.Presentation` and `App.Timeline.Seam` are
  resident seams. Bundle-only rebuilds cannot prove those changes.
- No production fake tracks, preview maps, ECS worlds, globes, or demo metadata.

---

## File-responsibility map

### Modify

- `project/contracts/App.Presentation/ITunnelPresentation.cs` — activation result and synchronous
  effective-state attempt.
- `project/contracts/App.Timeline/Providers/ITimelineFace.cs` — epoch-bearing HUD state.
- `project/contracts/App.Timeline/Providers/ITimelineFaceContext.cs` — desired HUD replay values.
- `project/contracts/App.World/LayerFilmstripPreview.cs` — requested/completed graph revision.
- `project/contracts/App.World/Services/IService.cs` — nullable stale-revision preview result.
- `project/plugins/App.World/Services/Service.cs` — start/end revision validation.
- `project/plugins/App.Timeline/TimelinePlugin.cs` — sole serialized command owner, mode epoch,
  reload ordering, and new face-context construction.
- `project/plugins/App.Timeline.Seam/TimelineFace.cs` — post-bind HUD replay and stale-deferred guard.
- `project/plugins/App.Timeline.Seam/TimelineFace.Lanes.cs` — one cheap preview revision read per
  rebuild, distinct from graph-family presenter revision.
- `project/plugins/App.Timeline.Seam/IFilmstripFrameSink.cs` — metadata-bearing cached payload.
- `project/plugins/App.Timeline.Seam/FilmstripTextureCacheKey.cs` — requested-tick identity.
- `project/plugins/App.Timeline.Seam/FilmstripPreviewController.cs` — metadata-preserving cache,
  request identity validation, and separate fine lane.
- `project/plugins/App.Timeline.Seam/TunnelCorridorLayout.cs` — left focus and initial active track.
- `project/plugins/App.Timeline.Seam/TunnelFinePreviewMapper.cs` — sampled-tick policy integration.
- `project/plugins/App.Timeline.Seam/TunnelGestureCoordinator.cs` — inactive inner rejection.
- `project/plugins/App.Timeline.Seam/TunnelScrubMapper.cs` — deterministic canonical phase.
- `project/plugins/App.Presentation/Tunnel/TunnelCameraFraming.cs` — shared spatial contract.
- `project/plugins/App.Presentation/PlanetPresentationBinder.cs` — stage-scene loss/rebind of the
  real planet body without replacing the world service generation.
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs` — effective activation,
  stage/world reset, epochs, camera/current-plane ownership, and disposal.
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Camera.cs` — interior camera and
  camera-relative instrument attachment.
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Corridors.cs` — depth mapping,
  graph revision, sphere population, unavailable states, and material ownership.
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Rings.cs` — sibling roots,
  stationary readouts, deterministic phase, and inspection lens.
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs` — command-only F9,
  camera-local hit plane, fine request integration, and cancellation.
- `project/tests/App.Presentation.Tests/TunnelCameraFramingTests.cs` — 16:9/16:10 hard contracts.
- `project/tests/App.Presentation.Tests/PlanetPresentationReloadGateTests.cs` — world/stage retry and
  hidden remount sequencing.
- `project/tests/App.Presentation.Tests/TunnelStagePreparationOrderingTests.cs` — tunnel-first stage
  reload ordering, generation rejection, and no premature mounted state.
- `project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj` — link new Godot-free seam files.
- `project/tests/App.Timeline.Tests/FilmstripTextureCacheKeyTests.cs` — every-field inequality.
- `project/tests/App.Timeline.Tests/TimelinePluginTests.cs` — activation/HUD/command results.
- `project/tests/App.Timeline.Tests/TimelinePluginLifecycleRaceTests.cs` — stale epoch and sever races.
- `project/tests/App.Timeline.Tests/TimelinePlaybackFlowTests.cs` — HUD interface fake update.
- `project/tests/App.Timeline.Tests/TimelineServiceTests.cs` — HUD interface fake update.
- `project/tests/App.Timeline.Tests/TimelineFilmstripTests.cs` — bounded four-slot planning near max.
- `project/tests/App.Timeline.Tests/T3PurityTests.cs` — inner path authority source audit.
- `project/tests/App.Timeline.Tests/TunnelCorridorLayoutTests.cs` — left focus and initial selection.
- `project/tests/App.Timeline.Tests/TunnelFinePreviewMapperTests.cs` — truncation/buckets/reset.
- `project/tests/App.Timeline.Tests/TunnelGestureCoordinatorTests.cs` — inactive inner unhandled.
- `project/tests/App.Timeline.Tests/TunnelScrubMapperTests.cs` — canonical phase.
- `AGENT-SUMMARY.md` — durable conclusions after the gate.

### Create

- `project/contracts/App.Presentation/TunnelModePolicy.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelActivationPolicy.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelSnapshotSourcePolicy.cs`
- `project/plugins/App.Presentation/Tunnel/SnapshotSphereFilmstripSink.cs`
- `project/plugins/App.Timeline.Seam/TunnelFineSamplePolicy.cs`
- `project/plugins/App.Timeline.Seam/TunnelFineRequestScheduler.cs`
- `project/plugins/App.World/Services/FilmstripRevisionGate.cs`
- `project/tests/App.Presentation.Tests/TunnelActivationPolicyTests.cs`
- `project/tests/App.Presentation.Tests/TunnelStagePreparationOrderingTests.cs`
- `project/tests/App.Presentation.Tests/TunnelSnapshotSourcePolicyTests.cs`
- `project/tests/App.Timeline.Tests/FilmstripFramePayloadTests.cs`
- `project/tests/App.Timeline.Tests/TunnelFineSamplePolicyTests.cs`
- `project/tests/App.Timeline.Tests/TunnelFineRequestSchedulerTests.cs`
- `project/tests/App.Timeline.Tests/TimelineHudReplayTests.cs`
- `project/tests/App.World.Tests/LayerFilmstripPreviewRevisionTests.cs`
- `vault/specs/evidence/2026-07-12-asymmetric-cockpit-tunnel-gate/README.md`
- Gate evidence files listed in Task 10.

### Delete after callers migrate

- `project/plugins/App.Presentation/Tunnel/QuadMaterialFilmstripSink.cs` — flat-quad tunnel sink.

---

## Task 1 — effective activation, serialized mode ownership, and command-only F9

**Paths:** `project/contracts/App.Presentation/ITunnelPresentation.cs:9-18`,
`project/contracts/App.Presentation/TunnelModePolicy.cs`,
`project/plugins/App.Presentation/Tunnel/TunnelActivationPolicy.cs`,
`project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs:107-181,348-398,591-620`,
`project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs:34-116`,
`project/plugins/App.Presentation/PlanetPresentationBinder.cs:690-723`,
`project/plugins/App.Timeline/TimelinePlugin.cs:254-425`,
`project/tests/App.Presentation.Tests/TunnelActivationPolicyTests.cs`,
`project/tests/App.Presentation.Tests/TunnelStagePreparationOrderingTests.cs`,
`project/tests/App.Timeline.Tests/TimelinePluginTests.cs`, and
`project/tests/App.Timeline.Tests/TimelinePluginLifecycleRaceTests.cs`.

**Interfaces**

- Consume: `IResource.IsLoaded(string)`, `ISceneRegistry`, `ITimelineController`, `ICommandService`,
  `ITunnelPresentation.IsEnabled`, existing `_lifecycleGate`.
- Produce: `TunnelActivationResult`, `TunnelActivationReadiness`,
  `TunnelStagePreparationReadiness`, `TunnelStagePreparationAction`,
  `ITunnelPresentation.TrySetEnabled(bool)`, `TunnelModeDecision`, monotonic mode epoch.
- Preserve: `Rebind()` may prepare a disabled mount asynchronously; it may not turn a prior failed
  enable request into latent desired state.

- [ ] Add RED policy tests covering success, every missing dependency, idempotent disable, timeline
  reload preservation, world/stage/controller loss, disposal, and the invariant that failed enable
  returns effective false with no auto-reenable. Add this complete contract policy:

```csharp
namespace FantaSim.App.Presentation;

public readonly record struct TunnelActivationResult(
    bool RequestedEnabled,
    bool EffectiveEnabled,
    string FailureReason);
```

```csharp
namespace FantaSim.App.Presentation;

public enum TunnelModeEvent
{
    EnableSucceeded,
    EnableFailed,
    DisableRequested,
    TimelineReload,
    WorldChanging,
    StageChanging,
    ControllerLost,
    Disposed,
}

public readonly record struct TunnelModeDecision(
    long ModeEpoch,
    bool EffectiveEnabled,
    bool HudVisible,
    bool CancelInteractionWork,
    bool CancelCommandWork,
    bool RestoreCamera,
    bool AutoReenable);

public static class TunnelModePolicy
{
    public static TunnelModeDecision Decide(
        TunnelModeEvent modeEvent,
        bool currentEffective,
        long currentEpoch)
    {
        var nextEpoch = currentEpoch == long.MaxValue ? long.MaxValue : currentEpoch + 1L;
        if (modeEvent == TunnelModeEvent.EnableSucceeded)
            return new(nextEpoch, true, false, false, false, false, false);
        if (modeEvent == TunnelModeEvent.TimelineReload)
            return new(nextEpoch, currentEffective, !currentEffective, false, true, false, false);
        return new(nextEpoch, false, true, true, true, true, false);
    }
}
```

```csharp
namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelActivationReadiness(
    bool WorldLoaded,
    bool StageLoaded,
    bool HasController,
    bool HasMount,
    bool HasCamera,
    bool HasPlanetBody);

internal static class TunnelActivationPolicy
{
    internal static string FailureReason(TunnelActivationReadiness value)
    {
        if (!value.WorldLoaded) return "world unavailable";
        if (!value.StageLoaded) return "stage unavailable";
        if (!value.HasController) return "timeline controller unavailable";
        if (!value.HasMount) return "tunnel mount unavailable";
        if (!value.HasCamera) return "tunnel camera unavailable";
        if (!value.HasPlanetBody) return "planet body unavailable";
        return string.Empty;
    }
}
```

  In the same Godot-free seam file, make stage preparation an explicit prerequisite rather than an
  incidental result of `AlignToPlanetBody`:

```csharp
internal readonly record struct TunnelStagePreparationReadiness(
    long ExpectedGeneration,
    long CurrentGeneration,
    bool BinderAlive,
    bool WorldLoaded,
    bool StageLoaded,
    bool HasEnvironment,
    bool HasValidPlanetBody,
    bool PlanetBodyInsideTree);

internal enum TunnelStagePreparationAction
{
    Ignore,
    RetryNextFrame,
    PrepareHidden,
}

internal static class TunnelStagePreparationPolicy
{
    internal static TunnelStagePreparationAction Decide(TunnelStagePreparationReadiness value)
    {
        if (!value.BinderAlive || value.ExpectedGeneration != value.CurrentGeneration)
            return TunnelStagePreparationAction.Ignore;
        if (!value.WorldLoaded || !value.StageLoaded)
            return TunnelStagePreparationAction.Ignore;
        return value.HasEnvironment && value.HasValidPlanetBody && value.PlanetBodyInsideTree
            ? TunnelStagePreparationAction.PrepareHidden
            : TunnelStagePreparationAction.RetryNextFrame;
    }
}
```

- [ ] Run:

```bash
dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TunnelActivationPolicyTests|FullyQualifiedName~TunnelStagePreparationOrderingTests"
```

  Expected: compile/test failure because the result/policy do not exist.

- [ ] Replace the activation member in `ITunnelPresentation` with:

```csharp
TunnelActivationResult TrySetEnabled(bool enabled);
bool IsEnabled { get; }
```

  Implement `TunnelPresentationBinder.TrySetEnabled` so disable always cancels gesture/fine work,
  hides the mount, restores the previous camera, clears latent intent, and returns effective false.
  Enable first computes `TunnelActivationReadiness` from live Godot/resource state. On a non-empty
  reason it performs the same fail-safe disable and returns that reason. Only a ready mount may be
  shown and take the camera; `IsEnabled` changes to true after camera activation succeeds. Binder and
  timeline orchestration apply `TunnelModePolicy.Decide`: successful/failed activation selects the
  matching event, timeline reload cancels command work only, and dependency-loss events consume all
  fail-safe flags in their documented order.

- [ ] Refactor `Rebind()`/`EnsureMounted()` so deferred preparation always mounts hidden and checks
  its captured binder generation. Do not call `TrySetEnabled(true)` from deferred work. Add a binder
  generation increment to world/stage change and disposal.

- [ ] Add RED stage-remount state tests to `PlanetPresentationReloadGateTests`: an independent stage
  mark becomes pending, Changed coalesces, an absent mount keeps retry armed, and `MarkMounted` ends
  retry. In `TunnelActivationPolicyTests`, assert the stage decision is effective false, restores the
  camera, and has `AutoReenable == false`. Add `TunnelStagePreparationOrderingTests` with this exact
  adversarial order: tunnel observes the new environment before planet bind and returns
  `RetryNextFrame`; no mount is committed and `MarkMounted` remains uncalled; a later observation of
  a valid, inside-tree `PlanetBody` for the same generation returns `PrepareHidden`; an observation
  from the old generation returns `Ignore`. Also cover invalid and detached Godot-body projections
  through booleans so a non-null stale body cannot pass. The exported gate in Task 10 proves actual
  node removal and remount. Update `PlanetPresentationBinder` to branch explicitly:

  - world changing: dispose generation subscription, clear root/view, mark remount pending;
  - stage changing: retain the world subscription/service, clear every stage-node reference, mark
    remount pending;
  - Changed: retry only when both world and stage are loaded and the new stage environment exists,
    then call `Rebind`;
  - successful bind: mark the remount gate complete.

  Update `TunnelPresentationBinder` with the same stage pending/retry pattern, with ordering made
  deterministic as follows:

  - replace void `AlignToPlanetBody` with `TryAlignToPlanetBody(Node3D body, long generation)`; it
    returns false on generation mismatch, invalid/detached body, missing/invalid mount, or teardown;
  - before creating `TunnelMount`, resolve both the stage environment and the current provider body,
    project their live state into `TunnelStagePreparationReadiness`, and apply its decision;
  - on `RetryNextFrame`, keep the remount gate pending and schedule exactly one next-frame callback
    through a stored one-shot `SceneTree.SignalName.ProcessFrame` `Callable`; never recurse with
    `CallDeferred` in the same idle queue. Disconnect that exact callable on generation change,
    world/stage unload, and disposal. Bound one retry burst to 120 process frames; exhaustion logs one
    warning and stays pending/effective-false/HUD-visible. A later explicit activation or resource
    `Changed` starts a fresh burst, but never stores latent enable intent;
  - on `PrepareHidden`, create the mount hidden, call `TryAlignToPlanetBody` with the already-validated
    body, then construct input/camera/shell. If any step fails, remove the partial mount and retry on
    the next frame;
  - call `_worldRuntimeReload.MarkMounted()` only after alignment, camera, relay, and shell are valid.
    Do not show the mount or call `TrySetEnabled(true)` from this path.

  This is the stage-order handshake: `PlanetPresentationBinder.BindDocument` may complete before or
  after the tunnel's first callback, but the tunnel cannot report mounted until the new generation's
  real `PlanetBody` exists inside the tree. Successful preparation leaves `IsEnabled == false`; only
  a later explicit command may activate it.

- [ ] Add RED command tests asserting response fields `requested`, `effective`, `failureReason`, and
  `modeEpoch`; missing tunnel and failed activation show HUD; disable is idempotent; stale command
  epochs cannot hide HUD after sever. Construct responses only with `JsonObject`:

```csharp
private static string BuildTunnelResultJson(TunnelActivationResult result, long modeEpoch)
{
    return new JsonObject
    {
        ["ok"] = result.RequestedEnabled == result.EffectiveEnabled,
        ["requested"] = result.RequestedEnabled,
        ["effective"] = result.EffectiveEnabled,
        ["failureReason"] = result.FailureReason,
        ["modeEpoch"] = modeEpoch,
    }.ToJsonString();
}
```

- [ ] Under `_lifecycleGate`, resolve `ITunnelPresentation` into a method-local variable, call
  `TrySetEnabled`, feed the result into `TunnelModePolicy.Decide`, store its `ModeEpoch`, apply its HUD
  state, and return `Task.FromResult`.
  When resolution is null, construct `(requested, false, "tunnel presentation unavailable")` and
  apply visible HUD. Never store the world-bundle object in `TimelinePlugin`; never await while
  holding the gate.

- [ ] Delete all direct `SetEnabled`/`TrySetEnabled` fallbacks from
  `TunnelPresentationBinder.Input.cs`. Resolve `ICommandService` per F9 press. Missing service,
  `CommandResult.Ok == false`, exception, cancellation, malformed JSON, or stale captured binder
  generation/mode epoch logs once and is inert. Add/replace lifecycle CTS cancellation on world,
  stage, disable, and disposal so the fire-and-forget async state machine cannot pin an outgoing ALC.
  Also handle `timeline` `RuntimeChanging` in the binder by cancelling only the F9 CTS and dropping
  the method-local command service; do not disable the still-effective tunnel for timeline reload.
  Pass that CTS token to `ICommandService.ExecuteAsync(request, token)`.

- [ ] Run the focused tests, then the two relevant suites:

```bash
dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TunnelActivationPolicyTests|FullyQualifiedName~TunnelStagePreparationOrderingTests|FullyQualifiedName~PlanetPresentationReloadGateTests"
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TimelinePluginTests|FullyQualifiedName~TimelinePluginLifecycleRaceTests"
```

  Expected: all selected tests pass; existing lifecycle resurrection guards remain green.

- [ ] Refactor duplicate fail-safe disable code into one binder method, run both complete projects,
  and commit only Task 1 paths:

```bash
git add project/contracts/App.Presentation/ITunnelPresentation.cs \
  project/contracts/App.Presentation/TunnelModePolicy.cs \
  project/plugins/App.Presentation/Tunnel/TunnelActivationPolicy.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs \
  project/plugins/App.Presentation/PlanetPresentationBinder.cs \
  project/plugins/App.Timeline/TimelinePlugin.cs \
  project/tests/App.Presentation.Tests/TunnelActivationPolicyTests.cs \
  project/tests/App.Presentation.Tests/TunnelStagePreparationOrderingTests.cs \
  project/tests/App.Presentation.Tests/PlanetPresentationReloadGateTests.cs \
  project/tests/App.Timeline.Tests/TimelinePluginTests.cs \
  project/tests/App.Timeline.Tests/TimelinePluginLifecycleRaceTests.cs
git commit -m "fix(tunnel): make activation fail closed"
```

## Task 2 — epoch-bearing HUD replay across face bind and reload

**Paths:** `project/contracts/App.Timeline/Providers/ITimelineFace.cs:13-61`,
`project/contracts/App.Timeline/Providers/ITimelineFaceContext.cs:12-41`,
`project/plugins/App.Timeline/TimelinePlugin.cs:134-225,428-484,610-690`,
`project/plugins/App.Timeline.Seam/TimelineFace.cs:144-277`, generated proxy/fakes listed in the file
map, and `project/tests/App.Timeline.Tests/TimelineHudReplayTests.cs`.

**Interfaces**

- Consume: generated `ITimelineFaceProxy.BindCrossTarget`, live `ITunnelPresentation.IsEnabled`,
  timeline/world/stage resource events.
- Produce: `TimelineHudState`, `ITimelineFace.ApplyHudState`, context `DesiredHudState`.
- ALC boundary: the resident face copies primitives/value records and never retains
  `ITimelineFaceContext` or a bundle callback.

- [ ] Add RED tests for pre-bind hide replay, higher-epoch wins, stale deferred show/hide rejection,
  timeline reload deriving hidden from a still-effective tunnel, world and stage reset showing HUD,
  stage reload retaining the timeline registration while forcing effective false, and no context
  retention after `ClearResidentContext`.

- [ ] Add the T1 value and replace the bool-only face method:

```csharp
public readonly record struct TimelineHudState(bool Visible, long ModeEpoch);

public interface ITimelineFace
{
    [CrossDelegate] void RebindResidentContext();
    [CrossDelegate] void Play();
    [CrossDelegate] void Pause();
    [CrossDelegate] void SeekTo(long tick);
    [CrossDelegate] void ApplyView(TimelineViewSnapshot snapshot);
    [CrossDelegate] void ApplyHudState(TimelineHudState state);
}
```

  Add `TimelineHudState DesiredHudState { get; }` to `ITimelineFaceContext`, its constructor, and
  properties. The concrete context must keep the latest value, not only its compose-time value:

```csharp
private readonly object _hudGate = new();
private TimelineHudState _desiredHudState;

public TimelineHudState DesiredHudState
{
    get { lock (_hudGate) return _desiredHudState; }
}

internal void SetDesiredHudState(TimelineHudState state)
{
    lock (_hudGate) _desiredHudState = state;
}
```

  Every mode transition calls `SetDesiredHudState` before forwarding to the face proxy, so a command
  arriving after context registration but before face bind is replayed correctly. Update every
  fake/proxy compile error in the mapped test files.

- [ ] Implement the resident face guard with copied primitives:

```csharp
private long _hudModeEpoch = -1;
private int _residentBindGeneration;

public void ApplyHudState(TimelineHudState state)
{
    var bindGeneration = _residentBindGeneration;
    void Apply()
    {
        if (bindGeneration != _residentBindGeneration || state.ModeEpoch < _hudModeEpoch)
            return;
        _hudModeEpoch = state.ModeEpoch;
        Visible = state.Visible;
    }

    if (OS.GetThreadCallerId() == OS.GetMainThreadId())
        Apply();
    else
        Callable.From(Apply).CallDeferred();
}
```

  Increment `_residentBindGeneration` before every bind/clear. At a successful new bind, reset
  `_hudModeEpoch` to `-1` after incrementing bind generation and before applying the new context; a
  reloaded timeline plugin legitimately starts its per-generation epoch again, while queued work
  from the prior bind fails the bind-generation comparison. Bind the proxy first, copy context
  members, then call `ApplyHudState(context.DesiredHudState)` directly. In `ClearResidentContext`,
  cancel render work, unbind, increment the generation, and null every copied bundle
  delegate/service as today.

- [ ] In `ComposeTimeline`, derive `DesiredHudState` before registering the new context from the
  method-local live tunnel effective state and current `_modeEpoch`. Never reuse `_faceContext` from
  an older generation. Replace all direct bool calls with one helper that first updates the current
  context and then invokes the proxy with the same epoch-bearing value.

- [ ] Make reload ordering explicit:

  1. timeline `RuntimeChanging` preserves current effective/HUD state, increments the generation,
     cancels only F9 work that captured the outgoing command service, and severs the old context;
  2. the next timeline context re-derives hidden when the still-live tunnel is effective;
  3. world or stage `RuntimeChanging` increments mode/bundle epochs, updates desired HUD to visible,
     applies it, cancels interaction/preview/command work, disables the tunnel/restores camera, then
     severs the affected references;
  4. bundle reload is local: stage reload does not unload timeline or world. On stage Changed the
     persistent planet and tunnel binders retry against the new environment, remount the real planet,
     prepare the tunnel hidden, and never change effective state to true.

- [ ] Run focused and full timeline tests:

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TimelineHudReplayTests|FullyQualifiedName~TimelinePluginTests|FullyQualifiedName~TimelinePluginLifecycleRaceTests"
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore
```

  Expected: HUD replay/race tests and all updated face fakes pass.

- [ ] Commit only Task 2 paths:

```bash
git add project/contracts/App.Timeline/Providers/ITimelineFace.cs \
  project/contracts/App.Timeline/Providers/ITimelineFaceContext.cs \
  project/plugins/App.Timeline/TimelinePlugin.cs \
  project/plugins/App.Timeline.Seam/TimelineFace.cs \
  project/tests/App.Timeline.Tests/TimelineHudReplayTests.cs \
  project/tests/App.Timeline.Tests/TimelinePluginTests.cs \
  project/tests/App.Timeline.Tests/TimelinePluginLifecycleRaceTests.cs \
  project/tests/App.Timeline.Tests/TimelinePlaybackFlowTests.cs \
  project/tests/App.Timeline.Tests/TimelineServiceTests.cs
git commit -m "fix(timeline): replay hud state after face bind"
```

## Task 3 — lossless filmstrip identity and metadata-bearing cache payloads

**Paths:** `project/plugins/App.Timeline.Seam/IFilmstripFrameSink.cs:1-22`,
`FilmstripTextureCacheKey.cs:41-48`, `FilmstripPreviewController.cs:34-355`,
`project/contracts/App.World/LayerFilmstripPreview.cs:7-25`,
`project/contracts/App.World/Services/IService.cs:60-75`,
`project/plugins/App.World/Services/Service.cs:334-365`,
`project/plugins/App.World/Services/FilmstripRevisionGate.cs`,
`project/contracts/App.Timeline/Providers/ITimelineFaceContext.cs`,
`project/plugins/App.Timeline/TimelinePlugin.cs`,
`project/plugins/App.Timeline.Seam/TimelineFace.cs`,
`project/plugins/App.Timeline.Seam/TimelineFace.Lanes.cs`,
`project/tests/App.Timeline.Tests/FilmstripTextureCacheKeyTests.cs`, and
`project/tests/App.Timeline.Tests/FilmstripFramePayloadTests.cs`, and
`project/tests/App.Timeline.Tests/TimelinePluginTests.cs`, and
`project/tests/App.World.Tests/LayerFilmstripPreviewRevisionTests.cs`.

**Interfaces**

- Consume: revision-bearing `LayerFilmstripPreviewRequest`, provider-proven
  `LayerFilmstripPreviewMap`, immutable `ImageTexture`.
- Produce: `FilmstripFrameMetadata`, `FilmstripFramePayload`, `IFilmstripFrameSink.SetFrame`, full
  `FilmstripTextureCacheKey`, graph-revision-aware request identity.
- Preserve: the 2D sink renders every current map regardless of tunnel source classification.

- [ ] Add RED equality tests that change exactly one of sphere, layer, requested tick, snapshot tick,
  rung, width, height, or graph revision; add payload cache-hit tests proving `SourceKind` and both
  ticks survive the fast path. Add world tests where requested/start/end revisions match, the start
  is already stale, and revision advances while a fake render is blocked. Add a timeline context
  test proving the cheap revision provider returns current generation products and is not retained
  after context disposal.

- [ ] Extend the T1 provider values and nullable service result exactly:

```csharp
public sealed record LayerFilmstripPreviewRequest(
    string SphereId,
    string LayerId,
    long Tick,
    string ViewRung,
    int GraphRevision,
    int Width = 96,
    int Height = 48);

public sealed record LayerFilmstripPreviewMap(
    string SphereId,
    string LayerId,
    long RequestedTick,
    long SnapshotTick,
    int GraphRevision,
    string ViewRung,
    int SourceFrequency,
    int Width,
    int Height,
    string SourceKind,
    byte[] Rgba32);
```

  Change `IService.GetLayerFilmstripPreview` and its implementation return to
  `LayerFilmstripPreviewMap?`.

- [ ] Add `Func<int> FilmstripGraphRevisionProvider { get; }` to `ITimelineFaceContext`. Construct it
  in `TimelinePlugin` as one lazy T1-only registry lookup returning
  `GetGenerationProductsAsync().GraphRevision` or zero. `TimelineFace` copies and clears that
  delegate with its other bundle callbacks. `BuildLanes` invokes it once per rebuild and threads the
  result only to filmstrip requests; keep `GenerationGraphFamily.Revision` for the graph presenter.
  Never call either provider once per sphere.

  Update ordinary `RequestTexture` to construct the request with its positive `graphRevision`
  argument, then remove the duplicate `QueuedFilmstripFrame.GraphRevision` member; all later key and
  completion logic reads `queued.Request.GraphRevision` as the single request-side source.

- [ ] Add and use the provider-side completion gate:

```csharp
internal static class FilmstripRevisionGate
{
    internal static bool IsStable(int requested, int start, int completed)
        => requested == start && start == completed;
}
```

  `Service.GetLayerFilmstripPreview` reads `GetGenerationProductsAsync().GraphRevision` before
  rendering and returns null immediately if the request revision differs. After rendering it reads
  revision again and returns null unless `IsStable(request.GraphRevision, start, completed)`; only a
  stable map receives that proven revision. Thread `start` into each crust/plate/mantle/procedural map
  constructor as `GraphRevision`; never stamp the caller value after rendering. This prevents a
  render that spans a graph change from being mislabeled by its caller.

- [ ] Replace the sink/cache declarations with:

```csharp
internal readonly record struct FilmstripFrameMetadata(
    string SphereId,
    string LayerId,
    long RequestedTick,
    long SnapshotTick,
    string ViewRung,
    int SourceFrequency,
    int Width,
    int Height,
    string SourceKind,
    int GraphRevision);

internal readonly record struct FilmstripFramePayload(
    ImageTexture Texture,
    FilmstripFrameMetadata Metadata);

internal interface IFilmstripFrameSink
{
    bool IsAlive { get; }
    void SetFrame(FilmstripFramePayload frame);
}

internal readonly record struct FilmstripTextureCacheKey(
    string SphereId,
    string LayerId,
    long RequestedTick,
    long SnapshotTick,
    string ViewRung,
    int Width,
    int Height,
    int GraphRevision);
```

  `TextureRectFilmstripSink.SetFrame` assigns only `frame.Texture`; it intentionally ignores metadata.

- [ ] Link `IFilmstripFrameSink.cs` into `App.Timeline.Tests.csproj` beside the existing cache-key
  link so payload tests compile against the production declaration:

```xml
<Compile Include="..\..\plugins\App.Timeline.Seam\IFilmstripFrameSink.cs"
         Link="IFilmstripFrameSink.cs" />
```

- [ ] Change `_filmstripTextureCache` to store `FilmstripFramePayload`. Build request fast-path
  identity with every request field plus `graphRevision`. Before caching/applying, reject a returned
  map unless sphere, layer, requested tick, requested rung, width, and height match the request; use
  returned snapshot tick/source metadata only after that check. A graph revision mismatch or stale
  controller generation drops without applying.

- [ ] Use one conversion method for provider completion and cache hit:

```csharp
private static FilmstripFramePayload BuildPayload(
    ImageTexture texture,
    LayerFilmstripPreviewMap map)
    => new(texture, new FilmstripFrameMetadata(
        map.SphereId,
        map.LayerId,
        map.RequestedTick,
        map.SnapshotTick,
        map.ViewRung,
        map.SourceFrequency,
        map.Width,
        map.Height,
        map.SourceKind,
        map.GraphRevision));
```

  Before image creation/cache/apply, require
  `map.GraphRevision == queued.Request.GraphRevision`. A null result or
  mismatch is a silent stale/unavailable outcome; it cannot reach either sink.

- [ ] Run:

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~FilmstripTextureCacheKeyTests|FullyQualifiedName~FilmstripFramePayloadTests"
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --no-restore \
  --filter FullyQualifiedName~LayerFilmstripPreviewRevisionTests
```

  Expected: identity, fast-path invalidation, map validation, and metadata-on-hit tests pass.

- [ ] Refactor duplicated key construction into one pure method, run the complete timeline suite,
  and commit:

```bash
git add project/plugins/App.Timeline.Seam/IFilmstripFrameSink.cs \
  project/plugins/App.Timeline.Seam/FilmstripTextureCacheKey.cs \
  project/plugins/App.Timeline.Seam/FilmstripPreviewController.cs \
  project/contracts/App.World/LayerFilmstripPreview.cs \
  project/contracts/App.World/Services/IService.cs \
  project/plugins/App.World/Services/Service.cs \
  project/plugins/App.World/Services/FilmstripRevisionGate.cs \
  project/contracts/App.Timeline/Providers/ITimelineFaceContext.cs \
  project/plugins/App.Timeline/TimelinePlugin.cs \
  project/plugins/App.Timeline.Seam/TimelineFace.cs \
  project/plugins/App.Timeline.Seam/TimelineFace.Lanes.cs \
  project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
  project/tests/App.Timeline.Tests/FilmstripTextureCacheKeyTests.cs \
  project/tests/App.Timeline.Tests/FilmstripFramePayloadTests.cs \
  project/tests/App.Timeline.Tests/TimelinePluginTests.cs \
  project/tests/App.World.Tests/LayerFilmstripPreviewRevisionTests.cs
git commit -m "fix(timeline): preserve filmstrip frame provenance"
```

## Task 4 — honest source classification and sphere/unavailable sink behavior

**Paths:** `project/plugins/App.Presentation/Tunnel/TunnelSnapshotSourcePolicy.cs`,
`SnapshotSphereFilmstripSink.cs`, deleted `QuadMaterialFilmstripSink.cs`,
`TunnelPresentationBinder.Corridors.cs:144-229`, and
`project/tests/App.Presentation.Tests/TunnelSnapshotSourcePolicyTests.cs`.

**Interfaces**

- Consume: `FilmstripFramePayload.Metadata.SourceKind`, the distinct sphere material, and an
  unavailable-sector root.
- Produce: exact-ordinal `TunnelSnapshotSourcePolicy.IsReal`, `SnapshotSphereFilmstripSink.SetFrame`.
- Preserve: null, cancellation, provider exception, procedural/unknown source, or stale completion
  leaves unavailable content visible and never manufactures a sphere texture.

- [ ] Add RED theory tests with accepted `crust-low-res`, `plate-low-res`, and
  `mantle-shell-low-res`, plus every rejected value from design section 4.2, empty string, and an
  unknown string. Add a sink-state test proving a rejected payload cannot reveal a sphere.

- [ ] Add the complete pure policy:

```csharp
namespace FantaSim.App.Presentation.Tunnel;

internal static class TunnelSnapshotSourcePolicy
{
    internal static bool IsReal(string? sourceKind)
        => sourceKind is "crust-low-res" or "plate-low-res" or "mantle-shell-low-res";
}
```

- [ ] Replace the flat sink with this state transition; constructor callers must pass a material
  created for that sphere only:

```csharp
internal sealed class SnapshotSphereFilmstripSink : IFilmstripFrameSink
{
    private readonly MeshInstance3D _sphere;
    private readonly StandardMaterial3D _material;
    private readonly Node3D _unavailable;

    internal SnapshotSphereFilmstripSink(
        MeshInstance3D sphere,
        StandardMaterial3D material,
        Node3D unavailable)
    {
        _sphere = sphere;
        _material = material;
        _unavailable = unavailable;
        _sphere.Visible = false;
        _unavailable.Visible = true;
    }

    public bool IsAlive
        => GodotObject.IsInstanceValid(_sphere)
           && GodotObject.IsInstanceValid(_unavailable)
           && _sphere.IsInsideTree();

    public void SetFrame(FilmstripFramePayload frame)
    {
        var real = TunnelSnapshotSourcePolicy.IsReal(frame.Metadata.SourceKind);
        _material.AlbedoTexture = real ? frame.Texture : null;
        _sphere.Visible = real;
        _unavailable.Visible = !real;
    }
}
```

- [ ] Keep provider failures in `FilmstripPreviewController` structured: one warning includes sphere,
  layer, requested tick, and requested rung; cancellation/stale drops are silent. Do not call the sink
  on failure, so its constructor-established unavailable state remains.

- [ ] Run:

```bash
dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --no-restore \
  --filter FullyQualifiedName~TunnelSnapshotSourcePolicyTests
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore \
  --filter FullyQualifiedName~FilmstripFramePayloadTests
```

  Expected: exact source policy and metadata preservation pass.

- [ ] Delete the old quad sink only after all callers compile, run both complete projects, and commit:

```bash
git add project/plugins/App.Presentation/Tunnel/TunnelSnapshotSourcePolicy.cs \
  project/plugins/App.Presentation/Tunnel/SnapshotSphereFilmstripSink.cs \
  project/plugins/App.Presentation/Tunnel/QuadMaterialFilmstripSink.cs \
  project/plugins/App.Timeline.Seam/FilmstripPreviewController.cs \
  project/tests/App.Presentation.Tests/TunnelSnapshotSourcePolicyTests.cs
git commit -m "feat(tunnel): classify honest snapshot previews"
```

## Task 5 — deterministic fine sampling and actively-cancelled latest-wins lane

**Paths:** `project/plugins/App.Timeline.Seam/TunnelFineSamplePolicy.cs`,
`TunnelFineRequestScheduler.cs`, `FilmstripPreviewController.cs:34-90,174-355`,
`TunnelFinePreviewMapper.cs:45-130`, test project links, and the two new policy test files.

**Interfaces**

- Consume: inner raw `double`, canonical base/max tick, descriptor `Content.CadenceTicks`, preview
  provider cancellation token, graph revision, mount generation, and focused track identity.
- Produce: `TunnelFineSample`, `TunnelFineRequestKey`, `TunnelFineScheduleDecision`, independent fine
  lane CTS/epoch.
- Guarantee: at most one provider call active, only newest pending bucket retained, at least 100 ms
  between starts, active call cancelled when a newer bucket wins, and replacement starts only after
  the old call unwinds.

- [ ] Add RED sample tests for positive/negative truncation, sub-tick hold, bounds, non-finite input,
  positive/zero/null cadence, and base-time reset. Implement:

```csharp
namespace FantaSim.App.Timeline.Seam;

internal readonly record struct TunnelFineSample(
    long BaseTick,
    long SampleTick,
    long Bucket,
    bool TextureChanged);

internal static class TunnelFineSamplePolicy
{
    internal static TunnelFineSample Map(
        long baseTick,
        long maxTick,
        double rawTickQuantity,
        long? cadenceTicks,
        long? previousBucket)
    {
        maxTick = Math.Max(0L, maxTick);
        baseTick = Math.Clamp(baseTick, 0L, maxTick);
        var sampleTick = baseTick;
        if (double.IsFinite(rawTickQuantity))
        {
            if (rawTickQuantity <= -baseTick)
                sampleTick = 0L;
            else if (rawTickQuantity >= maxTick - baseTick)
                sampleTick = maxTick;
            else
                sampleTick = baseTick + (long)Math.Truncate(rawTickQuantity);
        }

        var bucket = cadenceTicks is > 0 ? sampleTick / cadenceTicks.Value : sampleTick;
        return new TunnelFineSample(baseTick, sampleTick, bucket, previousBucket != bucket);
    }
}
```

- [ ] Add RED scheduler tests using integer monotonic milliseconds. They must prove: first starts;
  duplicate is inert; a newer key while active cancels and replaces pending; a third key replaces the
  pending key; completion before 100 ms waits; `TakeDue` starts only newest; reset cancels and makes
  old completion stale.

- [ ] Add the complete scheduler state machine:

```csharp
internal readonly record struct TunnelFineRequestKey(
    string SphereId,
    string LayerId,
    long SampleTick,
    long Bucket,
    string ViewRung,
    int GraphRevision,
    int MountGeneration,
    long Epoch);

internal enum TunnelFineScheduleAction { None, Start, CancelActive, Wait }

internal readonly record struct TunnelFineScheduleDecision(
    TunnelFineScheduleAction Action,
    TunnelFineRequestKey? Key,
    long DueAtMs);

internal sealed class TunnelFineRequestScheduler
{
    internal const long MinimumStartIntervalMs = 100L;
    private TunnelFineRequestKey? _active;
    private TunnelFineRequestKey? _pending;
    private CancellationTokenSource? _activeCts;
    private long _nextStartAtMs;

    internal CancellationToken ActiveToken => _activeCts?.Token ?? CancellationToken.None;

    internal TunnelFineScheduleDecision Offer(TunnelFineRequestKey key, long nowMs)
    {
        if (_active == key || _pending == key)
            return new(TunnelFineScheduleAction.None, null, _nextStartAtMs);
        if (_active is not null)
        {
            _pending = key;
            _activeCts?.Cancel();
            return new(TunnelFineScheduleAction.CancelActive, null, _nextStartAtMs);
        }
        if (nowMs < _nextStartAtMs)
        {
            _pending = key;
            return new(TunnelFineScheduleAction.Wait, null, _nextStartAtMs);
        }
        return Start(key, nowMs);
    }

    internal TunnelFineScheduleDecision ActiveFinished(
        TunnelFineRequestKey completedKey,
        long nowMs)
    {
        if (_active != completedKey)
            return new(TunnelFineScheduleAction.None, null, _nextStartAtMs);
        _active = null;
        _activeCts?.Dispose();
        _activeCts = null;
        return TakeDue(nowMs);
    }

    internal TunnelFineScheduleDecision TakeDue(long nowMs)
    {
        if (_active is not null || _pending is null)
            return new(TunnelFineScheduleAction.None, null, _nextStartAtMs);
        if (nowMs < _nextStartAtMs)
            return new(TunnelFineScheduleAction.Wait, null, _nextStartAtMs);
        var key = _pending.Value;
        _pending = null;
        return Start(key, nowMs);
    }

    internal bool Reset()
    {
        var cancel = _active is not null;
        _activeCts?.Cancel();
        _pending = null;
        return cancel;
    }

    private TunnelFineScheduleDecision Start(TunnelFineRequestKey key, long nowMs)
    {
        _active = key;
        _activeCts = new CancellationTokenSource();
        _nextStartAtMs = checked(nowMs + MinimumStartIntervalMs);
        return new(TunnelFineScheduleAction.Start, key, _nextStartAtMs);
    }
}
```

  Add `using System.Threading;` to the policy file. The controller passes `ActiveToken` to the
  provider. `Reset` deliberately keeps `_active` and its cancelled CTS until that provider unwinds;
  it only drops pending work. Tests retain the first token, offer a newer key, assert that token is
  cancelled, and call `ActiveFinished(firstKey, nowMs)` only after the blocking fake provider
  observes cancellation. A completion for any other key/epoch is inert.

- [ ] Link both pure files into `App.Timeline.Tests.csproj`. Run the two new test classes and observe
  RED before adding code, then GREEN after adding code. Add these exact links:

```xml
<Compile Include="..\..\plugins\App.Timeline.Seam\TunnelFineSamplePolicy.cs"
         Link="TunnelFineSamplePolicy.cs" />
<Compile Include="..\..\plugins\App.Timeline.Seam\TunnelFineRequestScheduler.cs"
         Link="TunnelFineRequestScheduler.cs" />
```

- [ ] Add a fine lane to `FilmstripPreviewController` with its own CTS and scheduler. On
  `CancelActive`, cancel but retain only the newest pending key. In the provider task `finally`, post
  back to the main thread, call `ActiveFinished(capturedKey, nowMs)`, and schedule `TakeDue` for the
  remaining delay only when that keyed completion was accepted.
  Before apply, compare cancellation epoch, mount generation, graph revision, focused sphere/layer,
  and requested bucket. Reuse the metadata-bearing texture cache but never enqueue fine work into the
  ordinary three-request queue.

- [ ] In `TunnelFineRequestSchedulerTests`, drive the real scheduler with a blocking fake provider:
  assert active count never exceeds one, second call starts only after the first observes cancellation
  and returns, Reset cannot permit overlap, stale `ActiveFinished(oldKey)` cannot clear a newer active
  key, ten starts require at least 900 ms between first/last, and an old-epoch completion is rejected
  before the test sink. No Godot controller type is linked into this pure test project.

- [ ] Run:

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TunnelFineSamplePolicyTests|FullyQualifiedName~TunnelFineRequestSchedulerTests"
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore
```

  Expected: scheduler invariants and the full timeline suite pass.

- [ ] Commit:

```bash
git add project/plugins/App.Timeline.Seam/TunnelFineSamplePolicy.cs \
  project/plugins/App.Timeline.Seam/TunnelFineRequestScheduler.cs \
  project/plugins/App.Timeline.Seam/TunnelFinePreviewMapper.cs \
  project/plugins/App.Timeline.Seam/FilmstripPreviewController.cs \
  project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
  project/tests/App.Timeline.Tests/TunnelFineSamplePolicyTests.cs \
  project/tests/App.Timeline.Tests/TunnelFineRequestSchedulerTests.cs
git commit -m "feat(timeline): schedule latest fine inspection"
```

## Task 6 — one current plane, interior off-axis camera, and left focus policy

**Paths:** `project/plugins/App.Presentation/Tunnel/TunnelCameraFraming.cs:1-13`,
`TunnelPresentationBinder.cs:21-50,256-270`, `TunnelCorridorLayout.cs`,
`TunnelCameraFramingTests.cs`, and `TunnelCorridorLayoutTests.cs`.

**Interfaces**

- Consume: canonical base/requested tick, `kb.UnitTicks`, max tick, planet visual radius, viewport
  aspect, real descriptor archive/activity state.
- Produce: one `CurrentPlaneZ`, future-only `TryTickToZ`, camera pose/projection, left focus angle,
  initial focus index.
- Preserve: planet remains on cylinder axis; near `MaxTick` unused depth stays empty.

- [ ] Replace old framing tests with RED invariants at `16d/9d` and `16d/10d`: camera axial/radial/
  planet clearance, planet normalized center/silhouette-height/crop, left current anchor/instrument,
  both ring bounds from the exact camera-local radii, non-overlap, the three approved
  `NearInteriorLip` shell cues, and visible separated depth cues.
  Add tick-to-Z tests for current, half kb, one kb, past rejection, beyond-kb rejection, and a
  shortened `MaxTick` range that does not stretch to the throat.

- [ ] Make `TunnelCameraFraming` the single owner of initial values and math:

```csharp
internal readonly record struct TunnelProjectedPoint(double X, double Y, double Depth);
internal readonly record struct TunnelProjectedBounds(
    double MinX,
    double MaxX,
    double MinY,
    double MaxY)
{
    internal double Height => MaxY - MinY;
}

internal static class TunnelCameraFraming
{
    internal const float TunnelRadius = 5.0f;
    internal const float MouthZ = 0.0f;
    internal const float CurrentPlaneZ = -5.0f;
    internal const float ThroatZ = -20.0f;
    internal const float TimelineDepth = CurrentPlaneZ - ThroatZ;
    internal const float FieldOfViewDegrees = 60.0f;
    internal const float RadialClearance = 0.5f;
    internal const float PlanetClearance = 0.25f;
    internal const float NearClip = 0.05f;
    internal const float PlanetVisualRadius = 2.06f;
    internal const float NearInteriorLipZ = -4.5f;
    internal static readonly Vector3 LocalPosition = new(-1.8f, 0.6f, -0.8f);
    internal static readonly Vector3 LocalTarget = new(-1.8f, 0.0f, -7.0f);
    internal static readonly Vector3 InstrumentLocalAnchor = new(-2.2f, 0.0f, -4.0f);
    internal const float InnerRingInnerRadius = 0.38f;
    internal const float InnerRingOuterRadius = 0.52f;
    internal const float OuterRingInnerRadius = 0.64f;
    internal const float OuterRingOuterRadius = 0.82f;

    internal static bool TryTickToZ(
        long requestedTick,
        long currentTick,
        long kbUnitTicks,
        out float z)
    {
        z = CurrentPlaneZ;
        if (kbUnitTicks <= 0 || requestedTick < currentTick)
            return false;
        var fraction = (requestedTick - (double)currentTick) / kbUnitTicks;
        if (fraction < 0d || fraction > 1d)
            return false;
        z = CurrentPlaneZ - (float)(fraction * TimelineDepth);
        return true;
    }

    internal static TunnelProjectedPoint Project(Vector3 point, double aspect)
    {
        var forward = Vector3.Normalize(LocalTarget - LocalPosition);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var relative = point - LocalPosition;
        var depth = Vector3.Dot(relative, forward);
        var tanHalf = Math.Tan(FieldOfViewDegrees * Math.PI / 360d);
        var x = 0.5d + Vector3.Dot(relative, right) / (2d * depth * tanHalf * aspect);
        var y = 0.5d - Vector3.Dot(relative, up) / (2d * depth * tanHalf);
        return new(x, y, depth);
    }

    internal static TunnelProjectedPoint ProjectInstrumentCenter(double aspect)
    {
        var depth = -InstrumentLocalAnchor.Z;
        var tanHalf = Math.Tan(FieldOfViewDegrees * Math.PI / 360d);
        return new(
            0.5d + InstrumentLocalAnchor.X / (2d * depth * tanHalf * aspect),
            0.5d - InstrumentLocalAnchor.Y / (2d * depth * tanHalf),
            depth);
    }

    internal static TunnelProjectedBounds ProjectSphereBounds(
        Vector3 center,
        double radius,
        double aspect)
    {
        var forward = Vector3.Normalize(LocalTarget - LocalPosition);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var relative = center - LocalPosition;
        var depth = Vector3.Dot(relative, forward);
        var cameraX = Vector3.Dot(relative, right);
        var cameraY = Vector3.Dot(relative, up);
        var (minHorizontal, maxHorizontal) = TangentSlopes(cameraX, depth, radius);
        var (minVertical, maxVertical) = TangentSlopes(cameraY, depth, radius);
        var tanHalf = Math.Tan(FieldOfViewDegrees * Math.PI / 360d);
        return new(
            0.5d + minHorizontal / (2d * tanHalf * aspect),
            0.5d + maxHorizontal / (2d * tanHalf * aspect),
            0.5d - maxVertical / (2d * tanHalf),
            0.5d - minVertical / (2d * tanHalf));
    }

    private static (double Min, double Max) TangentSlopes(
        double axisOffset,
        double depth,
        double radius)
    {
        if (radius <= 0d || depth <= radius)
            throw new ArgumentOutOfRangeException(nameof(radius));
        var root = radius * Math.Sqrt(axisOffset * axisOffset + depth * depth - radius * radius);
        var denominator = depth * depth - radius * radius;
        return ((axisOffset * depth - root) / denominator,
                (axisOffset * depth + root) / denominator);
    }
}
```

  If the seed misses a projection range, tune only `LocalPosition`/`LocalTarget` under the hard
  inequalities and record the final numbers in the evidence README. Do not weaken assertions.

- [ ] Replace duplicated binder constants with `TunnelCameraFraming` members. Align the real planet
  center to `(0, 0, CurrentPlaneZ)`, set the tunnel camera `Near` to the tested `NearClip`, and use
  `TryTickToZ` for both initial build and reposition.
  Tests use `ProjectSphereBounds` with the real maximum visible radius `2.06` (the scaled atmosphere
  rim) for height/crop rather than projecting non-silhouette top/bottom points.

- [ ] Change focused carousel angle to `180d`. Change initial-focus API to accept ordered descriptors
  and their activity flags, return the first active non-archived index, then first non-archived, then
  `-1`. Binder initialization calls it only when no valid focused identity exists; tick/activity
  changes update locked styling without auto-jumping focus.

- [ ] Run:

```bash
dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --no-restore \
  --filter FullyQualifiedName~TunnelCameraFramingTests
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore \
  --filter FullyQualifiedName~TunnelCorridorLayoutTests
```

  Expected: both aspect ratios, depth mapping, left focus, fallback, and no-auto-jump tests pass.

- [ ] Run both complete projects and commit:

```bash
git add project/plugins/App.Presentation/Tunnel/TunnelCameraFraming.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Corridors.cs \
  project/plugins/App.Timeline.Seam/TunnelCorridorLayout.cs \
  project/tests/App.Presentation.Tests/TunnelCameraFramingTests.cs \
  project/tests/App.Timeline.Tests/TunnelCorridorLayoutTests.cs
git commit -m "feat(tunnel): establish asymmetric current plane"
```

## Task 7 — bounded 3D snapshot-sphere corridors and axial cues

**Paths:** `TunnelPresentationBinder.Corridors.cs:20-430`,
`SnapshotSphereFilmstripSink.cs`, `TunnelPresentationBinder.cs:537-590`, and relevant project files.

**Interfaces**

- Consume: five visible registry tracks, exactly four `TimelineFilmstrip.PlanSlots` per real
  filmstrip track, current-plane math, metadata cache, source policy, cheap
  `IService.GetGenerationProductsAsync().GraphRevision`.
- Produce: at most twenty `SphereMesh` samples, one material per sphere, unavailable sectors,
  current-plane chevrons/slice, and non-uniform depth/curvature cues.
- Preserve: immutable cached textures may be shared; materials, sphere nodes, and lens node may not.

- [ ] Extend `TimelineFilmstripTests.cs` RED: five visible track plans produce no more than twenty
  total slots and a partial range near `MaxTick` retains four planned ticks without changing the kb
  depth divisor. Extend `TunnelCameraFramingTests.cs` RED: past/out-of-kb samples are rejected and
  `requestedTick == currentTick` maps to `CurrentPlaneZ`.

- [ ] Resolve graph revision immediately before each ordinary rebuild with a method-local world
  service:

```csharp
private int? ResolveGraphRevision()
{
    var world = _registry.TryGet<FantaSim.App.World.IService>();
    return world?.GetGenerationProductsAsync().GraphRevision;
}
```

  At the rebuild call site use:

```csharp
var resolvedRevision = ResolveGraphRevision();
if (resolvedRevision is not > 0)
    return;
var graphRevision = resolvedRevision.Value;
```

  Run this after the constructor-established unavailable nodes exist and before issuing requests, so
  the early return leaves those states visible. Never retain `world`; capture the proven integer
  into `LayerFilmstripPreviewRequest.GraphRevision` and every request identity. The provider returns
  the same revision only when it remained stable through rendering; controller validation from Task
  3 rejects every other completion. Do not issue revision-zero requests that could alias a real
  generation.

- [ ] Replace quad creation with one `MeshInstance3D` using `SphereMesh`, one newly constructed
  `StandardMaterial3D`, one unavailable-sector root/label, and one
  `SnapshotSphereFilmstripSink`. Keep a typed frame binding containing requested tick, descriptor,
  material, sphere, unavailable root, and mount generation. Position its center with
  `TryTickToZ`; do not crop the texture or create a second planet document.

- [ ] Use shaded sphere materials
  (`ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel`, roughness above zero) and add sparse
  non-interactive chevrons plus a faint transparent slice at `CurrentPlaneZ`. Add two or more
  separated axial light/value cues and a depth gradient on the cylinder shell. None has an annular
  hit region or dial marker.

- [ ] Ensure rebuild/reset frees old sphere materials/nodes and cancels their waiters before clearing
  lists. Ordinary base-time textures remain unchanged during fine inspection; only material
  desaturation changes later in Task 9.

- [ ] Add seam assertions where feasible and rely on Task 10 for Godot-visible facts: node mesh type,
  unique material instance identity, shared texture identity on cache hit, unavailable state for
  rejected source, 5 x 4 bound, and one current plane.

- [ ] Run presentation/timeline projects and the full solution wrapper:

```bash
dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --no-restore
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore
task test
```

  Expected: all pass and no existing 2D filmstrip behavior changes.

- [ ] Commit:

```bash
git add project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Corridors.cs \
  project/plugins/App.Presentation/Tunnel/SnapshotSphereFilmstripSink.cs \
  project/tests/App.Presentation.Tests/TunnelCameraFramingTests.cs \
  project/tests/App.Timeline.Tests/TimelineFilmstripTests.cs
git commit -m "feat(tunnel): render bounded snapshot spheres"
```

## Task 8 — camera-relative two-ring instrument and deterministic outer phase

**Paths:** `project/plugins/App.Timeline.Seam/TunnelScrubMapper.cs`,
`TunnelGestureCoordinator.cs`, `TunnelPresentationBinder.Rings.cs:23-229`,
`TunnelPresentationBinder.Camera.cs`, `TunnelPresentationBinder.Input.cs:270-407`, and corresponding
mapper/coordinator tests.

**Interfaces**

- Consume: canonical controller tick or current drag `ClampedTargetTick`, the active tunnel camera,
  visible ring radii, focused binding activity.
- Produce: `CanonicalPhaseDegrees`, camera-local `InstrumentRoot` with three sibling roots, matching
  camera-local hit plane.
- Preserve: one strong gesture owner; outer release commits exactly once; inactive inner owns no
  gesture and leaves normal application input available.

- [ ] Add RED phase tests at zero, arbitrary ticks, exact kb boundary, multiple kb, external seek
  back to zero, playback tick, and drag preview target. Add a RED coordinator test changing the old
  expectation: inactive inner press returns unhandled with `TunnelGestureKind.None`.

- [ ] Add canonical phase to `TunnelScrubMapper`:

```csharp
public static double CanonicalPhaseDegrees(long tick, long unitTicks)
{
    if (unitTicks <= 0L)
        return 0d;
    var remainder = tick % unitTicks;
    if (remainder < 0L)
        remainder += unitTicks;
    return -360d * remainder / unitTicks;
}
```

  Preserve existing coarse mapping: clockwise `+360` maps to `+1 kb`, counter-clockwise `-360` to
  `-1 kb`, rounding before clamp, with one release commit.

- [ ] Rebuild the exact node hierarchy under the active `TunnelCamera`:

```text
TunnelCamera
└── InstrumentRoot
    ├── OuterRotationRoot
    ├── InnerRotationRoot
    └── ReadoutRoot
        ├── OuterReadout
        ├── InnerReadout
        ├── StatusReadout
        └── InspectionLensRoot
```

  `InstrumentRoot`, rotation roots, and readout root are siblings/children exactly as shown. Labels,
  status, tether, and lens never become descendants of a rotating root. Reparenting occurs only after
  the camera is inside the scene tree and is undone before camera/mount cleanup. Set
  `InstrumentRoot.Position` from `TunnelCameraFraming.InstrumentLocalAnchor`, keep unit scale and zero
  rotation, build both visible meshes from the four framing-policy radii, and keep their geometry on
  instrument-local `Z = 0`. This exact transform/radius contract is what Task 6 projects and what the
  hit test consumes.

- [ ] Replace gesture-relative outer visuals. During outer motion use
  `CanonicalPhaseDegrees(mapping.ClampedTargetTick, kb.UnitTicks)`; at press, release, cancellation,
  playback/external `OnTickChanged`, rebind, and rebuild use the live canonical controller tick.
  Seeking to zero must set rotation to zero rather than leaving the previous gesture pose.

- [ ] Update hit testing to the instrument-local plane used by visible geometry:

```csharp
private bool TryProjectToInstrumentPlane(
    Vector2 screenPosition,
    Camera3D camera,
    out Vector3 instrumentPoint)
{
    instrumentPoint = default;
    if (_instrumentRoot is null || !GodotObject.IsInstanceValid(_instrumentRoot))
        return false;
    var rayOriginGlobal = camera.ProjectRayOrigin(screenPosition);
    var rayEndGlobal = rayOriginGlobal + camera.ProjectRayNormal(screenPosition) * 1000f;
    var inverse = _instrumentRoot.GlobalTransform.AffineInverse();
    var origin = inverse * rayOriginGlobal;
    var end = inverse * rayEndGlobal;
    var direction = end - origin;
    if (Mathf.IsZeroApprox(direction.Z))
        return false;
    var t = -origin.Z / direction.Z;
    if (t < 0f)
        return false;
    instrumentPoint = origin + direction * t;
    return true;
}
```

  Use `instrumentPoint.X/Y` and the exact mesh radii for ring arbitration. Remove the old mount-local
  `RingPlaneZ` projection.

- [ ] For an inactive focused track, keep identity visible, set the text exactly to
  `inactive at current time`, dim/lock the inner mesh, and reject its press before input is marked
  handled. Empty registry says `No track`; the outer ring remains usable.

- [ ] Run:

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TunnelScrubMapperTests|FullyQualifiedName~TunnelGestureCoordinatorTests|FullyQualifiedName~TunnelRayHitMapperTests"
dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --no-restore \
  --filter FullyQualifiedName~TunnelCameraFramingTests
```

  Expected: canonical phase, existing coarse mapping, single ownership, inactive rejection, and
  camera-local projection policy pass.

- [ ] Run complete presentation/timeline suites and commit:

```bash
git add project/plugins/App.Timeline.Seam/TunnelScrubMapper.cs \
  project/plugins/App.Timeline.Seam/TunnelGestureCoordinator.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Camera.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Rings.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs \
  project/tests/App.Timeline.Tests/TunnelScrubMapperTests.cs \
  project/tests/App.Timeline.Tests/TunnelGestureCoordinatorTests.cs \
  project/tests/App.Timeline.Tests/TunnelRayHitMapperTests.cs
git commit -m "feat(tunnel): anchor two-ring cockpit instrument"
```

## Task 9 — focused inspection lens, desaturation, and cancellation integration

**Paths:** `FilmstripPreviewController.cs`, `TunnelPresentationBinder.Rings.cs`,
`TunnelPresentationBinder.Input.cs:177-269,407-441`, `TunnelPresentationBinder.Corridors.cs`,
`TunnelPresentationBinder.cs:107-181,308-432,591-620`, and fine/coordinator tests.

**Interfaces**

- Consume: `TunnelFineSamplePolicy.Map`, active descriptor/cadence/rung, fine lane request API,
  focused lens sink, frame bindings/materials.
- Produce: inspection lens sphere under `ReadoutRoot`, bucket-change requests, base-texture-preserving
  desaturation, complete reset/cancel behavior.
- ALC boundary: request APIs receive primitives, request records, cancellation tokens, and a bounded
  sink. They receive no bundle callback predicate; binder calls cancel before sever and waits for
  provider unwind through the existing cancellation-aware path.

- [ ] Extend `TunnelFineRequestSchedulerTests.cs` RED for a non-zero first inspection offer,
  duplicate-bucket suppression, cancellation/reset, and ordinary-lane-independent state. Extend
  `T3PurityTests.cs` RED so production inner branches fail the audit if they call `PushTick`. Then add
  this exact consumer API to the internal `FilmstripPreviewController`:

```csharp
public void RequestFineTexture(
    LayerFilmstripPreviewRequest request,
    int mountGeneration,
    long fineEpoch,
    long bucket,
    IFilmstripFrameSink sink);

public void CancelFineRequests();
```

  The scheduler tests prove bucket duplicates do not start work, a changed bucket cancels active,
  reset prevents stale apply, and the fine scheduler has no reference to ordinary queue state. Full
  presentation/timeline compilation validates the binder/controller call signature.

- [ ] Build one enlarged `SphereMesh` lens under `InspectionLensRoot`, with its own
  `StandardMaterial3D` and `SnapshotSphereFilmstripSink`. Label/tether it `inspection`. It shares only
  immutable cached texture objects; it does not reparent/replace a corridor sphere or the current
  planet.

- [ ] On active inner motion, map raw quantity using base tick captured at gesture start. Update the
  fractional readout/cursor on every owned motion. Do not request at the zero-angle press; once raw
  quantity is non-zero, call `RequestFineTexture` only when `TextureChanged` is true. This permits a
  first sub-tick motion to show the current texture in the lens without claiming time advancement.
  Build the request from the focused real descriptor/rung and sampled tick, pass live graph revision,
  mount generation, fine epoch, and bucket. `request.GraphRevision` is the only revision argument; do
  not add a duplicate integer that can disagree. Do not call `PushTick`.

- [ ] While raw fine quantity is non-zero, set non-focused sphere materials to a deterministic
  desaturated modulation and keep their `AlbedoTexture` references byte-for-byte identical. Focused
  corridor spheres also remain at base time; the lens alone receives sampled textures. Reset restores
  original material colors.

- [ ] Make `ResetFinePreview` perform all of: increment fine epoch, `CancelFineRequests`, clear pending
  bucket, free lens sink/node/material, restore ordinary material colors, reset mapper/coordinator,
  and update stationary readout. Invoke it before ordinary rebuild on base-time change and on focus
  change, disable, controller loss, world change, stage change, disposal, and gesture cancellation.

- [ ] Add a controller spy/seam test proving every inner press/motion/release/reset path leaves
  `PushTick` count at zero. Confirm `PushTick` appears only inside `ApplyOuterScrubAction` with:

```bash
rg -n "PushTick" project/plugins/App.Presentation/Tunnel
```

  Expected: the outer action is the only production call site.

- [ ] Run:

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TunnelFine|FullyQualifiedName~TunnelGestureCoordinatorTests"
dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --no-restore
```

  Expected: fine lane, cancellation, no-authority, material-state, and full presentation tests pass.

- [ ] Run both full projects and commit:

```bash
git add project/plugins/App.Timeline.Seam/FilmstripPreviewController.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Corridors.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Rings.cs \
  project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs \
  project/tests/App.Timeline.Tests/TunnelFineSamplePolicyTests.cs \
  project/tests/App.Timeline.Tests/TunnelFineRequestSchedulerTests.cs \
  project/tests/App.Timeline.Tests/TunnelGestureCoordinatorTests.cs \
  project/tests/App.Timeline.Tests/T3PurityTests.cs
git commit -m "feat(tunnel): add bounded fine inspection lens"
```

## Task 10 — build, fresh export, adversarial runtime gate, and durable deposit

**Paths:** all implementation paths, `Taskfile.yml`, `build/build.config.json`,
`vault/specs/evidence/2026-07-12-asymmetric-cockpit-tunnel-gate/`, and `AGENT-SUMMARY.md`.

**Interfaces**

- Consume: Taskfile/UnifyBuild source of truth, exported app `0.1.2`, command ingress, real OS mouse
  and keyboard, runtime/bundle logs.
- Produce: committed stdout, hashes, PID/start provenance, raw command JSON, structured gesture
  records, post-action screenshots, exact ALC collection excerpts, conclusions/negative results.
- Gate owner: lead session performs OS input, visual judgment, and provenance review; a delegated
  worker may build and prepare command packets but may not self-accept the visual gate.

- [ ] From a clean tree after Task 9, run the deterministic gate and tee complete stdout/stderr into
  the evidence directory rather than relying on terminal history:

```bash
set -o pipefail
E=vault/specs/evidence/2026-07-12-asymmetric-cockpit-tunnel-gate
mkdir -p "$E"
task restore 2>&1 | tee "$E/01-restore.log"
task test 2>&1 | tee "$E/02-test.log"
task bundle:stagetool:test 2>&1 | tee "$E/03-dual-copy-audit.log"
task build:godot:desktop 2>&1 | tee "$E/04-export.log"
task bundles 2>&1 | tee "$E/05-bundles.log"
task bundle:install 2>&1 | tee "$E/06-bundle-install.log"
```

  Expected: every command exits zero; the dual-copy audit reports no forbidden copies. Stop any
  obsolete pre-export app before launch, but leave the accepted fresh app open after the gate.

- [ ] Record immutable provenance and launch one fresh process:

```bash
OUT=/tmp/fantasim-asymmetric-cockpit-gate
E="$PWD/vault/specs/evidence/2026-07-12-asymmetric-cockpit-tunnel-gate"
APP="$PWD/build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/complete-app"
mkdir -p "$OUT"
shasum -a 256 "$APP" > "$E/executable.sha256"
shasum -a 256 "$PWD"/build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/bundles/*.pck \
  > "$E/installed-bundles.sha256"
remote__enabled=true nohup "$APP" > "$OUT/runtime-live.log" 2>&1 &
APP_PID=$!
printf '%s\n' "$APP_PID" > "$E/pid.txt"
ps -p "$APP_PID" -o lstart= > "$E/process-start.txt"
curl -fsS --retry-connrefused --retry 60 --retry-delay 1 \
  http://127.0.0.1:19292/health > "$E/health.json"
python3 tools/fantasim-cmd.py status > "$E/status-initial.json"
```

- [ ] Use OS window controls to set a real 16:9 size, enable through the command, capture the command
  response, then take a post-enable screenshot. Repeat at 16:10. There is no remote viewport-resize
  command, so do not claim aspect evidence from a synthetic projection alone.

```bash
python3 tools/fantasim-cmd.py cmd timeline.tunnel_view '{"enabled":true}' \
  > "$E/tunnel-enable.json"
python3 tools/fantasim-cmd.py cmd render.screenshot \
  '{"path":"/tmp/fantasim-asymmetric-cockpit-gate/cockpit-16x9.png"}' \
  > "$E/screenshot-16x9-command.json"
cp "$OUT/cockpit-16x9.png" "$E/cockpit-16x9.png"
python3 tools/fantasim-cmd.py cmd render.screenshot \
  '{"path":"/tmp/fantasim-asymmetric-cockpit-gate/cockpit-16x10.png"}' \
  > "$E/screenshot-16x10-command.json"
cp "$OUT/cockpit-16x10.png" "$E/cockpit-16x10.png"
```

  Visually require: left focus/instrument, on-axis planet projected into the right third and large/
  vertically cropped, visible near-interior lip/wall/two depth cues, one current plane, at most twenty honest
  spheres/unavailable sectors, exactly two rings, and stationary labels.

- [ ] Perform a real-mouse outer gesture. Save camera-debug before/after/after-no-button-motion JSON,
  structured ownership/motion/release log excerpt, and a post-release screenshot. Seek externally to
  zero and a non-zero tick and capture phase screenshots after each response:

```bash
python3 tools/fantasim-cmd.py cmd timeline.seek '{"tick":0}' > "$E/seek-zero.json"
python3 tools/fantasim-cmd.py cmd render.screenshot \
  '{"path":"/tmp/fantasim-asymmetric-cockpit-gate/seek-zero.png"}' \
  > "$E/seek-zero-screenshot-command.json"
cp "$OUT/seek-zero.png" "$E/seek-zero.png"
python3 tools/fantasim-cmd.py cmd timeline.seek '{"tick":60000000}' > "$E/seek-nonzero.json"
python3 tools/fantasim-cmd.py cmd render.screenshot \
  '{"path":"/tmp/fantasim-asymmetric-cockpit-gate/seek-nonzero.png"}' \
  > "$E/seek-nonzero-screenshot-command.json"
cp "$OUT/seek-nonzero.png" "$E/seek-nonzero.png"
```

  Require one outer commit, canonical requested/committed tick, zero phase after seek zero, and no
  camera/orbit change.

- [ ] Perform real-mouse active-inner and wall gestures after the final hit-plane changes. Record
  inner before/after canonical tick, fine/lens screenshot, wall focus before/after, stationary labels,
  structured logs, and byte-identical settled camera debug fields. Focus an inactive real track and
  capture the visible locked state. Real mouse is mandatory; remote commands cannot substitute.

- [ ] Press real F9 once with no key repeat. Save status/debug before and after plus the runtime log
  segment. Require the command path in the log, requested/effective agreement, and HUD matching
  effective state. Exercise one failed/missing-dependency activation and require HUD visible.

- [ ] While enabled, reload timeline and wait at least two seconds after the new face binds:

```bash
START=$(($(wc -l < "$OUT/runtime-live.log") + 1))
python3 tools/fantasim-cmd.py cmd resource.reload_bundle '{"bundleId":"timeline"}' \
  > "$E/reload-timeline.json"
sleep 2
tail -n +"$START" "$OUT/runtime-live.log" > "$E/reload-timeline.log"
python3 tools/fantasim-cmd.py cmd render.screenshot \
  '{"path":"/tmp/fantasim-asymmetric-cockpit-gate/post-timeline-reload.png"}' \
  > "$E/post-timeline-reload-screenshot-command.json"
cp "$OUT/post-timeline-reload.png" "$E/post-timeline-reload.png"
```

  Require tunnel still visible, 2D HUD hidden, and exact
  `Hot-reload: old ALC collected for bundle timeline`.

- [ ] Reload world while enabled. Require HUD show before teardown in the log ordering, tunnel remain
  disabled after load, previous camera restored, no stale apply, and exact world ALC collection.
  Explicitly re-enable, then reload stage and require the same safe-2D result and exact stage ALC
  collection. After stage Changed, poll until the new environment and real `PlanetBody` are visible,
  prove the stage log orders the successful planet-bind line before the tunnel-mounted line that
  immediately precedes `MarkMounted`, prove the tunnel
  remains effectively false, then explicitly enable and prove the prepared hidden mount activates.
  A tunnel-mounted line before a generation-valid `PlanetBody`, a partial mount, or exhaustion of the
  preparation retry burst fails the gate. Save `reload-world.json/.log`, `reload-stage.json/.log`,
  post-action screenshots, and separate `alc-timeline.txt`, `alc-world.txt`, `alc-stage.txt`. Fail on
  any `old ALC still pinned`.

- [ ] Re-enable and repeat outer, inner, and wall real-mouse gestures after all three reloads. This
  proves rebuilt hit planes, F9 service resolution, and fine cancellation do not work only in the
  first generation.

- [ ] Hash every PNG and deposit a complete README:

```bash
GATE_END_LINE=$(wc -l < "$OUT/runtime-live.log")
head -n "$GATE_END_LINE" "$OUT/runtime-live.log" > "$E/runtime.log"
printf 'source=%s\nstartLine=1\nendLine=%s\n' \
  "$OUT/runtime-live.log" "$GATE_END_LINE" > "$E/runtime-window.txt"
shasum -a 256 "$E"/*.png > "$E/screenshots.sha256"
git rev-parse HEAD > "$E/implementation-head.txt"
git status --short > "$E/worktree-before-evidence-commit.txt"
```

  `README.md` records exact commands, UTC/local times, pass/fail per gate step, observed descriptor
  identities/rungs, all visual judgments, negative results, PID/start time, log window, and links to
  every raw file. `runtime.log` is the frozen gate window copied from the still-running process log;
  the process continues writing only to `/tmp`. The README must state that current production rungs
  were homogeneous `ka` if still true and must not claim heterogeneous behavior.

- [ ] Update `AGENT-SUMMARY.md` with durable authority/provenance/lifecycle conclusions and all
  empirical tuning values. Run final checks and commit only evidence/summary:

```bash
task test
task bundle:stagetool:test
git add vault/specs/evidence/2026-07-12-asymmetric-cockpit-tunnel-gate AGENT-SUMMARY.md
git diff --cached --check
git commit -m "docs(tunnel): record asymmetric cockpit gate"
git status --short --branch
```

  Expected: clean tree after commit, all tests/audit green, accepted exported process still open, and
  evidence files correspond to post-action state from that PID.

---

## Requirement-to-task coverage

| Design section | Implemented/tested by | Gate evidence |
|---|---|---|
| 1. Goal and named gate | Tasks 1-10 | Complete evidence directory and README verdict |
| 2. Locked decisions | Tasks 4, 7, 8, 9 | Two rings, real planet, honest spheres, unchanged authoritative tick |
| 3. Spatial contract | Tasks 6-8 | 16:9/16:10 screenshots, projection tests, current-plane/depth cues |
| 4. Snapshot-sphere data | Tasks 3, 4, 7 | Source/cache tests, sphere/material/cache-hit evidence |
| 5. Focus/time semantics | Tasks 5, 8, 9 | Seek phase, active/inactive inner, lens and unchanged tick |
| 6. Gestures | Tasks 1, 8, 9 | Real outer/inner/wall records and unchanged orbit fields |
| 7. Lifecycle/HUD | Tasks 1, 2, 9 | F9 plus timeline/world/stage reload logs/screenshots/ALC excerpts |
| 8. Component boundaries | Tasks 1-9 | Pure-policy tests, CodeGraph review, dual-copy audit |
| 9. Degraded states | Tasks 1, 2, 4, 5, 9 | Failed activation, unavailable preview, empty/inactive focus |
| 10. TDD contract | Every task | RED/GREEN command records and final complete suites |
| 11. Exported gate | Task 10 | All named raw files, hashes, screenshots, logs, and verdict |
| 12. Deferrals | Global constraints and list below | README explicitly avoids deferred claims |
| 13. Negative conclusions | Global constraints and list below | Final audit plus README negative results |

## Final evidence manifest

- [ ] `README.md`
- [ ] `01-restore.log` through `06-bundle-install.log`
- [ ] `runtime.log`, `runtime-window.txt`, `health.json`, `status-initial.json`
- [ ] `pid.txt`, `process-start.txt`, `implementation-head.txt`
- [ ] `executable.sha256`, `installed-bundles.sha256`, `screenshots.sha256`
- [ ] `tunnel-enable.json`, `seek-zero.json`, `seek-nonzero.json`
- [ ] `screenshot-16x9-command.json`, `screenshot-16x10-command.json`,
  `seek-zero-screenshot-command.json`, `seek-nonzero-screenshot-command.json`,
  `post-timeline-reload-screenshot-command.json`
- [ ] `cockpit-16x9.png`, `cockpit-16x10.png`, `seek-zero.png`, `seek-nonzero.png`,
  `post-timeline-reload.png`, `post-world-reload.png`, `post-stage-reload.png`
- [ ] `reload-timeline.json`, `reload-world.json`, `reload-stage.json`
- [ ] `reload-timeline.log`, `reload-world.log`, `reload-stage.log`
- [ ] `alc-timeline.txt`, `alc-world.txt`, `alc-stage.txt`
- [ ] `worktree-before-evidence-commit.txt`
- [ ] `outer-camera-before.json`, `outer-camera-after.json`, `outer-camera-after-move.json`,
  `outer-gesture.log`, `outer-post-release.png`
- [ ] `inner-tick-before.json`, `inner-tick-after.json`, `inner-camera-before.json`,
  `inner-camera-after.json`, `inner-camera-after-move.json`, `inner-gesture.log`, `inner-lens.png`
- [ ] `inactive-inner.png`, `wall-focus-before.json`, `wall-focus-after.json`,
  `wall-camera-before.json`, `wall-camera-after.json`, `wall-camera-after-move.json`, `wall-gesture.log`
- [ ] `f9-status-before.json`, `f9-status-after.json`, `f9-command.log`,
  `failed-activation.json`, `failed-activation.png`

## Later OpenCode packet recommendations

Each packet requires a separate explicit user authorization before external CLI dispatch.

1. **Lifecycle packet (Tasks 1-2):** allow only presentation/timeline contracts, binder lifecycle/input,
   timeline plugin/face, and named tests. Success gate is focused lifecycle/HUD suites; verifier is a
   fresh read-only agent checking ALC retention and stage/world ordering.
2. **Provenance packet (Tasks 3-4):** allow filmstrip sink/cache/controller, source/sphere sink, and
   named tests. Success gate is every-field inequality plus metadata-on-cache-hit and rejected-source
   tests; verifier checks no 2D behavior/source filtering changed.
3. **Fine-lane packet (Task 5):** allow only fine policies/controller/tests. Success gate is blocking
   provider proof of one active, cancellation-before-replacement, ten-per-second bound, and stale drop;
   verifier inspects callback/CTS ALC safety.
4. **Spatial/render packet (Tasks 6-9):** allow tunnel policies/partials and named pure tests. Success
   gate is both unit projects plus Godot seam assertions; verifier checks one current plane, two ring
   roots, material uniqueness, and no inner `PushTick`.
5. **Gate packet (Task 10):** worker may build/package and prepare commands only. Lead retains OS input,
   screenshot judgment, reload operation, evidence hash review, and final acceptance.

## Explicit deferrals retained

- Authoritative layer-local time or inner-ring world mutation.
- Heterogeneous per-track rung/cadence semantics beyond real current metadata.
- More than four samples per visible track, adaptive density, carousel inertia, and final polish.
- 3D graph/generic presenters and new real atmosphere/magma/stagnant-lid providers.
- Branch/edit/simulate behavior or ECS worlds per snapshot frame.
- Automatic tunnel restoration after world or stage reload.

## Negative conclusions retained

- Keep the real planet on the cylinder axis; composition comes from the camera.
- Do not use `WorldGlobeSnapshot` or another `PlanetPresentationDocument` for layer spheres.
- Texture identity includes requested tick; snapshot tick is provenance, not a substitute.
- Procedural placeholders remain unavailable and never become world-looking spheres.
- Fine work uses active cancellation/backpressure and never masquerades as authoritative time.
- Do not add a third/current-time/per-track ring.
- F9 never bypasses the command; HUD derives from effective state only.
- Do not retain face context across ALC generations.
- Tests/builds do not close the goal without the complete exported evidence manifest.

## Plan self-review before execution

- [ ] Search this plan for incomplete-marker vocabulary and omitted-body punctuation; resolve every
  accidental hit before dispatch.
- [ ] Re-check every introduced type/signature is defined before first consumption and matches all
  later tasks.
- [ ] Re-check every create/modify/delete path against the file-responsibility map.
- [ ] Re-check synchronous activation does not wait for the existing deferred mount and failed enable
  cannot auto-resurrect.
- [ ] Re-check every CTS/deferred callback unwinds or epoch-drops before collectible sever.
- [ ] Run `git diff --check -- vault/plans/2026-07-12-asymmetric-cockpit-tunnel-plan.md`.
