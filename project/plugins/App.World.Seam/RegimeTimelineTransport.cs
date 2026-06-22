using System;
using FantaSim.App.World.Composition;
using Godot;

namespace FantaSim.App.World.Seam;

/// <summary>
/// T4 Godot transport node: owns an <see cref="AnimationPlayer"/> + <see cref="AnimationTree"/>
/// state machine (Idle / Playing / Scrub) and drives tick playback across the three regime
/// sections (magma-ocean → stagnant-lid → mobile-plate).
///
/// <para><b>Usage.</b> Mount next to <see cref="GlobeView"/> in <c>ComposeWorldView</c>:
/// <code>
///   var transport = new RegimeTimelineTransport(globeView, schedule, maxTick, ticksPerSecond);
///   GetTree().Root.CallDeferred("add_child", transport);
/// </code>
/// The transport calls <c>globeView.SetTick(tick)</c> and <c>globeView.SetRegime(...)</c>
/// every playback frame. The existing <see cref="GlobeView"/> HSlider scrubber remains the user
/// input; Play/Pause is toggled via <see cref="SetPlaying"/>.</para>
///
/// <para><b>AnimationPlayer/AnimationTree pattern.</b> Matches the ref-project
/// <c>App.Timeline.Seam/TimelineTunnelLayer.cs</c> (lines 109-123 for player+tree setup,
/// 268-293 for state machine transitions). Four animations are registered (idle, playing, scrub,
/// autoplay); the AnimationTree state machine transitions between them based on user/transport
/// actions.</para>
///
/// <para><b>Regime boundaries.</b> The transport labels the three regime spans:
/// [0, MagmaOceanEndTick) = magma-ocean, [MagmaOceanEndTick, onsetTick) = stagnant-lid,
/// [onsetTick, maxTick] = mobile-plate. At each boundary the transport calls
/// <c>SetRegime</c> on the globe view so colour and cap visibility switch atomically with
/// the tick advance.</para>
/// </summary>
public sealed partial class RegimeTimelineTransport : Node
{
    // ---- AnimationTree state names (StringName) -----------------------------------------------
    private static readonly StringName AnimIdle     = new("idle");
    private static readonly StringName AnimPlaying  = new("playing");
    private static readonly StringName AnimScrub    = new("scrub");

    // ---- Defaults (can be overridden post-construction via public setters) -------------------

    /// <summary>Default playback rate when no explicit ticksPerSecond is provided.</summary>
    public const double DefaultTicksPerSecond = 5_000_000.0; // ~5 Ma/s at 100k ticks/Ma

    // ---- Construction state -------------------------------------------------------------------

    private readonly GlobeView _globeView;
    private readonly SphereRegimeSchedule _schedule;
    private readonly long _maxTick;
    private readonly double _ticksPerSecond;

    // ---- Runtime state -----------------------------------------------------------------------

    private AnimationPlayer? _animationPlayer;
    private AnimationTree? _animationTree;
    private AnimationNodeStateMachinePlayback? _playback;

    private bool _isPlaying;
    private double _tickAccum;     // sub-tick accumulator for smooth advance

    /// <summary>
    /// Construct the transport.
    /// </summary>
    /// <param name="globeView">The globe view to drive.</param>
    /// <param name="schedule">The geosphere regime schedule (GeosphereFor(onsetTick)).</param>
    /// <param name="maxTick">The tick at which playback wraps back to 0. Should be at least a
    ///   bit past the onset tick so the mobile-plate regime is visible.
    ///   Default: <see cref="SphereRegimeScheduleDefaults.PlateOnsetTick"/> + 20 000 000.</param>
    /// <param name="ticksPerSecond">Playback speed in canonical ticks per wall-clock second.
    ///   Default: <see cref="DefaultTicksPerSecond"/>.</param>
    public RegimeTimelineTransport(
        GlobeView globeView,
        SphereRegimeSchedule schedule,
        long maxTick = 0,
        double ticksPerSecond = DefaultTicksPerSecond)
    {
        _globeView = globeView ?? throw new ArgumentNullException(nameof(globeView));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _ticksPerSecond = ticksPerSecond > 0 ? ticksPerSecond : DefaultTicksPerSecond;

        // If caller passes 0 for maxTick, derive a sensible default: onset + 20 Ma.
        _maxTick = maxTick > 0 ? maxTick : (SphereRegimeScheduleDefaults.PlateOnsetTick + 20_000_000L);

        Name = "RegimeTimelineTransport";
    }

    // ---- Godot lifecycle ---------------------------------------------------------------------

    public override void _Ready()
    {
        // AnimationPlayer: owns the animation library; root node is the parent (GlobeView) so
        // scale tracks could animate globe children — not needed here but matches the ref pattern.
        _animationPlayer = new AnimationPlayer
        {
            Name = "RegimeAnimationPlayer",
            PlaybackDefaultBlendTime = 0.12,
        };
        AddChild(_animationPlayer);

        // AnimationTree: drives the state machine; references the player by path.
        _animationTree = new AnimationTree
        {
            Name = "RegimeAnimationTree",
            AnimPlayer = new NodePath("../RegimeAnimationPlayer"),
            Active = false,
        };
        AddChild(_animationTree);

        BuildAnimationRig();

        // Start in Idle; the user or an env-var can switch to Playing.
        _isPlaying = System.Environment.GetEnvironmentVariable("FANTASIM_TIMELINE_AUTOPLAY") == "1";
        TransitionState(_isPlaying ? AnimPlaying : AnimIdle);

        GD.Print($"[RegimeTimelineTransport] ready: maxTick={_maxTick:N0}, " +
                 $"ticksPerSec={_ticksPerSecond:N0}, autoplay={_isPlaying}, " +
                 $"onsetTick={SphereRegimeScheduleDefaults.PlateOnsetTick:N0}");
    }

    public override void _Process(double delta)
    {
        if (!_isPlaying) return;

        // Advance tick accumulator.
        _tickAccum += _ticksPerSecond * delta;
        long advance = (long)_tickAccum;
        if (advance <= 0) return;
        _tickAccum -= advance;

        long newTick = _globeView.Tick + advance;
        if (newTick > _maxTick)
        {
            // Wrap: restart from 0 (loop the regime sequence).
            newTick = 0;
            _tickAccum = 0;
        }

        AdvanceTo(newTick);
    }

    // ---- Public transport API ----------------------------------------------------------------

    /// <summary>Start or pause playback.</summary>
    public void SetPlaying(bool playing)
    {
        if (_isPlaying == playing) return;
        _isPlaying = playing;
        TransitionState(playing ? AnimPlaying : AnimIdle);
    }

    /// <summary>True while playing forward.</summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>Jump to a specific tick (called by an external scrubber or test).</summary>
    public void JumpTo(long tick)
    {
        _tickAccum = 0;
        AdvanceTo(Math.Clamp(tick, 0, _maxTick));
        TransitionState(AnimScrub);
    }

    // ---- Internal helpers -------------------------------------------------------------------

    // Advance the globe to tick and push the current regime to the view.
    private void AdvanceTo(long tick)
    {
        _globeView.SetTick(tick);

        var regime = _schedule.RegimeAt(tick);
        if (regime is not null)
            _globeView.SetRegime(regime.RegimeId, regime.ShowsPlateFeatures, regime.DefaultColorByField);
        else
        {
            // Tick outside all regime windows: default to mobile-plate behaviour.
            _globeView.SetRegime("mobile-plate", true, null);
        }
    }

    // ---- AnimationPlayer / AnimationTree rig -------------------------------------------------
    // Pattern: ref-project TimelineTunnelLayer.cs lines 109-123 (player+tree setup) + 268-293
    // (state machine). Three states: Idle, Playing, Scrub; all-to-all transitions at 0.12 s.

    private void BuildAnimationRig()
    {
        if (_animationPlayer is null || _animationTree is null) return;

        var library = new AnimationLibrary();

        // A 0.5 s looping scale-pulse on the transport node itself (subtle breathing effect).
        library.AddAnimation(AnimIdle,    BuildPulseAnimation(length: 0.8f, loop: true,  scaleA: Vector3.One, scaleB: Vector3.One));
        library.AddAnimation(AnimPlaying, BuildPulseAnimation(length: 0.6f, loop: true,  scaleA: Vector3.One, scaleB: new Vector3(1.008f, 1.008f, 1.008f)));
        library.AddAnimation(AnimScrub,   BuildPulseAnimation(length: 0.3f, loop: false, scaleA: new Vector3(1.02f, 1.02f, 1.02f), scaleB: Vector3.One));

        _animationPlayer.AddAnimationLibrary(new StringName(string.Empty), library);

        // State machine: all three states, all-to-all bidirectional transitions.
        var machine = new AnimationNodeStateMachine
        {
            AllowTransitionToSelf = true,
            ResetEnds = true,
        };
        machine.AddNode(AnimIdle,    new AnimationNodeAnimation { Animation = AnimIdle },    new Vector2(  0f,   0f));
        machine.AddNode(AnimPlaying, new AnimationNodeAnimation { Animation = AnimPlaying }, new Vector2(200f, -80f));
        machine.AddNode(AnimScrub,   new AnimationNodeAnimation { Animation = AnimScrub },   new Vector2(200f,  80f));

        var states = new[] { AnimIdle, AnimPlaying, AnimScrub };
        foreach (var from in states)
        {
            foreach (var to in states)
            {
                if (from == to) continue;
                machine.AddTransition(from, to, new AnimationNodeStateMachineTransition
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
        _playback?.Start(AnimIdle, reset: true);
    }

    private void TransitionState(StringName state, bool reset = false)
    {
        if (_animationTree is not { Active: true }) return;
        _playback ??= _animationTree.Get("parameters/playback").As<AnimationNodeStateMachinePlayback>();
        if (_playback is null) return;

        if (!_playback.IsPlaying())
            _playback.Start(state, reset);
        else
            _playback.Travel(state, reset);
    }

    /// <summary>Simple scale-pulse animation on the transport node (subtle, not intrusive).</summary>
    private static Animation BuildPulseAnimation(float length, bool loop, Vector3 scaleA, Vector3 scaleB)
    {
        var anim = new Animation
        {
            Length = length,
            LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None,
        };

        int track = anim.AddTrack(Animation.TrackType.Scale3D);
        anim.TrackSetPath(track, new NodePath("."));
        anim.TrackSetInterpolationType(track, Animation.InterpolationType.Cubic);
        anim.ScaleTrackInsertKey(track, 0.0, scaleA);
        anim.ScaleTrackInsertKey(track, length * 0.5, scaleB);
        anim.ScaleTrackInsertKey(track, length, scaleA);
        return anim;
    }
}
