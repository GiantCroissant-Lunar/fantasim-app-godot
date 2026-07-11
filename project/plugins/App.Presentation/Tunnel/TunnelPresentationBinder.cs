using System;
using System.Collections.Generic;
using FantaSim.App.Resource;
using FantaSim.App.Resource.Bundle;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using ResourceService = FantaSim.App.Resource.IService;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>
/// 3D tunnel timeline binder (slice 1, vault/plans/2026-07-11-tunnel-slice1-plan.md Tasks 7-11).
/// Lives in the world bundle (same assembly as PlanetPresentationBinder, spec Decision Point 1 /
/// plan's "placement decision"), mirrors PlanetPresentationBinder's lifecycle discipline: a plain
/// C# class (not a Node) so it survives ALC-unload symmetry, RuntimeChanging/RuntimeChanged
/// subscriptions on the resident IService for its own teardown/rebind (same "world" bundle-id
/// gate), execution-time TryGet resolution of ITimelineController/ILayerTrackRegistry (never
/// cached past a reload).
///
/// Task 2's shared-globe spike verdict was INCONCLUSIVE (architecture favors sharing, empirical
/// windowed check deferred) -- per the plan's own fallback rule this binder ships the EMPTY-THROAT
/// path: the tunnel's ThroatRadius is simply an empty hole with the Stage's own globe optionally
/// visible through it (whatever the Stage scene already renders at that world position), never a
/// second bound globe. TODO(tunnel slice 2, vault/specs/2026-07-11-tunnel-timeline-design.md
/// Decision Point 2 / vault/plans/2026-07-11-tunnel-slice1-plan.md Task 2): wire a SubViewport +
/// Camera3D aimed at PlanetPresentationBinder.ActiveRoot's world transform once the shared-node
/// approach is empirically verified in the windowed app.
/// </summary>
internal sealed partial class TunnelPresentationBinder : ITunnelPresentation
{
    private const string StageBundleId = "stage";
    private const string WorldBundleId = "world";
    private static readonly NodePath StageEnvironmentPath = new("Environment");

    // Flat-annulus tunnel geometry + face-on framing shared by Rings.cs, Corridors.cs, and
    // Camera.cs: every ring/wedge/quad is a sector of an annulus in the mount's local XY plane
    // (normal +Z), radius = "depth" per TunnelDepthMapper -- spec §2.1/§2.2's "no new domain math"
    // extended to rendering. Slice-1 placeholder magnitudes (spec Decision Point 12: real tuning is
    // a later eye-judged pass); the lead iterates these constants by hot-reloading the world bundle
    // in the windowed app. TunnelCameraDistance/FovDeg frame the annulus face-on (Camera.cs) so it
    // reads as a radial dartboard with the globe visible through the throat hole, instead of being
    // swallowed by the orbit rig's ~6.5-unit eye distance.
    private const float ThroatRadius = 2.5f;
    private const float OuterRadius = 10.0f;
    private const float TunnelCameraDistance = 16.0f;
    private const float TunnelCameraFovDeg = 55.0f;

    private readonly IRegistry _registry;
    private readonly IBundleSceneRegistry _sceneRegistry;
    private readonly ResourceService _resource;
    private readonly ILogger _log;
    private readonly FilmstripPreviewController _filmstrip;
    private readonly PlanetPresentationReloadGate _worldRuntimeReload = new();

    private ITimelineController? _ctl;
    private ILayerTrackRegistry? _layerTrackRegistry;
    private Node3D? _mount;
    private TunnelInputRelay? _inputRelay;
    private bool _enabled;
    private bool _builtOnce;
    private bool _disposed;

    public TunnelPresentationBinder(
        IRegistry registry,
        IBundleSceneRegistry sceneRegistry,
        ILoggerFactory loggerFactory)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sceneRegistry = sceneRegistry ?? throw new ArgumentNullException(nameof(sceneRegistry));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));

        _log = loggerFactory.CreateLogger("World.TunnelPresentation");
        // No ResourceService parameter on this binder's ctor (unlike PlanetPresentationBinder's --
        // see PresentationComposition.CreateTunnelPresentation's doc comment): resolved here via
        // Get<T>, matching PresentationPlugin.CreateDefault's resolution of the SAME resident
        // service for the planet binder (App.Resource is a permanent shared-assembly-policy floor,
        // always registered before this bundle loads).
        _resource = _registry.Get<ResourceService>();

        // The aliveness predicate matches FilmstripPreviewController's own doc comment: check
        // BEFORE any Godot call so a deferred completion firing after teardown bails cleanly.
        _filmstrip = new FilmstripPreviewController(
            isFaceAlive: () => _mount is not null && GodotObject.IsInstanceValid(_mount) && _mount.IsInsideTree(),
            deferToMainThread: action => Callable.From(action).CallDeferred(),
            log: _log);

        _resource.RuntimeChanging += OnResourceRuntimeChanging;
        _resource.RuntimeChanged += OnResourceRuntimeChanged;
    }

    public bool IsEnabled => _enabled;

    public void Rebind()
    {
        if (_disposed)
            return;

        var controller = _registry.TryGet<ITimelineController>();
        if (controller is null)
        {
            _log.LogWarning("Tunnel presentation skipped: ITimelineController is not registered.");
            return;
        }

        BindController(controller);
        BindLayerTrackRegistry(_registry.TryGet<ILayerTrackRegistry>());
        _filmstrip.SetPreviewProvider((request, ct) =>
            _registry.TryGet<FantaSim.App.World.IService>()?.GetLayerFilmstripPreview(request, ct));

        Callable.From(EnsureMounted).CallDeferred();
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || _enabled == enabled)
            return;

        _enabled = enabled;
        if (_mount is not null && GodotObject.IsInstanceValid(_mount))
        {
            _mount.Visible = enabled;
            if (enabled)
                ActivateTunnelCamera();
            else
                RestorePreviousCamera();
            TryBuildOnce();
        }
        // else: _mount is still pending its deferred EnsureMounted call, which reads _enabled
        // itself once the mount exists -- camera activation is handled there too.
    }

    private void TryBuildOnce()
    {
        if (!_enabled || _builtOnce)
            return;

        _builtOnce = true;
        RebuildRings();
        RebuildCorridors();
    }

    private void EnsureMounted()
    {
        if (_disposed || _mount is not null)
            return;

        var stageEnvironment = _sceneRegistry.GetNodeOrNull(StageBundleId, StageEnvironmentPath) as Node3D;
        if (stageEnvironment is null)
        {
            _log.LogWarning("Tunnel presentation skipped: stage Environment node not found at {Path}.", StageEnvironmentPath);
            return;
        }

        _mount = new Node3D { Name = "TunnelMount", Visible = _enabled };
        stageEnvironment.AddChild(_mount);

        // Mounted UNCONDITIONALLY (even while Visible=false / SetEnabled(false)) so the debug
        // keybind (Task 11) always works regardless of the tunnel's current show/hide state --
        // Godot's _UnhandledInput fires on invisible-but-in-tree nodes.
        _inputRelay = new TunnelInputRelay { Name = "TunnelInputRelay", OnEvent = HandleInputEvent };
        _mount.AddChild(_inputRelay);

        EnsureTunnelCamera();
        // Deferred-mount case: SetEnabled(true) ran before the mount existed -- activate now that
        // the camera exists, same path SetEnabled takes when the mount is already present.
        if (_enabled)
            ActivateTunnelCamera();

        _log.LogInformation("Tunnel presentation mounted under stage Environment (visible={Visible}).", _enabled);
        _worldRuntimeReload.MarkMounted();

        TryBuildOnce();
    }

    private void BindController(ITimelineController? controller)
    {
        if (ReferenceEquals(_ctl, controller))
            return;

        UnsubscribeController();
        _ctl = controller;
        if (_ctl is not null)
            _ctl.TickChanged += OnTickChanged;
    }

    private void UnsubscribeController()
    {
        if (_ctl is not null)
            _ctl.TickChanged -= OnTickChanged;
        _ctl = null;
    }

    // Rebind-safe, mirrors TimelineFace.BindLayerTrackRegistry/UnsubscribeLayerTrackRegistry
    // verbatim (plan Task 8 Step 1): swap only when the instance actually changed, always
    // unsubscribe the OLD instance first so a recompose never leaves two live subscriptions.
    private void BindLayerTrackRegistry(ILayerTrackRegistry? registry)
    {
        if (ReferenceEquals(_layerTrackRegistry, registry))
            return;

        UnsubscribeLayerTrackRegistry();
        _layerTrackRegistry = registry;
        if (_layerTrackRegistry is not null)
            _layerTrackRegistry.Changed += OnRegistryChanged;
    }

    private void UnsubscribeLayerTrackRegistry()
    {
        if (_layerTrackRegistry is not null)
            _layerTrackRegistry.Changed -= OnRegistryChanged;
        _layerTrackRegistry = null;
    }

    private void OnResourceRuntimeChanging(object? sender, ResourceRuntimeChangingEventArgs args)
    {
        if (!string.Equals(args.BundleId, WorldBundleId, StringComparison.OrdinalIgnoreCase))
            return;

        UnsubscribeController();
        UnsubscribeLayerTrackRegistry();
        SeverFilmstrip();
        _worldRuntimeReload.MarkRuntimeChanging();
        Callable.From(ClearMount).CallDeferred();
        _log.LogInformation("Tunnel presentation released before resource {Operation}: {BundleId}", args.Operation, args.BundleId);
    }

    private void OnResourceRuntimeChanged(object? sender, EventArgs args)
    {
        if (_disposed || !_worldRuntimeReload.TryScheduleDeferredAttempt())
            return;

        Callable.From(TryRebindAfterWorldRuntimeChange).CallDeferred();
    }

    private void TryRebindAfterWorldRuntimeChange()
    {
        _worldRuntimeReload.CompleteDeferredAttempt();
        if (_disposed || !_worldRuntimeReload.IsPending || !_resource.IsLoaded(WorldBundleId))
            return;

        // Defensive safety net mirroring PlanetPresentationBinder's own pattern -- in normal
        // operation Host.BindPlanetPresentation (Task 7 Step 4) already re-resolves and re-Rebind()s
        // this binder's NEXT-generation instance after every world reload, so this path is a
        // redundant catch for any RuntimeChanged the host-level hook misses.
        Rebind();
    }

    private void SeverFilmstrip()
    {
        _filmstrip.Supersede();
        _filmstrip.SetPreviewProvider(null);
        _filmstrip.CancelInFlight();
    }

    private void ClearMount()
    {
        // Restore the viewport BEFORE freeing the mount -- the tunnel camera is a child of _mount
        // and must never be left as the current camera pointing at freed geometry (ALC-unload safe).
        RestorePreviousCamera();

        ClearRingRoots();
        ClearCorridorsRoot();
        ClearTunnelCamera();

        if (_mount is not null && GodotObject.IsInstanceValid(_mount))
        {
            _mount.GetParent()?.RemoveChild(_mount);
            _mount.QueueFree();
        }

        _mount = null;
        _inputRelay = null;
        _builtOnce = false;
    }

    private static void ClearChildren(Node3D root)
    {
        foreach (var child in root.GetChildren())
        {
            root.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string SafeNodeName(string value)
    {
        var chars = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            chars[i] = char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_';
        }

        var name = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(name) ? "Track" : name;
    }

    // Shared flat annulus-sector mesh builder: ladder/current-tick rings (Rings.cs, full 360deg
    // spans) and corridor wall panels (Corridors.cs, wedge spans) are both flat sectors of one
    // annulus in the mount's local XY plane (normal +Z) -- one mesh shape, several callers, per
    // TunnelDepthMapper/TunnelCorridorLayout's own "no new domain math" discipline extended to
    // rendering. BuildUnlitMaterial below sets CullMode.Disabled on every ring/wedge material
    // (PlateBoundaryFocusRenderer precedent: thin flat geometry viewed from an unverified camera
    // angle stays double-sided rather than assuming which side faces the camera).
    private static ArrayMesh? BuildAnnulusSectorMesh(
        double startAngleDeg, double spanAngleDeg, float innerRadius, float outerRadius, double angularStepDeg = 6.0)
    {
        if (spanAngleDeg <= 0.0 || outerRadius <= innerRadius)
            return null;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normal = new Vector3(0f, 0f, 1f);

        var steps = Math.Max(1, (int)Math.Ceiling(spanAngleDeg / angularStepDeg));
        var stepDeg = spanAngleDeg / steps;

        for (var i = 0; i < steps; i++)
        {
            var a0 = Math.PI / 180.0 * (startAngleDeg + (stepDeg * i));
            var a1 = Math.PI / 180.0 * (startAngleDeg + (stepDeg * (i + 1)));

            var inner0 = new Vector3((float)(Math.Cos(a0) * innerRadius), (float)(Math.Sin(a0) * innerRadius), 0f);
            var outer0 = new Vector3((float)(Math.Cos(a0) * outerRadius), (float)(Math.Sin(a0) * outerRadius), 0f);
            var inner1 = new Vector3((float)(Math.Cos(a1) * innerRadius), (float)(Math.Sin(a1) * innerRadius), 0f);
            var outer1 = new Vector3((float)(Math.Cos(a1) * outerRadius), (float)(Math.Sin(a1) * outerRadius), 0f);

            var v0 = (float)i / steps;
            var v1 = (float)(i + 1) / steps;

            // Triangle winding (inner0, outer0, outer1) / (inner0, outer1, inner1) is CCW as seen
            // from +Z for increasing angle -- matches the +Z normal above (verified by hand for the
            // standard cos/sin XY-plane convention: a positive angle step gives a positive Z cross
            // product between consecutive edge vectors).
            vertices.Add(inner0); vertices.Add(outer0); vertices.Add(outer1);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(new Vector2(0f, v0)); uvs.Add(new Vector2(1f, v0)); uvs.Add(new Vector2(1f, v1));

            vertices.Add(inner0); vertices.Add(outer1); vertices.Add(inner1);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(new Vector2(0f, v0)); uvs.Add(new Vector2(1f, v1)); uvs.Add(new Vector2(0f, v1));
        }

        return BuildMeshFromArrays(vertices, normals, uvs);
    }

    private static ArrayMesh BuildQuadMesh(Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3)
    {
        var normal = new Vector3(0f, 0f, 1f);
        var vertices = new List<Vector3> { q0, q1, q2, q0, q2, q3 };
        var normals = new List<Vector3> { normal, normal, normal, normal, normal, normal };
        var uvs = new List<Vector2>
        {
            new(0f, 1f), new(1f, 1f), new(1f, 0f),
            new(0f, 1f), new(1f, 0f), new(0f, 0f),
        };
        return BuildMeshFromArrays(vertices, normals, uvs);
    }

    private static ArrayMesh BuildMeshFromArrays(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static StandardMaterial3D BuildUnlitMaterial(Color color, float alpha = 1f)
    {
        var tinted = new Color(color.R, color.G, color.B, alpha);
        return new StandardMaterial3D
        {
            AlbedoColor = tinted,
            Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = tinted,
            EmissionEnergyMultiplier = 0.6f,
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _resource.RuntimeChanging -= OnResourceRuntimeChanging;
        _resource.RuntimeChanged -= OnResourceRuntimeChanged;
        UnsubscribeController();
        UnsubscribeLayerTrackRegistry();
        SeverFilmstrip();
        _filmstrip.DisposeCache();
        _filmstrip.Dispose();
        ClearMount();
    }
}
