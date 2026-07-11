using System;
using FantaSim.App.Camera;
using Godot;
using Microsoft.Extensions.Logging;
using PhantomCamera;

namespace FantaSim.App.Camera.Seam;

/// <summary>
/// Resident Godot Node that translates drag + wheel/pinch input into orbit (yaw/pitch) and zoom
/// (spring length) on the active <see cref="PhantomCamera3D"/> for the globe view. Attached under
/// the host; reads the active pcam from the bound <see cref="PhantomCameraHost"/> each interaction.
/// Zoom is clamped to [<see cref="MinSpringLength"/>, <see cref="MaxSpringLength"/>]; pitch is
/// clamped to keep the camera off the gimbal poles. Mouse wheel + trackpad pinch are both wired.
/// </summary>
/// <remarks>
/// The host may not exist at mount time: <see cref="CameraRig.RegisterAsync"/> marshals node creation
/// onto the Godot main thread via a deferred callable, so a mount-time
/// <c>rig.GetHost("main")</c> races the rig's deferred build (the rig is built AFTER the mount
/// returns). Callers that have the host in hand use <see cref="Bind"/>; callers that do not (the
/// normal export-boot path) use <see cref="BindWhenAvailable"/> and this Node polls the rig each
/// <see cref="_Process"/> frame until the host appears, then binds exactly once. A one-shot ordering
/// hack is deliberately rejected: bundle hot-reloads re-run the mount handler and boot timing varies,
/// so a permanent lazy bind is the robust fix. The poll/bind-once shape is delegated to the
/// Godot-free <see cref="LazyBindOnce{T}"/> (unit-tested in App.Camera.Tests).
/// </remarks>
public sealed partial class GlobeOrbitControls : Node
{
    private PhantomCameraHost? _host;
    private float _initialYawDeg = 35f;
    private float _initialPitchDeg = -25f;
    private float _initialSpringLength = 4.0f;
    private float _orbitSensitivity = 0.25f;
    private float _wheelZoomFactor = 0.9f;
    private bool _dragging;
    private bool _applyToPcamPending;
    private Vector2 _lastMousePos;

    private readonly CameraOrbitState _orbit = new();
    private readonly LazyBindOnce<PhantomCameraHost> _lazyBind;
    private Func<PhantomCameraHost?>? _hostProvider;

    // ---- input-path diagnostics (read by CameraRig.DebugSnapshotImpl via ActiveInstance) ----
    // Counters sit BEFORE the host-null early returns so camera.debug can distinguish "events
    // never delivered" from "events delivered but the host/pcam link is broken".
    internal static GlobeOrbitControls? ActiveInstance { get; private set; }
    internal int DiagPressesSeen;
    internal int DiagReleasesSeen;
    internal int DiagWheelSeen;
    internal int DiagMotionsSeen;
    internal int DiagDragMotionsApplied;
    internal bool DiagDraggingNow => _dragging;
    internal bool DiagHostBound => _host is not null;

    // The atomic orbit pose at the instant of the debug snapshot (read-only, never re-applied):
    // yaw/pitch/distance the mouse controls + remote ingress share, sampled without invoking
    // camera.orbit (which reapplies state and is not an observation).
    internal CameraOrbitSnapshot DiagOrbitSnapshot => _orbit.Current;

    public GlobeOrbitControls()
    {
        _lazyBind = new LazyBindOnce<PhantomCameraHost>(BindHost);
    }

    public override void _EnterTree() => ActiveInstance = this;

    public override void _ExitTree()
    {
        if (ReferenceEquals(ActiveInstance, this))
            ActiveInstance = null;
    }

    /// <summary>Optional logger for lazy-bind diagnostics (bound confirmation).</summary>
    public ILogger? Logger { private get; set; }

    /// <summary>Initial orbit yaw in degrees (rotation around the globe Y axis).</summary>
    public float InitialYawDeg
    {
        get => _initialYawDeg;
        set
        {
            _initialYawDeg = value;
            ConfigureOrbitState();
        }
    }

    /// <summary>Initial orbit pitch in degrees (tilt; + looks down on the north pole).</summary>
    public float InitialPitchDeg
    {
        get => _initialPitchDeg;
        set
        {
            _initialPitchDeg = value;
            ConfigureOrbitState();
        }
    }

    /// <summary>Initial spring-arm length (distance from follow target to camera).</summary>
    public float InitialSpringLength
    {
        get => _initialSpringLength;
        set
        {
            _initialSpringLength = value;
            ConfigureOrbitState();
        }
    }

    public float MinSpringLength
    {
        get => (float)_orbit.MinDistance;
        set => _orbit.ConfigureDistanceBounds(value, _orbit.MaxDistance);
    }

    public float MaxSpringLength
    {
        get => (float)_orbit.MaxDistance;
        set => _orbit.ConfigureDistanceBounds(_orbit.MinDistance, value);
    }

    public float OrbitSensitivity
    {
        get => _orbitSensitivity;
        set => _orbitSensitivity = Math.Max(0.01f, value);
    }

    /// <summary>Bind the controls to the phantom host the rig built for the globe viewport.</summary>
    public void Bind(PhantomCameraHost host)
        => BindHost(host ?? throw new ArgumentNullException(nameof(host)));

    /// <summary>
    /// Apply a remote orbit command to the same yaw/pitch/spring state used by mouse controls.
    /// If the PhantomCameraHost is not bound yet, the values are remembered and applied on bind.
    /// Null values keep the current value.
    /// </summary>
    public CameraOrbitSnapshot ApplyOrbit(double? yawDeg, double? pitchDeg, double? distance)
    {
        ConfigureOrbitState();
        var snapshot = _orbit.Apply(yawDeg, pitchDeg, distance);
        ApplyToActivePcam();
        return snapshot;
    }

    /// <summary>
    /// Bind lazily: poll <paramref name="hostProvider"/> each <see cref="_Process"/> frame until the
    /// rig has built the <see cref="PhantomCameraHost"/> for the globe viewport, then bind exactly
    /// once. Use this when the host is not yet available at call time (the export-boot path: the rig
    /// registers its host via a deferred callable that races the mount).
    /// </summary>
    public void BindWhenAvailable(Func<PhantomCameraHost?> hostProvider)
    {
        _hostProvider = hostProvider ?? throw new ArgumentNullException(nameof(hostProvider));
    }

    private void BindHost(PhantomCameraHost host)
    {
        _host = host;
        ConfigureOrbitState();
        _orbit.Bind();
        ApplyToActivePcam();
        _hostProvider = null;
        Logger?.LogDebug("GlobeOrbitControls bound to PhantomCameraHost.");
    }

    public override void _Process(double delta)
    {
        if (!_lazyBind.IsBound && _hostProvider is not null)
            _lazyBind.TryResolve(_hostProvider);

        if (_applyToPcamPending)
            ApplyToActivePcam();
    }

    // Input routing is deliberately SPLIT between the two callbacks (windowed-verified 2026-07-08):
    // the PRESS decides gesture ownership in _UnhandledInput — a press the UI claimed (timeline
    // scrub, ledger buttons) never starts a globe drag — but the MOTION of an owned drag is tracked
    // in _Input. Once a button is held, the viewport routes motion through the GUI mouse-focus
    // path and it never reaches _UnhandledInput (empirical: wheel and press arrived, drag motion
    // never did), so motion-in-_UnhandledInput silently dead-ends — the "cannot rotate the planet"
    // defect. _Input fires for every event, and gating on _dragging keeps it inert outside an
    // owned gesture. Release is also handled in _Input so an over-UI release cannot strand the
    // drag flag. This is the same capture split as the official drag-and-drop example
    // (docs: tutorials/inputs/input_examples).
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right })
            DiagPressesSeen++;
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown })
            DiagWheelSeen++;

        if (_host is null)
            return;

        switch (@event)
        {
            case InputEventMouseButton mouse:
                HandleMouseButton(mouse);
                break;
            case InputEventMagnifyGesture pinch:
                HandlePinch(pinch);
                break;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion)
            DiagMotionsSeen++;
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left or MouseButton.Right })
            DiagReleasesSeen++;

        if (_host is null)
            return;

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right, Pressed: false }:
                _dragging = false;
                break;
            case InputEventMouseMotion motion when _dragging:
                DiagDragMotionsApplied++;
                HandleDrag(motion);
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouse)
    {
        switch (mouse.ButtonIndex)
        {
            case MouseButton.Left or MouseButton.Right:
                if (mouse.Pressed)
                {
                    _dragging = true;
                    _lastMousePos = mouse.Position;
                }
                break;
            case MouseButton.WheelUp:
                if (mouse.Pressed) ZoomByFactor(_wheelZoomFactor);
                break;
            case MouseButton.WheelDown:
                if (mouse.Pressed) ZoomByFactor(1.0f / _wheelZoomFactor);
                break;
        }
    }

    private void HandleDrag(InputEventMouseMotion motion)
    {
        var delta = motion.Position - _lastMousePos;
        _lastMousePos = motion.Position;
        _orbit.OrbitBy(-delta.X * _orbitSensitivity, delta.Y * _orbitSensitivity);
        ApplyToActivePcam();
    }

    private void HandlePinch(InputEventMagnifyGesture pinch)
    {
        // MagnifyFactor > 1 = spread (zoom out), < 1 = pinch (zoom in).
        ZoomByFactor(pinch.Factor);
    }

    private void ZoomByFactor(float factor)
    {
        _orbit.ZoomByFactor(factor);
        ApplyToActivePcam();
    }

    private void ConfigureOrbitState()
        => _orbit.ConfigureInitial(InitialYawDeg, InitialPitchDeg, InitialSpringLength);

    private void ApplyToActivePcam()
    {
        if (_host is null)
        {
            _applyToPcamPending = true;
            return;
        }

        var active = _host.GetActivePhantomCamera();
        if (active is not PhantomCamera3D pcam3d)
        {
            _applyToPcamPending = true;
            return;
        }

        var orbit = _orbit.Current;
        pcam3d.SetThirdPersonRotationDegrees(new Vector3((float)orbit.PitchDeg, (float)orbit.YawDeg, 0f));
        pcam3d.SpringLength = (float)orbit.Distance;
        _applyToPcamPending = false;
    }
}
