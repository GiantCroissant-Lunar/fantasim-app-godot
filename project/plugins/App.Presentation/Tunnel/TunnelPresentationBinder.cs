using System;
using System.Collections.Generic;
using FantaSim.App.Resource;
using FantaSim.App.Resource.Bundle;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using ResourceService = FantaSim.App.Resource.IService;

namespace FantaSim.App.Presentation.Tunnel;

internal sealed partial class TunnelPresentationBinder : ITunnelPresentation
{
    private const string StageBundleId = "stage";
    private const string WorldBundleId = "world";
    private static readonly NodePath StageEnvironmentPath = new("Environment");

    private const float TunnelRadius = 8.0f;
    private const float TunnelDepth = 14.0f;
    private const float MouthZ = 0.0f;
    private const float ThroatZ = -TunnelDepth;
    private const float InnerRingInnerRadius = 8.15f;
    private const float InnerRingOuterRadius = 8.85f;
    private const float OuterRingInnerRadius = 9.05f;
    private const float OuterRingOuterRadius = 10.0f;
    private const float CorridorSurfaceRadius = TunnelRadius - 0.06f;
    private const double CorridorSpanDegrees = 24.0;
    private const int FilmstripFramesPerCorridor = 4;
    private const float FineRailCenterZ = -TunnelDepth / 2.0f;
    private const float FineRailHalfLength = 2.5f;
    private const float TunnelCameraFovDeg = 55.0f;

    private static readonly Vector3 TunnelCameraLocalPosition = new(3.5f, 2.0f, 22.0f);
    private static readonly Vector3 TunnelCameraLocalTarget = new(0.0f, -1.0f, -7.0f);

    private readonly IRegistry _registry;
    private readonly IBundleSceneRegistry _sceneRegistry;
    private readonly ResourceService _resource;
    private readonly ILogger _log;
    private readonly FilmstripPreviewController _filmstrip;
    private readonly Func<Node3D?> _planetBodyProvider;
    private readonly PlanetPresentationReloadGate _worldRuntimeReload = new();

    private ITimelineController? _ctl;
    private ILayerTrackRegistry? _layerTrackRegistry;
    private Node3D? _mount;
    private TunnelInputRelay? _inputRelay;
    private bool _enabled;
    private bool _disposed;
    private int _generation;

    private int _focusIndex = -1;
    private IReadOnlyList<LayerTrackDescriptor> _sourceTracks = Array.Empty<LayerTrackDescriptor>();

    private double _lastPointerAngleDeg;
    private TunnelFineTrackBinding _fineBinding;
    private TunnelFinePreview _finePreview;
    private bool _pendingCorridorRebuild;

    public TunnelPresentationBinder(
        IRegistry registry,
        IBundleSceneRegistry sceneRegistry,
        ILoggerFactory loggerFactory,
        Func<Node3D?> planetBodyProvider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sceneRegistry = sceneRegistry ?? throw new ArgumentNullException(nameof(sceneRegistry));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
        _planetBodyProvider = planetBodyProvider ?? throw new ArgumentNullException(nameof(planetBodyProvider));

        _log = loggerFactory.CreateLogger("World.TunnelPresentation");
        _resource = _registry.Get<ResourceService>();

        _fineBinding = TunnelFinePreviewMapper.Bind(null, false, TunnelScrubMapper.ResolveOuterRung());
        _finePreview = TunnelFinePreviewMapper.Reset(_fineBinding, FineRailCenterZ, FineRailHalfLength);

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

        _generation++;
        Callable.From(EnsureMounted).CallDeferred();
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || _enabled == enabled)
            return;

        _enabled = enabled;
        if (!enabled)
        {
            CancelTunnelGesture("disabled");
            ResetFinePreview(TunnelFineResetReason.Disabled);
        }

        if (_mount is not null && GodotObject.IsInstanceValid(_mount))
        {
            _mount.Visible = enabled;
            if (enabled)
                ActivateTunnelCamera();
            else
                RestorePreviousCamera();
            TryBuildOnce();
        }
    }

    private void TryBuildOnce()
    {
        if (!_enabled || _mount is null || !GodotObject.IsInstanceValid(_mount))
            return;

        ResolveSourceTracks();
        RebuildTwoRingControls();
        RebuildCorridors();
        UpdateRingLabels();
    }

    private void EnsureMounted()
    {
        if (_disposed || _mount is not null)
            return;

        var gen = _generation;
        var stageEnvironment = _sceneRegistry.GetNodeOrNull(StageBundleId, StageEnvironmentPath) as Node3D;
        if (stageEnvironment is null)
        {
            _log.LogWarning("Tunnel presentation skipped: stage Environment node not found at {Path}.", StageEnvironmentPath);
            return;
        }

        _mount = new Node3D { Name = "TunnelMount", Visible = _enabled };
        stageEnvironment.AddChild(_mount);
        AlignToPlanetBody(gen);

        _inputRelay = new TunnelInputRelay
        {
            Name = "TunnelInputRelay",
            OnInput = e => HandleInputEvent(e),
            OnProcess = d => ConsumeTunnelFrame(d),
            OnCancel = r => CancelTunnelGesture(r),
        };
        _mount.AddChild(_inputRelay);

        EnsureTunnelCamera();
        if (_enabled)
            ActivateTunnelCamera();

        BuildDarkShell();

        _log.LogInformation("Tunnel presentation mounted under stage Environment (visible={Visible}).", _enabled);
        _worldRuntimeReload.MarkMounted();

        TryBuildOnce();
    }

    private void AlignToPlanetBody(int gen)
    {
        if (_disposed || gen != _generation || _mount is null || !GodotObject.IsInstanceValid(_mount))
            return;

        var body = _planetBodyProvider();
        if (body is null || !GodotObject.IsInstanceValid(body))
        {
            _log.LogWarning("Tunnel alignment degraded: PlanetBody provider returned null; shell remains mounted, throat is empty.");
            return;
        }

        _mount.GlobalPosition = body.GlobalPosition + Vector3.Back * TunnelDepth;
        _mount.GlobalBasis = Basis.Identity;
    }

    private void ResolveSourceTracks()
    {
        if (_layerTrackRegistry is null)
        {
            _sourceTracks = Array.Empty<LayerTrackDescriptor>();
            _focusIndex = -1;
            return;
        }

        _sourceTracks = TunnelCorridorLayout.SelectSourceTracks(_layerTrackRegistry.Current);
        _focusIndex = TunnelCorridorLayout.InitialFocusIndex(_sourceTracks.Count);
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

        _generation++;
        CancelTunnelGesture("bundle_teardown");
        ResetFinePreview(TunnelFineResetReason.BundleTeardown);
        SeverManagedInputCallbacks();
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

        Rebind();
    }

    private void SeverFilmstrip()
    {
        _filmstrip.Supersede();
        _filmstrip.SetPreviewProvider(null);
        _filmstrip.CancelInFlight();
    }

    private void SeverManagedInputCallbacks()
    {
        if (_inputRelay is not null && GodotObject.IsInstanceValid(_inputRelay))
        {
            _inputRelay.OnInput = null;
            _inputRelay.OnProcess = null;
            _inputRelay.OnCancel = null;
        }
    }

    private void ClearMount()
    {
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

    private static Vector3 CalculateNormal(Vector3 a, Vector3 b, Vector3 c)
        => (b - a).Cross(c - a).Normalized();

    private static ArrayMesh? BuildPlanarAnnulusSectorMesh(
        double startAngleDeg, double spanAngleDeg,
        float innerRadius, float outerRadius, float z,
        double angularStepDeg = 3.0)
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
            var a0 = Mathf.DegToRad((float)(startAngleDeg + (stepDeg * i)));
            var a1 = Mathf.DegToRad((float)(startAngleDeg + (stepDeg * (i + 1))));

            var inner0 = new Vector3(Mathf.Cos(a0) * innerRadius, Mathf.Sin(a0) * innerRadius, z);
            var outer0 = new Vector3(Mathf.Cos(a0) * outerRadius, Mathf.Sin(a0) * outerRadius, z);
            var inner1 = new Vector3(Mathf.Cos(a1) * innerRadius, Mathf.Sin(a1) * innerRadius, z);
            var outer1 = new Vector3(Mathf.Cos(a1) * outerRadius, Mathf.Sin(a1) * outerRadius, z);

            vertices.Add(inner0); vertices.Add(outer0); vertices.Add(outer1);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 1f));

            vertices.Add(inner0); vertices.Add(outer1); vertices.Add(inner1);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
        }

        return BuildMeshFromArrays(vertices, normals, uvs);
    }

    private static ArrayMesh? BuildCylinderSectorMesh(
        double startAngleDeg, double spanAngleDeg,
        float radius, float nearZ, float farZ,
        double angularStepDeg = 3.0)
    {
        if (spanAngleDeg <= 0.0 || Math.Abs(farZ - nearZ) < 0.001f)
            return null;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        var steps = Math.Max(1, (int)Math.Ceiling(spanAngleDeg / angularStepDeg));
        var stepDeg = spanAngleDeg / steps;

        for (var i = 0; i < steps; i++)
        {
            var a0 = Mathf.DegToRad((float)(startAngleDeg + (stepDeg * i)));
            var a1 = Mathf.DegToRad((float)(startAngleDeg + (stepDeg * (i + 1))));

            var n0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
            var n1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);

            var near0 = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, nearZ);
            var near1 = new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, nearZ);
            var far0 = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, farZ);
            var far1 = new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, farZ);

            // Inward-facing normals point toward the axis (negate the outward radial).
            var inN0 = -n0;
            var inN1 = -n1;

            vertices.Add(near0); vertices.Add(far0); vertices.Add(far1);
            normals.Add(inN0); normals.Add(inN0); normals.Add(inN1);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f));

            vertices.Add(near0); vertices.Add(far1); vertices.Add(near1);
            normals.Add(inN0); normals.Add(inN1); normals.Add(inN1);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));
        }

        return BuildMeshFromArrays(vertices, normals, uvs);
    }

    private void BuildDarkShell()
    {
        if (_mount is null)
            return;

        var shellRoot = _mount.GetNodeOrNull<Node3D>("DarkShell");
        if (shellRoot is not null)
            return;

        shellRoot = new Node3D { Name = "DarkShell" };
        _mount.AddChild(shellRoot);

        var shellMesh = BuildCylinderSectorMesh(0.0, 360.0, TunnelRadius, MouthZ, ThroatZ, angularStepDeg: 6.0);
        if (shellMesh is null)
            return;

        var shell = new MeshInstance3D
        {
            Name = "Shell",
            Mesh = shellMesh,
            MaterialOverride = BuildUnlitMaterial(new Color(0.04f, 0.05f, 0.07f)),
        };
        shellRoot.AddChild(shell);
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
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = tinted,
            EmissionEnergyMultiplier = 0.6f,
            NoDepthTest = false,
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _generation++;
        CancelTunnelGesture("disposed");
        ResetFinePreview(TunnelFineResetReason.Disposed);
        _resource.RuntimeChanging -= OnResourceRuntimeChanging;
        _resource.RuntimeChanged -= OnResourceRuntimeChanged;
        SeverManagedInputCallbacks();
        UnsubscribeController();
        UnsubscribeLayerTrackRegistry();
        SeverFilmstrip();
        _filmstrip.DisposeCache();
        _filmstrip.Dispose();
        ClearMount();
    }
}
