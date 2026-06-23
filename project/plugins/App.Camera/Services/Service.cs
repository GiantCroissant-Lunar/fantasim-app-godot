using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrosscutFoundation.Messaging;
using FantaSim.App.Camera.Providers;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Camera.Services;

/// <summary>
/// The virtual-camera service (<see cref="IService"/>) orchestrator - engine-agnostic (NO Godot).
/// Registers camera viewpoints by id, tracks the active camera per viewport, and forwards to the
/// <see cref="ICameraRig"/> seam (implemented by the Godot App.Camera.Seam). Bus-triggered:
/// subscribes to <see cref="ActivateCameraMessage"/> so remote surfaces can drive cameras without
/// referencing this service directly.
/// </summary>
public sealed class Service : IService, IDisposable
{
    private readonly ICameraRig _rig;
    private readonly ILogger _log;
    private readonly object _lock = new();
    private readonly Dictionary<string, CameraSpec> _specs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activeByViewport = new(StringComparer.Ordinal);
    private readonly IDisposable _activateSubscription;
    private bool _disposed;

    public Service(ICameraRig rig, IMessageBus bus, ILoggerFactory loggerFactory)
    {
        _rig = rig ?? throw new ArgumentNullException(nameof(rig));
        if (bus is null) throw new ArgumentNullException(nameof(bus));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
        _log = loggerFactory.CreateLogger("App.Camera.Service");

        _activateSubscription = bus.Subscribe<ActivateCameraMessage>(m =>
        {
            _ = ActivateAsync(m.CameraId).ContinueWith(
                t => _log.LogError(t.Exception, "Bus-triggered ActivateAsync({CameraId}) failed.", m.CameraId),
                TaskContinuationOptions.OnlyOnFaulted);
        });
    }

    public IReadOnlyList<CameraSpec> RegisteredCameras
    {
        get
        {
            lock (_lock)
            {
                return _specs.Values.ToList();
            }
        }
    }

    public event Action? CamerasChanged;

    public string? ActiveCameraId(string viewportId = "main")
    {
        lock (_lock)
        {
            return _activeByViewport.TryGetValue(viewportId, out var id) ? id : null;
        }
    }

    public async Task RegisterAsync(CameraSpec spec, CancellationToken cancellationToken = default)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.CameraId))
            throw new ArgumentException("CameraId must not be null or whitespace.", nameof(spec));

        lock (_lock)
        {
            _specs[spec.CameraId] = spec;
        }

        await _rig.RegisterAsync(spec).ConfigureAwait(false);
        _log.LogInformation("Camera registered: {CameraId} (viewport={ViewportId}).", spec.CameraId, spec.ViewportId);
        RaiseCamerasChanged();
    }

    public async Task ActivateAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        CameraSpec spec;
        lock (_lock)
        {
            if (!_specs.TryGetValue(cameraId, out spec!))
                throw new InvalidOperationException($"Unknown camera id '{cameraId}'.");

            if (_activeByViewport.TryGetValue(spec.ViewportId, out var current) && current == cameraId)
                return;

            _activeByViewport[spec.ViewportId] = cameraId;
        }

        await _rig.ActivateAsync(cameraId).ConfigureAwait(false);
        _log.LogInformation("Camera activated: {CameraId} (viewport={ViewportId}).", cameraId, spec.ViewportId);
        RaiseCamerasChanged();
    }

    public async Task UnregisterAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_specs.Remove(cameraId, out var spec))
            {
                _log.LogDebug("Unregister of unknown camera id '{CameraId}' - no-op.", cameraId);
                return;
            }

            if (_activeByViewport.TryGetValue(spec.ViewportId, out var active) && active == cameraId)
                _activeByViewport.Remove(spec.ViewportId);
        }

        await _rig.UnregisterAsync(cameraId).ConfigureAwait(false);
        _log.LogInformation("Camera unregistered: {CameraId}.", cameraId);
        RaiseCamerasChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activateSubscription.Dispose();
    }

    private void RaiseCamerasChanged()
    {
        var handler = CamerasChanged;
        if (handler is null) return;
        try
        {
            handler.Invoke();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CamerasChanged subscriber threw.");
        }
    }
}
