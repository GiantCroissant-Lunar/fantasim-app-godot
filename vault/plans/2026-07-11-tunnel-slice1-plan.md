# Tunnel timeline — slice 1 implementation plan (2026-07-11)

> **For the implementing agent:** execute task-by-task, strict TDD where a task lists tests. You
> do NOT commit, do NOT run git write operations, do NOT run the windowed gate — the lead reviews
> by artifacts, commits, and gates. Steps use checkbox (`- [ ]`) syntax. Mirrors the house recipe:
> [`2026-07-11-d8b-progressive-resolution-slice1-plan.md`](2026-07-11-d8b-progressive-resolution-slice1-plan.md)
> and [`2026-07-11-timelineface-split-plan.md`](2026-07-11-timelineface-split-plan.md).

**Spec:** `../specs/2026-07-11-tunnel-timeline-design.md` (LOCKED — the user resolved every
Decision Point by delegating to the spec's own recommendation). This plan implements exactly
spec §7's slice-1 in/out lists: Concept A (Concentric Tunnel) only, ring WIDGET deferred to
slice 2 (rung-navigation via the existing zoom buttons/wheel), filmstrip corridors reused via a
new sink seam, graph/generic corridors as labeled dimmed wedges, center globe reused via a
shared-node spike with an empty-throat fallback, scrub through the unchanged
`TimelineScrubCoalescer → PushTick(tick, origin)` pipeline, entry via a remote command + a debug
keybind.

## Grounding facts (verified against code 2026-07-11 — do not re-derive)

- **§3.3 verification done — the answer is "unconsumed."** `grep -rn "LayerTrackTimeDomain|CadenceTicks"` across `project/` (excluding `.godot`) shows `LayerTrackTimeDomain.Rung`/`LayerTrackContent.CadenceTicks` are only ever **constructed** (`TrackPipelineNodeCatalog.cs`, `WorldPlugin.cs`, test fixtures) — always `Rung: "ka"` verbatim, never parsed back. No production code reads either field. This plan's Task 3 (`TunnelCorridorLayout.ResolveCorridorRung`) is therefore the **first real consumer**, exactly as spec §3.3 anticipated.
- **`ITimelineController` and `ILayerTrackRegistry` are both owned by the WORLD bundle, not the timeline bundle.** `PlanetPresentationBinder`'s ctor constructs `PlanetTimelineController` and does `_registry.RegisterOwned<ITimelineController>(_timeline, ...)` (`project/plugins/App.Presentation/PlanetPresentationBinder.cs:105-118`). `WorldPlugin.cs:117` does `registry.RegisterOwned<ILayerTrackRegistry>(...)`. `App.Timeline`'s `TimelinePlugin.ComposeTimeline` only ever *reads* both via `_registry.TryGet<T>()` (confirmed by its own comment: "Owned and registered by the WORLD bundle (WorldPlugin), consumed here through the shared T1 contract only"). **Consequence: code living in the world bundle can resolve both directly via `IRegistry`, with no need to go through `ITimelineFaceContext` at all** — that wrapper only exists because the *resident* 2D face (`App.Timeline.Seam`, outside every bundle) needs the timeline bundle to broker world-owned services across the resident/collectible boundary. Bundle-local tunnel code sitting *in the same bundle as the source of truth* does not need that broker.
- **`FilmstripPreviewController` is `internal sealed class`, tightly coupled to `TextureRect`.** `RequestTexture(TextureRect textureRect, string sphere, string layerId, long tick, string rung)` holds the `TextureRect` directly in `PendingFilmstripFrame(int Generation, TextureRect TextureRect)`; `ApplyFilmstripPreview` does `textureRect.Texture = texture` after `GodotObject.IsInstanceValid(textureRect) && textureRect.IsInsideTree()`. Confirms spec §4.3's "study current coupling first" — it IS tightly coupled; Task 4 extracts the sink.
- **The 2026-07-11 polarity flip removed the old broad "shared everything under `FantaSim.App.`" rule.** `project/hosts/complete-app/config/shared-assembly-policy.json`'s own comment: *"POLARITY FLIPPED 2026-07-11 ... the broad FantaSim.App. prefix is gone — shared = T1 contracts (enumerated exactMatches...) + the resident floor."* Checked against this file: `FantaSim.App.Timeline.Contracts` and `FantaSim.App.Presentation.Contracts` ARE in `exactMatches` (safe, referenceable from anywhere). **`FantaSim.App.Timeline.Seam` is NOT in `exactMatches` or any `prefixes` entry.** It stays resident today only because `project/hosts/complete-app/complete-app.csproj:50` directly `ProjectReference`s it (confirmed) — i.e. it is host-composition-resident, not policy-declared-shared. This is a real gap the plan must close (Task 6) before any collectible code references it.
- **The `world` bundle mounts into Stage with zero `.tscn` edits.** `PlanetPresentationBinder.BindDocument` does `_sceneRegistry.GetNodeOrNull("stage", new NodePath("Environment/PlanetMount/Planet/LayerMounts"))` then `mount.AddChild(root)` where `root` is built entirely in C#. `project/bundles/stage/scenes/stage_entry.tscn` wraps `environment.tscn` as a child instance named `"Environment"`; `BundleSceneHost.RegisterScene("stage", ...)` registers the **StageEntry** root, so `GetSceneOrNull("stage")` already reaches `Environment` one hop down. The tunnel mounts the same way — `_sceneRegistry.GetNodeOrNull("stage", new NodePath("Environment"))` — no scene file touched.
- **`task bundle:world` and `task bundle:timeline` both exist** (`Taskfile.yml:163,219`) — both collectible tiers hot-reload via the standard `verify-windowed` loop. Only a `shared-assembly-policy.json`/`collectible-bundles.json`/`Host.cs`/T1-contract edit forces a full `task build:godot:desktop` → `task run:exported` cycle, per that skill's decision table.
- **`App.Timeline.Tests` links Godot-free seam files directly** (`<Compile Include="..\..\plugins\App.Timeline.Seam\TrackLaneViewModelBuilder.cs" Link="..." />`) rather than `ProjectReference`-ing `App.Timeline.Seam.csproj` (a `Godot.NET.Sdk` project the headless test host can't easily consume). New pure-math files in this plan follow the same link pattern.
- **Command precedent:** `TimelinePlugin.RegisterTimelineCommands` (`project/plugins/App.Timeline/TimelinePlugin.cs:240`) is where every `timeline.*` command registers/unregisters, resolving cross-bundle services lazily via `_registry?.TryGet<T>()` inside the handler body (never captured at registration) — `timeline.set_track_archived`'s handler is the exact template for `timeline.tunnel_view`.
- **Host lifecycle precedent:** `Host.cs:710-722` (`BindPlanetPresentation`) is the exact template for wiring a new `ITunnelPresentation` — resolve after `resource.IsLoaded("world")`, call `.Rebind()`, hold no owning reference (severed on `RuntimeChanging`, mirroring `_planetPresentation`).

## Placement decision (spec Decision Point 1 — resolved)

**The tunnel's Godot-typed rendering/mount code lives as NEW FILES inside the EXISTING
`project/plugins/App.Presentation/` project** (assembly `FantaSim.App.Presentation`), which
already ships collectibly inside the `world` bundle (`collectible-bundles.json`, confirmed
above) — **not** a new project, and **not** `App.Timeline.Seam`. Reasons, in order of weight:

1. **Dependency direction.** `ITimelineController` and `ILayerTrackRegistry` — the two things
   the tunnel most needs — are *owned* by the world bundle already (grounding facts above). Code
   in the same bundle resolves them with one `_registry.TryGet<T>()` call, no cross-bundle
   broker, no reload-ordering race against a *different* collectible ALC's lifecycle.
2. **Center-globe reuse (Decision Point 2) becomes nearly free.** If the tunnel binder lives in
   the SAME assembly as `PlanetPresentationBinder`, the shared-node spike (Task 2 below) is one
   `internal` accessor property, not a new cross-assembly contract.
3. **Zero NEW `collectible-bundles.json` entry.** `App.Presentation`'s assembly is already
   registered under `world`'s `projects`/`assemblyNames`. Adding tunnel code as new files in the
   same project changes nothing about *that* file. This directly answers the brief's ask about a
   new bundle registration: **none is needed** — but see Task 6, which DOES require a
   `shared-assembly-policy.json` edit (a different host-read config file) to let `App.Presentation`
   safely reference `App.Timeline.Seam`'s pure-math/filmstrip-sink classes. That edit is the one
   flagged full-rebuild checkpoint this slice needs.
4. **Mount pattern match.** `PlanetPresentationBinder` already proves the "mount into Stage's
   `Environment` via `IBundleSceneRegistry`, code-only, no `.tscn` edit" pattern the tunnel needs
   verbatim (grounding facts above).
5. **What the tunnel does NOT get by living here:** it does not automatically get
   `ITimelineFaceContext` (that stays timeline-bundle-owned) — the plan does not need it, per
   point 1. It also does not get `App.Timeline.Seam`'s classes for free — Task 6 makes that
   reference legal.

New file tree (all under `project/plugins/App.Presentation/Tunnel/`):
`TunnelPresentationBinder.cs` (core), `TunnelPresentationBinder.Rings.cs`,
`TunnelPresentationBinder.Corridors.cs`, `TunnelPresentationBinder.Input.cs`,
`TunnelInputRelay.cs`, `QuadMaterialFilmstripSink.cs`.

## Global constraints (hard)

- Canonical ticks + rung vocabulary everywhere; no Ma/Ga identifiers anywhere, including in
  corridor labels (reuse `TimelineTimeFormatter`/`TimelineModel` for every displayed number —
  never format a ladder value by hand).
- No new domain math for "which rings at which depth": every ring position comes from
  `TimelineModel.Ruler(viewStart, viewEnd, rung).Fraction` mapped through this plan's
  `TunnelDepthMapper.RadiusForFraction` — never a second `Ruler`-shaped function.
- ALC house rules apply to every new collectible-side file: no anonymous-type STJ serialization
  (`JsonObject`, not `JsonSerializer.Serialize(new {...})`), no static caching cross-assembly
  types, delegates/providers resolved at EXECUTION time (`_registry.TryGet<T>()` inside the
  method body, never captured in a ctor-time field that outlives a reload).
- Real-mouse doctrine (D2): every tunnel-specific interaction (scrub ring, corridor rebuild,
  activation) must be exercised with actual mouse/key input in the windowed app before it is
  claimed to work — ingress commands alone are not evidence of interaction.
- Suite baseline: verify `dotnet test project/FantaSim.sln` is green BEFORE Task 1; if red, STOP
  and report. Full `dotnet build project/FantaSim.sln` + `dotnet test project/FantaSim.sln` after
  every task.
- Prefix every shell command with
  `cd /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot && `.

---

### Task 1 — `TunnelDepthMapper` (pure, TDD)

**Files:** new `project/plugins/App.Timeline.Seam/TunnelDepthMapper.cs`; new
`project/tests/App.Timeline.Tests/TunnelDepthMapperTests.cs`; add a `<Compile Include>` link
entry to `project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj` mirroring the existing
`TimelineScrubMapper.cs`/`TrackLaneViewModelBuilder.cs` entries.

Maps a `TimelineModel.Ruler` mark's already-computed linear `Fraction` (0 at `viewStart`, 1 at
`viewEnd`) onto a ring RADIUS using a compression falloff (spec §1's salvage from Concept B,
§2.1/§2.2: reuse `Ruler`'s fraction, only the radius mapping is new). Slice-1 constants are a
DELIBERATE placeholder — spec Decision Point 12 explicitly defers real tuning to an eye-judged
pass against the true ~200M-tick `MaxTick`; this task pins the SHAPE (monotonic, parameterized),
not final numbers.

```csharp
namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Maps a linear view-fraction (TimelineModel.Ruler's Fraction, 0=near/viewStart, 1=far/viewEnd)
/// onto a tunnel ring radius via an inverse falloff, so rings crowd toward the throat exactly like
/// Concept A's wireframe (ringR(z) = R*k/(k+z)) — reparameterized on fraction so it composes with
/// the EXISTING Ruler call instead of a second tick-domain function (spec §2.2: no new domain
/// math). falloffK is a slice-1 placeholder pending DP12's real-data tuning pass — vault/specs/
/// 2026-07-11-tunnel-timeline-design.md.
/// </summary>
public static class TunnelDepthMapper
{
    public const double DefaultFalloffK = 3.0;

    public static double RadiusForFraction(
        double fraction, double throatRadius, double outerRadius, double falloffK = DefaultFalloffK);
}
```

- [ ] **Step 1: failing tests first.** `fraction=0` → `outerRadius` (exactly); `fraction=1` →
  strictly greater than `throatRadius` but closer to it than the midpoint (asymptotic, never
  reaches the throat at fraction=1 — matches the wireframe's own curve never hitting 0); strictly
  monotonically DECREASING as fraction increases (sample several points, assert non-increasing);
  `falloffK` larger ⇒ radius at a fixed mid-fraction is SMALLER (sharper crowd toward the throat);
  out-of-range fraction (negative, >1) clamps into `[throatRadius, outerRadius]` rather than
  throwing (never crash on a caller's rounding error).
- [ ] **Step 2:** implement `radius = throatRadius + (outerRadius - throatRadius) * falloffK / (falloffK + fraction * SomeScale)` shaped so `fraction=0 ⇒ outerRadius` exactly — pick constants that satisfy Step 1's tests; do not hand-tune beyond what the tests pin.
- [ ] **Step 3:** suite green (new tests only add; nothing else touched).

### Task 2 — shared-globe spike (spec Decision Point 2 / "task 1b") — timeboxed

**Files:** modify `project/plugins/App.Presentation/PlanetPresentationBinder.cs` (add ONE
internal read accessor); new throwaway/kept harness under
`project/plugins/App.Presentation/Tunnel/` depending on outcome. **Runs independently of Task 1**
— no shared files, safe to parallelize.

Question: can a second `Camera3D`/`SubViewport` view the SAME `PlanetPresentation` node tree the
Stage scene already owns (the `_activeRoot`/`_plateSurfaceRoot` `PlanetPresentationBinder` builds
under `Environment/PlanetMount/Planet`), instead of standing up an independent second binder?

- [ ] **Step 1:** add `internal Node3D? ActiveRoot => _activeRoot;` to `PlanetPresentationBinder`
  (core file, near the other private fields it exposes nowhere else — this is the ONE new seam).
- [ ] **Step 2 (timeboxed — cap at what fits in this task, do not let this balloon):** in a
  scratch scene/script, create a `SubViewport` + `Camera3D` pointed at `ActiveRoot`'s world
  position (read via `ActiveRoot.GlobalTransform.Origin`) from a different angle/distance than the
  Stage's own camera, and confirm in the windowed app (ad hoc `task bundle:world` iteration is
  fine here, this is exploratory) that the SAME globe geometry renders in both viewports
  simultaneously with no double-instancing, no material-sharing artifact, no extra draw-call
  explosion visible in the Godot profiler.
- [ ] **Step 3: record the verdict in AGENT-SUMMARY.md, explicitly, before continuing to any
  later task that assumes an answer:**
  - **PASS** → Task 7 builds a `SubViewport`+`Camera3D` inside the tunnel's own mount tree,
    aimed at `PlanetPresentationBinder.ActiveRoot`'s world transform, and composites it into the
    tunnel throat (e.g. as an unshaded quad or a second on-screen viewport region — pick the
    cheaper option when you reach Task 7, it is a rendering-composition detail, not an
    architecture one).
  - **FAIL or inconclusive within the timebox** → Task 7 ships the tunnel with an EMPTY throat
    for slice 1 (spec §7's explicit fallback — "an empty throat is acceptable for the first
    eye-judgment"). Do not spend Task 7's budget trying to force the shared-node approach if this
    spike says no.
- [ ] No unit test (Godot-coupled exploratory spike); no suite-green gate beyond "nothing else
  broke."

### Task 3 — `TunnelCorridorLayout` (pure, TDD) — the §3.3 first-consumer task

**Files:** new `project/plugins/App.Timeline.Seam/TunnelCorridorLayout.cs`; new
`project/tests/App.Timeline.Tests/TunnelCorridorLayoutTests.cs`; `<Compile Include>` link entry.
Depends on nothing from Tasks 1–2; parallelizable with both.

Two responsibilities, both pure functions over `TrackLaneViewModelBuilder.BuildLanes`'s existing
output (reused verbatim, never reimplemented — spec §4.1):

```csharp
namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Angular wedge layout for tunnel corridors: one sphere SECTOR per TrackLaneViewModel (equal
/// angular share, first-seen order — generalizes Concept A's hardcoded 6x60deg wireframe to N
/// spheres, spec §4.1/§1 point 4), subdivided into one corridor wedge per TrackRowViewModel within
/// that sector. Godot-free by design (mirrors TimelineScrubMapper/TrackLaneViewModelBuilder) —
/// linked directly into App.Timeline.Tests. vault/plans/2026-07-11-tunnel-slice1-plan.md.
/// </summary>
public static class TunnelCorridorLayout
{
    public readonly record struct CorridorWedge(
        string SphereId,
        string LayerId,
        double StartAngleDeg,
        double SpanAngleDeg,
        bool IsDimmed,
        TrackContentPresenterKind PresenterKind);

    /// <summary>Divides the full 360deg among lanes equally, in BuildLanes' first-seen order,
    /// then each lane's span equally among its tracks, in track order. Empty input -> empty
    /// output (no lanes, no throw).</summary>
    public static IReadOnlyList<CorridorWedge> BuildWedges(IReadOnlyList<TrackLaneViewModel> lanes);

    /// <summary>
    /// The first real consumer of LayerTrackDescriptor.TimeDomain.Rung (verified unconsumed
    /// elsewhere in the codebase, see this plan's Grounding facts). Resolves the track's declared
    /// native rung symbol against TimelineModel.GetLadderRungs(); an unrecognized or null symbol
    /// falls back to the caller's globally-selected rung -- the Unity round-trip degradation
    /// guarantee applied to a NEW field for the first time, never a throw.
    /// </summary>
    public static TimelineLadderRung ResolveCorridorRung(string? trackRungSymbol, TimelineLadderRung globalFallback);
}
```

- [ ] **Step 1: failing tests first.**
  `BuildWedges`: 1 lane with 1 track → wedge spans exactly 360deg starting at 0; 2 lanes with 1
  track each → two 180deg sectors in BuildLanes' order, each track's wedge equal to its
  (single-track) lane's full span; 1 lane with 3 tracks → 3 wedges of 120deg each inside that
  lane's own (full, since it's the only lane) 360deg sector, contiguous (no gaps, no overlap —
  assert `sum(SpanAngleDeg) == totalSectorSpan` per lane and cumulative start angles chain);
  `IsDimmed`/`PresenterKind` pass through the source `TrackRowViewModel` unchanged; empty
  `lanes` list → empty result, no throw.
  `ResolveCorridorRung`: a symbol matching an existing `TimelineModel.GetLadderRungs()` entry
  (e.g. `"ka"`) returns THAT rung, not the fallback; `null` returns the fallback exactly;
  an unrecognized symbol (e.g. `"bogus"`) returns the fallback exactly (never throws) — this is
  the degradation-guarantee test, name it as such in the test method name.
- [ ] **Step 2:** implement. `BuildWedges` iterates `lanes` in order, `360.0 / lanes.Count` per
  sector (guard `lanes.Count == 0`), then `sectorSpan / lane.Tracks.Count` per track (guard
  `Tracks.Count == 0` — a lane with zero non-archived tracks should not occur per
  `TrackLaneViewModelBuilder`'s own contract, but guard defensively rather than divide by zero).
- [ ] **Step 3:** suite green.

### Task 4 — `TunnelScrubMapper` (pure, TDD)

**Files:** new `project/plugins/App.Timeline.Seam/TunnelScrubMapper.cs`; new
`project/tests/App.Timeline.Tests/TunnelScrubMapperTests.cs`; `<Compile Include>` link entry.
Parallelizable with Tasks 1–3.

Spec §5.1 asks for the wireframe's own radius-gated dispatch (`mode = Math.abs(r-Rj)<48 ? 'time'
: 'wall'`) so idle wall-spin never moves the playhead. Slice 1 SIMPLIFIES the actual scrub
gesture from the wireframe's rotational "jog dial" to a straight horizontal-pixel-delta drag
(exactly `GlobeOrbitControls.HandleDrag`'s `delta.X * sensitivity` shape) — justified because
spec Decision Point 11 already treats the full ring WIDGET as a slice-2 stretch goal, so the
input GESTURE fidelity (dial vs. linear drag) is equally negotiable for the first eye-judgment;
record this simplification in AGENT-SUMMARY.md.

```csharp
namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Pure scrub-gesture math for the tunnel's current-tick ring: radius-gated press dispatch
/// (mirrors the wireframe's mode='time' vs 'wall' split, spec §5.1) plus a linear horizontal-
/// pixel-delta-to-tick-delta drag mapping reusing the SAME view span the 2D ruler uses, so an
/// identical pixel drag moves the SAME number of ticks in either view. Godot-free; linked into
/// App.Timeline.Tests. vault/plans/2026-07-11-tunnel-slice1-plan.md.
/// </summary>
public static class TunnelScrubMapper
{
    /// <summary>True when a press at screenRadiusPx from the ring's screen-projected center
    /// falls within bandPx of ringRadiusPx -- i.e. the press targets the ring, not the wall.</summary>
    public static bool IsWithinRingBand(float screenRadiusPx, float ringRadiusPx, float bandPx);

    /// <summary>Maps a horizontal drag delta (pixels) to a new absolute tick, clamped to
    /// [viewStartTick, viewEndTick], reusing the same linear span TimelineScrubMapper.
    /// TryLocalXToTick uses for the 2D ruler.</summary>
    public static long DragDeltaToTick(
        float pixelDeltaX, float viewportWidthPx, long viewStartTick, long viewEndTick, long baseTick);
}
```

- [ ] **Step 1: failing tests first.** `IsWithinRingBand`: exactly at `ringRadiusPx` → true;
  `bandPx` away → true (inclusive boundary); `bandPx + 1` away → false; negative `screenRadiusPx`
  handled without throwing. `DragDeltaToTick`: zero delta → `baseTick` unchanged; a delta of
  `viewportWidthPx` (a full-width drag) moves by the FULL `viewEndTick - viewStartTick` span;
  result clamps to `[viewStartTick, viewEndTick]` even when `baseTick + delta` would overshoot;
  `viewportWidthPx <= 0` returns `baseTick` unchanged rather than dividing by zero (mirrors
  `TimelineScrubMapper.TryLocalXToTick`'s `surfaceWidth <= 0f` guard).
- [ ] **Step 2:** implement, reusing `Math.Clamp` the same way `TimelineScrubMapper` does.
- [ ] **Step 3:** suite green.

### Task 5 — filmstrip sink seam extraction

**Files:** modify `project/plugins/App.Timeline.Seam/FilmstripPreviewController.cs`; new
`project/plugins/App.Timeline.Seam/IFilmstripFrameSink.cs` (interface +
`TextureRectFilmstripSink` adapter); modify
`project/plugins/App.Timeline.Seam/TimelineFace.Lanes.cs` (one call site). ZERO 2D behavior
change — this is a pure signature refactor gated by the existing suite + the lead's windowed
2D-filmstrip-still-renders check (no NEW unit test is possible: per the timelineface-split plan's
own precedent, `FilmstripPreviewController` is Godot-coupled and untested directly today —
`FilmstripCacheLedgerTests` staying green is the only automated signal available).

```csharp
namespace FantaSim.App.Timeline.Seam;

/// <summary>Where a fetched filmstrip texture lands. TextureRectFilmstripSink is the 2D adapter
/// (unchanged behavior); App.Presentation's QuadMaterialFilmstripSink (tunnel, 3D) is the second
/// implementation this seam exists for -- spec §4.3's "smallest seam that lets the controller feed
/// both sinks." vault/plans/2026-07-11-tunnel-slice1-plan.md.</summary>
internal interface IFilmstripFrameSink
{
    bool IsAlive { get; }
    void SetTexture(ImageTexture texture);
}

internal sealed class TextureRectFilmstripSink : IFilmstripFrameSink
{
    private readonly TextureRect _textureRect;
    public TextureRectFilmstripSink(TextureRect textureRect) => _textureRect = textureRect;
    public bool IsAlive => GodotObject.IsInstanceValid(_textureRect) && _textureRect.IsInsideTree();
    public void SetTexture(ImageTexture texture) => _textureRect.Texture = texture;
}
```

- [ ] **Step 1:** in `FilmstripPreviewController.cs`: change
  `internal sealed record PendingFilmstripFrame(int Generation, TextureRect TextureRect)` to
  `PendingFilmstripFrame(int Generation, IFilmstripFrameSink Sink)`; change
  `RequestTexture(TextureRect textureRect, ...)` to `RequestTexture(IFilmstripFrameSink sink, ...)`
  (rename the parameter through the method body — `waiters.Add(new PendingFilmstripFrame(generation, sink))`);
  in `ApplyFilmstripPreview`'s waiter loop, replace the `GodotObject.IsInstanceValid(textureRect) ||
  !textureRect.IsInsideTree()` early-out + `textureRect.Texture = texture` with
  `if (!pending.Sink.IsAlive) continue;` / `pending.Sink.SetTexture(texture);`. `BuildFramePlaceholder`
  is UNCHANGED (still builds the 2D `Control`+`TextureRect` tree for lane layout — the 3D corridor
  builds its own quad separately in Task 8 and never calls this method).
- [ ] **Step 2:** in `TimelineFace.Lanes.cs`'s `BuildCompactFilmstrip`, change
  `_filmstrip.RequestTexture(textureRect, sphere, layerId, slot.Tick, rung);` to
  `_filmstrip.RequestTexture(new TextureRectFilmstripSink(textureRect), sphere, layerId, slot.Tick, rung);`.
- [ ] **Step 3:** suite green. Note in AGENT-SUMMARY.md that the windowed 2D-filmstrip-render
  check is deferred to the lead's gate (this task changes no runtime behavior, only the seam
  shape).

### Task 6 — resident-visibility fix + `App.Presentation` cross-references (the flagged checkpoint)

**Files:** `project/hosts/complete-app/config/shared-assembly-policy.json`;
`project/plugins/App.Timeline.Seam/App.Timeline.Seam.csproj`;
`project/plugins/App.Presentation/App.Presentation.csproj`;
`project/plugins/App.Timeline/App.Timeline.csproj`. **This is the ONE full-rebuild checkpoint
this slice needs** — every file it touches is host-read-at-startup config or a `ProjectReference`
graph change, per the `verify-windowed` skill's decision table (T4 seam / resident-code rows).
Run it once every one of Tasks 1–5 has landed, so the checkpoint captures all new pure-math files
+ the sink seam in a single rebuild — do not spread this across multiple full rebuilds.

- [ ] **Step 1:** add `"FantaSim.App.Timeline.Seam"` to
  `shared-assembly-policy.json`'s `exactMatches` array, with a one-line comment: *"tunnel-slice-1:
  App.Timeline.Seam is host-composition-resident (complete-app.csproj direct ProjectReference) but
  was missing from the post-polarity-flip allow-list; App.Presentation (world bundle) now
  references its pure-math/filmstrip-sink classes and must resolve the SAME resident copy, not a
  bundled duplicate."* This keeps the runtime policy and the build-time stager (both consumers of
  this single file, per its own header comment) in sync — the exact discipline the file's comment
  demands.
- [ ] **Step 2:** add to `App.Timeline.Seam.csproj`:
  ```xml
  <ItemGroup>
    <InternalsVisibleTo Include="FantaSim.App.Presentation" />
  </ItemGroup>
  ```
  **Use the ASSEMBLY name, not the folder name** — `App.Presentation.csproj` overrides
  `<AssemblyName>FantaSim.App.Presentation</AssemblyName>` (confirmed by reading the file), so
  `Include="App.Presentation"` would silently grant nothing.
- [ ] **Step 3:** add to `App.Presentation.csproj`'s existing `<ItemGroup>` of `ProjectReference`s:
  ```xml
  <ProjectReference Include="..\..\contracts\App.Timeline\App.Timeline.csproj" />
  <ProjectReference Include="..\App.Timeline.Seam\App.Timeline.Seam.csproj" />
  ```
  (`contracts/App.Timeline` → `FantaSim.App.Timeline.Contracts`, already in `exactMatches` —
  no policy edit needed for this one, only the `ProjectReference`.)
- [ ] **Step 4:** add to `App.Timeline.csproj`'s `ProjectReference`s:
  ```xml
  <ProjectReference Include="..\..\contracts\App.Presentation\App.Presentation.csproj" />
  ```
  (needed by Task 11's `timeline.tunnel_view` command to resolve `ITunnelPresentation`;
  `FantaSim.App.Presentation.Contracts` is already in `exactMatches`.)
- [ ] **Step 5:** `dotnet build project/FantaSim.sln` — confirm the new reference graph compiles
  with no circular-reference error (App.Presentation → App.Timeline.Seam is a NEW edge; verify it
  does not close a cycle back through `contracts/App.World`/`App.Command`/etc. — read both
  csproj's full reference lists before building if unsure).
- [ ] **Step 6:** full `task build:godot:desktop` (this checkpoint does not stop at `dotnet
  build` — Godot's own C# build + PCK export must also succeed, since Step 1/2 touch host-loaded
  assemblies). Do NOT run `task run:exported` yet — Tasks 7–8 have not created any tunnel content;
  this checkpoint's job is only to prove the wiring compiles and exports cleanly. Record the build
  log path in AGENT-SUMMARY.md; the lead runs the actual windowed relaunch once real content
  exists (Task 12's gate).

### Task 7 — `ITunnelPresentation` contract + `PresentationComposition`/`PresentationPlugin` wiring

**Files:** new `project/contracts/App.Presentation/ITunnelPresentation.cs`; modify
`project/plugins/App.Presentation/PresentationComposition.cs`; modify
`project/plugins/App.Presentation/PresentationPlugin.cs`; modify
`project/hosts/complete-app/Host.cs`. Depends on Task 6 (needs the new references compiled) for
the csproj graph to make sense, but the CONTRACT file itself has no such dependency — safe to
write in parallel with Task 6, just don't wire the plugin/Host until after Task 6 lands.

```csharp
namespace FantaSim.App.Presentation;

/// <summary>Host-facing surface of the tunnel timeline presentation (slice 1). Mirrors
/// IPlanetPresentation's shape exactly -- Rebind on world-bundle availability, teardown -- plus
/// the activation toggle both the remote command and the debug keybind drive.
/// vault/plans/2026-07-11-tunnel-slice1-plan.md.</summary>
public interface ITunnelPresentation : IDisposable
{
    void Rebind();

    /// <summary>Shows/hides the tunnel geometry. false leaves the binder mounted-but-empty (the
    /// always-present input relay still captures the debug keybind while hidden).</summary>
    void SetEnabled(bool enabled);

    bool IsEnabled { get; }
}
```

- [ ] **Step 1:** add `ITunnelPresentation.cs` as above.
- [ ] **Step 2:** `PresentationComposition.cs` gains
  `public static ITunnelPresentation CreateTunnelPresentation(IRegistry registry, IBundleSceneRegistry sceneRegistry, ILoggerFactory loggerFactory) => new TunnelPresentationBinder(registry, sceneRegistry, loggerFactory);`
  (no `ResourceService`/`plateViewOverride`/`showWorldGraph` — the tunnel does not need them for
  slice 1; add only if a later task proves otherwise).
- [ ] **Step 3:** `PresentationPlugin.InitializeAsync` — **after** the existing
  `_presentation = _factory(context)` line (this ordering is load-bearing: creating
  `PlanetPresentationBinder` first is what registers `ITimelineController`, which the tunnel
  binder's ctor resolves) — construct and register the tunnel binder the same way:
  `_tunnelPresentation = PresentationComposition.CreateTunnelPresentation(registry, sceneRegistry, loggerFactory); _tunnelRegistration = registry.RegisterOwned<ITunnelPresentation>(_tunnelPresentation, new ServiceRegistration { Tags = new[] { "presentation", "tunnel", "world-bundle" }, Description = "tunnel timeline binder (world bundle)" });`.
  `ShutdownAsync` disposes it with the SAME main-thread-marshal-and-wait pattern already used for
  `_presentation` (copy the `_isOnMainThread()`/`Callable.From`/`TaskCompletionSource` block
  verbatim, parameterized on `_tunnelPresentation`).
- [ ] **Step 4:** `Host.cs` — add `private ITunnelPresentation? _tunnelPresentation;` alongside
  `_planetPresentation`; in `BindPlanetPresentation` (or immediately after its call site), add:
  ```csharp
  _tunnelPresentation = registry.TryGet<ITunnelPresentation>();
  _tunnelPresentation?.Rebind();
  ```
  `grep -n "_planetPresentation" project/hosts/complete-app/Host.cs` and mirror EVERY site
  (severing on `RuntimeChanging`, nulling on teardown) with a `_tunnelPresentation` sibling line —
  do not hand-pick a subset; the lifecycle must match exactly or the tunnel binder pins the old
  world ALC on reload.
- [ ] **Step 5:** `dotnet build` + full `task build:godot:desktop` (still inside the Task 6
  checkpoint's blast radius — this is resident Host.cs code). No `task run:exported` yet;
  `TunnelPresentationBinder` does not exist as a class until Task 8.

### Task 8 — `TunnelPresentationBinder`: mount + ring/corridor build + registry wiring

**Files:** new `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs`,
`TunnelPresentationBinder.Rings.cs`, `TunnelPresentationBinder.Corridors.cs`. Depends on Tasks
1–3, 6, 7. The "thin Godot layer" — mesh/material construction only, every geometric DECISION
(radius, angle, dimming, rung) comes from Tasks 1–3's pure functions.

- [ ] **Step 1 (core file):** ctor takes `(IRegistry registry, IBundleSceneRegistry sceneRegistry,
  ILoggerFactory loggerFactory)`, mirrors `PlanetPresentationBinder`'s
  `_resource.RuntimeChanging`/`RuntimeChanged` subscription for its OWN teardown/rebind (same
  `"world"` bundle-id gate). `Rebind()`: resolve `_registry.TryGet<ITimelineController>()` and
  `_registry.TryGet<ILayerTrackRegistry>()` (execution-time, not cached past this call);
  `_layerTrackRegistry.Changed += OnRegistryChanged` (subscribe once, unsubscribe on
  `RuntimeChanging`/`Dispose` — copy `TimelineFace.BindLayerTrackRegistry`/
  `UnsubscribeLayerTrackRegistry`'s exact reference-equality-guarded re-subscribe pattern);
  `_ctl.TickChanged += OnTickChanged`. Mount root: `_sceneRegistry.GetNodeOrNull("stage", new NodePath("Environment"))` as `Node3D`, `AddChild(new Node3D { Name = "TunnelMount" })`, add a
  `TunnelInputRelay` child (Task 10) UNCONDITIONALLY (mounted even when `Enabled == false`, so the
  debug keybind always works). `SetEnabled(bool)` shows/hides `TunnelMount` and, on first
  enable, triggers the initial ring+corridor build; does NOT tear down the mount on disable
  (cheap toggle, matches the "empty throat is acceptable" framing — hiding, not rebuilding, is the
  slice-1 disable path).
- [ ] **Step 2 (`.Rings.cs`):** `RebuildRings()` reads the SAME `_viewStartTick`/`_viewEndTick`
  state the 2D face's zoom buttons/wheel already drive — **the tunnel does not own a second view-
  range; slice 1 reuses whichever view range the 2D face last set via `ITimelineController`** (the
  simplest possible reading of spec §7's "driven by the EXISTING zoom controls" line: read
  `_ctl.MaxTick` and the currently-selected rung via `TimelineModel.SelectRungForSpan`, defaulting
  the visible span to `[0, MaxTick]` since the tunnel has no UI of its own to narrow it yet — do
  NOT invent a second `_viewStartTick` field that could drift from the 2D face's; if a shared
  view-range contract turns out to be needed, that is a slice-2 finding, not a slice-1 build).
  For each `TimelineModel.Ruler(0L, _ctl.MaxTick, rung)` mark, ring radius =
  `TunnelDepthMapper.RadiusForFraction(mark.Fraction, ThroatRadius, OuterRadius)`; build a thin
  ring mesh (e.g. a low-poly `TorusMesh` or a flat `ArrayMesh` circle strip — pick whichever is
  cheaper to regenerate per rebuild) at that radius, plus a `Label3D` showing `mark.Label`
  (already canonical-formatted by `TimelineModel.Ruler` — never reformat by hand). The
  CURRENT-tick ring is a separate, distinctly-colored ring at
  `TunnelDepthMapper.RadiusForFraction(TimelineScrubMapper.TickToFraction(_ctl.Tick, 0L, _ctl.MaxTick), ...)`,
  rebuilt on every `OnTickChanged` (cheap — one ring, not the whole ladder).
- [ ] **Step 3 (`.Corridors.cs`):** `RebuildCorridors()` calls
  `TrackLaneViewModelBuilder.BuildLanes(_layerTrackRegistry.Current)` →
  `TunnelCorridorLayout.BuildWedges(lanes)`; for each `CorridorWedge`, build a longitudinal wall
  panel mesh spanning `[StartAngleDeg, StartAngleDeg + SpanAngleDeg]` and the full visible depth
  (`ThroatRadius` to `OuterRadius`), tinted per `PresenterKind`/`IsDimmed` (dimmed wedges use the
  SAME alpha TimelineFace's `DimmedTrackModulate` constant uses — copy the value, do not
  reintroduce a different dim level). `PresenterKind.Graph`/`Generic` corridors get a `Label3D`
  with the track's `DisplayName` and stop there (spec §7: no in-3D graph, no pop-out yet).
  `PresenterKind.Filmstrip` corridors are built here as PLAIN tinted wedges too — Task 9 upgrades
  them to real textures; do not block this task on Task 9.
- [ ] **Step 4:** `OnRegistryChanged(LayerTrackRegistrySnapshot snapshot)` — copy
  `TimelineFace.OnLayerTrackRegistryChanged`'s `OS.GetThreadCallerId() == OS.GetMainThreadId()`
  guard + `Callable.From(...).CallDeferred()` fallback verbatim (this handler CAN fire off-main
  from a command handler, exactly as documented there); calls `RebuildCorridors()`.
  `OnTickChanged(long tick)` similarly marshals, then rebuilds only the current-tick ring (Step
  2), never the whole corridor set.
- [ ] No new unit test (Godot-coupled mesh/mount code — the pure decisions it calls are already
  tested in Tasks 1–3). Verified by the lead's windowed gate (Task 12).

### Task 9 — filmstrip corridors: `QuadMaterialFilmstripSink` wiring

**Files:** new `project/plugins/App.Presentation/Tunnel/QuadMaterialFilmstripSink.cs`; modify
`TunnelPresentationBinder.Corridors.cs`. Depends on Task 5 (sink interface) + Task 8 (corridor
mesh exists to attach a material to) + Task 6 (visibility to reference `FilmstripPreviewController`).
Parallelizable with Task 10 once Task 8 lands (different code regions of the same binder).

```csharp
namespace FantaSim.App.Presentation.Tunnel;

using FantaSim.App.Timeline.Seam;   // internal, granted via InternalsVisibleTo (Task 6)

internal sealed class QuadMaterialFilmstripSink : IFilmstripFrameSink
{
    private readonly MeshInstance3D _owner;
    private readonly StandardMaterial3D _material;

    public QuadMaterialFilmstripSink(MeshInstance3D owner, StandardMaterial3D material)
    {
        _owner = owner;
        _material = material;
    }

    public bool IsAlive => GodotObject.IsInstanceValid(_owner) && _owner.IsInsideTree();
    public void SetTexture(ImageTexture texture) => _material.AlbedoTexture = texture;
}
```

- [ ] **Step 1:** `TunnelPresentationBinder` owns its OWN `FilmstripPreviewController` instance
  (constructed the same way `TimelineFace` does:
  `new FilmstripPreviewController(isFaceAlive: () => GodotObject.IsInstanceValid(TunnelMount) && TunnelMount.IsInsideTree(), deferToMainThread: a => Callable.From(a).CallDeferred(), log: _log)`),
  wired to the world bundle's own filmstrip provider directly —
  `_filmstrip.SetPreviewProvider((request, ct) => _registry.TryGet<FantaSim.App.World.IService>()?.GetLayerFilmstripPreview(request, ct))`
  set once per `Rebind()` (execution-time resolve inside the lambda, matching the ALC rule).
  **Deliberate slice-1 simplification, record in AGENT-SUMMARY.md:** this is a SEPARATE cache
  from the 2D face's — the two views may independently re-fetch/re-cache the same frames. Not a
  bug; a documented slice-2 dedup opportunity if the user wants it.
- [ ] **Step 2:** for each `Filmstrip`-kind corridor wedge, build a `MeshInstance3D` quad (or a
  short `MultiMeshInstance3D` strip if multiple frames per corridor look better — pick the
  simpler single-quad-per-corridor for slice 1 unless Task 8's wedge width clearly wants more)
  with a `StandardMaterial3D`, wrap it in `QuadMaterialFilmstripSink`, and call
  `_filmstrip.RequestTexture(sink, sphereId, layerId, tick, rung)` for the current-tick's frame
  (one request per corridor, not a full compact-filmstrip's worth of slots — slice 1 shows ONE
  frame per corridor at the current tick, not a scrolling strip; that is explicitly the smallest
  useful cut and matches "labeled dimmed wedges" scope for everything else).
- [ ] **Step 3:** `Dispose()`/unmount path calls `_filmstrip.Supersede()` + `_filmstrip.CancelInFlight()`
  + `_filmstrip.DisposeCache()` at the same points `TimelineFace._ExitTree`/`ClearResidentContext`
  do, so the tunnel's filmstrip requests never outlive a world-bundle reload (same ALC-pin class
  the memory ledger's "seven pin classes" documents — this is pin class 7's exact shape, applied
  to a second consumer).
- [ ] No new unit test (Godot-coupled). Windowed gate (Task 12) verifies real texture appears.

### Task 10 — real-mouse scrub input

**Files:** new `project/plugins/App.Presentation/Tunnel/TunnelInputRelay.cs`; new
`project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs`. Depends on Task 4
(pure math) + Task 8 (mount + current-tick ring exists to hit-test against). Parallelizable with
Task 9.

```csharp
namespace FantaSim.App.Presentation.Tunnel;

/// <summary>Visual-affordance-free input relay: the binder is a plain C# class (mirrors
/// PlanetPresentationBinder), not a Node, so it cannot override _Input itself. This tiny Node
/// forwards press/motion/release to a delegate the binder supplies -- same shape as
/// TimelinePlayheadHandle's "input handled by the owner, not the Control" precedent.
/// vault/plans/2026-07-11-tunnel-slice1-plan.md.</summary>
internal sealed partial class TunnelInputRelay : Node3D
{
    public Action<InputEvent>? OnEvent;
    public override void _UnhandledInput(InputEvent @event) => OnEvent?.Invoke(@event);
}
```

- [ ] **Step 1:** `TunnelPresentationBinder.Input.cs` owns a `TimelineScrubCoalescer` field
  (Godot-free, already referenced via Task 6's `contracts/App.Timeline` reference — reused
  directly, exactly like `TimelineFace` does, not reimplemented). `HandlePress(InputEventMouseButton)`:
  project the current-tick ring's world position through the active `Camera3D`
  (`camera.UnprojectPosition(...)`) to get a screen-space center, compute the press's
  screen-space radius from that center, gate with `TunnelScrubMapper.IsWithinRingBand`; on hit,
  `_scrubCoalescer.Press(_ctl.Tick)` → `ApplyScrubAction`.
- [ ] **Step 2:** `HandleMotion(InputEventMouseMotion)` while dragging: compute
  `TunnelScrubMapper.DragDeltaToTick(motion.Relative.X, viewportWidth, 0L, _ctl.MaxTick, _lastAppliedTick)`
  → `_scrubCoalescer.Motion(tick)` → `ApplyScrubAction`. `HandleRelease`: `.Release(tick)` →
  `ApplyScrubAction`. A press OUTSIDE the ring band is a no-op here (spec §5.1: wall-spin is a
  pure camera gesture in slice 1 since full orbit parity is out of scope — do not wire a fallback
  "spin the tunnel" behavior in this task; an unhandled press simply does nothing this slice).
- [ ] **Step 3:** `ApplyScrubAction(TimelineScrubAction action)` — copy `TimelineFace.
  ApplyScrubAction`'s shape exactly: `if (!action.ShouldApply) return;` clamp tick to
  `[0, _ctl.MaxTick]`, `EchoTunnelTick(tick)` (local: rebuild ONLY the current-tick ring, Task 8
  Step 2's cheap path — never a full corridor rebuild on every scrub frame), then
  `_ctl.PushTick(tick, action.Origin)`.
- [ ] No new unit test (Godot input dispatch). Windowed gate (Task 12) is the only proof, per D2
  doctrine — this is exactly the class of claim ("the ring works") the spec repeatedly says
  ingress commands cannot substitute for.

### Task 11 — entry point: `timeline.tunnel_view` command + debug keybind

**Files:** modify `project/plugins/App.Timeline/TimelinePlugin.cs`; modify
`TunnelPresentationBinder`/`TunnelInputRelay` (from Tasks 8/10) to route a debug key through the
SAME toggle. Depends on Task 7 (`ITunnelPresentation`) + Task 10 (input relay exists).

- [ ] **Step 1:** in `TimelinePlugin.cs`, add
  `internal const string TunnelViewCommandId = "timeline.tunnel_view";` alongside the existing
  command-id constants; register it in `RegisterTimelineCommands` following the
  `SetTrackArchivedCommandId` handler's exact shape (lazy `_registry?.TryGet<T>()` inside the
  handler body, `JsonObject` never an anonymous type):
  ```csharp
  commandService.Register(
      new CommandDescriptor(
          Id: TunnelViewCommandId,
          Title: "Toggle tunnel view",
          Description: "Enables/disables the 3D tunnel timeline. Payload: {\"enabled\":true}.",
          Category: "timeline"),
      (payloadJson, _) =>
      {
          var payload = ParseTunnelViewPayload(payloadJson);   // mirrors ParseTrackArchivedRequestPayload
          if (!TryReadBool(payload["enabled"], out var enabled))
              throw new ArgumentException("timeline.tunnel_view requires boolean 'enabled'.");
          var tunnel = _registry?.TryGet<FantaSim.App.Presentation.ITunnelPresentation>();
          tunnel?.SetEnabled(enabled);
          return Task.FromResult<string?>(new JsonObject
          {
              ["ok"] = tunnel is not null,
              ["enabled"] = tunnel?.IsEnabled ?? false,
          }.ToJsonString());
      });
  ```
  Add the matching `commandService?.Unregister(TunnelViewCommandId);` line to
  `UnregisterTimelineCommands`.
- [ ] **Step 2:** `TunnelInputRelay.OnEvent` (or a second dedicated relay callback) checks for a
  debug key — use `Key.F9` (verified free: `grep -rn "Key.F9\|KeyF9" project --include="*.cs"`
  before committing to it; pick the next free function key if F9 is already claimed by
  `ViewToggleBar`'s per-entry shortcuts or elsewhere) — `InputEventKey { Pressed: true, Keycode: Key.F9 }`
  → `SetEnabled(!IsEnabled)` on the SAME binder instance the command calls, so both entry points
  are provably the same code path (no duplicated toggle logic to drift).
- [ ] **Step 3:** suite green; note the chosen key in AGENT-SUMMARY.md.

### Task 12 — handoff

- [ ] Final full `dotnet build project/FantaSim.sln` + `dotnet test project/FantaSim.sln`; record
  counts against the Task-0 baseline.
- [ ] Write `AGENT-SUMMARY.md` at repo root: per-task files/tests/deviations (with reasons —
  especially Task 2's spike verdict and Task 4's gesture-simplification note), anything the lead
  must know before gating. Do NOT commit anything; do NOT run the windowed gate below — that is
  the lead's step.

---

## Lead acceptance gate (lead-run; NOT the implementer)

**Placement dictates a fresh boot exactly once, then hot-reload for the rest:**

1. **Fresh boot** (`task build:godot:desktop` → `task run:exported`) — required because Task 6/7
   touched `shared-assembly-policy.json` and `Host.cs` (resident/host-read-at-startup). Confirm
   the app launches clean, no `MissingMethodException`/type-load error on the world bundle's
   first mount (this is the plan's riskiest assumption — see below — verify explicitly here).
2. From this point on, iterate purely via hot-reload: `task bundle:world && task bundle:install`
   for any further `App.Presentation`/`Tunnel/*` change; `task bundle:timeline && task bundle:install`
   for the `timeline.tunnel_view` command. Confirm `old ALC collected for bundle world` (and
   `for bundle timeline` if that bundle was touched) after each round.
3. **Activation:** send `timeline.tunnel_view {"enabled":true}` via the remote ingress tool —
   confirm corridors + rings appear (screenshot). Toggle off via the SAME command, confirm hidden.
   Press the Task 11 debug key in-window — confirm it toggles identically (real-mouse/keyboard
   proof, D2 doctrine — this is the "does the keybind actually work" claim ingress alone cannot
   prove).
4. **Real-mouse scrub (D2):** drag the current-tick ring with the actual mouse in the windowed
   app; confirm the ring moves, D8b's low-rung-then-climb binds appear in the log exactly as they
   do for the 2D face's drag (same `PushTick(tick, origin)` pipeline — no new resolution policy
   was written, so this should just work if the origin is actually reaching `_ctl.PushTick`).
   Screenshot pair (mid-drag vs. settled) for the user's eye-judgment.
5. **Add/remove:** `timeline.set_track_archived {"sphereId":...,"layerId":...,"archived":true}` —
   confirm the corresponding corridor wedge disappears live (`ILayerTrackRegistry.Changed` →
   `RebuildCorridors` wiring, Task 8 Step 4); restore (`archived:false`) — confirm it reappears in
   the correct sector angle (no permanent renumbering of sibling wedges' angles beyond what
   `TunnelCorridorLayout.BuildWedges`' equal-division rule naturally produces).
6. **Center globe:** confirm Task 2's spike verdict matches what's on screen — either the SAME
   globe geometry visible through the throat from a second angle (PASS path) or a deliberately
   empty throat with rings/corridors otherwise complete (FAIL-path fallback) — either is an
   acceptable slice-1 outcome per spec §7; only a THIRD state (crash, double-rendered globe,
   visible seam artifact) is a real gate failure.
7. Suite green at the merge commit. Screenshot evidence vendored per house convention (only on
   user request, not automatically) for the eye-judgment this whole slice exists to enable.

## Task-count summary

12 numbered tasks. Parallelizable before the Task 6 checkpoint: **Tasks 1, 2, 3, 4** (independent
pure-math/spike files, zero shared edits). Task 5 (filmstrip sink) is also independent and may run
alongside 1–4. Task 6 is a hard synchronization point (gathers every prior task's new files into
one compile+export). Task 7's contract file (not its plugin/Host wiring) may be written in
parallel with Task 6. Tasks 8 is sequential after 6/7 (needs the compiled reference graph). Tasks
9 and 10 may run in parallel once 8 lands (different regions of the same binder — filmstrip sink
vs. input relay). Task 11 is sequential after 7 and 10. Task 12 is last.

## Single riskiest assumption

**That adding `"FantaSim.App.Timeline.Seam"` to `shared-assembly-policy.json`'s `exactMatches`
(Task 6, Step 1) is sufficient for the collectible `world` ALC to resolve
`App.Presentation`'s reference to `App.Timeline.Seam` types against the SAME resident instance
the host already loaded, rather than the bundle stager/runtime loading a second private copy.**
This plan reasons from the config file's own stated purpose ("single source of truth... so the
two [runtime SharedAssemblyPolicy and build-time stager] can never drift") and from
`FilmstripCacheLedger`/`TrackLaneViewModelBuilder`/`TimelineScrubMapper` already being
Godot-free-but-resident classes today — but the actual `IsolatedComponentLoadContext`/
`SharedAssemblyPolicy` resolution code lives in the external PluginArchi package, not in this
repo, and was NOT read this session. If the assumption is wrong, Task 6's checkpoint build
either fails outright (fast, cheap failure — good) or, worse, silently loads two copies of
`FantaSim.App.Timeline.Seam` (one resident, one bundled), producing the exact
"MissingMethodException = cross-ALC type split" pin class the ALC-shared-type-identity memory
already catalogs seven instances of, and a `FilmstripCacheLedger`/`FilmstripPreviewController`
instance mismatch between the 2D face and the tunnel that would be confusing to debug blind.
**Fallback if the checkpoint's fresh boot (gate step 1) shows this symptom:** abandon the
`App.Presentation`-hosts-the-tunnel placement and fall back to spec §6.1's literal Option A —
move `TunnelPresentationBinder`'s Godot-node files into `App.Timeline.Seam` itself as a resident
sibling to `TimelineFace` (same assembly as `FilmstripPreviewController`, zero cross-assembly
reference, zero `InternalsVisibleTo`/`exactMatches` question), accepting the full-rebuild-per-
iteration cost that option carries. Flag this fallback explicitly to the user before taking it —
it is a real scope/cost change, not a drop-in swap.
