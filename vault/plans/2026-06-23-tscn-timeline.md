# tscn Timeline Implementation Plan

> **AUDIT (2026-07-06, code-verified):** COMPLETED — Timeline.tscn live, HSlider gone, ladder labels live. _(See the authority index in `vault/README.md`.)_


> For agentic workers: REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans. Steps use [ ] checkboxes.

Goal: Replace the hand-written playback loop and resident `HSlider` with an editor-authored, Godot-native `Timeline.tscn` scene in the hot-reloadable `timeline` bundle, driven by `AnimationPlayer` / `AnimationTree` and formatted using the odometer ladder.

Architecture: The timeline bundle is loaded as a `scene-tier` bundle containing a native scene `Timeline.tscn`. The scene represents a dynamic timeline UI mapping geosphere and atmosphere schedules into interactive regime button lanes and active layer tracks, utilizing the engine's `CanonicalDisplayFormatter` for label formatting. The resident `ITimelineController` is adjusted to receive pushed ticks from the bundle's `TimelineFace` script and push them to `GlobeView`, updating the visual globe. A static reference on `TimelinePlugin` bridges the ALC boundaries safely to provide the instantiated `TimelineFace` with the controller reference.

Tech Stack: .NET 8 C#, Godot 4 (.NET bindings), xUnit.

## Global Constraints
- Time axis is `CanonicalTick` (CT), where `UnitConverter.TicksPerMegaAnnum == 100_000`.
- Display formatting must use `CanonicalDisplayFormatter` with the `"geosphere.plate.time.v1"` profile (inputs in Kiloannum, 1 ka = 100,000 CT).
- No `git commit -A`. Run path-scoped `git add <file>` and commit after each task.
- Unsubscribe from resident events and unregister playback callbacks on bundle unload to prevent ALC pinning.
- Conventional commits format: `feat(timeline): <description>` or `test(timeline): <description>`.
- **ASCII ONLY** in all generated files (no unicode glyphs like ▶, ⏸, Ⅱ, · in labels, `.tscn` text, or scripts; use ASCII equivalents like "Play", "Pause", "playing", "paused", ":").

---

### Task 1: Adjust ITimelineController Interface
Files: Modify [ITimelineController.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/contracts/App.World/Composition/ITimelineController.cs)  
Interfaces: Consumes none / Produces `ITimelineController` with bundle-push and playhead registration methods.

- [ ] Modify `ITimelineController.cs` to add `PushTick`, `RegisterPlayback`, and `UnregisterPlayback` methods:
```csharp
using System;

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
    event Action<long>? TickChanged;

    void PushTick(long tick);
    void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying);
    void UnregisterPlayback();
}
```
- [ ] Run compile check:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/contracts/App.World/App.World.csproj -c Debug
```
Expected output: Build succeeds with warnings about missing interface implementations in `TimelineController.cs`.
- [ ] Commit interface changes:
```bash
git add yokan-projects/fantasim-app-godot/project/contracts/App.World/Composition/ITimelineController.cs
git commit -m "feat(timeline): extend ITimelineController with bundle-push and registration"
```

---

### Task 2: Implement Seam-Flipped TimelineController
Files: Modify [TimelineController.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.World.Seam/TimelineController.cs)  
Interfaces: Consumes modified `ITimelineController` / Produces updated `TimelineController` bridging directly to `GlobeView`.

- [ ] Replace `TimelineController.cs` content to reference `GlobeView` directly, handle callbacks, and implement the new `ITimelineController` methods:
```csharp
using System;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.Seam;

public sealed class TimelineController : ITimelineController
{
    private readonly GlobeView _globe;
    private long _tick;
    private Action? _onPlay;
    private Action? _onPause;
    private Action<long>? _onSeek;
    private Func<bool>? _checkPlaying;

    public TimelineController(GlobeView globe,
        SphereRegimeSchedule geosphere, SphereRegimeSchedule atmosphere, long maxTick)
    {
        _globe = globe ?? throw new ArgumentNullException(nameof(globe));
        GeosphereSchedule = geosphere ?? throw new ArgumentNullException(nameof(geosphere));
        AtmosphereSchedule = atmosphere ?? throw new ArgumentNullException(nameof(atmosphere));
        MaxTick = maxTick;
    }

    public long Tick => _tick;
    public long MaxTick { get; }
    public bool IsPlaying => _checkPlaying?.Invoke() ?? false;
    public SphereRegimeSchedule GeosphereSchedule { get; }
    public SphereRegimeSchedule AtmosphereSchedule { get; }

    public void Play() => _onPlay?.Invoke();
    public void Pause() => _onPause?.Invoke();
    public void SeekTo(long tick) => _onSeek?.Invoke(tick);

    public event Action<long>? TickChanged;

    public void PushTick(long tick)
    {
        _tick = tick;
        _globe.SetTick(tick);
        var regime = GeosphereSchedule.RegimeAt(tick);
        if (regime is not null)
        {
            _globe.SetRegime(regime.RegimeId, regime.ShowsPlateFeatures, regime.DefaultColorByField);
        }
        else
        {
            _globe.SetRegime("mobile-plate", true, null);
        }
        TickChanged?.Invoke(tick);
    }

    public void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying)
    {
        _onPlay = onPlay;
        _onPause = onPause;
        _onSeek = onSeek;
        _checkPlaying = checkPlaying;
    }

    public void UnregisterPlayback()
    {
        _onPlay = null;
        _onPause = null;
        _onSeek = null;
        _checkPlaying = null;
    }
}
```
- [ ] Compile App.World.Seam to verify structure compiles:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/plugins/App.World.Seam/App.World.Seam.csproj -c Debug
```
Expected output: Compiles successfully.
- [ ] Commit changes:
```bash
git add yokan-projects/fantasim-app-godot/project/plugins/App.World.Seam/TimelineController.cs
git commit -m "feat(timeline): implement seam-flipped TimelineController"
```

---

### Task 3: Retire Resident Transport and Scrubber, Update Host registration
Files: Modify [Host.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/hosts/complete-app/Host.cs), Modify [GlobeView.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.World.Seam/GlobeView.cs), Delete [RegimeTimelineTransport.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.World.Seam/RegimeTimelineTransport.cs)  
Interfaces: Consumes updated `TimelineController` / Produces cleaned host composition mapping and scrubber-free globe view.

- [ ] Delete the retired `RegimeTimelineTransport.cs` file:
```bash
git rm yokan-projects/fantasim-app-godot/project/plugins/App.World.Seam/RegimeTimelineTransport.cs
```
- [ ] Modify `GlobeView.cs` to remove scrubber setup and its UI fields:
  - Remove fields: `private Label? _label;`, `private HSlider? _slider;`
  - Remove calls to `BuildScrubber()` in `_Ready()`
  - Delete `BuildScrubber()` and `OnScrubberChanged(double tick)` methods
  - Remove lines referencing `_label` or `_slider` in `SetTick(long tick)` and `SetMaxTick(long maxTick)`
- [ ] Edit `Host.cs` (`ComposeWorldView` method):
  - Remove instantiation of `RegimeTimelineTransport transport`
  - Remove line `GetTree().Root.CallDeferred("add_child", transport);`
  - Update instantiation of `TimelineController` to:
  ```csharp
  var controller = new FantaSim.App.World.Seam.TimelineController(
      view, schedule, atmosphereSchedule, maxTransportTick);
  ```
  - Remove `transport.TickObserver = _ => controller.PumpTick();`
- [ ] Compile host project to check compliance:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/hosts/complete-app/complete-app.csproj -c Debug
```
Expected output: Success with zero errors.
- [ ] Commit changes:
```bash
git add yokan-projects/fantasim-app-godot/project/plugins/App.World.Seam/GlobeView.cs
git add yokan-projects/fantasim-app-godot/project/hosts/complete-app/Host.cs
git commit -m "feat(timeline): retire RegimeTimelineTransport and HSlider, update complete-app Host"
```

---

### Task 4: Configure Bundle Project References, Manifest, and Export Presets
Files: Modify [App.Timeline.csproj](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/App.Timeline.csproj), Modify [manifest.json](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/bundles/timeline/manifest.json), Modify [export_presets.cfg](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/hosts/content-app/export_presets.cfg)  
Interfaces: Consumes `manifest.json`, `export_presets.cfg` / Produces correctly packaged timeline bundle.

- [ ] Change `App.Timeline.csproj` Sdk to use `Godot.NET.Sdk/4.7.0` instead of `Microsoft.NET.Sdk`. Add a reference to `World.Shared.csproj` inside `App.Timeline.csproj`:
```xml
<Project Sdk="Godot.NET.Sdk/4.7.0">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>FantaSim.App.Timeline</RootNamespace>
    <AssemblyName>FantaSim.App.Timeline</AssemblyName>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\contracts\App.SceneFlow\App.SceneFlow.csproj" />
    <ProjectReference Include="..\..\contracts\App.World\App.World.csproj" />
    <ProjectReference Include="..\..\contracts\App.Ui\App.Ui.csproj" />
    <ProjectReference Include="$(YokanProjectsRoot)\fantasim-world\project\contracts\World.Shared\World.Shared.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="BoomHud.Foundation" />
    <PackageReference Include="GiantCroissant.ServiceArchi.Contracts" />
    <PackageReference Include="GiantCroissant.PluginArchi.Extensibility.Abstractions" />
    <PackageReference Include="GiantCroissant.PluginArchi.SourceGenerators" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```
- [ ] Modify `manifest.json` under `project/bundles/timeline/manifest.json` to register `"entryScene": "scenes/Timeline.tscn"`, `"scenes": ["scenes/Timeline.tscn"]`, and change its metadata to match `scene-tier` bundle configuration. Also declare `residentScripts` to attach `TimelineFace` script to the root node at load time (avoiding Godot-derived script compile references within `.tscn` file itself):
```json
{
  "bundleId": "timeline",
  "displayName": "Timeline",
  "version": "0.1.0",
  "entryScene": "scenes/Timeline.tscn",
  "pluginAssembly": "FantaSim.App.Timeline.dll",
  "scenes": [
    "scenes/Timeline.tscn"
  ],
  "metadata": {
    "bundleType": "scene-tier"
  },
  "residentScripts": [
    {
      "nodePath": ".",
      "residentType": "FantaSim.App.Timeline.TimelineFace"
    }
  ]
}
```
- [ ] Modify `export_presets.cfg` under `project/hosts/content-app/export_presets.cfg` under `[preset.2]` (timeline PCK) to filter and export the timeline `.tscn` file:
  - Add `"res://bundles/timeline/scenes/Timeline.tscn"` to `export_files`
  - Update `include_filter` to `"bundles/timeline/*.json,bundles/timeline/*.dll,bundles/timeline/scenes/*.tscn"`
```ini
export_files=PackedStringArray("res://bundles/timeline/scenes/Timeline.tscn")
include_filter="bundles/timeline/*.json,bundles/timeline/*.dll,bundles/timeline/scenes/*.tscn"
```
- [ ] Commit configuration updates:
```bash
git add yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/App.Timeline.csproj
git add yokan-projects/fantasim-app-godot/project/bundles/timeline/manifest.json
git add yokan-projects/fantasim-app-godot/project/hosts/content-app/export_presets.cfg
git commit -m "feat(timeline): configure project references, manifest scene, and export pack filters"
```

---

### Task 5: Implement TimelineActivator, TimelineActivation, and Bootstrap
Files: Modify [TimelineActivator.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineActivator.cs), Modify [TimelineActivation.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineActivation.cs), Modify [Bootstrap.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/Bootstrap.cs)  
Interfaces: Consumes `ISceneActivator`, `ISceneActivation` / Produces child scope activator.

- [ ] Ensure that `TimelineActivator.cs` remains a valid scene activator resolving parent dependencies.
- [ ] Verify that `TimelineActivation.cs` owns the scope and disposes it.
- [ ] Verify `Bootstrap.cs` prints logs correctly.
- [ ] Compile assembly to verify correctness:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/App.Timeline.csproj -c Debug
```
- [ ] Commit changes:
```bash
git add yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineActivator.cs
git add yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineActivation.cs
git add yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/Bootstrap.cs
git commit -m "feat(timeline): implement Timeline scene activator structures"
```

---

### Task 6: Implement Static Bridge on TimelinePlugin
Files: Modify [TimelinePlugin.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelinePlugin.cs)  
Interfaces: Consumes `ITimelineController` / Produces `TimelinePlugin.ActiveController` static bridge for scene access, while dropping the retired `TimelineViewSource`.

- [ ] Delete `TimelineViewSource.cs` file completely as it's retired:
```bash
git rm yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineViewSource.cs
```
- [ ] Modify `TimelinePlugin.cs` to capture `ActiveController` statically and clear it on shutdown, registering ONLY the `TimelineActivator` and avoiding the retired `IViewSource` and `Mount` logic:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;
using FantaSim.App.World.Composition;
using Microsoft.Extensions.DependencyInjection;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;

namespace FantaSim.App.Timeline;

[Plugin("app.timeline", Name = "Timeline HUD", Description = "Registers the timeline scene activator.", Tags = "scene-tier")]
public sealed partial class TimelinePlugin : ILifecyclePlugin
{
    private IDisposable? _activatorRegistration;

    public static ITimelineController? ActiveController { get; private set; }

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();

        _activatorRegistration = registry.RegisterOwned<ISceneActivator>(
            new TimelineActivator(),
            new ServiceRegistration { Tags = new[] { "scene-activator" }, Description = "timeline activator (bundle)" });

        var controller = registry.TryGet<ITimelineController>();
        if (controller is not null)
        {
            ActiveController = controller;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        ActiveController = null;
        _activatorRegistration?.Dispose();
        _activatorRegistration = null;
        return ValueTask.CompletedTask;
    }
}
```
- [ ] Build the App.Timeline project to check compilation:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/App.Timeline.csproj -c Debug
```
Expected output: Compile succeeds.
- [ ] Commit changes:
```bash
git add yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelinePlugin.cs
git commit -m "feat(timeline): implement active controller bridge and clean view source"
```

---

### Task 7: Implement Timeline C# Glue (TimelineFace)
Files: Create [TimelineFace.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineFace.cs)  
Interfaces: Consumes `TimelinePlugin.ActiveController` / Produces `TimelineFace` controlling dynamic Animation systems and UI layout.

- [ ] Create `TimelineFace.cs` wrapping the Playhead logic, Dynamic Animation player track configuration, and resize rendering logic. Ensure all label formatting and strings are **ASCII-only** (e.g. "playing" and "paused" instead of "▶ playing" and "Ⅱ paused"):
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FantaSim.App.World.Composition;
using FantaSim.World.Contracts.Quantities;
using FantaSim.World.Contracts.Units;

namespace FantaSim.App.Timeline;

public partial class TimelineFace : Control
{
    private ITimelineController? _ctl;
    private AnimationPlayer? _animationPlayer;
    private AnimationTree? _animationTree;
    private AnimationNodeStateMachinePlayback? _playback;

    private Button? _playPauseButton;
    private Label? _statusLabel;
    private Control? _lanesContainer;
    private ColorRect? _playheadLine;

    private readonly List<(Button Button, double Start, double Width)> _geosphereBands = new();
    private readonly List<(Button Button, double Start, double Width)> _atmosphereBands = new();
    private readonly List<(Control Control, string LayerId, string Sphere)> _tracks = new();

    private double _internalTick;
    private long _lastPushedTick = -1;
    private bool _isPlaying;
    private readonly double _ticksPerSecond = 5_000_000.0;

    public double InternalTick
    {
        get => _internalTick;
        set
        {
            _internalTick = value;
            var tick = (long)value;
            if (tick != _lastPushedTick && _ctl is not null)
            {
                _lastPushedTick = tick;
                _ctl.PushTick(tick);
                UpdateUI();
            }
        }
    }

    public override void _Ready()
    {
        _ctl = TimelinePlugin.ActiveController;
        if (_ctl is null)
        {
            GD.PushWarning("[TimelineFace] No active ITimelineController found.");
            SetProcess(false);
            return;
        }

        _playPauseButton = GetNode<Button>("VBoxContainer/Header/PlayPauseButton");
        _statusLabel = GetNode<Label>("VBoxContainer/Header/StatusLabel");
        _lanesContainer = GetNode<Control>("VBoxContainer/LanesContainer");
        _playheadLine = GetNode<ColorRect>("VBoxContainer/LanesContainer/PlayheadLine");

        _playPauseButton.Pressed += OnPlayPausePressed;
        _lanesContainer.GuiInput += OnLanesGuiInput;
        Resized += OnLanesResized;

        BuildLanes();
        _ctl.RegisterPlayback(Play, Pause, SeekTo, () => _isPlaying);
        SetupAnimationSystem();

        SeekTo(_ctl.Tick);
    }

    public override void _ExitTree()
    {
        if (_ctl is not null)
        {
            _ctl.UnregisterPlayback();
        }
        if (_playPauseButton is not null)
        {
            _playPauseButton.Pressed -= OnPlayPausePressed;
        }
        Resized -= OnLanesResized;
    }

    private void SetupAnimationSystem()
    {
        _animationPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        if (_animationPlayer is null)
        {
            _animationPlayer = new AnimationPlayer { Name = "AnimationPlayer" };
            AddChild(_animationPlayer);
        }

        _animationTree = GetNodeOrNull<AnimationTree>("AnimationTree");
        if (_animationTree is null)
        {
            _animationTree = new AnimationTree
            {
                Name = "AnimationTree",
                AnimPlayer = new NodePath("../AnimationPlayer"),
                Active = false
            };
            AddChild(_animationTree);
        }

        var library = new AnimationLibrary();

        var anim = new Animation();
        anim.Length = (float)(_ctl!.MaxTick / _ticksPerSecond);
        anim.LoopMode = Animation.LoopModeEnum.Linear;

        int trackIdx = anim.AddTrack(Animation.TrackType.Value);
        anim.TrackSetPath(trackIdx, new NodePath(".:InternalTick"));
        anim.TrackInsertKey(trackIdx, 0f, 0.0);
        anim.TrackInsertKey(trackIdx, anim.Length, (double)_ctl.MaxTick);

        library.AddAnimation(new StringName("playing"), anim);
        library.AddAnimation(new StringName("idle"), new Animation { Length = 1f, LoopMode = Animation.LoopModeEnum.Linear });
        library.AddAnimation(new StringName("scrub"), new Animation { Length = 1f, LoopMode = Animation.LoopModeEnum.None });

        _animationPlayer.AddAnimationLibrary(new StringName(string.Empty), library);

        var machine = new AnimationNodeStateMachine
        {
            AllowTransitionToSelf = true,
            ResetEnds = true,
        };
        machine.AddNode(new StringName("idle"),    new AnimationNodeAnimation { Animation = new StringName("idle") }, new Vector2(0f, 0f));
        machine.AddNode(new StringName("playing"), new AnimationNodeAnimation { Animation = new StringName("playing") }, new Vector2(200f, -80f));
        machine.AddNode(new StringName("scrub"),   new AnimationNodeAnimation { Animation = new StringName("scrub") }, new Vector2(200f, 80f));

        var states = new[] { "idle", "playing", "scrub" };
        foreach (var from in states)
        {
            foreach (var to in states)
            {
                if (from == to) continue;
                machine.AddTransition(new StringName(from), new StringName(to), new AnimationNodeStateMachineTransition
                {
                    XfadeTime = 0.12f,
                    SwitchMode = AnimationNodeStateMachineTransition.SwitchModeEnum.Sync,
                    AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Enabled,
                    Reset = false,
                });
            }
        }

        _animationTree.TreeRoot = machine;
        _animationTree.Active = true;
        _playback = _animationTree.Get("parameters/playback").As<AnimationNodeStateMachinePlayback>();
        _playback?.Start(new StringName("idle"), reset: true);
    }

    private void Play()
    {
        if (_ctl is null) return;
        _isPlaying = true;
        TransitionState("playing");
    }

    private void Pause()
    {
        if (_ctl is null) return;
        _isPlaying = false;
        TransitionState("idle");
    }

    private void SeekTo(long tick)
    {
        if (_ctl is null) return;
        tick = Math.Clamp(tick, 0L, _ctl.MaxTick);
        _internalTick = tick;
        _lastPushedTick = tick;

        if (_animationPlayer is not null)
        {
            var pos = tick / _ticksPerSecond;
            _animationPlayer.Seek(pos, update: true);
        }

        TransitionState("scrub");
        _ctl.PushTick(tick);
        UpdateUI();
    }

    private void TransitionState(string state)
    {
        if (_animationTree is not { Active: true } || _playback is null) return;
        var sn = new StringName(state);
        if (!_playback.IsPlaying())
            _playback.Start(sn, reset: false);
        else
            _playback.Travel(sn, reset: false);
    }

    private void OnPlayPausePressed()
    {
        if (_ctl is null) return;
        if (_isPlaying)
            _ctl.Pause();
        else
            _ctl.Play();
    }

    private void OnLanesGuiInput(InputEvent @event)
    {
        if (_ctl is null || _lanesContainer is null) return;

        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left && mouseBtn.Pressed)
            {
                HandleScrub(mouseBtn.Position.X);
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            if ((mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                HandleScrub(mouseMotion.Position.X);
            }
        }
    }

    private void HandleScrub(float localX)
    {
        if (_ctl is null || _lanesContainer is null) return;
        var totalWidth = _lanesContainer.Size.X;
        if (totalWidth <= 0) return;

        var fraction = localX / totalWidth;
        var tick = (long)Math.Clamp(fraction * _ctl.MaxTick, 0.0, _ctl.MaxTick);
        _ctl.SeekTo(tick);
    }

    private void OnBandPressed(long startTick)
    {
        _ctl?.SeekTo(startTick);
    }

    private void OnLanesResized()
    {
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        if (_lanesContainer is null) return;
        var width = _lanesContainer.Size.X;

        foreach (var band in _geosphereBands)
        {
            band.Button.Position = new Vector2((float)(band.Start * width), 0);
            band.Button.Size = new Vector2((float)(band.Width * width), 32);
        }

        foreach (var band in _atmosphereBands)
        {
            band.Button.Position = new Vector2((float)(band.Start * width), 0);
            band.Button.Size = new Vector2((float)(band.Width * width), 32);
        }

        UpdateUI();
    }

    private void BuildLanes()
    {
        if (_ctl is null || _lanesContainer is null) return;

        var geosphereRegimesRoot = GetNode<Control>("VBoxContainer/LanesContainer/LanesList/GeosphereLane/GeosphereRegimes");
        var geosphereTracksRoot = GetNode<Control>("VBoxContainer/LanesContainer/LanesList/GeosphereLane/GeosphereTracks");
        var atmosphereRegimesRoot = GetNode<Control>("VBoxContainer/LanesContainer/LanesList/AtmosphereLane/AtmosphereRegimes");
        var atmosphereTracksRoot = GetNode<Control>("VBoxContainer/LanesContainer/LanesList/AtmosphereLane/AtmosphereTracks");

        ClearChildren(geosphereRegimesRoot);
        ClearChildren(geosphereTracksRoot);
        ClearChildren(atmosphereRegimesRoot);
        ClearChildren(atmosphereTracksRoot);

        _geosphereBands.Clear();
        _atmosphereBands.Clear();
        _tracks.Clear();

        PopulateLane(_ctl.GeosphereSchedule, geosphereRegimesRoot, geosphereTracksRoot, _geosphereBands, "geosphere");
        PopulateLane(_ctl.AtmosphereSchedule, atmosphereRegimesRoot, atmosphereTracksRoot, _atmosphereBands, "atmosphere");
    }

    private void PopulateLane(
        SphereRegimeSchedule schedule,
        Control regimesRoot,
        Control tracksRoot,
        List<(Button Button, double Start, double Width)> bandList,
        string sphere)
    {
        var bands = TimelineModel.Bands(schedule, _ctl!.MaxTick, _ctl.Tick);
        foreach (var b in bands)
        {
            var btn = new Button
            {
                Text = b.RegimeId,
                ClipText = true,
                FocusMode = FocusModeEnum.None
            };

            var normalStyle = new StyleBoxFlat();
            normalStyle.BgColor = GetRegimeColor(b.RegimeId);
            normalStyle.SetCornerRadiusAll(3);
            btn.AddThemeStyleboxOverride("normal", normalStyle);
            btn.AddThemeStyleboxOverride("hover", normalStyle);
            btn.AddThemeStyleboxOverride("pressed", normalStyle);

            long seekTick = schedule.Regimes.FirstOrDefault(r => r.RegimeId == b.RegimeId)?.StartTick ?? 0L;
            btn.Pressed += () => OnBandPressed(seekTick);

            regimesRoot.AddChild(btn);
            bandList.Add((btn, b.StartFraction, b.WidthFraction));
        }

        var tracks = TimelineModel.Tracks(schedule, _ctl.Tick);
        foreach (var t in tracks)
        {
            var trackControl = new PanelContainer { CustomMinimumSize = new Vector2(0, 24) };
            var label = new Label { Text = $"  {t.LayerId}", VerticalAlignment = VerticalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 13);
            trackControl.AddChild(label);

            var style = new StyleBoxFlat { BgColor = new Color(0.12f, 0.15f, 0.18f, 0.5f) };
            style.SetBorderWidthAll(1);
            style.BorderColor = new Color(0.2f, 0.24f, 0.28f);
            style.SetCornerRadiusAll(3);
            trackControl.AddThemeStyleboxOverride("panel", style);

            tracksRoot.AddChild(trackControl);
            _tracks.Add((trackControl, t.LayerId, sphere));
        }
    }

    private Color GetRegimeColor(string regimeId) => regimeId switch
    {
        "magma-ocean" => Color.FromHtml("#ff9800"),
        "stagnant-lid" => Color.FromHtml("#607d8b"),
        "mobile-plate" => Color.FromHtml("#008080"),
        "primordial-steam" or "secondary-co2" => Color.FromHtml("#1e88e5"),
        "coupled-climate" => Color.FromHtml("#008080"),
        _ => Color.FromHtml("#9e9e9e")
    };

    private void UpdateUI()
    {
        if (_ctl is null || _statusLabel is null || _playPauseButton is null || _playheadLine is null || _lanesContainer is null) return;

        var tick = _ctl.Tick;
        double kaAmount = (double)tick / UnitConverter.TicksPerMegaAnnum;
        var timeLabel = CanonicalDisplayFormatter.Format(kaAmount, BaselineScaleProfiles.GeospherePlateTimeV1, new CanonicalFormatterOptions(IncludeUnitSuffix: true));

        var playState = _isPlaying ? "playing" : "paused";
        var geoRegime = _ctl.GeosphereSchedule.RegimeAt(tick)?.RegimeId ?? "-";
        _statusLabel.Text = $"{playState} : {geoRegime} : {timeLabel}";
        _playPauseButton.Text = _isPlaying ? "Pause" : "Play";

        var fraction = (double)tick / _ctl.MaxTick;
        _playheadLine.Position = new Vector2((float)(fraction * _lanesContainer.Size.X), 0);
        _playheadLine.Size = new Vector2(2, _lanesContainer.Size.Y);

        foreach (var band in _geosphereBands)
        {
            var isCurrent = _ctl.GeosphereSchedule.RegimeAt(tick)?.RegimeId == band.Button.Text;
            band.Button.Modulate = isCurrent ? new Color(1, 1, 1, 1f) : new Color(1, 1, 1, 0.3f);
        }

        foreach (var band in _atmosphereBands)
        {
            var isCurrent = _ctl.AtmosphereSchedule.RegimeAt(tick)?.RegimeId == band.Button.Text;
            band.Button.Modulate = isCurrent ? new Color(1, 1, 1, 1f) : new Color(1, 1, 1, 0.3f);
        }

        var activeGeoLayers = _ctl.GeosphereSchedule.RegimeAt(tick)?.ActiveLayers.Select(l => l.Value).ToHashSet() ?? new HashSet<string>();
        var activeAtmoLayers = _ctl.AtmosphereSchedule.RegimeAt(tick)?.ActiveLayers.Select(l => l.Value).ToHashSet() ?? new HashSet<string>();

        foreach (var track in _tracks)
        {
            bool isActive = track.Sphere == "geosphere"
                ? activeGeoLayers.Contains(track.LayerId)
                : activeAtmoLayers.Contains(track.LayerId);

            track.Control.Modulate = isActive ? new Color(1, 1, 1, 1f) : new Color(1, 1, 1, 0.3f);
        }
    }

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
```
- [ ] Build assembly to check compilation:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/App.Timeline.csproj -c Debug
```
Expected output: Success.
- [ ] Commit the glue script:
```bash
git add yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineFace.cs
git commit -m "feat(timeline): implement TimelineFace C# script"
```

---

### Task 8: Author the native Timeline Scene file (Timeline.tscn)
Files: Create [Timeline.tscn](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/bundles/timeline/scenes/Timeline.tscn)  
Interfaces: Consumes `TimelineFace.cs` / Produces native `.tscn` file loaded by Godot during bundle mount.

- [ ] Write the text format of `Timeline.tscn` node tree definition, mirroring the script-less design of `stage_entry.tscn` (and resolving references to `TimelineFace` at load time via the manifest `residentScripts` configuration) and ensuring all default texts are **ASCII-only**:
```text
[gd_scene format=3]

[node name="Timeline" type="PanelContainer"]
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 12.0
offset_top = 44.0
offset_right = -12.0
offset_bottom = -12.0
grow_horizontal = 2
grow_vertical = 2

[node name="VBoxContainer" type="VBoxContainer" parent="."]
layout_mode = 2

[node name="Header" type="HBoxContainer" parent="VBoxContainer"]
layout_mode = 2

[node name="PlayPauseButton" type="Button" parent="VBoxContainer/Header"]
layout_mode = 2
text = "Play"

[node name="StatusLabel" type="Label" parent="VBoxContainer/Header"]
layout_mode = 2
text = "paused : magma-ocean : 0 ka"

[node name="LanesContainer" type="Control" parent="VBoxContainer"]
layout_mode = 2
size_flags_vertical = 3

[node name="LanesList" type="VBoxContainer" parent="VBoxContainer/LanesContainer"]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2

[node name="GeosphereLane" type="VBoxContainer" parent="VBoxContainer/LanesContainer/LanesList"]
layout_mode = 2

[node name="TitleLabel" type="Label" parent="VBoxContainer/LanesContainer/LanesList/GeosphereLane"]
layout_mode = 2
text = "Geosphere"

[node name="GeosphereRegimes" type="Control" parent="VBoxContainer/LanesContainer/LanesList/GeosphereLane"]
custom_minimum_size = Vector2(0, 32)
layout_mode = 2

[node name="GeosphereTracks" type="VBoxContainer" parent="VBoxContainer/LanesContainer/LanesList/GeosphereLane"]
layout_mode = 2

[node name="AtmosphereLane" type="VBoxContainer" parent="VBoxContainer/LanesContainer/LanesList"]
layout_mode = 2

[node name="TitleLabel" type="Label" parent="VBoxContainer/LanesContainer/LanesList/AtmosphereLane"]
layout_mode = 2
text = "Atmosphere"

[node name="AtmosphereRegimes" type="Control" parent="VBoxContainer/LanesContainer/LanesList/AtmosphereLane"]
custom_minimum_size = Vector2(0, 32)
layout_mode = 2

[node name="AtmosphereTracks" type="VBoxContainer" parent="VBoxContainer/LanesContainer/LanesList/AtmosphereLane"]
layout_mode = 2

[node name="PlayheadLine" type="ColorRect" parent="VBoxContainer/LanesContainer"]
layout_mode = 0
offset_right = 2.0
color = Color(1, 1, 1, 1)

[node name="AnimationPlayer" type="AnimationPlayer" parent="."]

[node name="AnimationTree" type="AnimationTree" parent="."]
anim_player = NodePath("../AnimationPlayer")
```
- [ ] Verify the file is placed correctly:
```bash
ls -l yokan-projects/fantasim-app-godot/project/bundles/timeline/scenes/Timeline.tscn
```
Expected output: File exists.
- [ ] Commit the native `.tscn` file:
```bash
git add yokan-projects/fantasim-app-godot/project/bundles/timeline/scenes/Timeline.tscn
git commit -m "feat(timeline): author native Timeline.tscn layout scene"
```

---

### Task 9: Add Pure C# Unit Tests for Odometer Label formatting
Files: Create [OdometerLabelTests.cs](file:///Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/OdometerLabelTests.cs)  
Interfaces: Consumes `CanonicalDisplayFormatter` / Produces xUnit tests for odometer tick values.

- [ ] Create the test file `OdometerLabelTests.cs` validating time format rollovers using real engine assemblies (ensuring the formats use exact geosphere namespaces and baseline scale profiles):
```csharp
using Xunit;
using FantaSim.World.Contracts.Quantities;
using FantaSim.World.Contracts.Units;

namespace App.Timeline.Tests;

public class OdometerLabelTests
{
    [Theory]
    [InlineData(0, "0 ka")]
    [InlineData(500_000, "5 ka")]
    [InlineData(100_000_000, "1 kb")]
    [InlineData(150_000_000, "1.5 kb")]
    public void FormatCanonicalTick_YieldsCorrectOdometerLabel(long tick, string expected)
    {
        double kaAmount = (double)tick / UnitConverter.TicksPerMegaAnnum;
        var result = CanonicalDisplayFormatter.Format(
            kaAmount,
            BaselineScaleProfiles.GeospherePlateTimeV1,
            new CanonicalFormatterOptions(IncludeUnitSuffix: true));
        Assert.Equal(expected, result);
    }
}
```
- [ ] Execute tests (expect fail/pass):
```bash
dotnet test yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --filter OdometerLabelTests
```
Expected output: Passed 4 tests.
- [ ] Commit unit tests:
```bash
git add yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/OdometerLabelTests.cs
git commit -m "test(timeline): add unit tests for odometer tick labels"
```

---

### Task 10: Run the complete test suite
Files: Modify none  
Interfaces: Consumes all assemblies / Produces test report verifying no regressions.

- [ ] Execute all app unit tests:
```bash
dotnet test yokan-projects/fantasim-app-godot/project/FantaSim.sln
```
Expected output: All unit tests pass successfully.

---

### Task 11: Export Bundles and verify windowed run (WINDOWED VERIFY)
Files: Modify none (Build execution task)  
Interfaces: Consumes compiled C# DLLs + content project / Produces exported PCK bundles and executable.

- [ ] Run Taskfile command to build and package timeline bundle and install:
```bash
task bundle:timeline:build
task bundle:timeline
task bundle:install
```
Expected output: `timeline.pck` compiles successfully and is copied into the OS package bundle directories.
- [ ] Execute exported app run (WINDOWED VERIFY):
```bash
task run:exported
```
Verification Checklist:
1. Viewport launches successfully in windowed mode.
2. The bottom panel displays the multi-lane native `Timeline` scene instead of the BoomHudFlex HUD.
3. Tap the "Play" button: playhead advance starts smoothly from 0; the status label updates tick times (e.g. `5 ka`, `8 ka`) using odometer formatting.
4. Watch the playhead cross the `100_000_000` CT boundary: geosphere regime switches from stagnant-lid (no plate features rendered) to mobile-plate; plate cap geometry pops visible; time formatting shifts to `1 kb` (rollover).
5. Drag the playhead manually: timeline seeks to target tick correctly, driving both playhead position and GlobeView rotation axis.
6. Click different regime blocks (e.g. `coupled-climate` block): the timeline seeks to the start of that regime instantly.
7. Trigger bundle hot-reload (rebuild DLL while app is running): verify ALC unloads cleanly without pinning, reloading without a crash.
