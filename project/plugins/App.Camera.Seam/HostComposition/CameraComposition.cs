using FantaSim.App.Common;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Camera.Seam;

/// <summary>
/// T4 resident camera seam composition. Builds the <see cref="CameraRig"/> under the host node,
/// registers the T3 <see cref="FantaSim.App.Camera.IService"/> (the engine-agnostic orchestrator)
/// with the rig + crosscut bus + logger, registers a default globe camera viewpoint, configures it
/// for orbit+zoom via the vendored phantom-camera addon's ThirdPerson follow mode, and attaches a
/// <see cref="GlobeOrbitControls"/> node that translates drag/wheel/pinch input to the active
/// PhantomCamera3D. Mirrors RenderComposition / TimelineComposition: read what the context needs,
/// register services, return a handle the host unregisters on shutdown.
/// </summary>
public static class CameraComposition
{
    public const string DefaultGlobeCameraId = "globe.default";

    public static ICameraCompositionHandle ComposeCamera(HostCompositionContext ctx, Node hostNode)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.Camera");
        var registry = ctx.Registry;

        var bus = registry.TryGet<CrosscutFoundation.Messaging.IMessageBus>();
        if (bus is null)
        {
            log.LogWarning("Camera: no IMessageBus registered; camera service will be inert.");
            return new CameraCompositionHandle(null, null, registered: false);
        }

        var rig = new CameraRig(hostNode, ctx.LoggerFactory);
        var cameraService = new FantaSim.App.Camera.Services.Service(rig, bus, ctx.LoggerFactory);

        registry.Register<FantaSim.App.Camera.IService>(
            cameraService,
            new ServiceRegistration
            {
                OwnerId = "camera.resident",
                Priority = 100,
                Tags = new[] { "camera" },
                Description = "Virtual camera service (phantom-camera seam)"
            });

        log.LogInformation("registered: App.Camera IService (phantom-camera rig).");
        return new CameraCompositionHandle(cameraService, rig, registered: true);
    }

    /// <summary>
    /// Register the default globe camera viewpoint, configure it for ThirdPerson orbit+zoom, and
    /// attach the orbit controls. Called by the host after the world bundle has mounted the globe
    /// (the globe mesh lives at the world origin, so the follow target sits at Vector3.Zero).
    /// </summary>
    public static void MountDefaultGlobeCamera(
        HostCompositionContext ctx,
        Node hostNode,
        ICameraCompositionHandle handle)
    {
        if (!handle.Registered || handle is not CameraCompositionHandle real || real.Service is null || real.Rig is null)
            return;

        var log = ctx.LoggerFactory.CreateLogger("HostComposition.Camera");

        // The world-bundle-loaded handler re-runs on every hot-reload; the rig, pcam host, and
        // orbit controls are all resident, so the globe camera must mount exactly once.
        if (real.GlobeCameraMounted)
        {
            log.LogDebug("Default globe camera already mounted; skipping re-mount on bundle reload.");
            return;
        }
        real.GlobeCameraMounted = true;

        var spec = new FantaSim.App.Camera.CameraSpec(
            CameraId: DefaultGlobeCameraId,
            Position: new System.Numerics.Vector3(0, 1.5f, 3.0f),
            LookAt: System.Numerics.Vector3.Zero,
            ViewportId: "main",
            FieldOfViewDegrees: 60f);

        // The rig + service calls marshal onto the Godot main thread; the host calls this from a
        // deferred Callable so the scene tree is ready by the time the pcam is built.
        _ = real.Service.RegisterAsync(spec).ContinueWith(t =>
            log.LogError(t.Exception, "RegisterAsync(default globe) failed."),
            TaskContinuationOptions.OnlyOnFaulted);

        var followTarget = new Node3D { Name = "GlobeOrbitFollowTarget" };
        real.Rig.ConfigureGlobeOrbit(
            DefaultGlobeCameraId,
            followTarget,
            initialSpringLength: 4.0f,
            minSpring: 1.5f,
            maxSpring: 8.0f);

        _ = real.Service.ActivateAsync(DefaultGlobeCameraId).ContinueWith(t =>
            log.LogError(t.Exception, "ActivateAsync(default globe) failed."),
            TaskContinuationOptions.OnlyOnFaulted);

        var host = real.Rig.GetHost("main");

        var controls = new GlobeOrbitControls
        {
            InitialYawDeg = 35f,
            InitialPitchDeg = -25f,
            InitialSpringLength = 4.0f,
            MinSpringLength = 1.5f,
            MaxSpringLength = 8.0f,
            Logger = log,
        };
        hostNode.AddChild(controls);

        if (host is not null)
        {
            controls.Bind(host);
            log.LogInformation("default globe camera mounted + orbit controls bound.");
            return;
        }

        // The rig registers its PhantomCameraHost via a deferred callable (RegisterAsync marshals
        // node creation onto the Godot main thread); at mount time (itself deferred from the
        // world-bundle-loaded handler) the host may not exist yet. The controls poll the rig each
        // _Process frame and bind on first availability instead of demanding the host now. A
        // one-shot ordering hack would break on bundle reload (the handler re-runs) and boot timing
        // skew, so the bind is permanently lazy.
        controls.BindWhenAvailable(() => real.Rig.GetHost("main"));
        log.LogDebug("Default globe camera mount: PhantomCameraHost not yet available on 'main'; orbit controls will bind lazily.");
    }
}

public interface ICameraCompositionHandle : IDisposable
{
    bool Registered { get; }

    void Unregister(IRegistry registry);
}

internal sealed class CameraCompositionHandle : ICameraCompositionHandle
{
    public FantaSim.App.Camera.Services.Service? Service { get; }
    public CameraRig? Rig { get; }
    internal bool GlobeCameraMounted;
    private readonly bool _registered;
    private bool _disposed;

    public CameraCompositionHandle(FantaSim.App.Camera.Services.Service? service, CameraRig? rig, bool registered)
    {
        Service = service;
        Rig = rig;
        _registered = registered;
    }

    public bool Registered => _registered;

    public void Unregister(IRegistry registry)
    {
        if (!_registered)
            return;

        registry?.UnregisterAll<FantaSim.App.Camera.IService>();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { Service?.Dispose(); }
        catch { }
    }
}