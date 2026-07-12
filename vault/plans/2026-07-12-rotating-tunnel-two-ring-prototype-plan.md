# Rotating tunnel two-ring prototype — implementation plan

> **Status:** COMPLETED — implemented through OpenCode `zai-coding-plan/glm-5.2`, independently
> corrected and reviewed, and accepted by the exported-app evidence gate on 2026-07-12. Evidence:
> [`../specs/evidence/2026-07-12-rotating-tunnel-two-ring-prototype/README.md`](../specs/evidence/2026-07-12-rotating-tunnel-two-ring-prototype/README.md).
>
> **For the implementing agent:** execute tasks in order. For every behavioral task, add the named
> tests first, run the focused command and observe the intended RED, then add the minimum production
> code and observe GREEN. Commit each coherent green task with a Conventional Commit; do not push.
> Do not invent production tracks, rung metadata, textures, or a second globe. Stop and report if a
> plan instruction contradicts the approved spec or current source rather than silently changing the
> product contract.

**Governing design:**
[`../specs/2026-07-12-rotating-tunnel-two-ring-prototype-design.md`](../specs/2026-07-12-rotating-tunnel-two-ring-prototype-design.md)

**Supersedes for this prototype:** the flat-annulus geometry, ladder/current-tick ring model, and
horizontal-pixel scrub in
[`2026-07-11-tunnel-slice1-plan.md`](2026-07-11-tunnel-slice1-plan.md).

## Outcome and non-negotiable acceptance gate

Deliver one real hollow-cylinder timeline view with a five-track carousel and exactly two physical
rings:

1. The outer coarse ring maps one logical clockwise revolution to exactly one canonical `kb` and one
   counter-clockwise revolution to exactly minus one `kb`.
2. The inner ring is always bound to the real registry descriptor in the bottom-center slot and uses
   that descriptor's real rung. It is a view-only fine preview in this prototype and never mutates
   authoritative time.
3. The cylinder wall rotates continuously during a real mouse drag and snaps cyclically in 30°
   steps; only five unique tracks are mounted, even when the registry contains more.
4. A fresh oblique screenshot visibly separates mouth, interior wall, axial content, far throat, and
   the existing real globe.
5. Accepted tunnel gestures never move or strand the resident globe orbit controls.
6. A live `world` reload while the tunnel is enabled unloads/loads cleanly and emits the exact
   `Hot-reload: old ALC collected for bundle world` line.

Build success without real mouse, pixels, pose snapshots, and ALC collection is not completion.

## Verified baseline and topology

- Baseline run on 2026-07-12:
  `dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --filter
  'FullyQualifiedName~Tunnel|FullyQualifiedName~TimelineScrubCoalescer' --no-restore` → **34/34
  passed**.
- `FantaSim.App.Presentation` is collectible in `world.pck` according to
  `project/hosts/complete-app/config/collectible-bundles.json`; ignore the stale resident comment in
  `App.Presentation.csproj`.
- `FantaSim.App.Timeline.Seam` and `FantaSim.App.Camera.Seam` are resident/shared. Any change there
  requires one full exported-host rebuild and fresh launch. Later binder look-dev iterations use
  only `task bundle:world && task bundle:install` against the still-running app.
- `LayerTrackRegistrySnapshot.Tracks` is already stable-sorted by `TrackSetNodeHandler`: lane rank,
  sphere id, then layer id. Preserve that order and drop only `LayerTrackStates.Archived`.
- Every current production descriptor has `TimeDomain.Rung == "ka"`. Implement generic rung
  rebinding and test integral/sub-tick ladder entries, but runtime evidence must say that the real
  inputs are all `ka`; do not fabricate `jw`, `ju`, or any demo track.
- `TimelineScrubCoalescer` already has the needed `Press`, `Motion`, `ConsumeFrame`, `Release`, and
  `Cancel` API. The present binder is wrong because it never consumes a frame and releases a stale
  cached tick; do not change the coalescer contract to hide those caller defects.
- The existing `PlanetPresentationBinder.ActiveRoot` is in the same collectible ALC. Expose a
  read-only `PlanetBody` accessor/provider, but keep `TunnelMount` under Stage `Environment` rather
  than parenting it under the replaceable `PlanetPresentation` tree. Position the tunnel globally so
  local `ThroatZ` coincides with `PlanetBody.GlobalPosition`; a routine planet rebind must not free the
  tunnel as collateral.

## Source grounding

Use current source and these versioned/official APIs rather than reconstructing Godot behavior from
memory:

- Godot 4.7 input flow: `_Input` precedes GUI and `_UnhandledInput`; an accepted event is stopped by
  `Viewport.SetInputAsHandled()`:
  <https://docs.godotengine.org/en/4.7/tutorials/inputs/inputevent.html>
- Godot 4.7 `Viewport.SetInputAsHandled`:
  <https://docs.godotengine.org/en/4.7/classes/class_viewport.html#class-viewport-method-set-input-as-handled>
- Godot 4.7 `Camera3D.ProjectRayOrigin`, `ProjectRayNormal`, `UnprojectPosition`, and
  `IsPositionBehind`:
  <https://docs.godotengine.org/en/4.7/classes/class_camera3d.html>
- Godot procedural `ArrayMesh`: resize to `Mesh.ArrayType.Max`, supply equal-length vertices,
  normals, and UVs, use clockwise front-face winding, then `AddSurfaceFromArrays`:
  <https://docs.godotengine.org/en/4.7/tutorials/3d/procedural_geometry/arraymesh.html>
- .NET midpoint behavior: use `Math.Round(value, MidpointRounding.AwayFromZero)` exactly once at the
  outer target boundary and for carousel snapping:
  <https://learn.microsoft.com/en-us/dotnet/api/system.midpointrounding>

Project precedents to copy:

- owned press versus strong motion/release: `project/plugins/App.Camera.Seam/GlobeOrbitControls.cs`;
- per-frame scrub coalescing: `project/plugins/App.Timeline.Seam/TimelineFace.Input.cs`;
- schedule lookup/active join: `project/plugins/App.Timeline.Seam/TimelineFace.Lanes.cs`;
- registry source ordering: `project/plugins/App.World.Composition/TrackPipelineNodeCatalog.cs`;
- ALC-safe preview cancellation: `FilmstripPreviewController` and the current tunnel binder;
- mesh normal calculation/array construction: `PlateBoundaryFocusRenderer` and
  `BoundarySectionRenderer`;
- key-repeat-safe shortcut: `ViewToggleBar.cs`.

## End-state file map

### Modify

- `project/plugins/App.Timeline.Seam/TunnelScrubMapper.cs`
- `project/plugins/App.Timeline.Seam/TunnelCorridorLayout.cs`
- `project/tests/App.Timeline.Tests/TunnelScrubMapperTests.cs`
- `project/tests/App.Timeline.Tests/TunnelCorridorLayoutTests.cs`
- `project/tests/App.Timeline.Tests/TimelineScrubCoalescerTests.cs`
- `project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Rings.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Corridors.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Camera.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelInputRelay.cs`
- `project/plugins/App.Presentation/PlanetPresentationBinder.cs`
- `project/plugins/App.Presentation/PresentationComposition.cs`
- `project/plugins/App.Presentation/PresentationPlugin.cs`
- `project/plugins/App.Camera.Seam/GlobeOrbitControls.cs`
- `project/plugins/App.Camera.Seam/CameraRig.cs`
- `AGENT-SUMMARY.md`

### Create

- `project/plugins/App.Timeline.Seam/TunnelFinePreviewMapper.cs`
- `project/plugins/App.Timeline.Seam/TunnelGestureCoordinator.cs`
- `project/plugins/App.Timeline.Seam/TunnelRayHitMapper.cs`
- `project/plugins/App.Timeline.Seam/TunnelTrackActivity.cs`
- `project/tests/App.Timeline.Tests/TunnelFinePreviewMapperTests.cs`
- `project/tests/App.Timeline.Tests/TunnelGestureCoordinatorTests.cs`
- `project/tests/App.Timeline.Tests/TunnelRayHitMapperTests.cs`
- `project/tests/App.Timeline.Tests/TunnelTrackActivityTests.cs`
- `vault/specs/evidence/2026-07-12-rotating-tunnel-two-ring-prototype/README.md`

### Delete after callers migrate

- `project/plugins/App.Timeline.Seam/TunnelDepthMapper.cs`
- `project/tests/App.Timeline.Tests/TunnelDepthMapperTests.cs`
- the `TunnelDepthMapper.cs` compile link in `App.Timeline.Tests.csproj`

`TunnelDepthMapper` is radius compression for the diagnosed flat dartboard. Reusing it for axial Z
would preserve the error behind a misleading name.

---

## Task 1 — outer dial mapping and hit arbitration (pure RED → GREEN)

**Files:** modify `TunnelScrubMapper.cs`, replace `TunnelScrubMapperTests.cs`.

Replace the pixel-delta API with this complete public surface:

```csharp
namespace FantaSim.App.Timeline.Seam;

public enum TunnelHitRegion
{
    None = 0,
    OuterRing = 1,
    InnerRing = 2,
    Wall = 3,
}

public readonly record struct TunnelOuterTickMapping(
    double AccumulatedDegrees,
    TimelineLadderRung Rung,
    double RawTickDelta,
    long RoundedTickDelta,
    long RoundedTargetTick,
    long ClampedTargetTick);

public static class TunnelScrubMapper
{
    public const string OuterRungSymbol = "kb";

    public static TimelineLadderRung ResolveOuterRung();

    public static TunnelOuterTickMapping MapOuterAngleToTick(
        double accumulatedDegrees,
        long pressTick,
        long maxTick);

    public static double NormalizeClockwiseDeltaDegrees(
        double previousPointerDegrees,
        double currentPointerDegrees);

    public static TunnelHitRegion ResolveHitRegion(
        bool outerRingHit,
        bool innerRingHit,
        bool wallHit);
}
```

Implementation rules:

- Resolve the actual `kb` object from `TimelineModel.GetLadderRungs()`; fail loudly if the canonical
  ladder lacks `kb` instead of substituting another unit.
- `rawDelta = accumulatedDegrees / 360d * rung.UnitTicks`.
- Apply `AwayFromZero` once, use saturating double→long conversion and saturating addition for
  pathological finite angles, then clamp to `[0, Math.Max(0, maxTick)]`. Reject NaN/infinity with
  `ArgumentOutOfRangeException` rather than letting a cast create false evidence.
- A single ring hit wins over its backing wall. Outer+inner simultaneously is ambiguous and returns
  `None`; none returns `None`.
- Normalize a pointer-angle delta into `[-180,+180]`, then negate it so clockwise is positive.
  Reject non-finite inputs. In particular `previous=-179,current=179` is `+2°` clockwise, not a
  `-358°` jump, and the reverse crossing is `-2°`.

Write these tests first:

- `ResolveOuterRung_ReturnsTimelineModelsKbEntry`
- `MapOuterAngleToTick_PositiveFullRevolution_AddsExactlyOneKb`
- `MapOuterAngleToTick_NegativeFullRevolution_SubtractsExactlyOneKb`
- `MapOuterAngleToTick_FractionalAndMultipleRevolutions_MapProportionally`
- `MapOuterAngleToTick_PositiveAndNegativeMidpoints_RoundSymmetricallyAwayFromZero`
- `MapOuterAngleToTick_RoundsBeforeClamping`
- `MapOuterAngleToTick_ClampsAtBothTimelineBounds`
- `MapOuterAngleToTick_PathologicalFiniteAngles_SaturateBeforeFinalClamp`
- `MapOuterAngleToTick_NonFiniteAngle_Throws`
- `NormalizeClockwiseDeltaDegrees_UnwrapsBothDirectionsAcrossBranchCut`
- `NormalizeClockwiseDeltaDegrees_ClockwiseIsPositive`
- `NormalizeClockwiseDeltaDegrees_NonFiniteInputThrows`
- `ResolveHitRegion_EachSingleCandidate_IsExclusive`
- `ResolveHitRegion_RingWinsOverBackingWall`
- `ResolveHitRegion_AmbiguousRings_ReturnsNone`

RED/GREEN command:

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
  --filter 'FullyQualifiedName~TunnelScrubMapperTests'
```

Commit: `feat(timeline): map tunnel outer dial to canonical kb`

## Task 1B — oblique ray hit math (pure RED → GREEN)

**Files:** create `TunnelRayHitMapper.cs` and `TunnelRayHitMapperTests.cs`; add a direct compile link
to `App.Timeline.Tests.csproj`.

Use Godot-free doubles so the risky root selection is disproved without a viewport:

```csharp
public readonly record struct TunnelPoint3(double X, double Y, double Z);
public readonly record struct TunnelRay3(TunnelPoint3 Origin, TunnelPoint3 Direction);

public static class TunnelRayHitMapper
{
    public static bool TryIntersectMouthPlane(
        TunnelRay3 ray,
        double mouthZ,
        out TunnelPoint3 point);

    public static bool TryIntersectCylinder(
        TunnelRay3 ray,
        double radius,
        double throatZ,
        double mouthZ,
        out TunnelPoint3 point);
}
```

`TryIntersectMouthPlane` rejects a near-parallel ray and any `t < 0`. Cylinder intersection solves
`a*t²+b*t+c=0` for `x²+y²=radius²`, sorts the two real roots, and returns the nearest non-negative
root whose computed Z is inclusively within `[throatZ,mouthZ]`; if the near root is outside that Z
interval it must test the far root before returning false. Reject non-positive/non-finite radius,
non-finite vectors, and reversed Z bounds.

Tests: forward mouth hit, behind-camera mouth miss, parallel miss, nearest forward cylinder root,
negative-root rejection, near-root-outside/far-root-inside selection, both roots outside Z range,
tangent hit, ray parallel to the cylinder axis, and invalid arguments.

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
  --filter 'FullyQualifiedName~TunnelRayHitMapperTests'
```

Commit: `feat(timeline): define oblique tunnel ray hits`

## Task 2 — five-slot carousel and bottom-center focus (pure RED → GREEN)

**Files:** replace flat wedge responsibilities in `TunnelCorridorLayout.cs`; replace obsolete wedge
tests in `TunnelCorridorLayoutTests.cs` but retain rung-resolution coverage.

Use this surface:

```csharp
public static class TunnelCorridorLayout
{
    public const int VisibleTrackSlots = 5;
    public const double TrackSlotPitchDegrees = 30d;
    public const double BottomFocusAngleDegrees = -90d;

    public readonly record struct TunnelTrackSlot(
        LayerTrackDescriptor Descriptor,
        int RelativeSlot,
        double CenterAngleDegrees)
    {
        public bool IsFocused => RelativeSlot == 0;
    }

    public readonly record struct TunnelCarouselSnap(
        long StepDelta,
        int FocusIndex,
        double SnappedAngleDegrees);

    public static IReadOnlyList<LayerTrackDescriptor> SelectSourceTracks(
        LayerTrackRegistrySnapshot snapshot);
    public static int InitialFocusIndex(int trackCount);
    public static int NormalizeFocusIndex(int focusIndex, int trackCount);
    public static LayerTrackDescriptor? ResolveFocusedTrack(
        IReadOnlyList<LayerTrackDescriptor> tracks,
        int focusIndex);
    public static IReadOnlyList<TunnelTrackSlot> BuildFocusedWindow(
        IReadOnlyList<LayerTrackDescriptor> tracks,
        int focusIndex,
        double accumulatedDegrees = 0d);
    public static TunnelCarouselSnap SnapFocus(
        int focusIndex,
        int trackCount,
        double accumulatedDegrees);
    public static TimelineLadderRung ResolveCorridorRung(
        string? trackRungSymbol,
        TimelineLadderRung globalFallback);
}
```

Behavior:

- Preserve snapshot order; drop only exact archived state.
- Empty focus is `-1`; initial non-empty focus is `0`.
- Populate candidates in the exact order focus `0`, previous `-1`, next `+1`, second previous `-2`,
  second next `+2`; deduplicate by `(SphereId, LayerId)`; return final slots sorted `-2..+2`.
- Compute
  `CenterAngleDegrees = BottomFocusAngleDegrees + relativeSlot * 30d - accumulatedDegrees`.
  Therefore +30° clockwise brings the next (`+1`) track into the bottom focus before the focus index
  advances.
- Snap with `Round(degrees / 30d, AwayFromZero)`. `StepDelta` is `long`; normalize it before cyclic
  integer indexing. `trackCount <= 1` is a hard no-op.

Write explicit `N=0..6` tests for the required slot sets, stable identities, uniqueness, initial and
cyclic focus, positive/negative 15° threshold, symmetric multi-step drags, +30° visual polarity, and
bottom-center selection. Retain tests for known rung, null fallback, and unknown fallback.

RED/GREEN command:

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
  --filter 'FullyQualifiedName~TunnelCorridorLayoutTests'
```

Commit: `feat(timeline): define tunnel focused track carousel`

## Task 2B — regime-active track join (pure RED → GREEN)

**Files:** create `TunnelTrackActivity.cs` and `TunnelTrackActivityTests.cs`; add the direct compile
link to `App.Timeline.Tests.csproj`.

```csharp
public static class TunnelTrackActivity
{
    public static bool IsActive(
        LayerTrackDescriptor descriptor,
        long tick,
        SphereRegimeSchedule geosphereSchedule,
        SphereRegimeSchedule atmosphereSchedule);
}
```

Select `geosphereSchedule` for exact sphere id `"geosphere"`, `atmosphereSchedule` for exact
`"atmosphere"`, and return false for unknown spheres. For known spheres, return true only when
`schedule.RegimeAt(tick)?.ActiveLayers` contains a `LayerId` whose `.Value` equals
`descriptor.LayerId` with `StringComparison.Ordinal`. Tests cover active/inactive layers in both
schedules, a regime boundary, an unknown sphere, and a descriptor whose layer id differs only by
case. This activity flag styles/enables; it never filters the registry list.

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
  --filter 'FullyQualifiedName~TunnelTrackActivityTests'
```

Commit: `feat(timeline): resolve tunnel track activity by regime`

## Task 3 — provisional fine preview and one-owner gesture state (pure RED → GREEN)

**Files:** create both mapper/coordinator files and tests; add compile links; extend existing
`TimelineScrubCoalescerTests.cs`. Do not modify the coalescer production file.

Add these compile links:

```xml
<Compile Include="..\..\plugins\App.Timeline.Seam\TunnelFinePreviewMapper.cs"
         Link="TunnelFinePreviewMapper.cs" />
<Compile Include="..\..\plugins\App.Timeline.Seam\TunnelGestureCoordinator.cs"
         Link="TunnelGestureCoordinator.cs" />
<Compile Include="..\..\plugins\App.Timeline.Seam\TunnelRayHitMapper.cs"
         Link="TunnelRayHitMapper.cs" />
<Compile Include="..\..\plugins\App.Timeline.Seam\TunnelTrackActivity.cs"
         Link="TunnelTrackActivity.cs" />
```

Fine-preview surface:

```csharp
public readonly record struct TunnelFineTrackBinding(
    LayerTrackDescriptor? Descriptor,
    TimelineLadderRung? Rung,
    bool IsActive)
{
    public string OwnerLabel => Descriptor?.DisplayName ?? "No track";
    public bool CanAdjust => Descriptor is not null && Rung is not null && IsActive;
}

public readonly record struct TunnelFinePreview(
    TunnelFineTrackBinding Binding,
    double AccumulatedDegrees,
    double RungUnits,
    double RawTickQuantity,
    long? IntegralTickDelta,
    double CursorZ)
{
    public bool IsFractionalPresentation => Binding.CanAdjust && IntegralTickDelta is null;
}

public static class TunnelFinePreviewMapper
{
    public const double MaxAbsoluteDegrees = 360d;
    public static TunnelFineTrackBinding Bind(
        LayerTrackDescriptor? descriptor,
        bool isActive,
        TimelineLadderRung globalFallback);
    public static TunnelFinePreview Map(
        TunnelFineTrackBinding binding,
        double accumulatedDegrees,
        double railCenterZ,
        double railHalfLength);
    public static TunnelFinePreview Reset(
        TunnelFineTrackBinding binding,
        double railCenterZ,
        double railHalfLength);
}
```

Map rules: clamp to ±360°; `RungUnits = degrees / 360`; `RawTickQuantity = RungUnits * UnitTicks`;
`CursorZ = centerZ - RungUnits * abs(halfLength)`. Missing/inactive bindings stay centered and zero.
Only set `IntegralTickDelta` when the mathematical quantity is whole within a small numeric tolerance;
never round a fractional/sub-tick quantity into authority. Choose integral and sub-tick test rungs by
querying the actual ladder on each test, not by reconstructing ratios or naming an invalid unit.

Gesture surface:

```csharp
public enum TunnelGestureKind { None = 0, OuterRing = 1, InnerRing = 2, Wall = 3 }

public enum TunnelFineResetReason
{
    FocusChanged = 0,
    BaseTimeChanged = 1,
    Disabled = 2,
    ControllerLost = 3,
    BundleTeardown = 4,
    Disposed = 5,
}

public readonly record struct TunnelGesturePressContext(
    long CurrentTick,
    long MaxTick,
    int FocusIndex,
    int TrackCount,
    TunnelFineTrackBinding FineBinding,
    double FineRailCenterZ,
    double FineRailHalfLength);

public readonly record struct TunnelGestureUpdate(
    bool Handled,
    TunnelGestureKind Gesture,
    double AccumulatedDegrees,
    TimelineScrubAction ScrubAction,
    TunnelOuterTickMapping? OuterTick,
    TunnelFinePreview? FinePreview,
    TunnelCorridorLayout.TunnelCarouselSnap? CarouselSnap,
    TunnelFineResetReason? FineResetReason);

public sealed class TunnelGestureCoordinator
{
    public TunnelGestureKind ActiveGesture { get; }
    public bool OwnsGesture { get; }
    public double AccumulatedDegrees { get; }
    public TunnelGestureUpdate Press(TunnelHitRegion hitRegion, TunnelGesturePressContext context);
    public TunnelGestureUpdate Motion(double signedClockwiseDeltaDegrees);
    public TunnelGestureUpdate ConsumeFrame();
    public TunnelGestureUpdate Release();
    public TunnelGestureUpdate Cancel();
    public TunnelGestureUpdate ResetFinePreview(
        TunnelFineResetReason reason,
        TunnelFineTrackBinding binding,
        double railCenterZ,
        double railHalfLength);
}
```

Coordinator invariants:

- One owner at a time. `None` is unhandled. A press on the visible but inactive inner ring is handled
  so it cannot start globe orbit, but the fine result remains centered/inert.
- Outer press calls `coalescer.Press(currentTick)`. Motion stores accumulated signed clockwise angle,
  maps the latest target, and calls `Motion`; `ConsumeFrame` returns the latest preview once. Release
  maps the latest accumulated angle again and calls `Release(latest.ClampedTargetTick)` even if the
  pending motion was not consumed. A second release is unhandled/no commit.
- Inner motion returns only fine preview. Wall release returns only carousel snap. Neither emits a
  timeline scrub action.
- Cancel clears ownership and pending coalescer state without commit. Explicit fine resets return a
  centered preview plus the supplied reason.

Required coordinator tests include press exclusivity, refused owner replacement, unwrap-ready signed
deltas in both directions, per-frame latest-only echo, latest-value release, exactly one commit,
cancel, inner non-authority, inactive inner, wall snap, and all specified reset reasons. Extend the
direct coalescer tests to pin press/motion/consume/release/cancel behavior.

RED/GREEN command:

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
  --filter 'FullyQualifiedName~TunnelFinePreviewMapperTests|FullyQualifiedName~TunnelGestureCoordinatorTests|FullyQualifiedName~TimelineScrubCoalescerTests'
```

Then run all tunnel pure tests and remove obsolete depth files/link:

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
  --filter 'FullyQualifiedName~Tunnel|FullyQualifiedName~TimelineScrubCoalescer'
```

Commit: `feat(timeline): coordinate tunnel ring and wall gestures`

## Task 4 — atomic orbit diagnostics (RED → GREEN)

### 4A. Camera evidence seam

**Files:** modify `GlobeOrbitControls.cs`, `CameraRig.cs`; add/extend camera tests only where the
logic can be tested without a Godot tree.

Add:

```csharp
internal CameraOrbitSnapshot DiagOrbitSnapshot => _orbit.Current;
```

In `CameraRig.DebugSnapshotImpl`, add stable fields to the existing dictionary without deleting
current diagnostics:

- `cameraPath` from `rig.Camera.GetPath().ToString()`;
- `cameraTransform`, a dictionary of twelve numeric scalars named `basisXX`, `basisXY`, `basisXZ`,
  `basisYX`, `basisYY`, `basisYZ`, `basisZX`, `basisZY`, `basisZZ`, `originX`, `originY`, `originZ`;
- `activePcamPath` from the active pcam node path, and `activePcamTransform` using the same twelve
  scalar keys;
- `followTargetPath` from the target node path;
- `orbitYawDeg`, `orbitPitchDeg`, `orbitDistance` from the same atomic `DiagOrbitSnapshot`;
- `springLength`, `draggingNow`, `dragMotionsApplied`.

Do not implement diagnostics by invoking `camera.orbit`; that command reapplies state and is not a
read-only atomic observation.

Commands:

```bash
dotnet test project/tests/App.Camera.Tests/App.Camera.Tests.csproj
```

Commit: `feat(camera): expose atomic globe orbit proof`

## Task 5 — replace the dartboard with depth-tested cylinder geometry

**Files:** core binder, rings, corridors, camera, `PlanetPresentationBinder`, composition/plugin.
The visual geometry is accepted by exported pixels, but implement it only after Tasks 1–4 are green.

### 5A. Real globe alignment without duplicate binding

Add to `PlanetPresentationBinder`:

```csharp
internal Node3D? PlanetBody
    => _activeRoot?.GetNodeOrNull<Node3D>("PlanetBody");
```

Extend `PresentationComposition.CreateTunnelPresentation` and `TunnelPresentationBinder` with
`Func<Node3D?> planetBodyProvider`. In `PresentationPlugin`, pass a provider that resolves
`(_presentation as PlanetPresentationBinder)?.PlanetBody` at execution time.

Keep `TunnelMount` under Stage `Environment`. In `EnsureMounted`/refresh, set a global transform with
identity basis and origin `planetBody.GlobalPosition + Vector3.Back * TunnelDepth` (Godot `Back` is
positive Z), so local `ThroatZ == -TunnelDepth` lands on the real globe. If the provider is
temporarily null during rebind, keep the shell mounted, log the degraded empty-throat condition, and
realign on the next rebind; never instantiate another world/globe binder.

### 5B. Constants and mesh builders

Use this first eye-judgment geometry:

```csharp
private const float TunnelRadius = 8.0f;
private const float TunnelDepth = 14.0f;
private const float MouthZ = 0.0f;
private const float ThroatZ = -TunnelDepth;
private const float InnerRingInnerRadius = 8.15f;
private const float InnerRingOuterRadius = 8.85f;
private const float OuterRingInnerRadius = 9.05f;
private const float OuterRingOuterRadius = 10.0f;
private const float CorridorSurfaceRadius = TunnelRadius - 0.06f;
private const double CorridorSpanDegrees = 24.0;
private const int FilmstripFramesPerCorridor = 4;
private const float FineRailCenterZ = -TunnelDepth / 2.0f;
private const float FineRailHalfLength = 2.5f;
private const float TunnelCameraFovDeg = 55.0f;
```

Replace `BuildAnnulusSectorMesh` with:

```csharp
private static ArrayMesh? BuildPlanarAnnulusSectorMesh(
    double startAngleDeg, double spanAngleDeg,
    float innerRadius, float outerRadius, float z,
    double angularStepDeg = 3.0);

private static ArrayMesh? BuildCylinderSectorMesh(
    double startAngleDeg, double spanAngleDeg,
    float radius, float nearZ, float farZ,
    double angularStepDeg = 3.0);

private static Vector3 CalculateNormal(Vector3 a, Vector3 b, Vector3 c);
```

`BuildCylinderSectorMesh` must produce inward normals and vertices at both `nearZ` and `farZ`; every
corridor spans `0` to `-14`, not a shared plane. Add one dark opaque 360° shell at `TunnelRadius` so
zero tracks still read as a cylinder. Put colored corridor sectors at
`CorridorSurfaceRadius` to avoid coplanar fighting. Materials are opaque, depth-tested, unshaded with
emission; inactive/focus styling changes color/emission, never transparent sort order.

### 5C. Exactly two physical ring roots

Delete ladder/current roots. Create only `OuterCoarseRing` and `InnerFocusRing`, each with one planar
annulus at `MouthZ` and an asymmetric handle/marker child so visual rotation is observable. Keep
separate `Label3D` readouts, but no ladder ring, current ring, per-track ring, or third fine ring.

Required methods:

```csharp
private void EnsureRingRoots();
private void RebuildTwoRingControls();
private void UpdateOuterRingVisual(TunnelOuterTickMapping mapping);
private void UpdateInnerRingVisual(TunnelFineTrackBinding binding, TunnelFinePreview preview);
private void ClearRingRoots();
```

Outer label shows canonical base tick/`kb`; inner label shows owner, real rung, active/inactive, and
signed fine quantity. Outer node visual wraps modulo 360 while the coordinator retains the full
logical angle. Inner visual clamps to ±360.

### 5D. Five curved corridors and axial content

Replace `BuildWedges`. Resolve source tracks through `TunnelCorridorLayout.SelectSourceTracks`, keep a
normalized `_focusIndex`, build only `BuildFocusedWindow(...)`, and center the focused track at
bottom. Unknown-sphere tracks remain visible but inactive. Resolve active state through the same
geosphere/atmosphere schedule lookup by calling
`TunnelTrackActivity.IsActive(descriptor, tick, _ctl.GeosphereSchedule, _ctl.AtmosphereSchedule)`.
Active state styles/enables but never filters. Do not use `TrackRowViewModel.IsDimmed`, because that
field describes presenter degradation rather than regime activity.

Build the snapped five-slot set once under `_corridorsRoot`. During a wall drag, rotate that root by
the transient visual angle (`RotationDegrees.Z = -accumulatedClockwiseDegrees`) instead of destroying
and rebuilding frame sinks on every mouse motion. On release, apply the pure snap, reset root rotation
to zero, supersede the outgoing preview generation once, and rebuild once for the new focus. This is
both the required continuous interpolation and the guard against flooding the three-request
filmstrip queue with abandoned Godot owners.

For each slot, build a 24° cylindrical wall sector from mouth to throat, a depth-tested label, and
the real presenter path. Resolve the coarse end through
`var coarse = TunnelScrubMapper.MapOuterAngleToTick(360d, baseTick, MaxTick)`. Preserve
`coarse.Rung.UnitTicks` as the fixed one-`kb` **scale span** for Z normalization and use
`coarse.ClampedTargetTick` only as the valid **request end**. Near `MaxTick`, use
`TimelineFilmstrip.PlanSlots(baseTick, requestEnd, ...)`, request existing preview frames via
`FilmstripPreviewController`, but map each slot with
`z = MouthZ - ((slot.Tick - baseTick) / coarse.Rung.UnitTicks) * TunnelDepth`. The unused far segment
therefore stays empty instead of stretching the final partial time range to the throat. Clip
out-of-window slots;
pass `contentWidth = TimelineFilmstrip.ThumbnailWidth * FilmstripFramesPerCorridor` so the prototype
requests four real ticks per visible filmstrip corridor. Do not stretch one flat snapshot over the
whole track and do not fabricate content. Graph/generic tracks remain honest labeled curved sectors.

Only focused slot `0` builds an axial rail centered at `FineRailCenterZ`; its cursor consumes
`TunnelFinePreview.CursorZ`. It is not a ring.

### 5E. Tick/base refresh without request storms

Replace the old ring-only `OnTickChanged` with:

```csharp
private void OnTickChanged(long tick);
private void RefreshTunnelForBaseTick(long tick, bool rebuildFrameRequests);
private void RepositionExistingFrames(long baseTick, double coarseSpanTicks, long requestEndTick);
```

Marshal to the Godot main thread exactly as the current method does. Every tick must update the outer
readout, recompute schedule-active styling, rebind the focused inner owner/rung if activity changed,
recenter the provisional fine preview, and reposition each existing `(frameNode, frameTick)` along
the new `[baseTick,coarseEnd]` Z window, hiding frames outside it. A scrub preview performs only these
cheap updates—no new preview requests. `ApplyOuterScrubAction` performs one full corridor/frame
request rebuild after the single `ScrubCommit`. A non-tunnel tick schedules one rebuild only when it
moves outside the currently requested content window; coalesce that deferred rebuild with a boolean
pending flag. Registry/focus changes supersede the old filmstrip generation once and rebuild once.

This path is required: no outer/world tick may leave active state, inner binding, fine reset, or
axial positioning anchored to the previous base.

### 5F. Oblique camera

Use:

```csharp
private static readonly Vector3 TunnelCameraLocalPosition = new(3.5f, 2.0f, 22.0f);
private static readonly Vector3 TunnelCameraLocalTarget = new(0.0f, -1.0f, -7.0f);
```

Keep capture/restore of the previous current camera. Aim at the local target; do not disable depth
testing or use `Label3D.NoDepthTest` to compensate for bad framing.

At the mouth, 55° vertical FOV from roughly 22 units gives a half-height above 11 units, leaving
margin around the radius-10 outer ring before oblique correction. The exported screenshot is still
the authority: the lead may tune only camera distance/FOV/target to keep both complete rings and the
depth cues visible, and must record the final values and reason in evidence.

Compile/check commands:

```bash
dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
  --filter 'FullyQualifiedName~Tunnel'
task bundle:stagetool:test
```

Commit: `feat(presentation): render tunnel as focused 3d cylinder`

## Task 6 — real input ownership, continuous rotation, cancellation, and logs

**Files:** relay, binder input, binder lifecycle/corridor/ring update paths.

Replace the relay with:

```csharp
internal sealed partial class TunnelInputRelay : Node3D
{
    public Func<InputEvent, bool>? OnInput;
    public Action<double>? OnProcess;
    public Action<string>? OnCancel;

    public override void _Input(InputEvent @event);
    public override void _Process(double delta);
    public override void _Notification(int what);
    public override void _ExitTree();
}
```

`_Input` calls `OnInput`; when it returns true, call `GetViewport().SetInputAsHandled()`. `_Process`
calls the frame callback. Focus-out notifications and `_ExitTree` cancel. `_ExitTree` also clears all
delegates. When the binder tears down, explicitly null callbacks and disable input/process before
`QueueFree` so the outgoing collectible binder is not rooted until deferred native free.

Implement in binder input:

```csharp
private bool HandleInputEvent(InputEvent @event);
private void ConsumeTunnelFrame(double delta);
private bool TryBeginGesture(InputEventMouseButton press);
private bool HandleOwnedMotion(InputEventMouseMotion motion);
private bool HandleOwnedRelease(InputEventMouseButton release);
private bool TryResolveHit(Vector2 screenPosition, out TunnelHitSample hit);
private bool TryProjectToMouthPlane(Vector2 screenPosition, out Vector3 localPoint);
private bool TryIntersectTunnelWall(Vector2 screenPosition, out Vector3 localPoint);
private void ApplyOuterScrubAction(
    TimelineScrubAction action,
    TunnelOuterTickMapping mapping);
private void CancelTunnelGesture(string reason);
private void ResetFinePreview(TunnelFineResetReason reason);
private void DetachInputRelay();
private void SeverManagedInputCallbacks();
```

Hit testing must use `Camera3D.ProjectRayOrigin/ProjectRayNormal`, transformed into mount-local space:

- convert the local origin/direction to `TunnelRay3` and call the tested
  `TunnelRayHitMapper.TryIntersectMouthPlane` for the non-overlapping inner/outer annular bands;
- call the tested `TunnelRayHitMapper.TryIntersectCylinder` for the wall and accept only its nearest
  forward root in `[ThroatZ, MouthZ]`;
- feed the booleans to pure `ResolveHitRegion`.

For dial/wall pointer angle use local `atan2(y,x)`, then call the tested
`TunnelScrubMapper.NormalizeClockwiseDeltaDegrees`. Pass the signed delta to the pure
coordinator. Wall motion only rotates `_corridorsRoot` by the transient angle; release resets that
root transform, snaps/rebuilds once, rebinds the inner owner, and resets fine preview.
Outer `_Process` consumes one queued preview per frame; release always commits the latest mapping.
Inner never calls `PushTick`. F9 must require `Pressed: true, Echo: false`; accepted F9 is handled.

Cancellation must happen before controller/registry clearing on disable, controller loss, registry
loss, focus loss, world `RuntimeChanging`, relay exit, and dispose. Cancellation never commits.
Focus change, outer base-time movement, disable, and reload reset the fine cursor/readout to zero.

`OnResourceRuntimeChanging` must synchronously set a tearing-down/generation guard, cancel the
coordinator, null the relay's managed delegates through `SeverManagedInputCallbacks`, sever the
filmstrip provider, and cancel in-flight work before it schedules any deferred Godot node cleanup.
Every already-queued rebuild/tick callable captures and checks the generation guard before touching
state. The deferred callback is then node cleanup only. Do not rely on eventual `QueueFree` to sever
managed callbacks; the live old-ALC gate remains the final proof that the one-frame cleanup capture
was released.

Add structured `ILogger` templates with no anonymous-object serialization:

```text
tunnel gesture ownership: kind={GestureKind} pointer={Pointer} button={Button} handled={Handled}
tunnel outer gesture: phase={Phase} pressTick={PressTick} accumulatedDegrees={AccumulatedDegrees} unitSymbol={UnitSymbol} rawTickQuantity={RawTickQuantity} roundedTargetTick={RoundedTargetTick} clampedTargetTick={ClampedTargetTick} origin={Origin}
tunnel inner gesture: phase={Phase} sphereId={SphereId} layerId={LayerId} rung={Rung} active={Active} accumulatedDegrees={AccumulatedDegrees} rawTickQuantity={RawTickQuantity} cursorZ={CursorZ} authoritativeTick={AuthoritativeTick} mutated=false
tunnel wall gesture: phase={Phase} focusBefore={FocusBefore} stepDelta={StepDelta} focusAfter={FocusAfter} snappedDegrees={SnappedDegrees}
tunnel gesture cancelled: kind={GestureKind} reason={Reason}
```

Emit ownership once per accepted press and one outer commit per release. The inner log's
`authoritativeTick` before/after plus `mutated=false` is evidence, not a claim substituted for code
review.

Focused and suite commands:

```bash
dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
  --filter 'FullyQualifiedName~Tunnel|FullyQualifiedName~TimelineScrubCoalescer'
dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj
dotnet test project/tests/App.Camera.Tests/App.Camera.Tests.csproj
task test
task bundle:stagetool:test
```

Commit: `feat(presentation): own tunnel mouse gestures across hud`

## Task 7 — exported build and live acceptance gate

The implementing OpenCode agent may run the deterministic commands through bundle packaging. The
lead session owns OS-mouse operation, screenshot judgment, live-log evidence, and final acceptance.

### 7A. Required build path

Use the repository's test wrapper for the suite and the required UnifyBuild-backed wrapper for the
export; do not substitute raw build/export commands, `nuke`, or a hand-written Godot export:

```bash
task restore
task test
task bundle:stagetool:test
task build:godot:desktop
task bundles
task bundle:install
```

`task test` intentionally compiles/tests the solution through the Taskfile's normal `dotnet` path.
The artifact-producing gate is `task build:godot:desktop`, which invokes
`dotnet tool run unify-build -- BuildGodotDesktop`; the current artifact is `0.1.2`.

### 7B. Launch and evidence directory

```bash
OUT=/tmp/fantasim-two-ring-gate
mkdir -p "$OUT"
LOG="$OUT/app.log"
APP="$PWD/build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/complete-app"
remote__enabled=true nohup "$APP" >"$LOG" 2>&1 &
APP_PID=$!
curl -fsS --retry-connrefused --retry 60 --retry-delay 1 http://127.0.0.1:19292/health
python3 tools/fantasim-cmd.py status > "$OUT/status.json"
```

Terminate the obsolete pre-rebuild exported process first; the final evidence process must contain
the new resident seam.

Set a safe baseline and enable:

```bash
python3 tools/fantasim-cmd.py cmd timeline.seek '{"tick":60000000}' > "$OUT/seek-baseline.json"
python3 tools/fantasim-cmd.py cmd camera.orbit '{"yawDeg":35,"pitchDeg":-25,"distance":4}' > "$OUT/orbit-baseline.json"
python3 tools/fantasim-cmd.py cmd timeline.tunnel_view '{"enabled":true}' > "$OUT/tunnel-enable.json"
python3 tools/fantasim-cmd.py cmd render.screenshot \
  '{"path":"/tmp/fantasim-two-ring-gate/oblique.png"}' > "$OUT/screenshot-command.json"
```

Visually inspect `oblique.png`; require non-zero Z separation among mouth, wall, axial content, and
throat, exactly two rings, five-or-fewer unique tracks, bottom focus, and the existing globe.

### 7C. Three real-mouse gesture proofs

Before the first gesture, poll `camera.debug {}` at frame-separated intervals until two consecutive
samples have identical `cameraPath`, `cameraTransform`, `activePcamPath`, `activePcamTransform`,
`followTargetPath`, `orbitYawDeg`, `orbitPitchDeg`, `orbitDistance`, and `springLength`. Save those as
`orbit-settle-a.json`/`orbit-settle-b.json`. This prevents normal PhantomCamera spring convergence
after `camera.orbit` from being misdiagnosed as tunnel input leakage.

For wall, outer, and inner separately:

1. save `camera.debug {}` to `<kind>-before.json`;
2. use real OS mouse input, cross the relevant threshold, and release over an existing HUD control;
3. save `<kind>-after-release.json`;
4. move the pointer without a button and save `<kind>-after-move.json`.

All three snapshots must have identical camera identity, follow target, yaw, pitch, distance, real
camera transform, pcam transform, and `dragMotionsApplied`; `draggingNow` is false. Global event-seen
counters may increase.

Gesture-specific evidence:

- Wall: pass 15°, focus identity changes, another real track is at bottom, inner owner changes, and
  the logged real rung remains honestly `ka` if inputs are still homogeneous.
- Outer: away from bounds, observed tick delta equals
  `RoundAwayFromZero(degrees / 360 × kb.UnitTicks)` for the logged real angle; the exact ±360° claim
  comes from Task 1 tests.
- Inner: an active focused track's axial cursor and signed readout move; authoritative tick is
  identical before/after and no scrub action occurs.

### 7D. Live world reload while enabled

Keep the same app open:

```bash
task bundle:world
task bundle:install
python3 tools/fantasim-cmd.py cmd timeline.tunnel_view '{"enabled":true}' > "$OUT/tunnel-reenable.json"
START_LINE=$(($(wc -l < "$LOG") + 1))
python3 tools/fantasim-cmd.py cmd resource.reload_bundle \
  '{"bundleId":"world"}' > "$OUT/world-explicit-reload.json"
tail -n +"$START_LINE" "$LOG" > "$OUT/world-reload-segment.log"
```

Poll up to 60 seconds, then require all and forbid the pin line:

```text
Bundle unloaded: world
Bundle loaded: world
resource.reload_bundle: reloaded 'world'.
Hot-reload: old ALC collected for bundle world
```

```text
old ALC still pinned for bundle world
```

After collection, call `timeline.tunnel_view {"enabled":true}` again on the new binder and capture a
final screenshot. State persistence across a world reload is deliberately not added to this
prototype; the required claim is that the outgoing enabled binder unloads and its ALC collects.
Leave the accepted exported app running and record PID/log paths.

## Task 8 — deposit conclusions, evidence, and refinement boundary

Populate
`vault/specs/evidence/2026-07-12-rotating-tunnel-two-ring-prototype/README.md` with:

- commit ids and exact test/build command outcomes;
- screenshot link and an explicit visual judgment of cylinder depth;
- real-mouse coordinates/gesture kind plus the structured outer/inner/wall log excerpts;
- before/after/after-move orbit field comparison;
- world reload excerpt including exact old-ALC collection;
- actual descriptor identities/rungs observed (expected homogeneous `ka` today);
- any look-dev tuning made to constants and why;
- the exported app PID and full log path.

Update `AGENT-SUMMARY.md` with the durable implementation decisions and the one intentionally open
product question: after the user sees the running visual, should the fine ring mutate shared world
time, create a layer-local offset, or remain a view-only inspection? Do not answer that question in
this implementation.

Final audit before completion:

- no third physical ring or hidden ladder/current ring remains;
- no production fake track/rung/frame/globe was added;
- no corridor vertices are all coplanar at Z=0;
- inner code has no `PushTick` path;
- outer release emits one commit from latest accumulated angle;
- every relay delegate is severed before collectible teardown;
- final worktree contains only intended changes and all commits are local (not pushed unless the user
  separately requests it).

Commit: `docs(vault): record rotating tunnel prototype evidence`

## Doubt-driven checkpoint for the delegated run

Before editing, the OpenCode GLM-5.2 agent must cold-read the approved design, this plan, current
source, and tests; specifically challenge:

1. clockwise visual polarity versus focus-index polarity;
2. rounding-before-clamping and pathological overflow;
3. inactive inner ownership versus accidental globe orbit;
4. replaceable planet-root lifetime versus tunnel mount lifetime;
5. ray/cylinder hit selection under the oblique camera;
6. old-ALC pins from relay/provider/deferred callbacks;
7. whether filmstrip placement uses real frame ticks and a common axial base.

If a blocking contradiction is found, stop without production edits and report it. Otherwise record
the audit conclusion in the agent log and execute the plan. The lead will independently review the
diff and runtime result before accepting it.
