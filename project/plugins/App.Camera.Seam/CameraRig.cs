using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FantaSim.App.Camera;
using FantaSim.App.Camera.Providers;
using Godot;
using Microsoft.Extensions.Logging;
using PhantomCamera;

namespace FantaSim.App.Camera.Seam;

/// <summary>
/// The Godot camera-rig seam (App.Camera's <see cref="ICameraRig"/>): builds a per-viewport
/// Camera3D + PhantomCameraHost pair and one PhantomCamera3D per registered viewpoint;
/// activation is a phantom-camera priority switch, so transitions tween. Resident assembly;
/// the addon GDScript ships with the complete-app host. Plain C# (NOT Godot-derived): the rig
/// nodes are resident, so a camera bundle ships no Godot types and its collectible ALC unloads
/// cleanly.
/// </summary>
public sealed class CameraRig : ICameraRig
{
    private sealed class ViewportRig
    {
        public ViewportRig(
            Node3D root,
            Camera3D camera,
            Node host,
            PhantomCameraHost hostWrapper,
            SubViewport? viewport,
            SubViewportContainer? panel,
            int layerBit)
        {
            Root = root;
            Camera = camera;
            Host = host;
            HostWrapper = hostWrapper;
            Viewport = viewport;
            Panel = panel;
            LayerBit = layerBit;
        }

        public Node3D Root { get; }
        public Camera3D Camera { get; }
        public Node Host { get; }
        public PhantomCameraHost HostWrapper { get; }
        public SubViewport? Viewport { get; }
        public SubViewportContainer? Panel { get; }
        public int LayerBit { get; }
    }

    private sealed class CameraEntry
    {
        public CameraEntry(Node3D node, PhantomCamera3D wrapper, CameraSpec spec)
        {
            Node = node;
            Wrapper = wrapper;
            Spec = spec;
        }

        public Node3D Node { get; }
        public PhantomCamera3D Wrapper { get; }
        public CameraSpec Spec { get; }
    }

    private sealed record GlobeOrbitConfiguration(
        Node3D FollowTarget,
        float InitialSpringLength,
        float MinSpring,
        float MaxSpring);

    private readonly Node _parent;
    private readonly Control? _panelHost;
    private readonly ILogger _log;
    private readonly IReadOnlyDictionary<string, int> _categoryLayers;
    private readonly Dictionary<string, ViewportRig> _rigs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CameraEntry> _cameras = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GlobeOrbitConfiguration> _pendingGlobeOrbit = new(StringComparer.Ordinal);
    private readonly Stack<int> _freedLayerBits = new();
    private int _nextLayerIndex = 1;

    /// <summary>
    /// Creates a new <see cref="CameraRig"/>.
    /// </summary>
    /// <param name="parent">The parent node under which rig roots are added.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="panelHost">
    /// Optional host-provided Control (e.g. an HBoxContainer in a screen corner) that
    /// secondary-viewport panels mount under. When null, non-"main" viewports fall back to the
    /// root viewport with a warning (wave-1 behaviour).
    /// </param>
    /// <param name="categoryLayers">
    /// The semantic layer axis - world content declares render layers; cameras pick categories;
    /// this map is host policy (the world subsystem will own the canonical registry later).
    /// </param>
    public CameraRig(Node parent, ILoggerFactory loggerFactory, Control? panelHost = null,
                     IReadOnlyDictionary<string, int>? categoryLayers = null)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _ = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _panelHost = panelHost;
        _categoryLayers = categoryLayers ?? new Dictionary<string, int>(StringComparer.Ordinal);
        _log = loggerFactory.CreateLogger("App.Camera.CameraRig");
    }

    // The T3 service may call from off the Godot main thread (a bus handler); nodes must be
    // created on it, so marshal the rig operations onto the main thread.
    public Task RegisterAsync(CameraSpec spec)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Callable.From(() =>
        {
            try { RegisterImpl(spec); tcs.TrySetResult(); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }).CallDeferred();
        return tcs.Task;
    }

    public Task ActivateAsync(string cameraId)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Callable.From(() =>
        {
            try { ActivateImpl(cameraId); tcs.TrySetResult(); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }).CallDeferred();
        return tcs.Task;
    }

    public Task UnregisterAsync(string cameraId)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Callable.From(() =>
        {
            try { UnregisterImpl(cameraId); tcs.TrySetResult(); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }).CallDeferred();
        return tcs.Task;
    }

    private void RegisterImpl(CameraSpec spec)
    {
        EnsureViewportRig(spec.ViewportId);

        if (_cameras.TryGetValue(spec.CameraId, out var existing))
        {
            // Replace: update spec, reposition the existing pcam node.
            if (existing.Spec.ViewportId != spec.ViewportId)
            {
                _log.LogWarning(
                    "Camera '{CameraId}' viewport reassignment from '{OldViewport}' to " +
                    "'{NewViewport}' is not supported; keeping old viewport.",
                    spec.CameraId, existing.Spec.ViewportId, spec.ViewportId);
                spec = spec with { ViewportId = existing.Spec.ViewportId };
            }

            var pos = new Vector3(spec.Position.X, spec.Position.Y, spec.Position.Z);
            var target = new Vector3(spec.LookAt.X, spec.LookAt.Y, spec.LookAt.Z);
            if (!SafeLookAt(existing.Node, pos, target))
            {
                _log.LogWarning(
                    "Camera '{CameraId}' position and look-at are colinear with Up; " +
                    "rotation left unchanged.", spec.CameraId);
            }
            ApplyCameraResource(existing.Wrapper, spec);
            existing.Wrapper.HostLayers = _rigs[spec.ViewportId].LayerBit;
            var replacement = new CameraEntry(existing.Node, existing.Wrapper, spec);
            _cameras[spec.CameraId] = replacement;
            _log.LogInformation(
                "Camera replaced: {CameraId} (viewport = {ViewportId}, position = {Position}).",
                spec.CameraId, spec.ViewportId, spec.Position);
            if (_pendingGlobeOrbit.Remove(spec.CameraId, out var pendingGlobeOrbit))
                ApplyGlobeOrbitConfiguration(spec.CameraId, replacement, pendingGlobeOrbit);
            return;
        }

        var pcamScript = GD.Load<GDScript>(
            "res://addons/phantom_camera/scripts/phantom_camera/phantom_camera_3d.gd");
        var pcamNode = (Node3D)pcamScript.New();
        pcamNode.Name = $"PCam_{spec.CameraId}";

        var position = new Vector3(spec.Position.X, spec.Position.Y, spec.Position.Z);
        var lookAt = new Vector3(spec.LookAt.X, spec.LookAt.Y, spec.LookAt.Z);
        if (!SafeLookAt(pcamNode, position, lookAt))
        {
            _log.LogWarning(
                "Camera '{CameraId}' position and look-at are colinear with Up; " +
                "rotation left unchanged.", spec.CameraId);
        }

        var pcam = pcamNode.AsPhantomCamera3D();
        pcam.Priority = 0;
        pcam.HostLayers = _rigs[spec.ViewportId].LayerBit;
        ApplyCameraResource(pcam, spec);

        var entry = new CameraEntry(pcamNode, pcam, spec);
        if (_pendingGlobeOrbit.Remove(spec.CameraId, out var preReadyGlobeOrbit))
            ApplyGlobeOrbitConfiguration(spec.CameraId, entry, preReadyGlobeOrbit);

        _rigs[spec.ViewportId].Root.AddChild(pcamNode);
        _cameras[spec.CameraId] = entry;
        _log.LogInformation(
            "Camera registered: {CameraId} (viewport = {ViewportId}, position = {Position}).",
            spec.CameraId, spec.ViewportId, spec.Position);
    }

    private void ActivateImpl(string cameraId)
    {
        if (!_cameras.TryGetValue(cameraId, out var targetEntry))
        {
            _log.LogWarning("Activate ignored: unknown camera id '{CameraId}'.", cameraId);
            return;
        }

        var viewportId = targetEntry.Spec.ViewportId;
        foreach (var entry in _cameras.Values.Where(c => c.Spec.ViewportId == viewportId))
        {
            entry.Wrapper.Priority = 0;
        }

        targetEntry.Wrapper.Priority = 10;
        _log.LogInformation(
            "Camera activated: {CameraId} (viewport = {ViewportId}).",
            cameraId, viewportId);
    }

    private void UnregisterImpl(string cameraId)
    {
        if (!_cameras.TryGetValue(cameraId, out var entry))
            return;

        var viewportId = entry.Spec.ViewportId;
        _cameras.Remove(cameraId);
        _pendingGlobeOrbit.Remove(cameraId);
        entry.Node.QueueFree();
        _log.LogInformation("Camera unregistered: {CameraId}.", cameraId);

        if (viewportId != "main" && !_cameras.Values.Any(c => c.Spec.ViewportId == viewportId))
        {
            if (_rigs.TryGetValue(viewportId, out var rig))
            {
                if (rig.Panel is not null)
                {
                    rig.Panel.QueueFree();
                }
                else
                {
                    rig.Root.QueueFree();
                }

                _rigs.Remove(viewportId);

                if (rig.LayerBit != 1)
                {
                    _freedLayerBits.Push(rig.LayerBit);
                }

                _log.LogInformation(
                    "Viewport rig torn down: {ViewportId} (layer bit {LayerBit} reclaimed).",
                    viewportId, rig.LayerBit);
            }
        }
    }

    /// <summary>
    /// Configure a registered camera as a globe orbit camera: the addon's ThirdPerson follow mode
    /// with <paramref name="followTarget"/> at the globe origin, spring length clamped to
    /// [<paramref name="minSpring"/>, <paramref name="maxSpring"/>]. Must be called on the main
    /// thread (the composition root calls it from a deferred Callable, mirroring Register/Activate).
    /// </summary>
    public void ConfigureGlobeOrbit(
        string cameraId,
        Node3D followTarget,
        float initialSpringLength,
        float minSpring,
        float maxSpring)
    {
        if (followTarget is null) throw new ArgumentNullException(nameof(followTarget));

        var configuration = new GlobeOrbitConfiguration(
            followTarget,
            initialSpringLength,
            minSpring,
            maxSpring);

        if (!_cameras.ContainsKey(cameraId))
        {
            _pendingGlobeOrbit[cameraId] = configuration;
            _log.LogInformation(
                "ConfigureGlobeOrbit pending: camera id '{CameraId}' is not registered yet.",
                cameraId);
            return;
        }

        ApplyGlobeOrbitConfiguration(cameraId, configuration);
    }

    private void ApplyGlobeOrbitConfiguration(string cameraId, GlobeOrbitConfiguration configuration)
    {
        if (!_cameras.TryGetValue(cameraId, out var entry))
            return;

        ApplyGlobeOrbitConfiguration(cameraId, entry, configuration);
    }

    private void ApplyGlobeOrbitConfiguration(
        string cameraId,
        CameraEntry entry,
        GlobeOrbitConfiguration configuration)
    {
        // Keep the follow target node alive under the rig root before assigning it. The addon's
        // ThirdPerson _ready path reads the target and builds its SpringArm3D from this state.
        if (configuration.FollowTarget.GetParent() is null)
            _rigs[entry.Spec.ViewportId].Root.AddChild(configuration.FollowTarget);

        // follow_mode is an @export int on the GDScript pcam; set it via Node.Set so the addon's
        // setter marks ThirdPerson state before _ready builds the SpringArm3D.
        const int followModeThirdPerson = (int)FollowMode3D.ThirdPerson;
        entry.Node.Set("follow_mode", followModeThirdPerson);
        entry.Node.Set("follow_target", configuration.FollowTarget);
        entry.Wrapper.SpringLength = Math.Clamp(
            configuration.InitialSpringLength,
            configuration.MinSpring,
            configuration.MaxSpring);

        _log.LogInformation(
            "Camera '{CameraId}' configured for globe orbit (follow target = {TargetName}, spring = {Spring}).",
            cameraId, configuration.FollowTarget.Name, configuration.InitialSpringLength);
        _log.LogInformation(
            "Globe orbit follow engaged: cameraId={CameraId} followTarget={FollowTarget}.",
            cameraId, configuration.FollowTarget.Name);
    }

    /// <summary>
    /// Return the <see cref="PhantomCameraHost"/> the rig built for <paramref name="viewportId"/>,
    /// or null if that viewport has no rig yet. The orbit controls bind to this host to read the
    /// active pcam each interaction.
    /// </summary>
    public PhantomCameraHost? GetHost(string viewportId = "main")
        => _rigs.TryGetValue(viewportId, out var rig) ? rig.HostWrapper : null;

    /// <summary>
    /// Live rig introspection for the `camera.debug` ingress command: reports the real camera,
    /// the phantom host, the manager autoload, and every registered pcam's addon-visible state.
    /// Marshals onto the Godot main thread like every other rig operation, so it is safe to call
    /// from a command handler thread.
    /// </summary>
    public Task<string> DebugSnapshotAsync(string viewportId = "main")
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Callable.From(() =>
        {
            try { tcs.TrySetResult(DebugSnapshotImpl(viewportId)); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }).CallDeferred();
        return tcs.Task;
    }

    private string DebugSnapshotImpl(string viewportId)
    {
        var snapshot = new Godot.Collections.Dictionary();
        snapshot["viewportId"] = viewportId;

        if (!_rigs.TryGetValue(viewportId, out var rig))
        {
            snapshot["rigExists"] = false;
            return Json.Stringify(snapshot, "  ");
        }

        snapshot["rigExists"] = true;
        snapshot["layerBit"] = rig.LayerBit;
        snapshot["cameraInTree"] = rig.Camera.IsInsideTree();
        snapshot["cameraCurrent"] = rig.Camera.Current;
        snapshot["cameraGlobalPosition"] = rig.Camera.IsInsideTree() ? rig.Camera.GlobalPosition : Vector3.Zero;
        snapshot["hostInTree"] = rig.Host.IsInsideTree();
        snapshot["hostLayers"] = (int)rig.Host.Get("host_layers");
        snapshot["hostProcessMode"] = (int)rig.Host.ProcessMode;

        var tree = _parent.GetTree();
        var manager = tree?.Root.GetNodeOrNull("PhantomCameraManager");
        snapshot["managerAutoloadPresent"] = manager is not null;
        if (manager is not null)
        {
            // The addon manager tracks registered pcams/hosts in private arrays; read them for truth.
            snapshot["managerPcamCount"] = ((Godot.Collections.Array)manager.Call("get_phantom_camera_3ds")).Count;
            snapshot["managerHostCount"] = ((Godot.Collections.Array)manager.Call("get_phantom_camera_hosts")).Count;
        }

        var activePcam = rig.HostWrapper.GetActivePhantomCamera();
        snapshot["hostActivePcam"] = activePcam is PhantomCamera3D active ? active.Node3D.Name.ToString() : "(none)";

        var pcams = new Godot.Collections.Array();
        foreach (var (id, entry) in _cameras)
        {
            var p = new Godot.Collections.Dictionary();
            p["id"] = id;
            p["inTree"] = entry.Node.IsInsideTree();
            p["globalPosition"] = entry.Node.IsInsideTree() ? entry.Node.GlobalPosition : Vector3.Zero;
            p["visible"] = entry.Node.Visible;
            p["priority"] = (int)entry.Node.Get("priority");
            p["hostLayers"] = (int)entry.Node.Get("host_layers");
            p["followMode"] = (int)entry.Node.Get("follow_mode");
            p["followTarget"] = entry.Node.Get("follow_target").Obj is Node3D t ? t.Name.ToString() : "(null)";
            p["springLength"] = (float)entry.Node.Get("spring_length");
            p["isActive"] = (bool)entry.Node.Call("is_active");
            pcams.Add(p);
        }
        snapshot["pcams"] = pcams;

        return Json.Stringify(snapshot, "  ");
    }

    private void EnsureViewportRig(string viewportId)
    {
        if (_rigs.ContainsKey(viewportId))
            return;

        int layerBit;
        bool fallbackToRoot = false;

        if (viewportId == "main")
        {
            layerBit = 1;
        }
        else
        {
            if (_freedLayerBits.Count > 0)
            {
                layerBit = _freedLayerBits.Pop();
            }
            else if (_nextLayerIndex <= 19)
            {
                layerBit = 1 << _nextLayerIndex;
                _nextLayerIndex++;
            }
            else
            {
                _log.LogError(
                    "concurrent viewport limit (20) reached; rig falls back to the root viewport");
                layerBit = 1;
                fallbackToRoot = true;
            }
        }

        Node rigParent;
        SubViewport? subViewport = null;
        SubViewportContainer? panel = null;

        if (viewportId == "main" || fallbackToRoot)
        {
            rigParent = _parent;
        }
        else if (_panelHost is null)
        {
            _log.LogWarning(
                "Viewport '{ViewportId}' is not 'main' and no panel host was provided; " +
                "creating the rig in the root viewport instead of a SubViewport panel.", viewportId);
            rigParent = _parent;
        }
        else
        {
            panel = new SubViewportContainer
            {
                Name = $"CameraPanel_{viewportId}",
                Stretch = true,
                CustomMinimumSize = new Vector2(480, 270)
            };
            _panelHost.AddChild(panel);

            subViewport = new SubViewport
            {
                Name = $"Viewport_{viewportId}",
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            panel.AddChild(subViewport);
            rigParent = subViewport;
        }

        var rigRoot = new Node3D { Name = $"CameraRig_{viewportId}" };
        rigParent.AddChild(rigRoot);

        var camera = new Camera3D { Name = "Camera3D" };
        rigRoot.AddChild(camera);
        camera.MakeCurrent();

        var hostScript = GD.Load<GDScript>(
            "res://addons/phantom_camera/scripts/phantom_camera_host/phantom_camera_host.gd");
        var hostNode = (Node)hostScript.New();
        hostNode.Name = "PhantomCameraHost";

        var hostWrapper = hostNode.AsPhantomCameraHost();
        hostWrapper.HostLayers = layerBit;
        camera.AddChild(hostNode);

        _rigs[viewportId] = new ViewportRig(
            rigRoot, camera, hostNode, hostWrapper, subViewport, panel, layerBit);
        _log.LogInformation(
            "Viewport rig created: {ViewportId} (layer bit = {LayerBit}).",
            viewportId, layerBit);
    }

    private static bool SafeLookAt(Node3D pcamNode, Vector3 pos, Vector3 target)
    {
        var dir = target - pos;
        bool isColinear = dir.LengthSquared() < 0.0001f
            || (Mathf.Abs(dir.X) < 0.0001f && Mathf.Abs(dir.Z) < 0.0001f);

        if (isColinear)
        {
            pcamNode.GlobalPosition = pos;
            return false;
        }

        pcamNode.LookAtFromPosition(pos, target, Vector3.Up);
        return true;
    }

    private void ApplyCameraResource(PhantomCamera3D pcam, CameraSpec spec)
    {
        var resource = Camera3DResource.New();
        resource.Fov = spec.FieldOfViewDegrees;
        resource.CullMask = ComputeCullMask(spec.VisibleCategories);
        pcam.Camera3DResource = resource;
    }

    private int ComputeCullMask(IReadOnlyList<string>? categories)
    {
        if (categories is null)
            return 1048575;

        int mask = 0;
        foreach (var category in categories)
        {
            if (!_categoryLayers.TryGetValue(category, out var index))
            {
                _log.LogWarning(
                    "Unknown visible category '{Category}' in camera spec; skipping.",
                    category);
                continue;
            }

            if (index < 1 || index > 20)
            {
                _log.LogWarning(
                    "Visible category '{Category}' maps to render layer index {Index} " +
                    "which is outside 1..20; skipping.",
                    category, index);
                continue;
            }

            mask |= 1 << (index - 1);
        }

        if (mask == 0)
        {
            _log.LogWarning(
                "Camera cull mask resolved to 0 (no known categories); defaulting to see everything.");
            return 1048575;
        }

        return mask;
    }
}
