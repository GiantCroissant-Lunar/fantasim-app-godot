using FantaSim.App.World;
using FantaSim.App.World.Globe;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Presentation;

// The default assembled World mounts the same state-derived plate solids as the exploded view at
// factor zero. Ordinary contacts are closed; depth testing hides stored buried underlap.
internal sealed partial class PlanetPresentationBinder
{
    // The DECLARED World-surface presentation parameters (S1): which presentation the World view
    // renders (slab assembly by default) and the joint gap that keeps slab joints readable from
    // orbit. Same declared-profile pattern as _radialProfile/_slabProfile — a future host knob or
    // document-carried profile can override consciously.
    private WorldSurfacePresentationProfile _worldSurfaceProfile = WorldSurfacePresentationProfile.Default;

    // The mounted assembly root (under PlanetBody) and its reconcile flag. Driven by the
    // WorldSurfacePresentationPolicy gate in ApplyTimelineTick; entering/leaving the World view
    // (or flipping the declared presentation) builds/frees the root — the RebuildMantleLayer /
    // RebuildExplodedCrust reconcile pattern.
    private Node3D? _worldSlabAssemblyRoot;
    private bool _worldSlabAssemblyActive;

    // Free the old assembly root; if active, rebuild from the current document + cached surface
    // inputs and parent under PlanetBody. Mirrors RebuildExplodedCrust/RebuildMantleLayer.
    private void RebuildWorldSlabAssembly()
    {
        if (_worldSlabAssemblyRoot is not null && GodotObject.IsInstanceValid(_worldSlabAssemblyRoot))
        {
            _worldSlabAssemblyRoot.GetParent()?.RemoveChild(_worldSlabAssemblyRoot);
            _worldSlabAssemblyRoot.QueueFree();
        }
        _worldSlabAssemblyRoot = null;

        if (!_worldSlabAssemblyActive)
            return;

        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var body = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (body is null)
            return;

        _worldSlabAssemblyRoot = BuildWorldSlabAssemblyRoot();
        body.AddChild(_worldSlabAssemblyRoot);
        _log.LogInformation(
            "World crust volume assembly mounted: childNodes={ChildNodeCount}, contactGap=0.",
            _worldSlabAssemblyRoot.GetChildCount());
    }

    // The World crust assembly uses the same state-derived caps and closed solids as the exploded
    // view, with zero translation. Ordinary contacts remain closed and buried material is occluded.
    private Node3D BuildWorldSlabAssemblyRoot()
    {
        var root = new Node3D { Name = "WorldCrustVolumeAssembly" };
        var document = _currentDocument;
        var snapshot = document?.GlobeSnapshot;
        var volume = document?.CrustVolume;
        if (document is null || snapshot is null || volume is null)
            return root;

        var centroids = _lastCentroids ?? PlateSolidBuilder.ComputeCentroids(snapshot);
        var (caps, perPlateVertexColors) = BuildSlabTopCaps(document, snapshot);
        var solids = PlateSolidBuilder.Build(caps, volume);
        var interior = new MeshInstance3D
        {
            Name = "InteriorContext",
            Mesh = new SphereMesh { Radius = 0.86f, Height = 1.72f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = PlanetShaderLibrary.BuildMoltenInteriorMaterial(),
            Scale = Vector3.One * 2.0f,
        };
        root.AddChild(interior);
        AddSlabMeshInstances(
            root,
            caps,
            solids,
            centroids,
            offsetMag: 0.0,
            slabPerPlateVertexColors: perPlateVertexColors);
        _log.LogInformation(
            "Assembled crust volume mounted: digest={Digest}, plates={PlateCount}, contactGap=0.",
            volume.Digest,
            solids.Count);
        return root;
    }
}
