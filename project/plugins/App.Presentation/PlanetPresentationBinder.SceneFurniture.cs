using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Rendering;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Presentation;

// Scene furniture: lighting/camera rig, mantle base sphere, atmosphere rim, product-layer roots,
// status label, and mantle regime materials. Split from PlanetPresentationBinder 2026-07-11
// (vault/plans/2026-07-11-planet-presentation-binder-split-plan.md).
internal sealed partial class PlanetPresentationBinder
{
    private MeshInstance3D? _mantle;
    private MeshInstance3D? _atmosphereRim;
    private ShaderMaterial? _atmosphereRimMaterial;
    private DirectionalLight3D? _sunLight;
    private WorldEnvironment? _planetEnvironment;
    private Label3D? _statusLabel;

    private static string BuildVerticalScaleIndicator(PlanetPresentationDocument document, GlobeViewMode viewMode)
    {
        if (viewMode == GlobeViewMode.World)
            return VerticalScaleLabel.BuildIndicatorSuffix(WorldHeightScale, WorldHeightExponent);

        var projection = LayerProjectionProfileResolver.ResolveForView(
            document,
            viewMode,
            worldMetresToUnitRadius: WorldHeightScale,
            worldHeightExponent: WorldHeightExponent);
        return VerticalScaleLabel.BuildIndicatorSuffix(
            projection.MetresToUnitRadius,
            projection.HeightExponent,
            projection.TrueScaleMetresToUnitRadius);
    }

    private void AddLightingAndCamera(Node3D root, GlobeViewMode viewMode)
    {
        var tuning = PlanetLightingTuning.ForView(viewMode);
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = tuning.SunLightEnergy,
            LightColor = tuning.SunColor,
            ShadowEnabled = false,
        };
        root.AddChild(sun);
        _sunLight = sun;
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
                AmbientLightColor = tuning.AmbientColor,
                AmbientLightEnergy = tuning.AmbientLightEnergy,
            }
        };
        root.AddChild(environment);
        _planetEnvironment = environment;

        if (_registry.TryGet<FantaSim.App.Camera.IService>() is not null)
        {
            _log.LogDebug("Planet fallback camera skipped; App.Camera IService is registered.");
            return;
        }

        var camera = new Camera3D
        {
            Name = "PlanetCamera",
            Current = true,
            Position = new Vector3(0.0f, 1.3f, 6.3f),
        };
        root.AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);
    }

    private void ApplyLightingForView(GlobeViewMode viewMode)
    {
        if (_sunLight is null || !GodotObject.IsInstanceValid(_sunLight))
            return;
        if (_planetEnvironment is null || !GodotObject.IsInstanceValid(_planetEnvironment))
            return;

        PlanetLightingTuning.ForView(viewMode).ApplyTo(_sunLight, _planetEnvironment);
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

    private MeshInstance3D BuildAtmosphereRim()
    {
        var mesh = new SphereMesh
        {
            Radius = 1.03f,
            Height = 2.06f,
            RadialSegments = 64,
            Rings = 32,
        };

        _atmosphereRimMaterial = new ShaderMaterial
        {
            Shader = PlanetShaderLibrary.AtmosphereRimShader,
            RenderPriority = 2,
        };

        return new MeshInstance3D
        {
            Name = "AtmosphereRim",
            Mesh = mesh,
            Scale = Vector3.One * 2.0f,
            Visible = false,
            MaterialOverride = _atmosphereRimMaterial,
        };
    }

    private void UpdateAtmosphereRim(long tick)
    {
        if (_atmosphereRim is null
            || !GodotObject.IsInstanceValid(_atmosphereRim)
            || _atmosphereRimMaterial is null)
            return;

        var state = AtmosphereRimStateMapper.Map(_timeline.AtmosphereSchedule, tick);
        // Exploded crust owns the whole visual interior. The ordinary atmosphere sphere would fill
        // every separation gap and make the plate volumes read as a skin over another globe.
        bool visible = state.Exists
            && WorldViewContentGate.IsActive(_timeline.SelectedLayer)
            && !_explodedActive;
        _atmosphereRim.Visible = visible;

        if (!visible)
            return;

        _atmosphereRimMaterial.SetShaderParameter("u_intensity", (float)state.Intensity);
        _atmosphereRimMaterial.SetShaderParameter("u_tint",
            new Color((float)state.Tint.R, (float)state.Tint.G, (float)state.Tint.B));
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

    private Material? _magmaMantleMaterial;
    private Material? _stagnantMantleMaterial;
    private Material? _baseMantleMaterial;

    private Material ResolveMantleMaterial(RegimeSurfaceKind kind) =>
        kind switch
        {
            RegimeSurfaceKind.MagmaOcean => _magmaMantleMaterial ??= PlanetShaderLibrary.BuildMagmaMantleMaterial(),
            RegimeSurfaceKind.StagnantLid => _stagnantMantleMaterial ??= PlanetShaderLibrary.BuildStagnantMantleMaterial(),
            _ => _baseMantleMaterial ??= PlanetShaderLibrary.BuildBaseMantleMaterial(),
        };
}
