# Timeline Face — boom-hud track/section HUD in a hot-reloadable bundle (Plan 5a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the vestigial trackless `AnimationTree` with a REAL track/section timeline face — per-sphere regime section bands + layer track rows + a playhead/transport — authored as a **boom-hud** `RuntimeSurfaceDocument` in **C# code**, shipped in a **hot-reloadable `timeline` bundle**.

**Architecture:** A new `App.Timeline` plugin (a scene-less collectible bundle, like `assist`) registers a `TimelineViewSource : IViewSource` whose `BuildDocument()` emits the boom-hud HUD. The existing `App.Ui.Seam` `ViewRenderer`/`ViewHost` mounts it (resolve-by-`ViewId`, render boom-hud → Godot Controls, `Changed`→`Rebind`, action-dispatch) and the already-wired `App.Resource` `FileSystemWatcher` (`WatchResource("timeline")`) hot-reloads it on PCK change. The HUD reads the tick and drives play/pause/seek through a new shared `ITimelineController` contract that the resident `App.World.Seam` implements — clean across the collectible-ALC boundary. The bare `HSlider` in `GlobeView` stays (boom-hud's `progressBar` is display-only).

**Tech Stack:** C# `net8.0`, Godot 4.x (.NET) in the T4 seam, **BoomHud.Foundation + BoomHud.Godot.Runtime 0.1.18** (already referenced by `App.Ui.Seam`), xUnit, the PCK bundle + Taskfile pipeline.

## Global Constraints

- **Use boom-hud — do not hand-roll Godot Controls.** The HUD is a `RuntimeSurfaceDocument` (runtime path) rendered by `RuntimeSurfaceRenderer` via the existing `IViewSource`/`ViewRenderer` seam. Catalog: `boomhud.runtime.basic.v1` (`container`, `panel`, `label`, `badge`, `button`, `progressBar`, `list`, `spacer`). Interactive = `button`/`list` only.
- **C# code for UI** — author the document by constructing `RuntimeComponentNode` records in C# (no `.tscn`, no Figma/Pencil codegen path, no GDScript).
- **Hot-reloadable bundle** — ship in a `timeline` PCK; rebuilding + copying the PCK must update the running app with NO restart (the `WatchResource`→`ReloadAsync`→ALC-swap→`Rebind` path).
- **One new plugin project** (`App.Timeline`) + one new bundle — user-approved (workspace rule 1). The `ITimelineController` contract goes into the EXISTING `contracts/App.World` project. No other new projects without fresh approval.
- **Collectible-ALC clean** — the bundle assembly talks to the resident app ONLY through shared-kernel contracts (`IRegistry`, `IViewSource`, `ITimelineController`, `FantaSim.App.*`, `BoomHud.*` — all already in the shared-assembly prefix policy). Never statically reference resident host/seam classes (e.g. `GlobeView`, `Host`).
- **Keep the `HSlider`** — `GlobeView`'s scrubber stays for smooth drag-seek; the boom-hud HUD adds the track/section face + transport buttons + region-jump. Do NOT remove the slider.
- **Determinism of the model** — `TimelineModel` (band/track layout) is a pure function of `(schedule, maxTick, tick)`; unit-tested. No Godot types in `TimelineModel` or `ITimelineController`.
- **Build:** `dotnet build project/<App>.sln -p:UseProjectReferences=true` (confirm the sln name on Task 1). **Bundle:** `task bundle:timeline` then `task bundle:install`. **Commits:** conventional-commit, path-scoped, end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Branch: `feat/timeline-face-boomhud`.

**Key reference files (read before touching):**
- boom-hud authoring example: `project/hosts/complete-app/RuntimeStatusViewSource.cs` (constructs a `RuntimeSurfaceDocument` in C#).
- IViewSource contract: `project/contracts/App.Ui/IViewSource.cs` — `{ string ViewId; RuntimeSurfaceDocument BuildDocument(); event Action? Changed; void Dispatch(string action, string? componentId); }`.
- Mount/hot-reload: `project/plugins/App.Ui.Seam/ViewHost.cs` (resolve-by-ViewId at :63, `WatchResource` at :70), `ViewRenderer.cs`.
- Bundle pattern (scene-less): `project/bundles/assist/manifest.json` + `project/plugins/App.Assist/` (Plugin/Activator/Activation/Bootstrap) + the `bundle:assist*` Taskfile tasks.
- Transport/globe to wrap: `project/plugins/App.World.Seam/RegimeTimelineTransport.cs` (`SetPlaying`/`JumpTo`/`IsPlaying`), `GlobeView.cs` (`Tick`/`SetTick`).
- Schedules/catalogs: `project/plugins/App.World.Composition/SphereRegimeScheduleDefaults.cs`, `GeosphereFieldCatalog.cs`, `AtmosphereFieldCatalog.cs`.

---

### Task 1: `TimelineModel` — pure band/track layout (the testable core)

**Files:**
- Create: `project/plugins/App.Timeline/TimelineModel.cs` (the plugin project is created in Task 4; for Task 1 place these in a temporary `project/plugins/App.World.Composition/Timeline/` location IF the plugin doesn't exist yet — OR sequence Task 4's csproj first. **Recommended: do Task 4 Step 1 (create the empty `App.Timeline.csproj` + test project) before Task 1**, then this file lands in `App.Timeline`.)
- Test: `project/tests/App.Timeline.Tests/TimelineModelTests.cs`

**Interfaces:**
- Consumes: `FantaSim.App.World.Composition.{SphereRegimeSchedule, SphereRegime, LayerId}` (existing).
- Produces:
  - `sealed record TimelineBand(string RegimeId, double StartFraction, double WidthFraction, string Variant, bool IsActive)`
  - `sealed record TimelineTrack(string LayerId, bool IsActive)`
  - `static class TimelineModel`:
    - `IReadOnlyList<TimelineBand> Bands(SphereRegimeSchedule schedule, long maxTick, long currentTick)`
    - `IReadOnlyList<TimelineTrack> Tracks(SphereRegimeSchedule schedule, long currentTick)`
    - `string VariantFor(string regimeId)` (stable color key per regime)

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.Timeline;
using Xunit;

namespace App.Timeline.Tests;

public class TimelineModelTests
{
    private static SphereRegimeSchedule Geo() =>
        SphereRegimeScheduleDefaults.GeosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick); // onset = 1e8

    [Fact]
    public void Bands_AreProportional_AndCoverZeroToOne()
    {
        var bands = TimelineModel.Bands(Geo(), maxTick: 120_000_000, currentTick: 0);
        Assert.Equal(3, bands.Count);                              // magma / lid / mobile
        Assert.Equal(0.0, bands[0].StartFraction, 6);             // magma starts at 0
        // widths sum to maxTick coverage (mobile clamped to maxTick): ~1.0
        Assert.Equal(1.0, bands.Sum(b => b.WidthFraction), 3);
        Assert.True(bands[0].WidthFraction < bands[1].WidthFraction); // magma (1e6) << lid (1e6..1e8)
    }

    [Fact]
    public void Bands_MarkActiveRegime()
    {
        var bands = TimelineModel.Bands(Geo(), 120_000_000, currentTick: 500_000); // magma-ocean
        Assert.Equal("magma-ocean", bands.Single(b => b.IsActive).RegimeId);

        var atOnset = TimelineModel.Bands(Geo(), 120_000_000, currentTick: 100_000_000); // mobile-plate
        Assert.Equal("mobile-plate", atOnset.Single(b => b.IsActive).RegimeId);
    }

    [Fact]
    public void Tracks_ListAllLayers_HighlightActive()
    {
        var tracks = TimelineModel.Tracks(Geo(), currentTick: 500_000); // magma regime active
        Assert.Contains(tracks, t => t.LayerId == "geosphere.magma-ocean" && t.IsActive);
        Assert.Contains(tracks, t => t.LayerId == "geosphere.plate" && !t.IsActive);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj` → FAIL (no `TimelineModel`). (Requires Task 4 Step 1's csprojs.)

- [ ] **Step 3: Implement `TimelineModel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline;

public sealed record TimelineBand(string RegimeId, double StartFraction, double WidthFraction, string Variant, bool IsActive);
public sealed record TimelineTrack(string LayerId, bool IsActive);

public static class TimelineModel
{
    public static IReadOnlyList<TimelineBand> Bands(SphereRegimeSchedule schedule, long maxTick, long currentTick)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (maxTick <= 0) throw new ArgumentOutOfRangeException(nameof(maxTick));
        double max = maxTick;
        var bands = new List<TimelineBand>(schedule.Regimes.Count);
        foreach (var r in schedule.Regimes)
        {
            long end = Math.Min(r.EndTick, maxTick);          // clamp open-end (long.MaxValue) to maxTick
            if (r.StartTick >= maxTick) continue;             // regime entirely past the view
            double start = r.StartTick / max;
            double width = Math.Max(0.0, (end - r.StartTick) / max);
            bands.Add(new TimelineBand(r.RegimeId, start, width, VariantFor(r.RegimeId), r.Contains(currentTick)));
        }
        return bands;
    }

    public static IReadOnlyList<TimelineTrack> Tracks(SphereRegimeSchedule schedule, long currentTick)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var active = schedule.RegimeAt(currentTick)?.ActiveLayers.Select(l => l.Value).ToHashSet() ?? new HashSet<string>();
        // Full track list = union of every regime's layers, in first-seen order.
        var seen = new List<string>();
        var set = new HashSet<string>();
        foreach (var r in schedule.Regimes)
            foreach (var l in r.ActiveLayers)
                if (set.Add(l.Value)) seen.Add(l.Value);
        return seen.Select(layer => new TimelineTrack(layer, active.Contains(layer))).ToList();
    }

    // Stable variant (color) key per regime — themed by the boom-hud renderer.
    public static string VariantFor(string regimeId) => regimeId switch
    {
        "magma-ocean"     => "danger",   // hot
        "stagnant-lid"    => "warning",  // cooling
        "mobile-plate"    => "success",  // plates
        "primordial-steam" or "secondary-co2" or "coupled-climate" => "info",
        _ => "default",
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass** — `dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj` → PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add project/plugins/App.Timeline/TimelineModel.cs project/tests/App.Timeline.Tests/
git commit -m "feat(timeline): TimelineModel — pure band/track layout from regime schedules

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `ITimelineController` contract + resident Godot adapter

**Files:**
- Create: `project/contracts/App.World/Composition/ITimelineController.cs`
- Create: `project/plugins/App.World.Seam/TimelineController.cs` (the resident adapter wrapping `RegimeTimelineTransport` + `GlobeView`)
- Modify: `project/plugins/App.World.Seam/RegimeTimelineTransport.cs` (raise a tick-changed callback) — or expose `GlobeView.Tick` polling in the adapter.

**Interfaces:**
- Produces (contract, no Godot types):
  ```csharp
  namespace FantaSim.App.World.Composition;
  public interface ITimelineController
  {
      long Tick { get; }
      long MaxTick { get; }
      bool IsPlaying { get; }
      SphereRegimeSchedule GeosphereSchedule { get; }
      SphereRegimeSchedule AtmosphereSchedule { get; }
      void Play();
      void Pause();
      void SeekTo(long tick);
      event Action<long>? TickChanged;   // fired when Tick advances (per frame while playing, or on seek)
  }
  ```
- Consumes: `RegimeTimelineTransport.{SetPlaying, JumpTo, IsPlaying}`, `GlobeView.Tick`.

- [ ] **Step 1: Create the contract** — `ITimelineController.cs` exactly as the Interfaces block above. Build `project/contracts/App.World/App.World.csproj` → 0 errors.

- [ ] **Step 2: Implement the adapter** — `TimelineController.cs` in `App.World.Seam`:

```csharp
using System;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.Seam;

/// <summary>Resident adapter: bridges the bundled HUD (via ITimelineController in the shared kernel)
/// to the Godot RegimeTimelineTransport + GlobeView. Registered into the shared registry in ComposeWorldView.</summary>
public sealed class TimelineController : ITimelineController
{
    private readonly RegimeTimelineTransport _transport;
    private readonly GlobeView _globe;
    private long _lastTick = -1;

    public TimelineController(RegimeTimelineTransport transport, GlobeView globe,
        SphereRegimeSchedule geosphere, SphereRegimeSchedule atmosphere, long maxTick)
    {
        _transport = transport; _globe = globe;
        GeosphereSchedule = geosphere; AtmosphereSchedule = atmosphere; MaxTick = maxTick;
    }

    public long Tick => _globe.Tick;
    public long MaxTick { get; }
    public bool IsPlaying => _transport.IsPlaying;
    public SphereRegimeSchedule GeosphereSchedule { get; }
    public SphereRegimeSchedule AtmosphereSchedule { get; }
    public void Play() => _transport.SetPlaying(true);
    public void Pause() => _transport.SetPlaying(false);
    public void SeekTo(long tick) => _transport.JumpTo(tick);
    public event Action<long>? TickChanged;

    /// <summary>Call once per frame from a resident _Process (e.g. the transport) to emit TickChanged.</summary>
    public void PumpTick()
    {
        var t = _globe.Tick;
        if (t != _lastTick) { _lastTick = t; TickChanged?.Invoke(t); }
    }
}
```

- [ ] **Step 3: Pump the tick** — in `RegimeTimelineTransport._Process` (after `AdvanceTo`), invoke an optional `Action? OnTickAdvanced` the controller subscribes to (or have the controller poll in its own `_Process` if it is a Node). Wire `TimelineController.PumpTick` to fire each frame. Keep it a plain callback (no new Godot node needed): add `public Action<long>? TickObserver;` to the transport, call `TickObserver?.Invoke(tick)` in `AdvanceTo`, and have `ComposeWorldView` set `transport.TickObserver = _ => controller.PumpTick();`.

- [ ] **Step 4: Build** — `dotnet build project/plugins/App.World.Seam/App.World.Seam.csproj -p:UseProjectReferences=true` → 0 errors. (Godot adapter behavior is windowed-verified in Task 6; no unit test — it wraps Godot nodes.)

- [ ] **Step 5: Commit**

```bash
git add project/contracts/App.World/Composition/ITimelineController.cs project/plugins/App.World.Seam/TimelineController.cs project/plugins/App.World.Seam/RegimeTimelineTransport.cs
git commit -m "feat(timeline): ITimelineController contract + resident transport/globe adapter

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `TimelineViewSource` — the boom-hud document (C# authored)

**Files:**
- Create: `project/plugins/App.Timeline/TimelineViewSource.cs`
- Test: `project/tests/App.Timeline.Tests/TimelineViewSourceTests.cs`

**Interfaces:**
- Consumes: `TimelineModel` (Task 1), `ITimelineController` (Task 2), `FantaSim.App.Ui.IViewSource`, `BoomHud.Abstractions.Runtime.{RuntimeSurfaceDocument, RuntimeComponentNode, RuntimeLayoutSpec, RuntimeValue, RuntimeActionDescriptor}`.
- Produces: `TimelineViewSource : IViewSource` with `ViewId => "timeline"`.

- [ ] **Step 1: Write the failing test** (assert the document STRUCTURE — DRY: build once, walk the tree)

```csharp
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.Timeline;
using BoomHud.Abstractions.Runtime;
using Xunit;

namespace App.Timeline.Tests;

public class TimelineViewSourceTests
{
    private static TimelineViewSource Make(long tick)
    {
        var ctl = new FakeController(tick);
        return new TimelineViewSource(ctl);
    }

    [Fact]
    public void Document_HasPlayPause_AndRegimeBands_AndTracks()
    {
        var doc = Make(500_000).BuildDocument();
        Assert.Equal("timeline", doc.SurfaceId);
        var ids = Flatten(doc.Root).Select(n => n.Id).ToList();
        Assert.Contains("btn-playpause", ids);
        Assert.Contains("band-geosphere-magma-ocean", ids);   // a band panel per geosphere regime
        Assert.Contains("track-geosphere-geosphere.magma-ocean", ids); // a track row per layer
    }

    [Fact]
    public void PlayPauseButton_DispatchesToController()
    {
        var ctl = new FakeController(0);
        var vs = new TimelineViewSource(ctl);
        vs.Dispatch("timeline.play", "btn-playpause");
        Assert.True(ctl.Played);
        vs.Dispatch("timeline.seek:100000000", "band-geosphere-mobile-plate");
        Assert.Equal(100_000_000, ctl.SeekedTo);
    }

    private static System.Collections.Generic.IEnumerable<RuntimeComponentNode> Flatten(RuntimeComponentNode n)
    {
        yield return n;
        foreach (var c in n.Children) foreach (var d in Flatten(c)) yield return d;
    }

    private sealed class FakeController : ITimelineController
    {
        public FakeController(long t) { Tick = t; }
        public long Tick { get; } public long MaxTick => 120_000_000; public bool IsPlaying => false;
        public SphereRegimeSchedule GeosphereSchedule => SphereRegimeScheduleDefaults.GeosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick);
        public SphereRegimeSchedule AtmosphereSchedule => SphereRegimeScheduleDefaults.AtmosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick);
        public bool Played; public long SeekedTo = -1;
        public void Play() => Played = true; public void Pause() { } public void SeekTo(long t) => SeekedTo = t;
        public event System.Action<long>? TickChanged;
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --filter TimelineViewSourceTests` → FAIL (no `TimelineViewSource`).

- [ ] **Step 3: Implement `TimelineViewSource.cs`** (build the boom-hud document from the model; stable IDs for reconcile)

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Ui;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline;

public sealed class TimelineViewSource : IViewSource
{
    private readonly ITimelineController _ctl;
    private const double TrackWidth = 760.0;  // px the band row spans
    private const double MinBand = 6.0;        // floor so a tiny regime (magma ~0.8%) stays visible

    public TimelineViewSource(ITimelineController controller)
    {
        _ctl = controller ?? throw new ArgumentNullException(nameof(controller));
        _ctl.TickChanged += _ => Changed?.Invoke();   // re-render on tick advance
    }

    public string ViewId => "timeline";
    public event Action? Changed;

    public RuntimeSurfaceDocument BuildDocument()
    {
        long tick = _ctl.Tick;
        var children = new List<RuntimeComponentNode> { Header(tick) };
        children.Add(SphereSection("geosphere", _ctl.GeosphereSchedule, tick));
        children.Add(SphereSection("atmosphere", _ctl.AtmosphereSchedule, tick));

        return new RuntimeSurfaceDocument
        {
            SurfaceId = "timeline",
            CatalogId = "boomhud.runtime.basic.v1",
            Revision = 1,
            Root = new RuntimeComponentNode
            {
                Id = "root", Type = "container",
                Layout = new RuntimeLayoutSpec { Type = "vertical", Gap = 6, Padding = 8 },
                Children = children,
            },
        };
    }

    private RuntimeComponentNode Header(long tick)
    {
        var geo = _ctl.GeosphereSchedule.RegimeAt(tick)?.RegimeId ?? "—";
        return new RuntimeComponentNode
        {
            Id = "header", Type = "container",
            Layout = new RuntimeLayoutSpec { Type = "horizontal", Gap = 8 },
            Children = new[]
            {
                Button("btn-playpause", _ctl.IsPlaying ? "⏸ Pause" : "▶ Play", _ctl.IsPlaying ? "timeline.pause" : "timeline.play"),
                Label("lbl-tick", $"tick {tick:N0}  ·  {geo}"),
            },
        };
    }

    private RuntimeComponentNode SphereSection(string sphere, SphereRegimeSchedule schedule, long tick)
    {
        var bands = TimelineModel.Bands(schedule, _ctl.MaxTick, tick);
        var tracks = TimelineModel.Tracks(schedule, tick);

        var bandRow = new RuntimeComponentNode
        {
            Id = $"bands-{sphere}", Type = "container",
            Layout = new RuntimeLayoutSpec { Type = "horizontal", Gap = 2 },
            Children = bands.Select(b => new RuntimeComponentNode
            {
                Id = $"band-{sphere}-{b.RegimeId}", Type = "button",   // button so region-jump works
                Layout = new RuntimeLayoutSpec { Width = Math.Max(MinBand, b.WidthFraction * TrackWidth) },
                Properties = new Dictionary<string, RuntimeValue>
                {
                    ["text"] = Lit(b.IsActive ? $"▮ {b.RegimeId}" : b.RegimeId),
                    ["variant"] = Lit(b.IsActive ? b.Variant : b.Variant + "-dim"),
                },
                Actions = new[] { new RuntimeActionDescriptor { Event = "pressed", Command = $"timeline.seek:{SeekTickFor(schedule, b.RegimeId)}" } },
            }).ToArray(),
        };

        var trackRows = tracks.Select(t => new RuntimeComponentNode
        {
            Id = $"track-{sphere}-{t.LayerId}", Type = "badge",
            Properties = new Dictionary<string, RuntimeValue>
            {
                ["text"] = Lit(t.LayerId),
                ["variant"] = Lit(t.IsActive ? "success" : "muted"),
            },
        }).ToArray();

        return new RuntimeComponentNode
        {
            Id = $"sphere-{sphere}", Type = "panel",
            Properties = new Dictionary<string, RuntimeValue> { ["title"] = Lit(sphere) },
            Layout = new RuntimeLayoutSpec { Type = "vertical", Gap = 4, Padding = 6 },
            Children = new[] { bandRow }.Concat(trackRows).ToArray(),
        };
    }

    private static long SeekTickFor(SphereRegimeSchedule s, string regimeId) =>
        s.Regimes.FirstOrDefault(r => r.RegimeId == regimeId)?.StartTick ?? 0;

    public void Dispatch(string action, string? componentId)
    {
        if (action == "timeline.play") _ctl.Play();
        else if (action == "timeline.pause") _ctl.Pause();
        else if (action.StartsWith("timeline.seek:", StringComparison.Ordinal)
                 && long.TryParse(action["timeline.seek:".Length..], out var t)) _ctl.SeekTo(t);
    }

    private static RuntimeComponentNode Button(string id, string text, string command) => new()
    {
        Id = id, Type = "button",
        Properties = new Dictionary<string, RuntimeValue> { ["text"] = Lit(text) },
        Actions = new[] { new RuntimeActionDescriptor { Event = "pressed", Command = command } },
    };
    private static RuntimeComponentNode Label(string id, string text) => new()
    { Id = id, Type = "label", Properties = new Dictionary<string, RuntimeValue> { ["text"] = Lit(text) } };
    private static RuntimeValue Lit(string s) => new() { Literal = JsonValue.Create(s) };
}
```

> If `variant` values like `"danger-dim"`/`"muted"` aren't in the boom-hud theme, the renderer falls back to default styling (acceptable) — confirm against `RuntimeSurfaceTheme`; adjust to supported variant names if the renderer throws on unknown variants.

- [ ] **Step 4: Run the tests to verify they pass** — `dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj` → PASS (5 tests: 3 model + 2 view source).

- [ ] **Step 5: Commit**

```bash
git add project/plugins/App.Timeline/TimelineViewSource.cs project/tests/App.Timeline.Tests/TimelineViewSourceTests.cs
git commit -m "feat(timeline): TimelineViewSource — boom-hud track/section document (C# authored)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: `App.Timeline` plugin + `timeline` bundle (scene-less, like assist)

**Files:**
- Create: `project/plugins/App.Timeline/App.Timeline.csproj` (mirror `project/plugins/App.Assist/App.Assist.csproj`)
- Create: `project/plugins/App.Timeline/{TimelinePlugin,TimelineActivator,TimelineActivation,Bootstrap}.cs` (mirror `App.Assist/*`)
- Create: `project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj`
- Create: `project/bundles/timeline/manifest.json`
- Modify: `project/hosts/complete-app/config/collectible-bundles.json` (add timeline)
- Modify: `Taskfile.yml` (add `bundle:timeline:build`, `bundle:timeline`, extend `bundle:install`)

**Interfaces:**
- Consumes: `IRegistry`, `ISceneActivator`, `IViewHost` (`FantaSim.App.Ui.IService`/`ViewHost`), `TimelineViewSource`, `ITimelineController`.
- Produces: a `timeline` bundle whose activation registers `TimelineViewSource` into the shared registry and calls `viewHost.Mount("timeline")`.

- [ ] **Step 1: Create the csproj + test csproj** — mirror `App.Assist.csproj` for the plugin (PluginArchi + ServiceArchi refs, collectible). The test csproj references `App.Timeline` + `App.World.Composition` (for schedules) + xUnit; mirror a sibling `*.Tests.csproj`. Add BOTH to the app solution WITH `ProjectConfigurationPlatforms` entries (Plan-3 lesson — a config-less project is silently skipped). Build → 0 errors. (This unblocks Tasks 1 & 3.)

- [ ] **Step 2: Implement the plugin quartet** — copy `App.Assist/{AssistPlugin,AssistActivator,AssistActivation,Bootstrap}.cs` → `Timeline*` equivalents (rename namespaces/ids `assist`→`timeline`). In `TimelineActivator.ActivateAsync` (or `Bootstrap.RunAsync`), after the child provider is built:
  ```csharp
  var registry = parent.GetRequiredService<IRegistry>();
  var controller = registry.Get<ITimelineController>();          // resident, registered in ComposeWorldView (Task 5)
  var viewSource = new TimelineViewSource(controller);
  registry.RegisterOwned<FantaSim.App.Ui.IViewSource>(viewSource, new ServiceRegistration { Description = "timeline view (bundle)" });
  registry.Get<FantaSim.App.Ui.IService>()?.Mount("timeline");   // IViewHost.Mount
  ```
  (Dispose the registration in `ShutdownAsync`/activation `Dispose` so reload re-registers cleanly.)

- [ ] **Step 3: manifest.json** (scene-less, like assist):
```json
{
  "bundleId": "timeline",
  "displayName": "Timeline",
  "version": "0.1.0",
  "pluginAssembly": "FantaSim.App.Timeline.dll",
  "metadata": { "bundleType": "hud-view" }
}
```

- [ ] **Step 4: Register collectible + Taskfile** — add `{ "bundleId": "timeline", "pluginAssembly": "FantaSim.App.Timeline.dll" }` to `collectible-bundles.json`. Add to `Taskfile.yml` (mirror `bundle:assist*`):
```yaml
bundle:timeline:build:
  cmds:
    - dotnet build project/plugins/App.Timeline/App.Timeline.csproj -c Debug -v q -nologo
    - cp project/plugins/App.Timeline/bin/Debug/net8.0/FantaSim.App.Timeline.dll project/bundles/timeline/FantaSim.App.Timeline.dll
bundle:timeline:
  deps: [bundle:link, bundle:timeline:build]
  cmds:
    - mkdir -p {{.BUILD_DIR}}/_artifacts/{{.GITVERSION_MAJORMINORPATCH}}/godot/bundles
    - '{{.GODOT}} --headless --path {{.CONTENT_PROJECT}} --export-pack "timeline PCK" {{.ROOT_DIR}}/{{.BUILD_DIR}}/_artifacts/{{.GITVERSION_MAJORMINORPATCH}}/godot/bundles/timeline.pck'
```
Extend `bundle:install` with `cp "{{.PCKS}}/timeline.pck" "{{.MACOS}}/bundles/timeline.pck"`.

- [ ] **Step 5: Build the bundle** — `task bundle:timeline` → produces `build/_artifacts/<ver>/godot/bundles/timeline.pck`. Commit.

```bash
git add project/plugins/App.Timeline/ project/bundles/timeline/ project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj \
        project/hosts/complete-app/config/collectible-bundles.json Taskfile.yml project/<App>.sln
git commit -m "feat(timeline): App.Timeline plugin + scene-less timeline bundle (registers IViewSource + mounts)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Wire load + controller registration in the host

**Files:**
- Modify: `project/hosts/complete-app/Host.cs` (`ComposeWorldView` ~273-340: register `ITimelineController`; after stage/assist enter ~151-156: enter the `timeline` bundle).

**Interfaces:**
- Consumes: `TimelineController` (Task 2), `SceneFlow.EnterAsync`, the schedules + transport + globe already built in `ComposeWorldView`.

- [ ] **Step 1: Register the controller** — in `ComposeWorldView`, after `transport` is constructed, build + register the adapter and pump ticks:
```csharp
var controller = new TimelineController(transport, view, schedule, atmosphereSchedule, maxTransportTick);
transport.TickObserver = _ => controller.PumpTick();
composition.Bootstrap.Registry.Register<FantaSim.App.World.Composition.ITimelineController>(controller);
```
(`atmosphereSchedule = SphereRegimeScheduleDefaults.AtmosphereFor(onsetTick)` — add if not already built.)

- [ ] **Step 2: Enter the timeline bundle** — after the assist enter (`Host.cs:156`), add:
```csharp
var timeline = await sceneFlow.EnterAsync(new FantaSim.App.SceneFlow.SceneRequest("timeline", "stage"));
```
(Ordering: the controller must be registered before the bundle activates and resolves it — `ComposeWorldView` runs in `_Ready` before the scene enters; confirm the sequence and reorder if needed so `ITimelineController` is registered first.)

- [ ] **Step 3: Build + smoke-run headless once** — `dotnet build project/<App>.sln -p:UseProjectReferences=true` → 0 errors. Optionally run the exe and confirm the log shows `View mounted: timeline` and no `No IViewSource is registered` warning.

- [ ] **Step 4: Commit**

```bash
git add project/hosts/complete-app/Host.cs
git commit -m "feat(timeline): register ITimelineController + enter the timeline bundle in the host

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Windowed verification — HUD renders, tracks, drives, and HOT-RELOADS

**Files:** none (verification + a handover note).

- [ ] **Step 1: Export + launch** — `task bundle:stage bundle:assist bundle:timeline build:godot:desktop bundle:install` (ensure all three PCKs install), then launch the exported `complete-app.app` windowed (autoplay on). Read the run log: expect `Bundle loaded: timeline`, `View mounted: timeline`, no `No IViewSource` warning, no `ObjectDisposedException`.

- [ ] **Step 2: Observe the HUD** — the boom-hud timeline panel shows: a **▶/⏸ button** + tick/regime label; a **geosphere band row** (magma/lid/mobile, proportional, the current one highlighted) + its track badges; an **atmosphere** section likewise. As autoplay advances, the **active band + tick label update**, and the active track badge follows the regime (magma→lid→mobile). Screenshot each regime.

- [ ] **Step 3: Drive it** — click **⏸/▶** → playback toggles (globe stops/starts). Click a **band** (e.g. mobile-plate) → the globe jumps to that regime (`SeekTo` → `JumpTo`), and the 6 plate caps appear. The `HSlider` still drag-scrubs.

- [ ] **Step 4: HOT-RELOAD (the headline)** — with the app still running, change a visible string in `TimelineViewSource` (e.g. the play label `"▶ Play"` → `"▶ Run"`), then:
  ```bash
  task bundle:timeline
  cp build/_artifacts/<ver>/godot/bundles/timeline.pck "<exported>.app/Contents/MacOS/bundles/timeline.pck"
  ```
  Within ~1s (500ms debounce + reload) the running app's HUD updates to the new label **without restart**. Confirm via screenshot + the log (`Bundle ... reloaded` / `View mounted: timeline`). **If the view-bundle hot-reload does NOT fire** (no view ships as a bundle today — this is the first), fall back: make `App.Timeline` a scene-tier bundle whose `timeline_entry.tscn` root C# node builds the HUD via `RuntimeSurfaceRenderer.Mount` directly (proven stage/assist reload path), and re-verify. Record which path worked.

- [ ] **Step 5: Record** — write `vault/handover/2026-06-22-timeline-face.md` with screenshots + which hot-reload path worked + any boom-hud variant/theme adjustments. Commit.

```bash
git add vault/handover/2026-06-22-timeline-face.md
git commit -m "docs(timeline): windowed-verify record — boom-hud HUD renders/drives/hot-reloads

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage** (against the approved design):
- Real track/section timeline (bands + track rows) → Tasks 1 (model) + 3 (document). ✅
- boom-hud, C# authored → Task 3 (`RuntimeSurfaceDocument` records). ✅
- Hot-reloadable bundle → Task 4 (bundle) + Task 6 Step 4 (verify reload; fallback noted). ✅
- New `App.Timeline` plugin + `timeline` bundle; `ITimelineController` in existing `App.World` contracts → Tasks 2, 4. ✅
- Keep the `HSlider` → Global Constraints + Task 6 Step 3. ✅
- Drives the transport (play/pause/seek) across the ALC boundary via the shared contract → Tasks 2, 3, 5. ✅

**2. Placeholder scan:** model + document code is complete; tests have full code + commands + expected results; the bundle recipe is concrete (mirrors assist). The two honest unknowns are explicitly handled, not hidden: (a) boom-hud variant/theme names (Task 3 note — fall back to default styling), (b) view-bundle hot-reload being first-of-its-kind (Task 6 Step 4 — scene-tier fallback). The app `.sln` name + a few `Host.cs` line numbers are "confirm on read." ✅

**3. Type consistency:** `ITimelineController` (Tick/MaxTick/IsPlaying/GeosphereSchedule/AtmosphereSchedule/Play/Pause/SeekTo/TickChanged) is identical across Tasks 2, 3 (FakeController), 5. `TimelineModel.Bands/Tracks` signatures match Tasks 1 + 3. `TimelineViewSource(ITimelineController)` ctor + `ViewId=="timeline"` consistent across Tasks 3, 4. Action strings (`timeline.play`/`timeline.pause`/`timeline.seek:<tick>`) consistent between the document (Task 3 author) and `Dispatch` (Task 3 handler). ✅

**Dependency:** Task 4 Step 1 (csprojs) precedes Tasks 1 & 3 (their tests need the test project). Task 2's `ITimelineController` must be registered (Task 5 Step 1) before the bundle activates (Task 5 Step 2).
