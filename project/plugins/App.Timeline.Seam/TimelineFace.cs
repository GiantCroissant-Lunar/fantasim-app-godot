using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using FantaSim.App.World.Composition;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.Timeline;
using FantaSim.App.Command;
using Microsoft.Extensions.Logging;

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

    private readonly List<(Button Button, double Start, double Width)> _geosphereBands = new();
    private readonly List<(Button Button, double Start, double Width)> _atmosphereBands = new();
    private readonly List<(Button Button, string LayerId, string Sphere, StyleBoxFlat NormalStyle, StyleBoxFlat InactiveStyle, StyleBoxFlat SelectedStyle)> _tracks = new();

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
    private const float TrackHeight = 26f;
    private const float PlayheadHandleWidth = 22f;
    private const float PlayheadHandleHeight = 20f;

    private TimelineLadderRung SelectedRung => TimelineModel.SelectRungForSpan(_viewEndTick - _viewStartTick);

    private readonly ILogger _log;

    /// <summary>
    /// Set by Host.cs ComposeTimeline BEFORE the timeline bundle scene instantiates this face.
    /// The resident seam owns the reference; the collectible bundle's TimelinePlugin no longer
    /// holds a static. This is the same pattern as IiiBridge (Node-backed seam exception:
    /// the face needs _Ready/_ExitTree lifecycle, so it is a Node, but it exposes only
    /// ITimelineFace upward to T3).
    /// </summary>
    public static ITimelineController? ResidentController { get; set; }

    public static DeferredTimelineFace? ResidentProxy;

    public static IClient? ResidentCommandClient { get; set; }

    /// <summary>
    /// Shared factory set by TimelineComposition before the collectible bundle scene instantiates
    /// this face. Required because Godot scene instantiation uses the parameterless constructor.
    /// </summary>
    public static ILoggerFactory? ResidentLoggerFactory { get; set; }

    /// <summary>
    /// Configurable ticks-per-second for the playhead animation. Set by TimelineComposition
    /// before the face instantiates; read by BindResidentContext into the instance field. Default
    /// 5M ticks/sec (the crust snapshot spacing). Mirrors the ResidentController/ResidentProxy
    /// resident-statics pattern so the value can cross the ALC boundary without a scene edit.
    /// </summary>
    public static double ResidentTicksPerSecond { get; set; } = 5_000_000.0;

    public TimelineFace()
    {
        _log = ResidentLoggerFactory?.CreateLogger("Timeline.Face")
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

    public void RebindResidentContext()
        => BindResidentContext(forceProxyBind: true);

    private void BindResidentContext(bool forceProxyBind)
    {
        var controller = ResidentController;
        if (controller is null)
        {
            _log.LogWarning("No active ITimelineController found.");
            SetProcess(false);
            return;
        }

        if (_playbackRegistered && !ReferenceEquals(_ctl, controller))
            _ctl?.UnregisterPlayback();

        _ctl = controller;
        SetProcess(true);

        _ticksPerSecond = ResidentTicksPerSecond > 0.0 ? ResidentTicksPerSecond : 5_000_000.0;

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
            ResidentProxy?.BindCrossTarget(this);
            _proxyBound = ResidentProxy is not null;
        }

        SeekTo(_ctl.Tick);
        UpdateLayout();
    }

    public override void _ExitTree()
    {
        if (_ctl is not null && _playbackRegistered)
        {
            _ctl.UnregisterPlayback();
        }
        _playbackRegistered = false;
        _proxyBound = false;
        _ctl = null;
        ResidentController = null;
        DisconnectIfConnected(_playPauseButton, BaseButton.SignalName.Pressed, Callable.From(OnPlayPausePressed));
        DisconnectIfConnected(_zoomOutButton, BaseButton.SignalName.Pressed, Callable.From(OnZoomOutPressed));
        DisconnectIfConnected(_fitButton, BaseButton.SignalName.Pressed, Callable.From(OnFitPressed));
        DisconnectIfConnected(_zoomInButton, BaseButton.SignalName.Pressed, Callable.From(OnZoomInPressed));
        DisconnectIfConnected(_lanesContainer, Control.SignalName.GuiInput, Callable.From<InputEvent>(OnLanesGuiInput));
        DisconnectIfConnected(this, Control.SignalName.GuiInput, Callable.From<InputEvent>(OnFaceGuiInput));
        DisconnectIfConnected(this, Control.SignalName.Resized, Callable.From(OnLanesResized));

        // Sever the resident-to-collectible-ALC bind so the old timeline bundle's ALC can
        // collect on hot-reload. ResidentProxy holds a generated __crossTarget typed as
        // ITimelineFace (defined in the collectible App.Timeline assembly); without unbinding,
        // the static keeps the old ALC pinned. ResidentLoggerFactory is nulled for symmetry
        // so every resident-set static is cleared on exit (it is repopulated before re-entry).
        ResidentProxy?.UnbindCrossTarget();
        ResidentProxy = null;
        ResidentLoggerFactory = null;
        ResidentCommandClient = null;
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

        _ctl.PushTick(tick);
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
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                _scrubDragging = false;
                break;
            case InputEventMouseMotion motion when _scrubDragging:
                HandleScrub(motion.Position.X - _rulerRoot.GlobalPosition.X, _rulerRoot.Size.X);
                break;
        }
    }

    private void OnLanesGuiInput(InputEvent @event)
    {
        if (_ctl is null || _lanesContainer is null) return;

        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left && mouseBtn.Pressed)
            {
                _scrubDragging = true;
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
                HandleScrub(FaceToRulerLocalX(mouseBtn.Position.X), _rulerRoot.Size.X);
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            if ((mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                HandleScrub(FaceToRulerLocalX(mouseMotion.Position.X), _rulerRoot.Size.X);
                AcceptEvent();
            }
        }
    }

    private float FaceToRulerLocalX(float faceLocalX)
        => faceLocalX - (_rulerRoot!.GlobalPosition.X - GlobalPosition.X);

    private void HandleScrub(float localX)
    {
        if (_ctl is null || _lanesContainer is null) return;
        HandleScrub(localX, _lanesContainer.Size.X);
    }

    private void HandleScrub(float localX, float surfaceWidth)
    {
        if (_ctl is null) return;
        if (!TimelineScrubMapper.TryLocalXToTick(localX, surfaceWidth, _viewStartTick, _viewEndTick, out var tick))
        {
            // Loud failure per the ingress doctrine: a scrub that maps to nothing is a layout bug.
            _log.LogInformation("timeline scrub rejected: localX={X} width={W}", localX, surfaceWidth);
            return;
        }

        // Mirror the ingress seek sequence (service seek + push/echo). _ctl.SeekTo routes to the
        // service but never echoes back to this face — face-initiated scrubs updated the WORLD
        // while the label/playhead/handle stayed stale (the long-standing "timeline cannot be
        // adjusted" perception, proven live 2026-07-08). SeekTo(tick) is the local echo: it
        // pushes the tick to the controller and refreshes the UI.
        _ctl.SeekTo(tick);
        SeekTo(tick);
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

        UpdateUI();
        UpdateRuler();
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

        if (_ctl.MaxTick <= 0L) return;

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
        foreach (var t in tracks)
        {
            var trackSphere = sphere;
            var trackLayerId = t.LayerId;
            var btn = new Button
            {
                CustomMinimumSize = new Vector2(0, TrackHeight),
                Text = $"  {FriendlyLayerLabel(t.LayerId)}",
                TooltipText = $"{sphere}:{t.LayerId}",
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
            };
            btn.AddThemeFontSizeOverride("font_size", 12);
            btn.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f, 0.98f));
            btn.AddThemeColorOverride("font_hover_color", new Color(1.0f, 1.0f, 1.0f, 1.0f));

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

            btn.Pressed += () => OnTrackPressed(trackSphere, trackLayerId);

            tracksRoot.AddChild(btn);
            _tracks.Add((btn, t.LayerId, sphere, normalStyle, inactiveStyle, selectedStyle));
        }
    }

    private async void OnTrackPressed(string sphere, string layerId)
    {
        if (_ctl is null || !IsLayerActive(sphere, layerId))
            return;

        var commandClient = ResidentCommandClient;
        if (commandClient is null)
        {
            _ctl.SelectLayer(sphere, layerId);
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
                Command: "timeline.select_layer",
                PayloadJson: payload,
                ActorKind: "user",
                ActorId: "godot"));
            if (!result.Ok)
            {
                _log.LogWarning(
                    "Timeline layer selection command failed: {LayerId} ({Error})",
                    layerId,
                    result.Error?.Message ?? "unknown error");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Timeline layer selection command failed for {LayerId}.", layerId);
            _ctl.SelectLayer(sphere, layerId);
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

        var selected = _ctl.SelectedLayer;
        foreach (var track in _tracks)
        {
            bool isActive = track.Sphere == "geosphere"
                ? activeGeoLayers.Contains(track.LayerId)
                : activeAtmoLayers.Contains(track.LayerId);

            bool isSelected = isActive
                && selected is not null
                && string.Equals(selected.SphereId, track.Sphere, StringComparison.Ordinal)
                && string.Equals(selected.LayerId, track.LayerId, StringComparison.Ordinal);

            track.Button.Disabled = false;
            track.Button.Modulate = isActive ? new Color(1, 1, 1, 1f) : new Color(1, 1, 1, 0.68f);
            track.Button.AddThemeStyleboxOverride("normal", isSelected
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
        long nextSpan = Math.Clamp(targetSpan, MinViewSpanTicks, _ctl.MaxTick);
        long anchor = Math.Clamp(_ctl.Tick, _viewStartTick, _viewEndTick);
        double anchorFraction = span > 0 ? (anchor - _viewStartTick) / (double)span : 0.5;
        long nextStart = anchor - (long)(nextSpan * anchorFraction);
        SetViewRange(nextStart, nextStart + nextSpan);
    }

    private void SetViewRange(long startTick, long endTick)
    {
        if (_ctl is null) return;
        long span = Math.Max(MinViewSpanTicks, endTick - startTick);
        if (span >= _ctl.MaxTick)
        {
            _viewStartTick = 0L;
            _viewEndTick = _ctl.MaxTick;
        }
        else
        {
            long start = Math.Clamp(startTick, 0L, _ctl.MaxTick - span);
            _viewStartTick = start;
            _viewEndTick = start + span;
        }

        BuildLanes();
        UpdateLayout();
    }

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
