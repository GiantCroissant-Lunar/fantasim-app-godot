using System.Runtime.CompilerServices;
using FantaSim.App.Resource;
using FantaSim.App.Resource.Bundle;
using FantaSim.App.Ui;
using FantaSim.App.Ui.NodeGraph;
using FantaSim.App.Ui.Providers;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
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
    private long _globeReferenceTick;
    private PlanetGenerationGraphSource? _graphSource;
    private NodeGraphViewSource? _graphView;
    private PlanetGenerationGraphSource.PlanetGenerationTimelineGraphBinding? _graphBinding;
    private IDisposable? _graphViewRegistration;
    private bool _graphViewMounted;
    private int? _subscribedWorldHash;
    private bool _worldRuntimeChangePending;
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
            _plateSurfaceRoot = BuildPlateSurface(document.GlobeSnapshot);
            body.AddChild(_plateSurfaceRoot);

            _boundaryRenderer = new PlateBoundaryFocusRenderer(document.GlobeSnapshot);
            body.AddChild(_boundaryRenderer);
        }

        body.AddChild(BuildProductLayerRoot(document));
        _statusLabel = BuildStatusLabel(document);
        body.AddChild(_statusLabel);
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
        var showsPlateFeatures = regime?.ShowsPlateFeatures ?? true;

        bool isMobilePlate = regime?.RegimeId == "mobile-plate";
        bool isPlateFocused = _timeline.SelectedLayer?.LayerId == "geosphere.plate";
        bool showBoundaries = showsPlateFeatures && isMobilePlate && isPlateFocused;

        if (_plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot))
            _plateSurfaceRoot.Visible = showsPlateFeatures;

        if (_boundaryRenderer is not null && GodotObject.IsInstanceValid(_boundaryRenderer))
            _boundaryRenderer.Visible = showBoundaries;

        ApplyPlateMotion(tick, showsPlateFeatures);

        if (_mantle is not null && GodotObject.IsInstanceValid(_mantle))
            _mantle.MaterialOverride = ResolveMantleMaterial(RegimeSurfaceResolver.Resolve(regime?.RegimeId));

        if (_statusLabel is not null && GodotObject.IsInstanceValid(_statusLabel))
            _statusLabel.Text = $"{regime?.RegimeId ?? "world"} : t={tick:N0}";
    }

    private void OnLayerSelectionChanged(TimelineLayerSelection? selection)
    {
        if (_disposed)
            return;
        ApplyTimelineTick(_timeline.Tick);
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

    private Node3D BuildPlateSurface(WorldGlobeSnapshot snapshot)
    {
        _plateMotions.Clear();
        var root = new Node3D
        {
            Name = "PlateSurface",
            Scale = Vector3.One * 2.0f,
        };
        var plates = snapshot.Plates.ToDictionary(plate => plate.PlateId);

        foreach (var group in snapshot.Cells.Where(cell => cell.PlateId >= 0).GroupBy(cell => cell.PlateId).OrderBy(group => group.Key))
        {
            var plate = BuildPlateMesh(group.Key, group.ToArray());
            root.AddChild(plate);
            if (plates.TryGetValue(group.Key, out var motionPlate)
                && TryNormalize(ToV3(motionPlate.Axis), out var axis)
                && motionPlate.RatePerTick != 0.0)
            {
                _plateMotions[group.Key] = new PlateMotionBinding(plate, axis, motionPlate.RatePerTick);
            }
        }

        return root;
    }

    private static MeshInstance3D BuildPlateMesh(int plateId, IReadOnlyList<GlobeCell> cells)
    {
        var vertices = new Vector3[cells.Count * 3];
        var normals = new Vector3[vertices.Length];

        // Per-vertex smooth normals: each corner is on the origin-centred globe sphere, so its outward
        // normal is normalize(position) — exact, no face-normal averaging needed (Godot's sphere example).
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var a = ToV3(cell.C0);
            var b = ToV3(cell.C1);
            var c = ToV3(cell.C2);
            var offset = i * 3;
            vertices[offset] = a;
            vertices[offset + 1] = b;
            vertices[offset + 2] = c;
            normals[offset] = a.Normalized();
            normals[offset + 1] = b.Normalized();
            normals[offset + 2] = c.Normalized();
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Name = $"Plate_{plateId}",
            Mesh = mesh,
            MaterialOverride = BuildPlateMaterial(plateId),
        };
    }

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

    private static StandardMaterial3D BuildPlateMaterial(int plateId)
    {
        var color = PlateColor(plateId);
        return new()
        {
            AlbedoColor = new Color(color.R, color.G, color.B, 0.72f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 0.08f,
            Roughness = 0.86f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

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

    private static Shader? _magmaShader;
    private static Shader? _stagnantShader;

    private Material? _magmaMantleMaterial;
    private Material? _stagnantMantleMaterial;
    private Material? _baseMantleMaterial;

    private static Shader MagmaShader => _magmaShader ??= new Shader { Code = MagmaShaderCode };
    private static Shader StagnantShader => _stagnantShader ??= new Shader { Code = StagnantShaderCode };

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

    private static Color PlateColor(int plateId)
    {
        ReadOnlySpan<Color> palette =
        [
            new Color(0.34f, 0.58f, 0.42f),
            new Color(0.26f, 0.50f, 0.58f),
            new Color(0.55f, 0.47f, 0.33f),
            new Color(0.45f, 0.38f, 0.55f),
            new Color(0.30f, 0.60f, 0.54f),
            new Color(0.63f, 0.58f, 0.34f),
            new Color(0.38f, 0.46f, 0.66f),
            new Color(0.56f, 0.42f, 0.32f),
        ];

        return palette[Math.Abs(plateId) % palette.Length];
    }

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
