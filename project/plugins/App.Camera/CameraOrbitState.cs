using System;

namespace FantaSim.App.Camera;

public readonly record struct CameraOrbitSnapshot(double YawDeg, double PitchDeg, double Distance);

/// <summary>
/// Godot-free orbit state shared by mouse controls and remote camera ingress. Before the
/// PhantomCameraHost is bound, remote updates are remembered as pending values; the first bind
/// overlays those pending values onto the configured initial orbit and returns the snapshot to
/// apply to the actual camera.
/// </summary>
public sealed class CameraOrbitState
{
    private double _initialYawDeg = 35.0;
    private double _initialPitchDeg = -25.0;
    private double _initialDistance = 4.0;
    private double _minDistance = CameraOrbitLimits.MinDistance;
    private double _maxDistance = CameraOrbitLimits.MaxDistance;

    private double _yawDeg = 35.0;
    private double _pitchDeg = -25.0;
    private double _distance = 4.0;
    private double? _pendingYawDeg;
    private double? _pendingPitchDeg;
    private double? _pendingDistance;

    public bool IsBound { get; private set; }

    public CameraOrbitSnapshot Current => new(_yawDeg, _pitchDeg, _distance);

    public double MinDistance => _minDistance;

    public double MaxDistance => _maxDistance;

    public void ConfigureInitial(double yawDeg, double pitchDeg, double distance)
    {
        _initialYawDeg = CameraOrbitLimits.RequireFinite(yawDeg, "yawDeg");
        _initialPitchDeg = ClampPitch(pitchDeg);
        _initialDistance = ClampDistance(distance);

        if (IsBound)
            return;

        if (!_pendingYawDeg.HasValue)
            _yawDeg = _initialYawDeg;
        if (!_pendingPitchDeg.HasValue)
            _pitchDeg = _initialPitchDeg;
        if (!_pendingDistance.HasValue)
            _distance = _initialDistance;
    }

    public void ConfigureDistanceBounds(double minDistance, double maxDistance)
    {
        minDistance = Math.Max(0.1, CameraOrbitLimits.RequireFinite(minDistance, "distance"));
        maxDistance = Math.Max(minDistance + 0.1, CameraOrbitLimits.RequireFinite(maxDistance, "distance"));

        _minDistance = minDistance;
        _maxDistance = maxDistance;
        _initialDistance = ClampDistance(_initialDistance);
        _distance = ClampDistance(_distance);
        if (_pendingDistance.HasValue)
            _pendingDistance = ClampDistance(_pendingDistance.Value);
    }

    public CameraOrbitSnapshot Bind()
    {
        _yawDeg = _initialYawDeg;
        _pitchDeg = _initialPitchDeg;
        _distance = _initialDistance;
        IsBound = true;

        if (_pendingYawDeg.HasValue)
            _yawDeg = _pendingYawDeg.Value;
        if (_pendingPitchDeg.HasValue)
            _pitchDeg = _pendingPitchDeg.Value;
        if (_pendingDistance.HasValue)
            _distance = _pendingDistance.Value;

        _pendingYawDeg = null;
        _pendingPitchDeg = null;
        _pendingDistance = null;
        return Current;
    }

    public CameraOrbitSnapshot Apply(double? yawDeg, double? pitchDeg, double? distance)
    {
        if (yawDeg.HasValue)
        {
            _yawDeg = CameraOrbitLimits.RequireFinite(yawDeg.Value, "yawDeg");
            if (!IsBound)
                _pendingYawDeg = _yawDeg;
        }

        if (pitchDeg.HasValue)
        {
            _pitchDeg = ClampPitch(pitchDeg.Value);
            if (!IsBound)
                _pendingPitchDeg = _pitchDeg;
        }

        if (distance.HasValue)
        {
            _distance = ClampDistance(distance.Value);
            if (!IsBound)
                _pendingDistance = _distance;
        }

        return Current;
    }

    public CameraOrbitSnapshot OrbitBy(double yawDeltaDeg, double pitchDeltaDeg)
        => Apply(_yawDeg + yawDeltaDeg, _pitchDeg + pitchDeltaDeg, null);

    public CameraOrbitSnapshot ZoomByFactor(double factor)
    {
        factor = CameraOrbitLimits.RequireFinite(factor, "distance");
        return Apply(null, null, _distance * factor);
    }

    private static double ClampPitch(double pitchDeg)
        => Math.Clamp(
            CameraOrbitLimits.RequireFinite(pitchDeg, "pitchDeg"),
            CameraOrbitLimits.MinPitchDeg,
            CameraOrbitLimits.MaxPitchDeg);

    private double ClampDistance(double distance)
        => Math.Clamp(
            CameraOrbitLimits.RequireFinite(distance, "distance"),
            _minDistance,
            _maxDistance);
}
