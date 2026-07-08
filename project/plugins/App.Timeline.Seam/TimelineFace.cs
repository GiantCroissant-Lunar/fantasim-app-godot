using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.Timeline;
using FantaSim.App.Command;
using FantaSim.App.Ui.Seam;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Timeline.Seam;

public partial class TimelineFace : Control, ITimelineFace
{
    private sealed class TrackRowBinding
    {
        public required Control RowRoot { get; init; }
        public required Button ToggleButton { get; init; }
        public required Button ChevronButton { get; init; }
        public required Control ContentRoot { get; init; }
        public required string LayerId { get; init; }
        public required string Sphere { get; init; }
        public required StyleBoxFlat NormalStyle { get; init; }
        public required StyleBoxFlat InactiveStyle { get; init; }
        public required StyleBoxFlat SelectedStyle { get; init; }
        public required Callable ToggleCallable { get; init; }
        public required Callable ChevronCallable { get; init; }
        public IDisposable? GraphBinding { get; set; }
    }

    private sealed record PendingFilmstripFrame(int Generation, TextureRect TextureRect);

    private sealed record QueuedFilmstripFrame(
        LayerFilmstripPreviewRequest Request,
        string RequestKey);

    private sealed record TrackGraphPortItem(string PortId, string Label, string KindHint, bool Required);

    private sealed record TrackGraphNodeItem(
        string NodeId,
        string TypeId,
        int InputCount,
        int OutputCount,
        string Category,
        string TypeKey,
        string Summary,
        string Detail,
        bool IsSideEffect,
        bool IsExpensive,
        IReadOnlyList<TrackGraphPortItem> Inputs,
        IReadOnlyList<TrackGraphPortItem> Outputs,
        IReadOnlyList<string> ParameterLines);

    private sealed record TrackGraphWireItem(
        string FromNodeId,
        int FromSlot,
        string ToNodeId,
        int ToSlot,
        string FromPortId,
        string ToPortId,
        string KindHint);

    private sealed class TrackGraphEditViewModel
    {
        public TrackGraphEditViewModel(LayerTrackGraphView graph)
        {
            Nodes = graph.Nodes.Select(node => new TrackGraphNodeItem(
                    node.NodeId,
                    node.TypeId,
                    node.InputCount,
                    node.OutputCount,
                    node.Category,
                    node.TypeId,
                    node.Summary,
                    string.Join('\n', node.ParameterLines),
                    IsSideEffect: false,
                    IsExpensive: false,
                    node.Inputs.Select(port => new TrackGraphPortItem(port.PortId, port.Label, port.KindHint, port.Required)).ToArray(),
                    node.Outputs.Select(port => new TrackGraphPortItem(port.PortId, port.Label, port.KindHint, port.Required)).ToArray(),
                    node.ParameterLines))
                .ToArray();
            Wires = graph.Wires.Select(wire => new TrackGraphWireItem(
                    wire.FromNodeId,
                    wire.FromSlot,
                    wire.ToNodeId,
                    wire.ToSlot,
                    wire.FromPortId,
                    wire.ToPortId,
                    wire.KindHint))
                .ToArray();
        }

        public bool CompactCards => true;
        public IReadOnlyList<TrackGraphNodeItem> Nodes { get; }
        public IReadOnlyList<TrackGraphWireItem> Wires { get; }
    }

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

    private readonly List<(Button Button, double Start, double Width)> _geosphereBands = new();
    private readonly List<(Button Button, double Start, double Width)> _atmosphereBands = new();
    private readonly List<TrackRowBinding> _tracks = new();
    private readonly Dictionary<TimelineFilmstripCacheKey, ImageTexture> _filmstripTextureCache = new();
    private readonly Dictionary<string, TimelineFilmstripCacheKey> _filmstripRequestTextureKeys = new(StringComparer.Ordinal);
    private readonly Queue<QueuedFilmstripFrame> _filmstripQueue = new();
    private readonly HashSet<string> _filmstripQueuedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _filmstripActiveKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<PendingFilmstripFrame>> _filmstripWaiters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedTracks = new(StringComparer.Ordinal);
    private WorldGenerationGraphFamilyDocument? _cachedGraphFamily;
    private long? _cachedGraphFamilyTick;
    private IClient? _commandClient;
    private Func<long, WorldGenerationGraphFamilyDocument?>? _generationGraphFamilyProvider;
    private Func<LayerFilmstripPreviewRequest, LayerFilmstripPreviewMap?>? _filmstripPreviewProvider;

    private double _internalTick;
    private long _lastPushedTick = -1;
    private bool _isPlaying;
    private long _viewStartTick;
    private long _viewEndTick;
    private TimelineViewSnapshot? _lastViewSnapshot;
    private bool _nodesInitialized;
    private bool _playbackRegistered;
    private bool _proxyBound;
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
    private const int MaxConcurrentFilmstripRequests = 3;
    private const double ViewRebuildCoalesceSeconds = 0.08;

    private TimelineLadderRung SelectedRung => TimelineModel.SelectRungForSpan(_viewEndTick - _viewStartTick);
    private int _filmstripActiveRequests;
    private int _filmstripGeneration;
    private int _viewRebuildVersion;
    private bool _viewRebuildQueued;

    private ILogger _log;

    /// <summary>
    /// Resident registry bridge set by the resident host before timeline scene entry. The
    /// collectible bundle never writes this static; it only registers ITimelineFaceContext into
    /// the registry.
    /// </summary>
    public static IRegistry? ResidentRegistry { get; private set; }

    public static void SetResidentRegistry(IRegistry registry)
        => ResidentRegistry = registry ?? throw new ArgumentNullException(nameof(registry));

    public TimelineFace()
    {
        _log = ResidentRegistry?.TryGet<ILoggerFactory>()?.CreateLogger("Timeline.Face")
            ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger("Timeline.Face");
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
        => BindResidentContext(forceProxyBind: false);

    public override void _Process(double delta)
    {
        if (_ctl is null)
        {
            BindResidentContext(forceProxyBind: false);
            return;
        }

        ApplyScrubAction(_scrubCoalescer.ConsumeFrame());
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
        _filmstripPreviewProvider = context.FilmstripPreviewProvider;
        _ticksPerSecond = context.TicksPerSecond > 0.0 ? context.TicksPerSecond : 5_000_000.0;

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

        if (forceProxyBind || !_proxyBound)
        {
            context.Proxy.BindCrossTarget(this);
            _proxyBound = true;
        }

        SeekTo(_ctl.Tick);
        UpdateLayout();
    }

    private void ClearResidentContext()
    {
        if (_ctl is not null && _playbackRegistered)
            _ctl.UnregisterPlayback();
        _playbackRegistered = false;
        _proxyBound = false;
        _ctl = null;
        _commandClient = null;
        _generationGraphFamilyProvider = null;
        _filmstripPreviewProvider = null;
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        _filmstripGeneration++;
        _scrubDragging = false;
        _scrubCoalescer.Cancel();
        ClearResidentContext();
        DisposeTrackBindings();
        DisposeFilmstripTextureCache();
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

    // True while a scrub gesture owns the mouse (press landed on a scrub surface — lanes or
    // ruler/chrome). Motion is tracked in _Input: exactly like GlobeOrbitControls, held-button
    // motion is routed through the viewport's GUI focus path and does not reliably reach
    // gui_input handlers, so per-frame drag updates must be captured at the _Input stage.
    private bool _scrubDragging;

    public override void _Input(InputEvent @event)
    {
        if (!_nodesInitialized || _ctl is null || _rulerRoot is null) return;

        switch (@event)
        {
            case InputEventMouseButton mouseBtn when mouseBtn.Pressed && TryHandleTimelineWheelZoom(mouseBtn):
                AcceptEvent();
                break;
            case InputEventMagnifyGesture magnify when TryHandleTimelineMagnifyZoom(magnify):
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouseBtn
                when TryStartPlayheadLineScrub(mouseBtn.Position):
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mouseBtn:
                if (_scrubDragging)
                {
                    HandleScrubRelease(mouseBtn.Position.X - _rulerRoot.GlobalPosition.X, _rulerRoot.Size.X);
                    AcceptEvent();
                }
                _scrubDragging = false;
                break;
            case InputEventMouseMotion motion when _scrubDragging:
                QueueScrubMotion(motion.Position.X - _rulerRoot.GlobalPosition.X, _rulerRoot.Size.X);
                break;
        }
    }

    private bool TryStartPlayheadLineScrub(Vector2 globalPosition)
    {
        if (_playheadLine is null || _lanesContainer is null || _rulerRoot is null)
            return false;

        var lineRect = _playheadLine.GetGlobalRect();
        var lanesRect = _lanesContainer.GetGlobalRect();
        var grabRect = new Rect2(
            new Vector2(lineRect.Position.X - PlayheadLineGrabMargin, lanesRect.Position.Y),
            new Vector2(lineRect.Size.X + (PlayheadLineGrabMargin * 2f), lanesRect.Size.Y));
        if (!grabRect.HasPoint(globalPosition))
            return false;

        _scrubDragging = true;
        HandleScrubPress(globalPosition.X - _rulerRoot.GlobalPosition.X, _rulerRoot.Size.X);
        return true;
    }

    private bool TryHandleTimelineWheelZoom(InputEventMouseButton mouseBtn)
    {
        if (_ctl is null || _rulerRoot is null || !IsTimelineZoomPosition(mouseBtn.Position))
            return false;

        TimelineLadderRung? targetRung = mouseBtn.ButtonIndex switch
        {
            MouseButton.WheelUp => TimelineModel.TryGetFinerRung(SelectedRung),
            MouseButton.WheelDown => TimelineModel.TryGetCoarserRung(SelectedRung),
            _ => null
        };
        if (targetRung is null)
            return false;

        return ZoomToSpanAroundLocalX(
            TimelineModel.SpanTicksForRung(targetRung, RungSpanUnits),
            mouseBtn.Position.X - _rulerRoot.GlobalPosition.X);
    }

    private bool TryHandleTimelineMagnifyZoom(InputEventMagnifyGesture magnify)
    {
        if (_ctl is null || _rulerRoot is null || magnify.Factor <= 0f || !IsTimelineZoomPosition(magnify.Position))
            return false;

        var currentSpan = Math.Max(MinViewSpanTicks, _viewEndTick - _viewStartTick);
        var targetSpan = Math.Max(MinViewSpanTicks, (long)Math.Round(currentSpan / magnify.Factor));
        if (targetSpan == currentSpan)
            return false;

        return ZoomToSpanAroundLocalX(targetSpan, magnify.Position.X - _rulerRoot.GlobalPosition.X);
    }

    private bool IsTimelineZoomPosition(Vector2 globalPosition)
        => _rulerRoot?.GetGlobalRect().HasPoint(globalPosition) == true
           || _lanesContainer?.GetGlobalRect().HasPoint(globalPosition) == true;

    private void OnLanesGuiInput(InputEvent @event)
    {
        if (_ctl is null || _lanesContainer is null) return;

        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left && mouseBtn.Pressed)
            {
                _scrubDragging = true;
                HandleScrubPress(mouseBtn.Position.X);
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            if ((mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                QueueScrubMotion(mouseMotion.Position.X);
            }
        }
    }

    // Face-root scrub surface: fires for every mouse event the child controls (buttons, lane
    // container, band/track buttons) did NOT consume — i.e. the ruler band, the playhead handle
    // (visual-only, MouseFilter.Ignore), and any empty timeline chrome. Maps the face-local X
    // into ruler-local X so the tick arithmetic matches the drawn ruler exactly.
    private void OnFaceGuiInput(InputEvent @event)
    {
        if (_ctl is null || _rulerRoot is null) return;

        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left && mouseBtn.Pressed)
            {
                _scrubDragging = true;
                HandleScrubPress(FaceToRulerLocalX(mouseBtn.Position.X), _rulerRoot.Size.X);
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            if ((mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                QueueScrubMotion(FaceToRulerLocalX(mouseMotion.Position.X), _rulerRoot.Size.X);
                AcceptEvent();
            }
        }
    }

    private float FaceToRulerLocalX(float faceLocalX)
        => faceLocalX - (_rulerRoot!.GlobalPosition.X - GlobalPosition.X);

    private void HandleScrubPress(float localX)
    {
        if (_ctl is null || _lanesContainer is null) return;
        HandleScrubPress(localX, _lanesContainer.Size.X);
    }

    private void HandleScrubPress(float localX, float surfaceWidth)
    {
        if (!TryScrubTick(localX, surfaceWidth, out var tick))
            return;

        ApplyScrubAction(_scrubCoalescer.Press(tick));
    }

    private void QueueScrubMotion(float localX)
    {
        if (_ctl is null || _lanesContainer is null) return;
        QueueScrubMotion(localX, _lanesContainer.Size.X);
    }

    private void QueueScrubMotion(float localX, float surfaceWidth)
    {
        if (!TryScrubTick(localX, surfaceWidth, out var tick))
            return;

        ApplyScrubAction(_scrubCoalescer.Motion(tick));
    }

    private void HandleScrubRelease(float localX, float surfaceWidth)
    {
        if (!TryScrubTick(localX, surfaceWidth, out var tick))
        {
            _scrubCoalescer.Cancel();
            return;
        }

        ApplyScrubAction(_scrubCoalescer.Release(tick));
    }

    private bool TryScrubTick(float localX, float surfaceWidth, out long tick)
    {
        tick = 0L;
        if (_ctl is null)
            return false;

        if (TimelineScrubMapper.TryLocalXToTick(localX, surfaceWidth, _viewStartTick, _viewEndTick, out tick))
            return true;

        // Loud failure per the ingress doctrine: a scrub that maps to nothing is a layout bug.
        _log.LogInformation("timeline scrub rejected: localX={X} width={W}", localX, surfaceWidth);
        return false;
    }

    private void ApplyScrubAction(TimelineScrubAction action)
    {
        if (_ctl is null || !action.ShouldApply)
            return;

        var tick = Math.Clamp(action.Tick, 0L, _ctl.MaxTick);
        EchoSeekTo(tick);
        _ctl.PushTick(tick, action.Origin);
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

    private void UpdateLayout()
    {
        if (_lanesContainer is null) return;
        var width = _lanesContainer.Size.X;

        foreach (var band in _geosphereBands)
        {
            band.Button.Position = new Vector2((float)(band.Start * width), 0);
            band.Button.Size = new Vector2((float)(band.Width * width), RegimeBandHeight);
        }

        foreach (var band in _atmosphereBands)
        {
            band.Button.Position = new Vector2((float)(band.Start * width), 0);
            band.Button.Size = new Vector2((float)(band.Width * width), RegimeBandHeight);
        }

        UpdateTrackContentLayout(width);
        UpdateUI();
        UpdateRuler();
    }

    private void UpdateTrackContentLayout(float laneWidth)
    {
        float contentWidth = ResolveTrackContentWidth(laneWidth);
        foreach (var track in _tracks)
        {
            float rowHeight = Math.Max(track.RowRoot.CustomMinimumSize.Y, track.RowRoot.Size.Y);
            track.ContentRoot.Position = new Vector2(TrackHeaderWidth + TrackContentGap, 0f);
            track.ContentRoot.Size = new Vector2(contentWidth, rowHeight);
            track.ContentRoot.CustomMinimumSize = new Vector2(contentWidth, rowHeight);

            var strip = track.ContentRoot.GetNodeOrNull<Control>("CompactFilmstrip");
            if (strip is not null)
                strip.Size = new Vector2(contentWidth, TrackHeight);
        }
    }

    private void BuildLanes()
    {
        if (_ctl is null || _lanesContainer is null) return;
        SupersedeFilmstripGeneration();

        var geosphereRegimesRoot = GetNode<Control>("VBoxContainer/LanesContainer/LanesList/GeosphereLane/GeosphereRegimes");
        var geosphereTracksRoot = GetNode<Control>("VBoxContainer/LanesContainer/LanesList/GeosphereLane/GeosphereTracks");
        var atmosphereRegimesRoot = GetNode<Control>("VBoxContainer/LanesContainer/LanesList/AtmosphereLane/AtmosphereRegimes");
        var atmosphereTracksRoot = GetNode<Control>("VBoxContainer/LanesContainer/LanesList/AtmosphereLane/AtmosphereTracks");

        DisposeTrackBindings();
        ClearChildren(geosphereRegimesRoot);
        ClearChildren(geosphereTracksRoot);
        ClearChildren(atmosphereRegimesRoot);
        ClearChildren(atmosphereTracksRoot);

        _geosphereBands.Clear();
        _atmosphereBands.Clear();

        if (_ctl.MaxTick <= 0L) return;

        PopulateLane(_ctl.GeosphereSchedule, geosphereRegimesRoot, geosphereTracksRoot, _geosphereBands, "geosphere");
        PopulateLane(_ctl.AtmosphereSchedule, atmosphereRegimesRoot, atmosphereTracksRoot, _atmosphereBands, "atmosphere");
        UpdateLanesMinimumHeight();
    }

    private void PopulateLane(
        SphereRegimeSchedule schedule,
        Control regimesRoot,
        Control tracksRoot,
        List<(Button Button, double Start, double Width)> bandList,
        string sphere)
    {
        var bands = TimelineModel.Bands(schedule, _ctl!.MaxTick, _ctl.Tick, _viewStartTick, _viewEndTick);
        foreach (var b in bands)
        {
            var btn = new Button
            {
                Text = b.RegimeId,
                ClipText = true,
                FocusMode = FocusModeEnum.None
            };
            btn.AddThemeFontSizeOverride("font_size", 12);

            var normalStyle = new StyleBoxFlat();
            normalStyle.BgColor = GetRegimeColor(b.RegimeId);
            normalStyle.BorderColor = new Color(0.95f, 0.98f, 1.0f, 0.55f);
            normalStyle.SetBorderWidthAll(1);
            normalStyle.SetCornerRadiusAll(3);
            btn.AddThemeStyleboxOverride("normal", normalStyle);
            btn.AddThemeStyleboxOverride("hover", normalStyle);
            btn.AddThemeStyleboxOverride("pressed", normalStyle);

            btn.Pressed += () => OnBandPressed(b.StartTick);

            regimesRoot.AddChild(btn);
            bandList.Add((btn, b.StartFraction, b.WidthFraction));
        }

        var tracks = TimelineModel.Tracks(schedule, _ctl.Tick);
        var trackLayouts = TimelineTrackLayout.ToRowMap(TimelineTrackLayout.Plan(
            tracks.Select(track => new TimelineTrackLayoutInput(
                TrackKey(sphere, track.LayerId),
                IsExpanded: _expandedTracks.Contains(TrackKey(sphere, track.LayerId)))),
            TrackHeight,
            ExpandedTrackHeight));
        var generationFamily = string.Equals(sphere, "geosphere", StringComparison.Ordinal)
            ? ResolveGenerationGraphFamily()
            : null;

        foreach (var t in tracks)
        {
            var trackSphere = sphere;
            var trackLayerId = t.LayerId;
            var trackKey = TrackKey(trackSphere, trackLayerId);
            var rowLayout = trackLayouts[trackKey];
            var expanded = _expandedTracks.Contains(trackKey);
            var regimeId = ResolveTrackRegime(schedule, trackLayerId, _ctl.Tick);
            var graph = string.Equals(trackSphere, "geosphere", StringComparison.Ordinal)
                ? LayerTrackGraphProjection.Resolve(generationFamily, trackSphere, trackLayerId, regimeId)
                : LayerTrackGraphView.Empty(trackSphere, trackLayerId, regimeId, "deferred");

            var row = new Control
            {
                Name = $"TrackRow_{SafeNodeName(trackLayerId)}",
                CustomMinimumSize = new Vector2(0, rowLayout.Height),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore,
            };

            var chevron = new Button
            {
                Text = expanded ? "v" : ">",
                TooltipText = expanded ? "Collapse layer graph" : "Expand layer graph",
                FocusMode = FocusModeEnum.None,
                ClipText = true,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            };
            chevron.AddThemeFontSizeOverride("font_size", 12);
            ConfigureTrackRowChild(chevron, 0f, TrackChevronWidth, TrackHeight);
            row.AddChild(chevron);

            var btn = new Button
            {
                Text = $" {FriendlyLayerLabel(t.LayerId)}",
                TooltipText = $"{sphere}:{t.LayerId}",
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                ClipText = true,
            };
            btn.AddThemeFontSizeOverride("font_size", 12);
            btn.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f, 0.98f));
            btn.AddThemeColorOverride("font_hover_color", new Color(1.0f, 1.0f, 1.0f, 1.0f));
            ConfigureTrackRowChild(btn, TrackChevronWidth, TrackHeaderWidth - TrackChevronWidth, TrackHeight);

            var normalStyle = new StyleBoxFlat { BgColor = new Color(0.14f, 0.20f, 0.24f, 0.78f) };
            normalStyle.SetBorderWidthAll(1);
            normalStyle.BorderColor = new Color(0.28f, 0.42f, 0.48f, 0.86f);
            normalStyle.SetCornerRadiusAll(3);

            var inactiveStyle = new StyleBoxFlat { BgColor = new Color(0.08f, 0.10f, 0.12f, 0.72f) };
            inactiveStyle.SetBorderWidthAll(1);
            inactiveStyle.BorderColor = new Color(0.20f, 0.24f, 0.28f, 0.74f);
            inactiveStyle.SetCornerRadiusAll(3);

            var selectedStyle = new StyleBoxFlat { BgColor = new Color(0.20f, 0.33f, 0.46f, 0.92f) };
            selectedStyle.SetBorderWidthAll(2);
            selectedStyle.BorderColor = new Color(0.58f, 0.82f, 1.00f, 0.98f);
            selectedStyle.SetCornerRadiusAll(3);

            btn.AddThemeStyleboxOverride("normal", normalStyle);
            btn.AddThemeStyleboxOverride("hover", selectedStyle);
            btn.AddThemeStyleboxOverride("pressed", selectedStyle);

            row.AddChild(btn);

            var content = new Control
            {
                Name = "GraphContent",
                MouseFilter = expanded ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore,
                ClipContents = true,
            };
            ConfigureTrackContent(content, ResolveTrackContentWidth(_lanesContainer?.Size.X ?? 0f), rowLayout.Height);
            row.AddChild(content);

            IDisposable? graphBinding = null;
            if (expanded)
                graphBinding = BuildExpandedGraph(content, graph);
            else
                BuildCompactFilmstrip(content, schedule, trackSphere, trackLayerId);

            var toggleCallable = Callable.From(() => OnTrackPressed(trackSphere, trackLayerId));
            var chevronCallable = Callable.From(() => OnTrackExpandPressed(trackSphere, trackLayerId));
            btn.Connect(BaseButton.SignalName.Pressed, toggleCallable);
            chevron.Connect(BaseButton.SignalName.Pressed, chevronCallable);

            tracksRoot.AddChild(row);
            _tracks.Add(new TrackRowBinding
            {
                RowRoot = row,
                ToggleButton = btn,
                ChevronButton = chevron,
                ContentRoot = content,
                LayerId = t.LayerId,
                Sphere = sphere,
                NormalStyle = normalStyle,
                InactiveStyle = inactiveStyle,
                SelectedStyle = selectedStyle,
                ToggleCallable = toggleCallable,
                ChevronCallable = chevronCallable,
                GraphBinding = graphBinding,
            });
        }
    }

    private void UpdateLanesMinimumHeight()
    {
        if (_lanesContainer is null)
            return;

        var lanesList = GetNodeOrNull<Control>("VBoxContainer/LanesContainer/LanesList");
        if (lanesList is null)
            return;

        var minHeight = Math.Max(120f, lanesList.GetCombinedMinimumSize().Y);
        _lanesContainer.CustomMinimumSize = new Vector2(0f, minHeight);
    }

    private static void ConfigureTrackRowChild(Control child, float left, float width, float height)
    {
        child.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        child.Position = new Vector2(left, 0f);
        child.Size = new Vector2(width, height);
        child.CustomMinimumSize = new Vector2(width, height);
    }

    private static void ConfigureTrackContent(Control content, float width, float height)
    {
        content.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        content.Position = new Vector2(TrackHeaderWidth + TrackContentGap, 0f);
        content.Size = new Vector2(width, height);
        content.CustomMinimumSize = new Vector2(width, height);
    }

    private static float ResolveTrackContentWidth(float laneWidth)
        => Math.Max(1f, laneWidth - TrackHeaderWidth - TrackContentGap);

    private void BuildCompactFilmstrip(
        Control content,
        SphereRegimeSchedule schedule,
        string sphere,
        string layerId)
    {
        content.MouseFilter = MouseFilterEnum.Ignore;
        var contentWidth = ResolveTrackContentWidth(_lanesContainer?.Size.X ?? content.Size.X);
        var strip = new Control
        {
            Name = "CompactFilmstrip",
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true,
        };
        strip.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        strip.Position = Vector2.Zero;
        strip.Size = new Vector2(contentWidth, TrackHeight);
        strip.CustomMinimumSize = new Vector2(contentWidth, TrackHeight);
        content.AddChild(strip);

        var slots = TimelineFilmstrip.PlanSlots(
            _viewStartTick,
            _viewEndTick,
            contentWidth,
            TimelineFilmstrip.ThumbnailWidth);

        if (slots.Count == 0)
        {
            strip.AddChild(CompactStripLabel("preview unavailable", muted: true));
            return;
        }

        var rung = SelectedRung.Symbol;
        var orderedSlots = TimelineFilmstrip.OrderSlotsNearestToTick(slots, _ctl?.Tick ?? _viewStartTick);
        foreach (var slot in slots)
        {
            bool activeAtSlot = schedule.RegimeAt(slot.Tick)?.ActiveLayers.Any(layer =>
                string.Equals(layer.Value, layerId, StringComparison.Ordinal)) == true;
            var frame = BuildFilmstripFramePlaceholder(slot, activeAtSlot);
            strip.AddChild(frame);
        }

        foreach (var slot in orderedSlots)
        {
            var frame = strip.GetNodeOrNull<Control>($"Frame_{slot.Index}");
            if (frame is null)
                continue;
            var textureRect = frame.GetNode<TextureRect>("Texture");
            RequestFilmstripTexture(textureRect, sphere, layerId, slot.Tick, rung);
        }
    }

    private static Control BuildFilmstripFramePlaceholder(TimelineFilmstripFrameSlot slot, bool activeAtSlot)
    {
        var frame = new PanelContainer
        {
            Name = $"Frame_{slot.Index}",
            Position = new Vector2(slot.X, 0f),
            Size = new Vector2(slot.Width, TrackHeight),
            CustomMinimumSize = new Vector2(slot.Width, TrackHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        frame.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.11f, 0.12f, 0.86f),
            BorderColor = new Color(0.24f, 0.30f, 0.34f, 0.82f),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(0);
        frame.AddThemeStyleboxOverride("panel", style);

        var texture = new TextureRect
        {
            Name = "Texture",
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Modulate = activeAtSlot ? Colors.White : new Color(1f, 1f, 1f, 0.58f),
        };
        texture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        frame.AddChild(texture);
        return frame;
    }

    private void RequestFilmstripTexture(
        TextureRect textureRect,
        string sphere,
        string layerId,
        long tick,
        string rung)
    {
        var provider = _filmstripPreviewProvider;
        if (provider is null)
            return;

        var request = new LayerFilmstripPreviewRequest(
            sphere,
            layerId,
            tick,
            rung,
            TimelineFilmstrip.ThumbnailWidth,
            TimelineFilmstrip.ThumbnailHeight);
        string requestKey = FilmstripRequestKey(request);
        if (_filmstripRequestTextureKeys.TryGetValue(requestKey, out var cachedKey)
            && _filmstripTextureCache.TryGetValue(cachedKey, out var cachedTexture)
            && GodotObject.IsInstanceValid(cachedTexture))
        {
            textureRect.Texture = cachedTexture;
            return;
        }

        // Hold the TextureRect reference directly: the frame is built BEFORE the track row
        // enters the tree, so GetPath() errors and NodePath resolution would be impossible here
        // (windowed gate 2026-07-08). IsInstanceValid + IsInsideTree at apply time is the guard.
        var generation = _filmstripGeneration;
        if (!_filmstripWaiters.TryGetValue(requestKey, out var waiters))
        {
            waiters = new List<PendingFilmstripFrame>();
            _filmstripWaiters[requestKey] = waiters;
        }

        waiters.Add(new PendingFilmstripFrame(generation, textureRect));
        if (_filmstripActiveKeys.Contains(requestKey) || !_filmstripQueuedKeys.Add(requestKey))
            return;

        _filmstripQueue.Enqueue(new QueuedFilmstripFrame(request, requestKey));
        PumpFilmstripQueue();
    }

    private void PumpFilmstripQueue()
    {
        var provider = _filmstripPreviewProvider;
        if (provider is null)
            return;

        while (_filmstripActiveRequests < MaxConcurrentFilmstripRequests && _filmstripQueue.Count > 0)
        {
            var queued = _filmstripQueue.Dequeue();
            _filmstripQueuedKeys.Remove(queued.RequestKey);
            PruneFilmstripWaiters(queued.RequestKey);
            if (!_filmstripWaiters.ContainsKey(queued.RequestKey))
            {
                continue;
            }

            _filmstripActiveRequests++;
            _filmstripActiveKeys.Add(queued.RequestKey);
            StartFilmstripRequest(provider, queued);
        }
    }

    private void StartFilmstripRequest(
        Func<LayerFilmstripPreviewRequest, LayerFilmstripPreviewMap?> provider,
        QueuedFilmstripFrame queued)
    {
        _ = Task.Run(() =>
        {
            LayerFilmstripPreviewMap? map = null;
            try
            {
                map = provider(queued.Request);
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    ex,
                    "Timeline filmstrip preview failed for {LayerId} at {Tick}.",
                    queued.Request.LayerId,
                    queued.Request.Tick);
            }

            Callable.From(() =>
            {
                _filmstripActiveRequests = Math.Max(0, _filmstripActiveRequests - 1);
                _filmstripActiveKeys.Remove(queued.RequestKey);
                if (map is not null)
                    ApplyFilmstripPreview(queued.RequestKey, map);
                _filmstripWaiters.Remove(queued.RequestKey);
                PumpFilmstripQueue();
            }).CallDeferred();
        });
    }

    private static string FilmstripRequestKey(LayerFilmstripPreviewRequest request)
        => $"{request.SphereId}:{request.LayerId}:{request.Tick}:{request.ViewRung}:{request.Width}x{request.Height}";

    private void ApplyFilmstripPreview(string requestKey, LayerFilmstripPreviewMap map)
    {
        if (!IsInsideTree())
            return;

        if (!_filmstripWaiters.TryGetValue(requestKey, out var waiters))
            return;

        var key = new TimelineFilmstripCacheKey(
            map.SphereId,
            map.LayerId,
            map.SnapshotTick,
            map.ViewRung,
            map.Width,
            map.Height);
        if (!_filmstripTextureCache.TryGetValue(key, out var texture)
            || !GodotObject.IsInstanceValid(texture))
        {
            var image = Image.CreateFromData(map.Width, map.Height, false, Image.Format.Rgba8, map.Rgba32);
            texture = ImageTexture.CreateFromImage(image);
            _filmstripTextureCache[key] = texture;
        }

        _filmstripRequestTextureKeys[requestKey] = key;
        foreach (var pending in waiters)
        {
            if (pending.Generation != _filmstripGeneration)
                continue;

            var textureRect = pending.TextureRect;
            if (!GodotObject.IsInstanceValid(textureRect) || !textureRect.IsInsideTree())
                continue;

            textureRect.Texture = texture;
        }
    }

    private void PruneFilmstripWaiters(string requestKey)
    {
        if (!_filmstripWaiters.TryGetValue(requestKey, out var waiters))
            return;

        waiters.RemoveAll(pending => pending.Generation != _filmstripGeneration);
        if (waiters.Count == 0)
            _filmstripWaiters.Remove(requestKey);
    }

    private IDisposable? BuildExpandedGraph(Control content, LayerTrackGraphView graph)
    {
        content.MouseFilter = MouseFilterEnum.Stop;
        if (graph.Nodes.Count == 0)
        {
            var label = CompactStripLabel(graph.Label, muted: true);
            label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            label.VerticalAlignment = VerticalAlignment.Center;
            content.AddChild(label);
            return null;
        }

        var graphEdit = new GraphEdit
        {
            Name = "LayerGraphEdit",
            MouseFilter = MouseFilterEnum.Stop,
            ClipContents = true,
        };
        graphEdit.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        graphEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        graphEdit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        content.AddChild(graphEdit);

        return EmbeddedNodeGraphRenderer.TryBindReadOnly(
            graphEdit,
            new TrackGraphEditViewModel(graph),
            _log);
    }

    private static Label CompactChip(string text)
    {
        var chip = CompactStripLabel(text, muted: false);
        chip.CustomMinimumSize = new Vector2(82f, TrackHeight);
        chip.HorizontalAlignment = HorizontalAlignment.Center;
        chip.AddThemeStyleboxOverride("normal", CompactChipStyle());
        return chip;
    }

    private static Label CompactStripLabel(string text, bool muted)
    {
        var label = new Label
        {
            Text = text,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 10);
        label.AddThemeColorOverride("font_color", muted
            ? new Color(0.64f, 0.70f, 0.76f, 0.72f)
            : new Color(0.90f, 0.94f, 0.98f, 0.95f));
        return label;
    }

    private static StyleBoxFlat CompactChipStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.12f, 0.14f, 0.78f),
            BorderColor = new Color(0.24f, 0.36f, 0.42f, 0.72f),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        style.ContentMarginLeft = 5f;
        style.ContentMarginRight = 5f;
        return style;
    }

    private WorldGenerationGraphFamilyDocument? ResolveGenerationGraphFamily()
    {
        if (_ctl is null)
            return null;

        if (_cachedGraphFamilyTick == _ctl.Tick)
            return _cachedGraphFamily;

        _cachedGraphFamilyTick = _ctl.Tick;
        try
        {
            _cachedGraphFamily = _generationGraphFamilyProvider?.Invoke(_ctl.Tick);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Timeline layer graph family unavailable.");
            _cachedGraphFamily = null;
        }

        return _cachedGraphFamily;
    }

    private static string? ResolveTrackRegime(SphereRegimeSchedule schedule, string layerId, long currentTick)
    {
        var active = schedule.RegimeAt(currentTick);
        if (active is not null && HasLayer(active, layerId))
            return active.RegimeId;

        return schedule.Regimes.FirstOrDefault(regime => HasLayer(regime, layerId))?.RegimeId;
    }

    private static bool HasLayer(SphereRegime regime, string layerId)
        => regime.ActiveLayers.Any(layer => string.Equals(layer.Value, layerId, StringComparison.Ordinal));

    private void OnTrackExpandPressed(string sphere, string layerId)
    {
        var key = TrackKey(sphere, layerId);
        if (!_expandedTracks.Add(key))
            _expandedTracks.Remove(key);
        BuildLanes();
        UpdateLayout();
    }

    private async void OnTrackPressed(string sphere, string layerId)
    {
        if (_ctl is null || !IsLayerActive(sphere, layerId))
            return;

        var commandClient = _commandClient;
        if (commandClient is null)
        {
            _ctl.ToggleLayer(sphere, layerId);
            UpdateUI();
            return;
        }

        try
        {
            var schedule = string.Equals(sphere, "atmosphere", StringComparison.Ordinal)
                ? _ctl.AtmosphereSchedule
                : _ctl.GeosphereSchedule;
            var payload = JsonSerializer.Serialize(new
            {
                sphereId = sphere,
                layerId,
                regimeId = schedule.RegimeAt(_ctl.Tick)?.RegimeId
            });
            var result = await commandClient.CommandAsync(new CommandRequest(
                Command: "timeline.toggle_layer",
                PayloadJson: payload,
                ActorKind: "user",
                ActorId: "godot"));
            if (!result.Ok)
            {
                _log.LogWarning(
                    "Timeline layer toggle command failed: {LayerId} ({Error})",
                    layerId,
                    result.Error?.Message ?? "unknown error");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Timeline layer toggle command failed for {LayerId}.", layerId);
            _ctl.ToggleLayer(sphere, layerId);
        }

        UpdateUI();
    }

    private bool IsLayerActive(string sphere, string layerId)
    {
        if (_ctl is null)
            return false;

        var schedule = string.Equals(sphere, "atmosphere", StringComparison.Ordinal)
            ? _ctl.AtmosphereSchedule
            : _ctl.GeosphereSchedule;

        return schedule.RegimeAt(_ctl.Tick)?.ActiveLayers.Any(layer =>
            string.Equals(layer.Value, layerId, StringComparison.Ordinal)) == true;
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

        foreach (var band in _geosphereBands)
        {
            var isCurrent = _ctl.GeosphereSchedule.RegimeAt(tick)?.RegimeId == band.Button.Text;
            band.Button.Modulate = isCurrent ? new Color(1, 1, 1, 1f) : new Color(1, 1, 1, 0.58f);
        }

        foreach (var band in _atmosphereBands)
        {
            var isCurrent = _ctl.AtmosphereSchedule.RegimeAt(tick)?.RegimeId == band.Button.Text;
            band.Button.Modulate = isCurrent ? new Color(1, 1, 1, 1f) : new Color(1, 1, 1, 0.58f);
        }

        var activeGeoLayers = _ctl.GeosphereSchedule.RegimeAt(tick)?.ActiveLayers.Select(l => l.Value).ToHashSet() ?? new HashSet<string>();
        var activeAtmoLayers = _ctl.AtmosphereSchedule.RegimeAt(tick)?.ActiveLayers.Select(l => l.Value).ToHashSet() ?? new HashSet<string>();

        var selectedGeo = _ctl.ActiveLayers
            .Where(l => string.Equals(l.SphereId, "geosphere", StringComparison.Ordinal))
            .Select(l => l.LayerId).ToHashSet();
        var selectedAtmo = _ctl.ActiveLayers
            .Where(l => string.Equals(l.SphereId, "atmosphere", StringComparison.Ordinal))
            .Select(l => l.LayerId).ToHashSet();

        foreach (var track in _tracks)
        {
            bool isActive = track.Sphere == "geosphere"
                ? activeGeoLayers.Contains(track.LayerId)
                : activeAtmoLayers.Contains(track.LayerId);

            bool isSelected = isActive
                && (track.Sphere == "geosphere"
                    ? selectedGeo.Contains(track.LayerId)
                    : selectedAtmo.Contains(track.LayerId));

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

    private void DisposeTrackBindings()
    {
        foreach (var track in _tracks)
        {
            track.GraphBinding?.Dispose();
            DisconnectIfConnected(track.ToggleButton, BaseButton.SignalName.Pressed, track.ToggleCallable);
            DisconnectIfConnected(track.ChevronButton, BaseButton.SignalName.Pressed, track.ChevronCallable);
        }

        _tracks.Clear();
    }

    private void SupersedeFilmstripGeneration()
    {
        _filmstripGeneration++;
        _filmstripQueue.Clear();
        _filmstripQueuedKeys.Clear();
        foreach (var requestKey in _filmstripWaiters.Keys.ToArray())
            PruneFilmstripWaiters(requestKey);
    }

    private void DisposeFilmstripTextureCache()
    {
        foreach (var texture in _filmstripTextureCache.Values)
        {
            if (GodotObject.IsInstanceValid(texture))
                texture.Dispose();
        }

        _filmstripTextureCache.Clear();
        _filmstripRequestTextureKeys.Clear();
        _filmstripQueue.Clear();
        _filmstripQueuedKeys.Clear();
        _filmstripActiveKeys.Clear();
        _filmstripWaiters.Clear();
        _filmstripActiveRequests = 0;
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
