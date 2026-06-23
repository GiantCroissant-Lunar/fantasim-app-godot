using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline;

public partial class TimelineFace : Control
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

    private readonly List<(Button Button, double Start, double Width)> _geosphereBands = new();
    private readonly List<(Button Button, double Start, double Width)> _atmosphereBands = new();
    private readonly List<(Control Control, string LayerId, string Sphere)> _tracks = new();

    private double _internalTick;
    private long _lastPushedTick = -1;
    private bool _isPlaying;
    private long _viewStartTick;
    private long _viewEndTick;
    private readonly double _ticksPerSecond = 5_000_000.0;
    private const long MinViewSpanTicks = 1L;

    [Export]
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
        _zoomOutButton = GetNode<Button>("VBoxContainer/Header/ZoomOutButton");
        _fitButton = GetNode<Button>("VBoxContainer/Header/FitButton");
        _zoomInButton = GetNode<Button>("VBoxContainer/Header/ZoomInButton");
        _statusLabel = GetNode<Label>("VBoxContainer/Header/StatusLabel");
        _zoomLabel = GetNode<Label>("VBoxContainer/Header/ZoomLabel");
        _rulerRoot = GetNode<Control>("VBoxContainer/Ruler");
        _lanesContainer = GetNode<Control>("VBoxContainer/LanesContainer");
        _playheadLine = GetNode<ColorRect>("VBoxContainer/LanesContainer/PlayheadLine");

        _viewStartTick = 0L;
        _viewEndTick = _ctl.MaxTick;

        _playPauseButton.Pressed += OnPlayPausePressed;
        _zoomOutButton.Pressed += OnZoomOutPressed;
        _fitButton.Pressed += OnFitPressed;
        _zoomInButton.Pressed += OnZoomInPressed;
        _lanesContainer.GuiInput += OnLanesGuiInput;
        Resized += OnLanesResized;

        BuildLanes();
        _ctl.RegisterPlayback(Play, Pause, SeekTo, () => _isPlaying);
        SetupAnimationSystem();

        SeekTo(_ctl.Tick);
        UpdateLayout();
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
        if (_zoomOutButton is not null)
        {
            _zoomOutButton.Pressed -= OnZoomOutPressed;
        }
        if (_fitButton is not null)
        {
            _fitButton.Pressed -= OnFitPressed;
        }
        if (_zoomInButton is not null)
        {
            _zoomInButton.Pressed -= OnZoomInPressed;
        }
        if (_lanesContainer is not null)
        {
            _lanesContainer.GuiInput -= OnLanesGuiInput;
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
        var tick = (long)Math.Clamp(_viewStartTick + (fraction * (_viewEndTick - _viewStartTick)), _viewStartTick, _viewEndTick);
        _ctl.SeekTo(tick);
    }

    private void OnBandPressed(long startTick)
    {
        _ctl?.SeekTo(startTick);
    }

    private void OnZoomOutPressed()
    {
        ZoomAroundCurrentTick(2.0);
    }

    private void OnFitPressed()
    {
        if (_ctl is null) return;
        SetViewRange(0L, _ctl.MaxTick);
    }

    private void OnZoomInPressed()
    {
        ZoomAroundCurrentTick(0.5);
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

            var normalStyle = new StyleBoxFlat();
            normalStyle.BgColor = GetRegimeColor(b.RegimeId);
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
        var timeLabel = TimelineTimeFormatter.ForTick(tick);

        var playState = _isPlaying ? "playing" : "paused";
        var geoRegime = _ctl.GeosphereSchedule.RegimeAt(tick)?.RegimeId ?? "-";
        _statusLabel.Text = $"{playState} : {geoRegime} : {timeLabel}";
        _playPauseButton.Text = _isPlaying ? "Pause" : "Play";
        if (_zoomLabel is not null)
        {
            string viewRange = $"view {TimelineTimeFormatter.ForTick(_viewStartTick)} - {TimelineTimeFormatter.ForTick(_viewEndTick)}";
            if (_viewEndTick > _viewStartTick)
            {
                long step = TimelineModel.RulerStepTicks(_viewStartTick, _viewEndTick);
                _zoomLabel.Text = $"{viewRange} | step {TimelineTimeFormatter.ForTick(step)}";
            }
            else
            {
                _zoomLabel.Text = viewRange;
            }
        }

        var span = Math.Max(1L, _viewEndTick - _viewStartTick);
        var fraction = Math.Clamp((tick - _viewStartTick) / (double)span, 0.0, 1.0);
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

    private void UpdateRuler()
    {
        if (_rulerRoot is null) return;

        ClearChildren(_rulerRoot);
        var width = _rulerRoot.Size.X;
        if (width <= 0) return;
        if (_viewEndTick <= _viewStartTick) return;

        var baseline = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.28f),
            Position = new Vector2(0f, 25f),
            Size = new Vector2(width, 1f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _rulerRoot.AddChild(baseline);

        foreach (var mark in TimelineModel.Ruler(_viewStartTick, _viewEndTick))
        {
            float x = (float)(mark.Fraction * width);
            var tick = new ColorRect
            {
                Color = new Color(1f, 1f, 1f, 0.45f),
                Position = new Vector2(x, 13f),
                Size = new Vector2(1f, 12f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            _rulerRoot.AddChild(tick);

            var label = new Label
            {
                Text = mark.Label,
                Position = new Vector2(Math.Clamp(x - 34f, 0f, Math.Max(0f, width - 68f)), 0f),
                Size = new Vector2(68f, 13f),
                ClipText = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore
            };
            label.AddThemeFontSizeOverride("font_size", 10);
            _rulerRoot.AddChild(label);
        }
    }

    private void ZoomAroundCurrentTick(double factor)
    {
        if (_ctl is null) return;
        long span = _viewEndTick - _viewStartTick;
        long nextSpan = (long)Math.Clamp(span * factor, MinViewSpanTicks, _ctl.MaxTick);
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
}
