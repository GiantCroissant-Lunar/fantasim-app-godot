using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.Timeline;
using FantaSim.App.Command;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Timeline.Seam;

public partial class TimelineFace : Control, ITimelineFace
{
    private ITimelineController? _ctl;
    private AnimationPlayer? _animationPlayer;
    private AnimationTree? _animationTree;
    private AnimationNodeStateMachinePlayback? _playback;

    private Button? _playPauseButton;
    private Button? _zoomOutButton;
    private Button? _fitButton;
    private Button? _zoomInButton;
    private Label? _statusLabel;
    private Label? _zoomLabel;
    private Control? _rulerRoot;
    private Control? _lanesContainer;
    private ColorRect? _playheadLine;
    private TimelinePlayheadHandle? _playheadHandle;
    private readonly TimelineScrubCoalescer _scrubCoalescer = new();

    // Band lists keyed by SphereId (populated fresh in BuildLanes from the registry-driven lane
    // list -- never a fixed geosphere/atmosphere pair).
    private readonly Dictionary<string, List<(Button Button, double Start, double Width)>> _bandsBySphere = new(StringComparer.Ordinal);
    private readonly List<TrackRowBinding> _tracks = new();
    private readonly Dictionary<TrackContentPresenterKind, Action<TrackContentRenderContext>> _trackContentPresenters;
    private readonly FilmstripPreviewController _filmstrip;
    private IClient? _commandClient;
    private Func<long, WorldGenerationGraphFamilyDocument?>? _generationGraphFamilyProvider;
    private Func<int>? _filmstripGraphRevisionProvider;
    private ILayerTrackRegistry? _layerTrackRegistry;

    private double _internalTick;
    private long _lastPushedTick = -1;
    private bool _isPlaying;
    private long _viewStartTick;
    private long _viewEndTick;
    private TimelineViewSnapshot? _lastViewSnapshot;
    private bool _nodesInitialized;
    private bool _playbackRegistered;
    private bool _proxyBound;
    private long _hudModeEpoch = -1L;
    private int _residentBindGeneration;
    private double _ticksPerSecond = 5_000_000.0;
    private const long MinViewSpanTicks = 1L;
    private const int RungSpanUnits = 10;
    private const float RegimeBandHeight = 28f;
    private const float TrackHeight = TimelineFilmstrip.CompactTrackHeight;
    private const float ExpandedTrackHeight = TimelineTrackLayout.ExpandedTrackHeight;
    private const float TrackHeaderWidth = 190f;
    private const float TrackChevronWidth = 24f;
    private const float TrackContentGap = 5f;
    private const float PlayheadLineGrabMargin = 8f;
    private const float PlayheadHandleWidth = 22f;
    private const float PlayheadHandleHeight = 20f;
    private const double ViewRebuildCoalesceSeconds = 0.08;

    private TimelineLadderRung SelectedRung => TimelineModel.SelectRungForSpan(_viewEndTick - _viewStartTick);
    private int _viewRebuildVersion;
    private bool _viewRebuildQueued;

    private ILogger _log;

    /// <summary>
    /// Resident registry bridge set by the resident host before timeline scene entry. The
    /// collectible bundle never writes this static; it only registers ITimelineFaceContext into
    /// the registry.
    /// </summary>
    public static IRegistry? ResidentRegistry { get; private set; }
    private static WeakReference<TimelineFace>? _activeFace;

    public static void SetResidentRegistry(IRegistry registry)
        => ResidentRegistry = registry ?? throw new ArgumentNullException(nameof(registry));

    public static bool TryRestoreSafeHud()
    {
        if (_activeFace?.TryGetTarget(out var face) != true
            || !GodotObject.IsInstanceValid(face)
            || !face.IsInsideTree())
        {
            return false;
        }

        face.Visible = true;
        return true;
    }

    public TimelineFace()
    {
        _log = ResidentRegistry?.TryGet<ILoggerFactory>()?.CreateLogger("Timeline.Face")
            ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger("Timeline.Face");

        // Content-strip presenter lookup, keyed by the presenter kind TrackLaneViewModelBuilder
        // resolves from content.Type (single source of truth for which types are known --
        // vault/specs/2026-07-10-layer-track-registry-design.md). Data-keyed, never a switch on
        // layer/sphere ids.
        _trackContentPresenters = new Dictionary<TrackContentPresenterKind, Action<TrackContentRenderContext>>
        {
            [TrackContentPresenterKind.Filmstrip] = RenderFilmstripTrackContent,
            [TrackContentPresenterKind.Graph] = RenderGraphTrackContent,
            [TrackContentPresenterKind.Generic] = RenderGenericTrackContent,
        };

        // The aliveness predicate copies the EXACT guards the 825461b fix introduced: the
        // deferred callable checked IsInstanceValid, and ApplyFilmstripPreview checked
        // IsInstanceValid && IsInsideTree. The controller uses the stronger (both) predicate
        // everywhere — at least as strong as both original guards, and safe because &&
        // short-circuits so IsInsideTree is never called on a freed instance.
        _filmstrip = new FilmstripPreviewController(
            isFaceAlive: () => GodotObject.IsInstanceValid(this) && IsInsideTree(),
            deferToMainThread: action => Callable.From(action).CallDeferred(),
            log: _log);
    }

    [Export]
    public double InternalTick
    {
        get => _internalTick;
        set
        {
            _internalTick = value;
            var tick = (long)value;
            if (!_isPlaying)
                return;
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
        _activeFace = new WeakReference<TimelineFace>(this);
        BindResidentContext(forceProxyBind: false);
    }

    public override void _Process(double delta)
    {
        if (_ctl is null)
        {
            BindResidentContext(forceProxyBind: false);
            return;
        }

        ApplyScrubAction(_scrubCoalescer.ConsumeFrame());
    }

    public void ApplyHudState(TimelineHudState state)
    {
        var bindGeneration = _residentBindGeneration;
        void Apply()
        {
            if (!TimelineHudReplayPolicy.CanApply(
                    bindGeneration,
                    _residentBindGeneration,
                    state.ModeEpoch,
                    _hudModeEpoch))
                return;
            _hudModeEpoch = state.ModeEpoch;
            Visible = state.Visible;
        }

        // Same off-thread marshal as RebindResidentContext: Control.Visible is main-thread-only,
        // and the tunnel-view command can reach this face from the ingress path.
        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
            Apply();
        else
            Callable.From(Apply).CallDeferred();
    }

    public void RebindResidentContext()
    {
        // TimelinePlugin raises this from the resource watcher's thread-pool thread; the bind
        // walks into UpdateLayout/AddChild, which Godot only allows on the main thread. The
        // deleted host machinery marshaled via CallDeferred — the face owns that marshal now.
        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
        {
            BindResidentContext(forceProxyBind: true);
            return;
        }

        Callable.From(() => BindResidentContext(forceProxyBind: true)).CallDeferred();
    }

    private void BindResidentContext(bool forceProxyBind)
    {
        var context = ResidentRegistry?.TryGet<ITimelineFaceContext>();
        if (context is null)
        {
            ClearResidentContext();
            _log.LogWarning("No active timeline face context found.");
            SetProcess(true);
            return;
        }

        _residentBindGeneration++;
        _hudModeEpoch = -1L;
        if (forceProxyBind || !_proxyBound)
        {
            // Bind forwarding before copying the context and replaying HUD state. A command that
            // lands immediately after this point can forward, and epoch ordering makes either
            // arrival order deterministic.
            context.Proxy.BindCrossTarget(this);
            _proxyBound = true;
        }

        var controller = context.Controller;
        _log = context.LoggerFactory.CreateLogger("Timeline.Face");

        // A controller swap (world hot-reload) must re-register playback on the NEW controller:
        // clear the flag after unregistering from the old one, or the RegisterPlayback below is
        // skipped and Play/Pause/Seek callbacks silently stay unwired after the first reload.
        if (_playbackRegistered && !ReferenceEquals(_ctl, controller))
        {
            _ctl?.UnregisterPlayback();
            _playbackRegistered = false;
        }

        _ctl = controller;
        SetProcess(true);

        _commandClient = context.CommandClient as IClient;
        _generationGraphFamilyProvider = context.GenerationGraphFamilyProvider;
        _filmstripGraphRevisionProvider = context.FilmstripGraphRevisionProvider;
        _filmstrip.SetPreviewProvider(context.FilmstripPreviewProvider);
        _ticksPerSecond = context.TicksPerSecond > 0.0 ? context.TicksPerSecond : 5_000_000.0;
        BindLayerTrackRegistry(context.LayerTrackRegistry);

        if (!_nodesInitialized)
        {
            _playPauseButton = GetNode<Button>("VBoxContainer/Header/PlayPauseButton");
            _zoomOutButton = GetNode<Button>("VBoxContainer/Header/ZoomOutButton");
            _fitButton = GetNode<Button>("VBoxContainer/Header/FitButton");
            _zoomInButton = GetNode<Button>("VBoxContainer/Header/ZoomInButton");
            _statusLabel = GetNode<Label>("VBoxContainer/Header/StatusLabel");
            _zoomLabel = GetNode<Label>("VBoxContainer/Header/ZoomLabel");
            _rulerRoot = GetNode<Control>("VBoxContainer/Ruler");
            _lanesContainer = GetNode<Control>("VBoxContainer/LanesContainer");
            _playheadLine = GetNode<ColorRect>("VBoxContainer/LanesContainer/PlayheadLine");

            _playPauseButton.Pressed += OnPlayPausePressed;
            _zoomOutButton.Pressed += OnZoomOutPressed;
            _fitButton.Pressed += OnFitPressed;
            _zoomInButton.Pressed += OnZoomInPressed;
            _lanesContainer.GuiInput += OnLanesGuiInput;
            // The Ruler control cannot receive input directly: LanesList (a later sibling's
            // child) bleeds ~37px ABOVE LanesContainer's rect and overlaps the whole ruler band,
            // and later tree order wins GUI picking (found live via the camera.debug control
            // probe, 2026-07-08). The face's ROOT panel receives every click its children do not
            // consume, so ruler-band scrubbing is wired here and mapped into ruler-local X.
            _rulerRoot.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            GuiInput += OnFaceGuiInput;
            Resized += OnLanesResized;

            SetupAnimationSystem();
            _nodesInitialized = true;
        }

        _viewStartTick = 0L;
        _viewEndTick = _ctl.MaxTick;

        BuildLanes();
        if (!_playbackRegistered)
        {
            _ctl.RegisterPlayback(Play, Pause, SeekTo, () => _isPlaying);
            _playbackRegistered = true;
        }

        ApplyHudState(context.DesiredHudState);
        SeekTo(_ctl.Tick);
        UpdateLayout();
    }

    private void ClearResidentContext()
    {
        _residentBindGeneration++;
        _hudModeEpoch = -1L;
        if (_ctl is not null && _playbackRegistered)
            _ctl.UnregisterPlayback();
        _playbackRegistered = false;
        _proxyBound = false;
        _ctl = null;
        _commandClient = null;
        _generationGraphFamilyProvider = null;
        _filmstripGraphRevisionProvider = null;
        // Drop queued filmstrip work with the severed provider: entries carry no provider
        // reference anymore (execution-time resolution), but launching them after a sever is
        // useless Task churn, and the generation bump makes any in-flight completion a no-op.
        _filmstrip.Supersede();
        _filmstrip.SetPreviewProvider(null);
        // Unwind IN-FLIGHT renders too: their threadpool stacks execute bundle code and would
        // root the outgoing timeline+world ALCs past the collection probe (mechanism C,
        // clrstack-proven 2026-07-10). The world render honors the token per pixel row.
        _filmstrip.CancelInFlight();
        UnsubscribeLayerTrackRegistry();
        SetProcess(false);
    }

    // Rebind-safe, mirroring _generationGraphFamilyProvider's provider-field pattern: swaps the
    // registry reference only when it actually changed (a recompose may hand back the SAME
    // instance), and always unsubscribes the OLD instance's Changed event first so a bundle
    // recompose never leaves two live subscriptions pinning the previous registry.
    private void BindLayerTrackRegistry(ILayerTrackRegistry? registry)
    {
        if (ReferenceEquals(_layerTrackRegistry, registry))
            return;

        UnsubscribeLayerTrackRegistry();
        _layerTrackRegistry = registry;
        if (_layerTrackRegistry is not null)
            _layerTrackRegistry.Changed += OnLayerTrackRegistryChanged;
    }

    // ALC discipline: called from ClearResidentContext AND _ExitTree so the face never holds a
    // live subscription into the timeline bundle's registry after either path.
    private void UnsubscribeLayerTrackRegistry()
    {
        if (_layerTrackRegistry is not null)
            _layerTrackRegistry.Changed -= OnLayerTrackRegistryChanged;
        _layerTrackRegistry = null;
    }

    private void OnLayerTrackRegistryChanged(LayerTrackRegistrySnapshot snapshot)
    {
        // SetArchived/Reload may be invoked from a command handler off the main thread; the
        // rebuild walks into AddChild/UpdateLayout, which Godot only allows on the main thread --
        // the same CallDeferred discipline RebindResidentContext already uses.
        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
        {
            ScheduleViewRebuild();
            return;
        }

        Callable.From(ScheduleViewRebuild).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (_activeFace?.TryGetTarget(out var activeFace) == true && ReferenceEquals(activeFace, this))
            _activeFace = null;
        _filmstrip.Supersede();
        _scrubDragging = false;
        _scrubCoalescer.Cancel();
        ClearResidentContext();
        // Belt-and-suspenders with ClearResidentContext's call above (ALC discipline hard rule):
        // UnsubscribeLayerTrackRegistry is idempotent, so calling it again here costs nothing and
        // guarantees the subscription is gone even if ClearResidentContext's contract changes.
        UnsubscribeLayerTrackRegistry();
        DisposeTrackBindings();
        _filmstrip.DisposeCache();
        DisconnectIfConnected(_playPauseButton, BaseButton.SignalName.Pressed, Callable.From(OnPlayPausePressed));
        DisconnectIfConnected(_zoomOutButton, BaseButton.SignalName.Pressed, Callable.From(OnZoomOutPressed));
        DisconnectIfConnected(_fitButton, BaseButton.SignalName.Pressed, Callable.From(OnFitPressed));
        DisconnectIfConnected(_zoomInButton, BaseButton.SignalName.Pressed, Callable.From(OnZoomInPressed));
        DisconnectIfConnected(_lanesContainer, Control.SignalName.GuiInput, Callable.From<InputEvent>(OnLanesGuiInput));
        DisconnectIfConnected(this, Control.SignalName.GuiInput, Callable.From<InputEvent>(OnFaceGuiInput));
        DisconnectIfConnected(this, Control.SignalName.Resized, Callable.From(OnLanesResized));

        // The collectible plugin owns the proxy and unbinds it during shutdown. This resident face
        // clears only its local references here.
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

    public void Play()
    {
        if (_ctl is null) return;
        _isPlaying = true;
        if (_animationPlayer is not null)
            _animationPlayer.Seek(_ctl.Tick / _ticksPerSecond, update: false);
        TransitionState("playing");
    }

    public void Pause()
    {
        if (_ctl is null) return;
        _isPlaying = false;
        TransitionState("idle");
    }

    public void SeekTo(long tick)
    {
        if (_ctl is null) return;
        _scrubDragging = false;
        _scrubCoalescer.Cancel();
        EchoSeekTo(tick);
        _ctl.PushTick(tick);
    }

    private void EchoSeekTo(long tick)
    {
        if (_ctl is null) return;
        tick = Math.Clamp(tick, 0L, _ctl.MaxTick);
        _isPlaying = false;
        _internalTick = tick;
        _lastPushedTick = tick;

        // UpdateUI renders from _lastViewSnapshot when present, and face-initiated seeks get no
        // fresh snapshot from the service (only the ingress path round-trips one) — so a stale
        // snapshot kept the label/playhead frozen while the world scrubbed underneath (proven
        // live 2026-07-08; the long-standing "timeline cannot be adjusted" feedback gap). Move
        // the snapshot to the sought tick so the UI echoes immediately; the next real service
        // snapshot replaces it wholesale via ApplyView.
        if (_lastViewSnapshot is not null)
            _lastViewSnapshot = _lastViewSnapshot with { Tick = tick };

        if (_animationPlayer is not null)
        {
            var pos = tick / _ticksPerSecond;
            _animationPlayer.Seek(pos, update: true);
        }

        UpdateUI();
    }

    public void ApplyView(TimelineViewSnapshot snapshot)
    {
        _lastViewSnapshot = snapshot;
        _internalTick = snapshot.Tick;
        _lastPushedTick = snapshot.Tick;
        _isPlaying = snapshot.State == TimelinePlaybackState.Playing;
        UpdateUI();
    }

    private void TransitionState(string state)
    {
        if (_animationTree is not { Active: true } || _playback is null) return;
        var sn = new StringName(state);
        if (!_playback.IsPlaying())
            _playback.Start(sn, reset: false);
        else
            _playback.Travel(sn, false);
    }

    private void OnPlayPausePressed()
    {
        if (_ctl is null) return;
        if (_isPlaying)
            _ctl.Pause();
        else
            _ctl.Play();
    }

    private void OnBandPressed(long startTick)
    {
        _ctl?.SeekTo(startTick);
    }

    private void OnZoomOutPressed()
    {
        if (_ctl is null) return;
        var coarser = TimelineModel.TryGetCoarserRung(SelectedRung);
        if (coarser is null) return;
        ZoomToSpanAroundCurrentTick(TimelineModel.SpanTicksForRung(coarser, RungSpanUnits));
    }

    private void OnFitPressed()
    {
        if (_ctl is null) return;
        SetViewRange(0L, _ctl.MaxTick);
    }

    private void OnZoomInPressed()
    {
        if (_ctl is null) return;
        var finer = TimelineModel.TryGetFinerRung(SelectedRung);
        if (finer is null) return;
        ZoomToSpanAroundCurrentTick(TimelineModel.SpanTicksForRung(finer, RungSpanUnits));
    }

    private void OnLanesResized()
    {
        UpdateLayout();
        ScheduleViewRebuild();
    }

    private void UpdateUI()
    {
        if (_ctl is null || _statusLabel is null || _playPauseButton is null || _playheadLine is null || _lanesContainer is null) return;

        var snapshot = _lastViewSnapshot;
        var tick = snapshot?.Tick ?? _ctl.Tick;
        var timeLabel = TimelineTimeFormatter.ForTick(tick);

        var playState = snapshot?.State switch
        {
            TimelinePlaybackState.Playing => "playing",
            TimelinePlaybackState.Scrubbing => "scrubbing",
            _ => _isPlaying ? "playing" : "paused"
        };
        var geoRegime = snapshot?.ActiveRegimeId ?? _ctl.GeosphereSchedule.RegimeAt(tick)?.RegimeId ?? "-";
        _statusLabel.Text = $"{playState} : {geoRegime} : {timeLabel}";
        _playPauseButton.Text = playState == "playing" ? "Pause" : "Play";
        if (_zoomLabel is not null)
        {
            _zoomLabel.Text = TimelineTimeFormatter.ForViewRange(_viewStartTick, _viewEndTick, SelectedRung);
        }

        var fraction = TimelineScrubMapper.TickToFraction(tick, _viewStartTick, _viewEndTick);
        _playheadLine.Position = new Vector2((float)(fraction * _lanesContainer.Size.X), 0);
        _playheadLine.Size = new Vector2(2, _lanesContainer.Size.Y);
        UpdatePlayheadHandle(tick);

        foreach (var (sphereId, bandList) in _bandsBySphere)
        {
            var currentRegimeId = ResolveScheduleForSphere(sphereId)?.RegimeAt(tick)?.RegimeId;
            foreach (var band in bandList)
            {
                var isCurrent = currentRegimeId == band.Button.Text;
                band.Button.Modulate = isCurrent ? new Color(1, 1, 1, 1f) : new Color(1, 1, 1, 0.58f);
            }
        }

        var selectedLayersBySphere = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var selection in _ctl.ActiveLayers)
        {
            if (!selectedLayersBySphere.TryGetValue(selection.SphereId, out var selectedSet))
                selectedLayersBySphere[selection.SphereId] = selectedSet = new HashSet<string>(StringComparer.Ordinal);
            selectedSet.Add(selection.LayerId);
        }

        var activeLayersBySphere = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var track in _tracks)
        {
            if (!activeLayersBySphere.TryGetValue(track.Sphere, out var activeLayers))
            {
                activeLayers = ResolveScheduleForSphere(track.Sphere)?.RegimeAt(tick)?.ActiveLayers
                    .Select(l => l.Value).ToHashSet(StringComparer.Ordinal)
                    ?? new HashSet<string>(StringComparer.Ordinal);
                activeLayersBySphere[track.Sphere] = activeLayers;
            }

            bool isActive = activeLayers.Contains(track.LayerId);
            bool isSelected = isActive
                && selectedLayersBySphere.TryGetValue(track.Sphere, out var selectedLayers)
                && selectedLayers.Contains(track.LayerId);

            track.ToggleButton.Disabled = false;
            track.ToggleButton.Modulate = isActive ? new Color(1, 1, 1, 1f) : new Color(1, 1, 1, 0.68f);
            track.ToggleButton.AddThemeStyleboxOverride("normal", isSelected
                ? track.SelectedStyle
                : isActive
                    ? track.NormalStyle
                    : track.InactiveStyle);
        }
    }

    private static string FriendlyLayerLabel(string layerId)
    {
        var name = layerId.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? layerId;
        return string.Join(' ', name.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private void UpdateRuler()
    {
        if (_rulerRoot is null) return;

        ClearChildren(_rulerRoot);
        _playheadHandle = null;
        var width = _rulerRoot.Size.X;
        if (width <= 0) return;
        if (_viewEndTick <= _viewStartTick) return;

        var baseline = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.28f),
            Position = new Vector2(0f, 19f),
            Size = new Vector2(width, 1f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _rulerRoot.AddChild(baseline);

        foreach (var mark in TimelineModel.Ruler(_viewStartTick, _viewEndTick, SelectedRung))
        {
            float x = (float)(mark.Fraction * width);
            var tick = new ColorRect
            {
                Color = new Color(1f, 1f, 1f, 0.45f),
                Position = new Vector2(x, 10f),
                Size = new Vector2(1f, 9f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            _rulerRoot.AddChild(tick);

            var label = new Label
            {
                Text = mark.Label,
                Position = new Vector2(Math.Clamp(x - 34f, 0f, Math.Max(0f, width - 68f)), 0f),
                Size = new Vector2(68f, 11f),
                ClipText = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore
            };
            label.AddThemeFontSizeOverride("font_size", 9);
            _rulerRoot.AddChild(label);
        }

        // Visual-only: the handle shows WHERE to grab, but input for the whole ruler band —
        // including on top of the handle — arrives at the face root (OnFaceGuiInput), because the
        // ruler band is occluded for GUI picking by the LanesList overlap (see _Ready wiring).
        _playheadHandle = new TimelinePlayheadHandle
        {
            Name = "PlayheadHandle",
            Size = new Vector2(PlayheadHandleWidth, PlayheadHandleHeight),
            MouseFilter = MouseFilterEnum.Ignore,
            MouseDefaultCursorShape = Control.CursorShape.Hsize,
            ZIndex = 2
        };
        _rulerRoot.AddChild(_playheadHandle);
        UpdatePlayheadHandle(_lastViewSnapshot?.Tick ?? _ctl?.Tick ?? _internalTick);
    }

    private void UpdatePlayheadHandle(double tick)
    {
        if (_playheadHandle is null || _rulerRoot is null) return;

        var fraction = TimelineScrubMapper.TickToFraction((long)tick, _viewStartTick, _viewEndTick);
        var x = (float)(fraction * _rulerRoot.Size.X);
        var halfWidth = PlayheadHandleWidth / 2f;
        var maxX = Math.Max(-halfWidth, _rulerRoot.Size.X - halfWidth);
        _playheadHandle.Position = new Vector2(Math.Clamp(x - halfWidth, -halfWidth, maxX), 1f);
        _playheadHandle.Size = new Vector2(PlayheadHandleWidth, PlayheadHandleHeight);
    }

    private void ZoomToSpanAroundCurrentTick(long targetSpan)
    {
        if (_ctl is null) return;
        long span = Math.Max(MinViewSpanTicks, _viewEndTick - _viewStartTick);
        long anchor = Math.Clamp(_ctl.Tick, _viewStartTick, _viewEndTick);
        double anchorFraction = span > 0 ? (anchor - _viewStartTick) / (double)span : 0.5;
        TimelineScrubMapper.ZoomWindowAroundFraction(
            _viewStartTick,
            _viewEndTick,
            targetSpan,
            MinViewSpanTicks,
            _ctl.MaxTick,
            anchorFraction,
            out var nextStart,
            out var nextEnd);
        SetViewRange(nextStart, nextEnd);
    }

    private bool ZoomToSpanAroundLocalX(long targetSpan, float rulerLocalX)
    {
        if (_ctl is null || _rulerRoot is null) return false;
        if (!TimelineScrubMapper.TryZoomWindowAroundLocalX(
            rulerLocalX,
            _rulerRoot.Size.X,
            _viewStartTick,
            _viewEndTick,
            targetSpan,
            MinViewSpanTicks,
            _ctl.MaxTick,
            out var nextStart,
            out var nextEnd))
        {
            _log.LogInformation("timeline zoom rejected: localX={X} width={W}", rulerLocalX, _rulerRoot.Size.X);
            return false;
        }

        SetViewRange(nextStart, nextEnd);
        return true;
    }

    private void SetViewRange(long startTick, long endTick)
    {
        if (_ctl is null) return;
        long span = Math.Max(MinViewSpanTicks, endTick - startTick);
        long nextStart;
        long nextEnd;
        if (span >= _ctl.MaxTick)
        {
            nextStart = 0L;
            nextEnd = _ctl.MaxTick;
        }
        else
        {
            long start = Math.Clamp(startTick, 0L, _ctl.MaxTick - span);
            nextStart = start;
            nextEnd = start + span;
        }

        if (nextStart == _viewStartTick && nextEnd == _viewEndTick)
            return;

        _viewStartTick = nextStart;
        _viewEndTick = nextEnd;
        ScheduleViewRebuild();
        UpdateLayout();
    }

    private async void ScheduleViewRebuild()
    {
        if (!IsInsideTree())
            return;

        _viewRebuildVersion++;
        if (_viewRebuildQueued)
            return;

        _viewRebuildQueued = true;
        while (IsInsideTree())
        {
            int targetVersion = _viewRebuildVersion;
            var tree = GetTree();
            if (tree is null)
                break;

            await ToSignal(tree.CreateTimer(ViewRebuildCoalesceSeconds), SceneTreeTimer.SignalName.Timeout);
            if (!IsInsideTree())
                break;

            if (targetVersion != _viewRebuildVersion)
                continue;

            _viewRebuildQueued = false;
            BuildLanes();
            UpdateLayout();
            return;
        }

        _viewRebuildQueued = false;
    }

    private static string TrackKey(string sphere, string layerId)
        => $"{sphere}:{layerId}";

    private static string SafeNodeName(string value)
        => value.Replace('.', '_').Replace('-', '_').Replace(':', '_');

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void DisconnectIfConnected(GodotObject? source, StringName signal, Callable callable)
    {
        if (source is not null && GodotObject.IsInstanceValid(source) && source.IsConnected(signal, callable))
            source.Disconnect(signal, callable);
    }
}
