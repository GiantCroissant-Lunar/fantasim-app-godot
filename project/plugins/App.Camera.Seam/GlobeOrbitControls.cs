using System;
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
    private float _yawDeg;
    private float _pitchDeg;
    private float _springLength;
    private float _minSpring = 1.5f;
    private float _maxSpring = 8.0f;
    private float _minPitchDeg = -85f;
    private float _maxPitchDeg = 85f;
    private float _orbitSensitivity = 0.25f;
    private float _wheelZoomFactor = 0.9f;
    private bool _dragging;
    private Vector2 _lastMousePos;

    private readonly LazyBindOnce<PhantomCameraHost> _lazyBind;
    private Func<PhantomCameraHost?>? _hostProvider;

    public GlobeOrbitControls()
    {
        _lazyBind = new LazyBindOnce<PhantomCameraHost>(BindHost);
    }

    /// <summary>Optional logger for lazy-bind diagnostics (bound confirmation).</summary>
    public ILogger? Logger { private get; set; }

    /// <summary>Initial orbit yaw in degrees (rotation around the globe Y axis).</summary>
    public float InitialYawDeg { get; set; } = 35f;

    /// <summary>Initial orbit pitch in degrees (tilt; + looks down on the north pole).</summary>
    public float InitialPitchDeg { get; set; } = -25f;

    /// <summary>Initial spring-arm length (distance from follow target to camera).</summary>
    public float InitialSpringLength { get; set; } = 4.0f;

    public float MinSpringLength
    {
        get => _minSpring;
        set => _minSpring = Math.Max(0.1f, value);
    }

    public float MaxSpringLength
    {
        get => _maxSpring;
        set => _maxSpring = Math.Max(_minSpring + 0.1f, value);
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
        _yawDeg = InitialYawDeg;
        _pitchDeg = Math.Clamp(InitialPitchDeg, _minPitchDeg, _maxPitchDeg);
        _springLength = Math.Clamp(InitialSpringLength, _minSpring, _maxSpring);
        ApplyToActivePcam();
        _hostProvider = null;
        Logger?.LogDebug("GlobeOrbitControls bound to PhantomCameraHost.");
    }

    public override void _Process(double delta)
    {
        if (_lazyBind.IsBound || _hostProvider is null)
            return;

        _lazyBind.TryResolve(_hostProvider);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_host is null)
            return;

        switch (@event)
        {
            case InputEventMouseButton mouse:
                HandleMouseButton(mouse);
                break;
            case InputEventMouseMotion motion when _dragging:
                HandleDrag(motion);
                break;
            case InputEventMagnifyGesture pinch:
                HandlePinch(pinch);
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouse)
    {
        switch (mouse.ButtonIndex)
        {
            case MouseButton.Left or MouseButton.Right:
                _dragging = mouse.Pressed;
                if (_dragging) _lastMousePos = mouse.Position;
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
        _yawDeg -= delta.X * _orbitSensitivity;
        _pitchDeg = Math.Clamp(
            _pitchDeg + delta.Y * _orbitSensitivity,
            _minPitchDeg,
            _maxPitchDeg);
        ApplyToActivePcam();
    }

    private void HandlePinch(InputEventMagnifyGesture pinch)
    {
        // MagnifyFactor > 1 = spread (zoom out), < 1 = pinch (zoom in).
        ZoomByFactor(pinch.Factor);
    }

    private void ZoomByFactor(float factor)
    {
        _springLength = Math.Clamp(_springLength * factor, _minSpring, _maxSpring);
        ApplyToActivePcam();
    }

    private void ApplyToActivePcam()
    {
        if (_host is null)
            return;

        var active = _host.GetActivePhantomCamera();
        if (active is not PhantomCamera3D pcam3d)
            return;

        pcam3d.SetThirdPersonRotationDegrees(new Vector3(_pitchDeg, _yawDeg, 0f));
        pcam3d.SpringLength = _springLength;
    }
}