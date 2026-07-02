using System.Runtime.CompilerServices;
using FantaSim.App.Resource;
using FantaSim.App.Resource.Bundle;
using FantaSim.App.Ui;
using FantaSim.App.Ui.NodeGraph;
using FantaSim.App.Ui.Providers;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Shared;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using ResourceService = FantaSim.App.Resource.IService;
using WorldService = FantaSim.App.World.IService;

namespace FantaSim.App.Common.Entry;

internal sealed class PlanetPresentationBinder : IDisposable
{
    private const string StageBundleId = "stage";
    private const string WorldBundleId = "world";
    private static readonly NodePath PlanetLayerMountPath = new("Environment/PlanetMount/Planet/LayerMounts");
    private static readonly Vector3 PlanetBodyPreviewOffset = new(0.8f, 0.0f, 0.0f);
    private const float MaxPlateMotionPreviewRadians = 0.08f;

    private readonly IRegistry _registry;
    private readonly ResourceService _resource;
    private readonly IBundleSceneRegistry _sceneRegistry;
    private readonly ILogger _log;
    private readonly PlanetTimelineController _timeline;
    private readonly IDisposable _timelineRegistration;
    private readonly IDisposable _watch;
    private IDisposable? _generationSubscription;
    private Node3D? _activeRoot;
    private Node3D? _plateSurfaceRoot;
    private PlateBoundaryFocusRenderer? _boundaryRenderer;
    private MeshInstance3D? _mantle;
    private Label3D? _statusLabel;
    private readonly Dictionary<int, PlateMotionBinding> _plateMotions = new();
    private GlobePlateSurfaces? _plateSurfaces;
    private long _globeReferenceTick;
    private PlanetGenerationGraphSource? _graphSource;
    private NodeGraphViewSource? _graphView;
    private PlanetGenerationGraphSource.PlanetGenerationTimelineGraphBinding? _graphBinding;
    private IDisposable? _graphViewRegistration;
    private bool _graphViewMounted;
    private int? _subscribedWorldHash;
    private bool _worldRuntimeChangePending;
    private string? _boundRegimeId;
    private bool _regimeRefreshPending;
    private bool _disposed;

    public PlanetPresentationBinder(
        IRegistry registry,
        ResourceService resource,
        IBundleSceneRegistry sceneRegistry,
        ILoggerFactory loggerFactory)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        _sceneRegistry = sceneRegistry ?? throw new ArgumentNullException(nameof(sceneRegistry));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));

        _log = loggerFactory.CreateLogger("World.PlanetPresentation");
        _timeline = new PlanetTimelineController(ApplyTimelineTick);

        _timelineRegistration = _registry.RegisterOwned<ITimelineController>(
            _timeline,
            new ServiceRegistration
            {
                Tags = new[] { "world", "timeline", "presentation" },
                Description = "Resident contract-only planet timeline controller"
            });

        _timeline.LayerSelectionChanged += OnLayerSelectionChanged;

        _resource.RuntimeChanging += OnResourceRuntimeChanging;
        _resource.RuntimeChanged += OnResourceRuntimeChanged;
        _watch = _resource.WatchResource(WorldBundleId);
    }

    private void ResetRegimeTracking()
    {
        _boundRegimeId = null;
        _regimeRefreshPending = false;
    }

    public void Rebind()
    {
        if (_disposed)
            return;

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _log.LogWarning("Planet presentation skipped: world service is not registered.");
            return;
        }

        SubscribeGenerationChanged(world);

        PlanetPresentationDocument document;
        try
        {
            document = world.GetPlanetPresentationAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Planet presentation document failed.");
            return;
        }

        // Clear the bound-regime baseline so the UpdateFrom -> PushTick -> ApplyTimelineTick path
        // below (and any intermediate tick) cannot mistake this rebind for a regime transition.
        ResetRegimeTracking();
        _timeline.UpdateFrom(document);
        EnsureNodeGraphView(document);
        Callable.From(() => BindDocument(document)).CallDeferred();
    }

    private void EnsureNodeGraphView(PlanetPresentationDocument document)
    {
        if (_graphView is null)
        {
            _graphSource = BuildInitialGraphSource(document);
            _graphView = new NodeGraphViewSource(_graphSource, title: "world generation graph");
            _graphBinding = new PlanetGenerationGraphSource.PlanetGenerationTimelineGraphBinding(_timeline, _graphSource);
            _graphViewRegistration = _registry.RegisterOwned<IViewSource>(
                _graphView,
                new ServiceRegistration
                {
                    Tags = new[] { "ui", "node-graph", "world" },
                    Description = "Resident world-generation layer graph view"
                });
        }
        else if (document.GenerationGraphFamily is not null)
        {
            _graphSource!.UpdateFamily(document.GenerationGraphFamily, _timeline.Tick);
            _graphBinding?.FollowNow();
        }

        if (_graphViewMounted)
            return;

        var viewHost = _registry.TryGet<IViewHost>();
        if (viewHost is null)
        {
            _log.LogWarning("World node graph view skipped: UI view host is not registered.");
            return;
        }

        viewHost.Mount(_graphView.ViewId);
        _graphViewMounted = true;
        _log.LogInformation("World node graph view requested: {ViewId}", _graphView.ViewId);
    }

    private PlanetGenerationGraphSource BuildInitialGraphSource(PlanetPresentationDocument document)
    {
        var family = document.GenerationGraphFamily
            ?? PlanetGenerationGraphSource.BuildFallbackFamily(document);
        return new PlanetGenerationGraphSource("world-generation", family, _timeline.Tick);
    }

    private void SubscribeGenerationChanged(WorldService world)
    {
        var hash = RuntimeHelpers.GetHashCode(world);
        if (_subscribedWorldHash == hash)
            return;

        _subscribedWorldHash = hash;
        _generationSubscription?.Dispose();
        _generationSubscription = world.SubscribeGenerationChanged(_ =>
        {
            if (_disposed)
                return;
            Callable.From(Rebind).CallDeferred();
        });
    }

    private void BindDocument(PlanetPresentationDocument document)
    {
        if (_disposed)
            return;

        var mount = _sceneRegistry.GetNodeOrNull(StageBundleId, PlanetLayerMountPath) as Node3D;
        if (mount is null)
        {
            _log.LogWarning(
                "Planet presentation skipped: stage mount not found at {Path}.",
                PlanetLayerMountPath);
            return;
        }

        ClearActiveRoot();

        var root = new Node3D { Name = "PlanetPresentation" };
        root.SetMeta("planetId", document.PlanetId);
        root.SetMeta("sourceWorldId", document.SourceWorldId);
        root.SetMeta("revision", document.Revision);
        root.SetMeta("referenceTick", document.ReferenceTick);
        root.SetMeta("globeReferenceTick", document.GlobeReferenceTick);
        mount.AddChild(root);
        _activeRoot = root;
        _globeReferenceTick = document.GlobeReferenceTick;

        AddLightingAndCamera(root);

        var body = new Node3D
        {
            Name = "PlanetBody",
            Position = PlanetBodyPreviewOffset,
        };
        root.AddChild(body);

        _mantle = BuildMantle(document);
        body.AddChild(_mantle);

        if (document.GlobeSnapshot is not null)
        {
            _plateSurfaceRoot = BuildPlateSurface(document);
            body.AddChild(_plateSurfaceRoot);

            _boundaryRenderer = new PlateBoundaryFocusRenderer(
                document.BoundaryArcs ?? Array.Empty<PlateBoundaryArc>());
            body.AddChild(_boundaryRenderer);
        }

        body.AddChild(BuildProductLayerRoot(document));
        _statusLabel = BuildStatusLabel(document);
        body.AddChild(_statusLabel);
        _boundRegimeId = _timeline.GeosphereSchedule.RegimeAt(_timeline.Tick)?.RegimeId;
        ApplyTimelineTick(_timeline.Tick);

        _log.LogInformation(
            "Planet presentation mounted under stage Environment: planet={PlanetId}, plates={PlateCount}, cells={CellCount}, productLayers={LayerCount}, revision={Revision}.",
            document.PlanetId,
            document.GlobeSnapshot?.PlateCount ?? 0,
            document.GlobeSnapshot?.CellCount ?? 0,
            document.Layers.Count,
            document.Revision);
    }

    private void ApplyTimelineTick(long tick)
    {
        var regime = _timeline.GeosphereSchedule.RegimeAt(tick);
        var regimeId = regime?.RegimeId;
        var showsPlateFeatures = regime?.ShowsPlateFeatures ?? true;

        if (_boundRegimeId is not null
            && !string.Equals(_boundRegimeId, regimeId, StringComparison.Ordinal))
        {
            // Regime-gated content (boundary arcs) differs across this boundary: re-fetch the
            // presentation document so arcs built at the current tick reach the renderer. The id
            // is stamped synchronously so rapid ticks cannot stack refreshes; the pending flag
            // bounds the deferred work to one in-flight rebind.
            var previousRegimeId = _boundRegimeId;
            _boundRegimeId = regimeId;
            _log.LogInformation(
                "Planet regime transition {Previous} -> {Current} at t={Tick}: refreshing presentation.",
                previousRegimeId, regimeId ?? "<none>", tick);
            ScheduleRegimeRefresh();
        }

        bool isMobilePlate = regimeId == "mobile-plate";
        bool isPlateFocused = _timeline.SelectedLayer?.LayerId == "geosphere.plate";
        bool showBoundaries = showsPlateFeatures && isMobilePlate && isPlateFocused;

        if (_plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot))
            _plateSurfaceRoot.Visible = showsPlateFeatures;

        if (_boundaryRenderer is not null && GodotObject.IsInstanceValid(_boundaryRenderer))
            _boundaryRenderer.Visible = showBoundaries;

        ApplyPlateMotion(tick, showsPlateFeatures);

        if (_mantle is not null && GodotObject.IsInstanceValid(_mantle))
            _mantle.MaterialOverride = ResolveMantleMaterial(RegimeSurfaceResolver.Resolve(regimeId));

        if (_statusLabel is not null && GodotObject.IsInstanceValid(_statusLabel))
            _statusLabel.Text = $"{regimeId ?? "world"} : t={tick:N0}";
    }

    private void OnLayerSelectionChanged(TimelineLayerSelection? selection)
    {
        if (_disposed)
            return;
        ApplyTimelineTick(_timeline.Tick);
    }

    private void ScheduleRegimeRefresh()
    {
        if (_regimeRefreshPending)
            return;
        _regimeRefreshPending = true;
        Callable.From(RefreshPresentationForRegime).CallDeferred();
    }

    private void RefreshPresentationForRegime()
    {
        if (_disposed)
        {
            _regimeRefreshPending = false;
            return;
        }

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _regimeRefreshPending = false;
            _log.LogWarning("Planet presentation regime refresh skipped: world service is not registered.");
            return;
        }

        PlanetPresentationDocument document;
        try
        {
            document = world.GetPlanetPresentationAsync(_timeline.Tick);
        }
        catch (Exception ex)
        {
            _regimeRefreshPending = false;
            _log.LogError(ex, "Planet presentation document failed during regime refresh at tick {Tick}.", _timeline.Tick);
            return;
        }

        _timeline.UpdateFrom(document);
        EnsureNodeGraphView(document);
        BindDocument(document);
        _regimeRefreshPending = false;
    }

    private void ApplyPlateMotion(long tick, bool showsPlateFeatures)
    {
        if (_plateMotions.Count == 0)
            return;

        var deltaTick = showsPlateFeatures ? tick - _globeReferenceTick : 0L;
        foreach (var motion in _plateMotions.Values)
        {
            if (!GodotObject.IsInstanceValid(motion.Instance))
                continue;

            // This preview still uses rigid plate caps; keep long-range drift readable until boundary geology is rendered.
            var angle = (float)Math.Clamp(motion.RatePerTick * deltaTick, -MaxPlateMotionPreviewRadians, MaxPlateMotionPreviewRadians);
            motion.Instance.Basis = new Basis(motion.Axis, angle);
        }
    }

    private static void AddLightingAndCamera(Node3D root)
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.8f,
            ShadowEnabled = false,
        };
        root.AddChild(sun);
        sun.Position = new Vector3(5.2f, 2.2f, 4.3f);
        sun.LookAt(Vector3.Zero, Vector3.Up);

        var environment = new WorldEnvironment
        {
            Name = "PlanetEnvironment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.015f, 0.018f, 0.022f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.34f, 0.36f, 0.40f),
                AmbientLightEnergy = 0.42f,
            }
        };
        root.AddChild(environment);

        var camera = new Camera3D
        {
            Name = "PlanetCamera",
            Current = true,
            Position = new Vector3(0.0f, 1.3f, 6.3f),
        };
        root.AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);
    }

    private MeshInstance3D BuildMantle(PlanetPresentationDocument document)
    {
        var mesh = new SphereMesh
        {
            Radius = document.GlobeSnapshot is null ? 1.0f : 0.96f,
            Height = document.GlobeSnapshot is null ? 2.0f : 1.92f,
            RadialSegments = 64,
            Rings = 32,
        };

        return new MeshInstance3D
        {
            Name = "BaseSphere",
            Mesh = mesh,
            Scale = Vector3.One * 2.0f,
            MaterialOverride = ResolveMantleMaterial(RegimeSurfaceKind.Default),
        };
    }

    private Node3D BuildPlateSurface(PlanetPresentationDocument document)
    {
        var snapshot = document.GlobeSnapshot!;
        _plateMotions.Clear();
        var root = new Node3D
        {
            Name = "PlateSurface",
            Scale = Vector3.One * 2.0f,
        };
        var plates = snapshot.Plates.ToDictionary(plate => plate.PlateId);

        // Watertight per-plate caps: within a plate, cells that meet at a corner SHARE that corner,
        // so adjacent triangles meet exactly (no black cracks). Topology is cached once per snapshot.
        // Displacement and hypsometric tint read the SAME per-cell crust elevations so color and
        // relief stay coherent; flat-zero when crust products have not flowed yet.
        _plateSurfaces = new GlobePlateSurfaces(snapshot);
        IReadOnlyList<double> elevations =
            document.CellElevations is { } cellElevations && cellElevations.Count == snapshot.CellCount
                ? cellElevations
                : new double[snapshot.CellCount];
        var caps = _plateSurfaces.BuildSurfaces(elevations, exaggeration: WatertightDisplacementExaggeration);

        // A2: per-cell hypsometric tint + typed feature accents, driven by the document's crust data.
        // Bypassed (neutral mid-ramp) when the document carries no crust surface data.
        var (perCellColor, perCellEmission) = BuildCellAppearance(snapshot.CellCount, document);

        foreach (var cap in caps.OrderBy(c => c.PlateId))
        {
            var plate = BuildPlateMesh(cap, perCellColor, perCellEmission);
            root.AddChild(plate);
            if (plates.TryGetValue(cap.PlateId, out var motionPlate)
                && TryNormalize(ToV3(motionPlate.Axis), out var axis)
                && motionPlate.RatePerTick != 0.0)
            {
                _plateMotions[cap.PlateId] = new PlateMotionBinding(plate, axis, motionPlate.RatePerTick);
            }
        }

        return root;
    }

    // Computes per-cell Godot.Color (hypsometric tint with trench/ridge accent baked in) and per-cell
    // volcanic emission intensity, from the document's crust elevation + feature data. Falls back to a
    // neutral mid-ramp tint when crust data is absent (pre-onset or pipeline unavailable).
    private static (Color[] Colors, float[] Emission) BuildCellAppearance(
        int cellCount,
        PlanetPresentationDocument document)
    {
        var colors = new Color[cellCount];
        var emission = new float[cellCount];

        var elevations = document.CellElevations;
        if (elevations is null || elevations.Count != cellCount)
        {
            var fallback = ToColor(HypsometricTint.ComputeColors(new double[] { 0.0 })[0]);
            for (int c = 0; c < cellCount; c++) colors[c] = fallback;
            return (colors, emission);
        }

        var features = document.CellFeatures;
        var rampColors = HypsometricTint.ComputeColors(elevations);
        for (int c = 0; c < cellCount; c++)
        {
            var tint = rampColors[c];
            byte kind = 0;
            double magnitude = 0.0;
            if (features is not null && c < features.Count)
            {
                kind = features[c].Kind;
                magnitude = features[c].Magnitude;
            }
            var accent = CrustAccentMapper.Map(kind, magnitude);
            colors[c] = ToColor(CrustAccentMapper.Apply(tint, accent));
            emission[c] = (float)accent.VolcanicEmission;
        }
        return (colors, emission);
    }

    private static Color ToColor(RampColor c) => new((float)c.R, (float)c.G, (float)c.B);

    // Matches GlobeView's magnitude so the mantle sphere (radius 0.96 * 2) stays hidden under the caps.
    private const float WatertightDisplacementExaggeration = 0.00012f;

    private static MeshInstance3D BuildPlateMesh(PlateCap cap, Color[] perCellColor, float[] perCellEmission)
    {
        var surface = cap.Surface;
        int triCount = surface.TriangleCount;
        int vertCount = triCount * 3;

        // Watertight + smooth: positions come from the shared-vertex GlobeSurface.Positions, so
        // adjacent triangles meet EXACTLY at shared corners (no black cracks), while per-vertex
        // SmoothNormals keep the silhouette round instead of faceted.
        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];

        for (int t = 0; t < triCount; t++)
        {
            int i0 = surface.Triangles[(t * 3) + 0];
            int i1 = surface.Triangles[(t * 3) + 1];
            int i2 = surface.Triangles[(t * 3) + 2];

            int b = t * 3;
            vertices[b + 0] = ToV3(surface.Positions[i0]);
            vertices[b + 1] = ToV3(surface.Positions[i1]);
            vertices[b + 2] = ToV3(surface.Positions[i2]);
            normals[b + 0] = ToV3(surface.SmoothNormals[i0]);
            normals[b + 1] = ToV3(surface.SmoothNormals[i1]);
            normals[b + 2] = ToV3(surface.SmoothNormals[i2]);
        }

        // A2: per-vertex hypsometric color (ArrayType.Color) + volcanic emission (ArrayType.TexUV2.x).
        // Separate loop from positions/normals so this merges cleanly with concurrent mesh-path work.
        var colors = new Color[vertCount];
        var uv2 = new Vector2[vertCount];
        for (int t = 0; t < triCount; t++)
        {
            int cellId = cap.CellIds[t];
            var color = cellId >= 0 && cellId < perCellColor.Length
                ? perCellColor[cellId]
                : new Color(0.3f, 0.35f, 0.28f);
            float emis = cellId >= 0 && cellId < perCellEmission.Length
                ? perCellEmission[cellId]
                : 0f;

            int b = t * 3;
            colors[b + 0] = color;
            colors[b + 1] = color;
            colors[b + 2] = color;
            uv2[b + 0] = new Vector2(emis, 0f);
            uv2[b + 1] = new Vector2(emis, 0f);
            uv2[b + 2] = new Vector2(emis, 0f);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Name = $"Plate_{cap.PlateId}",
            Mesh = mesh,
            MaterialOverride = HypsoPlateMaterial,
        };
    }

    private static Vector3 ToV3(CartesianPoint3 p) => new((float)p.X, (float)p.Y, (float)p.Z);

    private static Node3D BuildProductLayerRoot(PlanetPresentationDocument document)
    {
        var root = new Node3D { Name = "ProductLayers" };
        for (var i = 0; i < document.Layers.Count; i++)
        {
            var layer = document.Layers[i];
            var node = new Node3D { Name = SafeNodeName($"{i:D2}_{layer.LayerId}") };
            node.SetMeta("layerId", layer.LayerId);
            node.SetMeta("regimeId", layer.RegimeId);
            node.SetMeta("variant", layer.Variant);
            node.SetMeta("branch", layer.Branch);
            node.SetMeta("productDomain", layer.ProductDomain);
            node.SetMeta("productName", layer.ProductName);
            node.SetMeta("productTick", layer.ProductTick);
            node.SetMeta("productAddress", layer.ProductAddress);
            root.AddChild(node);
        }

        return root;
    }

    private static Label3D BuildStatusLabel(PlanetPresentationDocument document)
        => new()
        {
            Name = "PlanetStatus",
            Text = document.PlanetId,
            Position = new Vector3(-2.6f, -2.1f, 0.0f),
            FontSize = 22,
            PixelSize = 0.008f,
            Modulate = new Color(0.86f, 0.90f, 0.95f, 0.88f),
        };

    // magma-ocean mantle: emissive molten lava with a slowly drifting fBm churn. lava-hot tracks the
    // sibling GlobeView.MagmaAlbedoForTemperature lava endpoint so both render paths read as the same lava.
    private const string MagmaShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_lava_hot  : source_color = vec4(1.00, 0.46, 0.10, 1.0);
uniform vec4 u_lava_cool : source_color = vec4(0.16, 0.04, 0.03, 1.0);
uniform float u_emission_energy : hint_range(0.0, 8.0) = 1.6;
uniform float u_noise_scale = 2.6;
uniform float u_drift_speed = 0.05;

varying vec3 v_obj_pos;

vec3 hash3(vec3 p) {
    p = vec3(dot(p, vec3(127.1, 311.7, 74.7)),
             dot(p, vec3(269.5, 183.3, 246.1)),
             dot(p, vec3(113.5, 271.9, 124.6)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float noise3(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 0.0)), f - vec3(0.0, 0.0, 0.0)),
                dot(hash3(i + vec3(1.0, 0.0, 0.0)), f - vec3(1.0, 0.0, 0.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 0.0)), f - vec3(0.0, 1.0, 0.0)),
                dot(hash3(i + vec3(1.0, 1.0, 0.0)), f - vec3(1.0, 1.0, 0.0)), u.x), u.y),
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 1.0)), f - vec3(0.0, 0.0, 1.0)),
                dot(hash3(i + vec3(1.0, 0.0, 1.0)), f - vec3(1.0, 0.0, 1.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 1.0)), f - vec3(0.0, 1.0, 1.0)),
                dot(hash3(i + vec3(1.0, 1.0, 1.0)), f - vec3(1.0, 1.0, 1.0)), u.x), u.y),
        u.z);
}

float fbm(vec3 p) {
    float n  = noise3(p *  5.0) * 0.5000;
          n += noise3(p * 10.0) * 0.2500;
          n += noise3(p * 20.0) * 0.1250;
          n += noise3(p * 40.0) * 0.0625;
    return n;
}

void vertex() {
    v_obj_pos = VERTEX;
}

void fragment() {
    vec3 q = v_obj_pos * u_noise_scale + vec3(0.0, TIME * u_drift_speed, 0.0);
    float n = fbm(q);
    float t = smoothstep(-0.05, 0.45, n);
    vec3 col = mix(u_lava_cool.rgb, u_lava_hot.rgb, t);
    float vein = smoothstep(0.55, 0.80, n);
    col += u_lava_hot.rgb * vein * 0.60;

    ALBEDO = col;
    EMISSION = col * u_emission_energy + u_lava_hot.rgb * vein * u_emission_energy;
    ROUGHNESS = 0.62;
    METALLIC = 0.0;
}
";

    // stagnant-lid mantle: dark basaltic cooled crust, subtle noise-modulated albedo/roughness, and a
    // thin faintly-emissive crack band where the fBm crosses a threshold (cheap: one smoothstep pair).
    private const string StagnantShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_basalt_dark  : source_color = vec4(0.05, 0.05, 0.06, 1.0);
uniform vec4 u_basalt_light : source_color = vec4(0.20, 0.19, 0.21, 1.0);
uniform float u_crack_glow : hint_range(0.0, 2.0) = 0.16;
uniform float u_noise_scale = 3.2;

varying vec3 v_obj_pos;

vec3 hash3(vec3 p) {
    p = vec3(dot(p, vec3(127.1, 311.7, 74.7)),
             dot(p, vec3(269.5, 183.3, 246.1)),
             dot(p, vec3(113.5, 271.9, 124.6)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float noise3(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 0.0)), f - vec3(0.0, 0.0, 0.0)),
                dot(hash3(i + vec3(1.0, 0.0, 0.0)), f - vec3(1.0, 0.0, 0.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 0.0)), f - vec3(0.0, 1.0, 0.0)),
                dot(hash3(i + vec3(1.0, 1.0, 0.0)), f - vec3(1.0, 1.0, 0.0)), u.x), u.y),
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 1.0)), f - vec3(0.0, 0.0, 1.0)),
                dot(hash3(i + vec3(1.0, 0.0, 1.0)), f - vec3(1.0, 0.0, 1.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 1.0)), f - vec3(0.0, 1.0, 1.0)),
                dot(hash3(i + vec3(1.0, 1.0, 1.0)), f - vec3(1.0, 1.0, 1.0)), u.x), u.y),
        u.z);
}

float fbm(vec3 p) {
    float n  = noise3(p *  5.0) * 0.5000;
          n += noise3(p * 10.0) * 0.2500;
          n += noise3(p * 20.0) * 0.1250;
          n += noise3(p * 40.0) * 0.0625;
    return n;
}

void vertex() {
    v_obj_pos = VERTEX;
}

void fragment() {
    vec3 q = v_obj_pos * u_noise_scale;
    float n = fbm(q);
    float t = smoothstep(-0.10, 0.40, n);
    vec3 col = mix(u_basalt_dark.rgb, u_basalt_light.rgb, t);
    float crack = smoothstep(0.45, 0.55, n) - smoothstep(0.55, 0.70, n);
    col += u_basalt_light.rgb * crack * 0.40;

    ALBEDO = col;
    EMISSION = u_basalt_light.rgb * crack * u_crack_glow;
    ROUGHNESS = mix(0.88, 0.62, t);
    METALLIC = 0.0;
}
";

    // Hypsometric plate-cap shader (A2): per-vertex COLOR carries the terrain tint (deep ocean →
    // shelf → lowland → upland → mountain → snow, computed on the CPU with percentile normalization);
    // UV2.x carries the volcanic-vent emission intensity (0 = none). Trench darkening and ridge
    // brightening are baked into the vertex COLOR on the CPU (CrustAccentMapper.Apply) so the shader
    // only needs albedo + a gated emission pass. Half-Lambert light keeps displaced relief readable.
    // Godot 4 docs: COLOR (vec4, auto-populated from ArrayType.Color, no flag on ShaderMaterial) and
    // UV2 (vec2, auto-populated from ArrayType.TexUV2); EMISSION is out-vec3 in fragment().
    private const string HypsoPlateShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_volcanic_glow : source_color = vec4(1.0, 0.42, 0.10, 1.0);
uniform float u_volcanic_energy : hint_range(0.0, 8.0) = 1.4;

void fragment() {
    ALBEDO = COLOR.rgb;
    float vent = UV2.x;
    if (vent > 0.001) {
        EMISSION = u_volcanic_glow.rgb * vent * u_volcanic_energy;
    }
    ROUGHNESS = 0.92;
    METALLIC = 0.0;
}

void light() {
    float ndotl = dot(normalize(NORMAL), normalize(LIGHT));
    float wrap = ndotl * 0.5 + 0.5;
    wrap *= wrap;
    DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * ATTENUATION * wrap;
}
";

    private static Shader? _magmaShader;
    private static Shader? _stagnantShader;
    private static Shader? _hypsoPlateShader;

    private Material? _magmaMantleMaterial;
    private Material? _stagnantMantleMaterial;
    private Material? _baseMantleMaterial;
    private static Material? _hypsoPlateMaterial;

    private static Shader MagmaShader => _magmaShader ??= new Shader { Code = MagmaShaderCode };
    private static Shader StagnantShader => _stagnantShader ??= new Shader { Code = StagnantShaderCode };
    private static Shader HypsoPlateShader => _hypsoPlateShader ??= new Shader { Code = HypsoPlateShaderCode };

    private static Material HypsoPlateMaterial => _hypsoPlateMaterial ??= new ShaderMaterial { Shader = HypsoPlateShader };

    private Material ResolveMantleMaterial(RegimeSurfaceKind kind) =>
        kind switch
        {
            RegimeSurfaceKind.MagmaOcean => _magmaMantleMaterial ??= BuildMagmaMantleMaterial(),
            RegimeSurfaceKind.StagnantLid => _stagnantMantleMaterial ??= BuildStagnantMantleMaterial(),
            _ => _baseMantleMaterial ??= BuildBaseMantleMaterial(),
        };

    private static ShaderMaterial BuildMagmaMantleMaterial() => new() { Shader = MagmaShader };

    private static ShaderMaterial BuildStagnantMantleMaterial() => new() { Shader = StagnantShader };

    private static StandardMaterial3D BuildBaseMantleMaterial() =>
        new()
        {
            AlbedoColor = new Color(0.02f, 0.20f, 0.28f),
            Roughness = 0.82f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

    private static Vector3 ToV3(GlobeVec3 value)
        => new(value.X, value.Y, value.Z);

    private static bool TryNormalize(Vector3 value, out Vector3 normalized)
    {
        if (value.LengthSquared() < 0.000001f)
        {
            normalized = Vector3.Zero;
            return false;
        }

        normalized = value.Normalized();
        return true;
    }

    private static string SafeNodeName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray();
        var name = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(name) ? "Layer" : name;
    }

    private void OnResourceRuntimeChanging(object? sender, ResourceRuntimeChangingEventArgs args)
    {
        if (!string.Equals(args.BundleId, WorldBundleId, StringComparison.OrdinalIgnoreCase))
            return;

        _subscribedWorldHash = null;
        _generationSubscription?.Dispose();
        _generationSubscription = null;
        _worldRuntimeChangePending = true;
        ResetRegimeTracking();
        Callable.From(() =>
        {
            ClearActiveRoot();
            ReleaseNodeGraphView();
        }).CallDeferred();
        _log.LogInformation("Planet presentation released before resource {Operation}: {BundleId}", args.Operation, args.BundleId);
    }

    private void OnResourceRuntimeChanged(object? sender, EventArgs args)
    {
        if (_disposed || !_worldRuntimeChangePending || !_resource.IsLoaded(WorldBundleId))
            return;

        _worldRuntimeChangePending = false;
        Callable.From(Rebind).CallDeferred();
    }

    private void ClearActiveRoot()
    {
        if (_activeRoot is not null && GodotObject.IsInstanceValid(_activeRoot))
        {
            _activeRoot.GetParent()?.RemoveChild(_activeRoot);
            _activeRoot.QueueFree();
        }

        _activeRoot = null;
        _plateSurfaceRoot = null;
        _plateSurfaces = null;
        if (_boundaryRenderer is not null && GodotObject.IsInstanceValid(_boundaryRenderer))
        {
            _boundaryRenderer.GetParent()?.RemoveChild(_boundaryRenderer);
            _boundaryRenderer.QueueFree();
        }
        _boundaryRenderer = null;
        _mantle = null;
        _statusLabel = null;
        _plateMotions.Clear();
        _globeReferenceTick = 0L;
    }

    private void ReleaseNodeGraphView()
    {
        if (_graphViewMounted && _graphView is not null)
        {
            var viewHost = _registry.TryGet<IViewHost>();
            if (viewHost is not null)
            {
                viewHost.UnmountNow(_graphView.ViewId);
                viewHost.Unmount(_graphView.ViewId);
            }
        }

        _graphViewMounted = false;
        _graphBinding?.Dispose();
        _graphBinding = null;
        _graphViewRegistration?.Dispose();
        _graphViewRegistration = null;
        _graphView?.Dispose();
        _graphView = null;
        _graphSource = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timeline.LayerSelectionChanged -= OnLayerSelectionChanged;
        _resource.RuntimeChanging -= OnResourceRuntimeChanging;
        _resource.RuntimeChanged -= OnResourceRuntimeChanged;
        _watch.Dispose();
        _generationSubscription?.Dispose();
        _generationSubscription = null;
        ReleaseNodeGraphView();
        _timelineRegistration.Dispose();
        ClearActiveRoot();
    }
}

internal sealed record PlateMotionBinding(MeshInstance3D Instance, Vector3 Axis, double RatePerTick);

internal sealed class PlanetTimelineController : ITimelineController
{
    private readonly Action<long> _applyTick;
    private long _tick;
    private long _maxTick = 1;
    private TimelineLayerSelection? _selectedLayer;
    private Action? _onPlay;
    private Action? _onPause;
    private Action<long>? _onSeek;
    private Func<bool>? _checkPlaying;

    public PlanetTimelineController(Action<long> applyTick)
    {
        _applyTick = applyTick ?? throw new ArgumentNullException(nameof(applyTick));
        GeosphereSchedule = EmptySchedule("geosphere");
        AtmosphereSchedule = EmptySchedule("atmosphere");
    }

    public long Tick => _tick;

    public long MaxTick => _maxTick;

    public bool IsPlaying => _checkPlaying?.Invoke() ?? false;

    public SphereRegimeSchedule GeosphereSchedule { get; private set; }

    public SphereRegimeSchedule AtmosphereSchedule { get; private set; }

    public TimelineLayerSelection? SelectedLayer => _selectedLayer;

    public event Action<long>? TickChanged;
    public event Action<TimelineLayerSelection?>? LayerSelectionChanged;

    public void UpdateFrom(PlanetPresentationDocument document)
    {
        GeosphereSchedule = document.GeosphereSchedule ?? EmptySchedule("geosphere");
        AtmosphereSchedule = document.AtmosphereSchedule ?? EmptySchedule("atmosphere");
        _maxTick = Math.Max(1L, document.MaxTick);
        PushTick(Math.Clamp(_tick, 0L, _maxTick));
    }

    public void Play() => _onPlay?.Invoke();

    public void Pause() => _onPause?.Invoke();

    public void SeekTo(long tick) => _onSeek?.Invoke(Math.Clamp(tick, 0L, _maxTick));

    public void SelectLayer(string sphereId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sphereId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        var next = new TimelineLayerSelection(sphereId, layerId);
        if (Equals(_selectedLayer, next))
            return;

        _selectedLayer = next;
        LayerSelectionChanged?.Invoke(_selectedLayer);
    }

    public void PushTick(long tick)
    {
        _tick = Math.Clamp(tick, 0L, _maxTick);
        _applyTick(_tick);
        TickChanged?.Invoke(_tick);
    }

    public void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying)
    {
        _onPlay = onPlay;
        _onPause = onPause;
        _onSeek = onSeek;
        _checkPlaying = checkPlaying;
    }

    public void UnregisterPlayback()
    {
        _onPlay = null;
        _onPause = null;
        _onSeek = null;
        _checkPlaying = null;
    }

    private static SphereRegimeSchedule EmptySchedule(string sphereId)
        => new(new SphereId(sphereId), Array.Empty<SphereRegime>());
}
