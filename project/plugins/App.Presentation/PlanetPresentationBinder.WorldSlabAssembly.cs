using FantaSim.App.World.Globe;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Presentation;

// Assembled-world slice 1 (vault/specs/2026-07-16-assembled-world-northstar.md): the DEFAULT World
// view presents the per-plate SOLID slab assembly — "the normal complete sphere could not see how
// convergent, divergent, transform being presented. But the split part with thickness can." The
// watertight-sphere World path stays available behind the declared WorldSurfacePresentationProfile
// fallback; the slab path reuses the SAME machinery as the mantle-layer/exploded views
// (BuildSlabTopCaps formed-relief tops + PlateSolidBuilder lit strata walls) with a small declared
// JOINT GAP instead of the exploded view's radial translation.
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
            "World slab assembly mounted: plates={PlateCount}, jointGap={JointGap}R.",
            _worldSlabAssemblyRoot.GetChildCount() / 2,
            _worldSurfaceProfile.SlabJointGapUnitRadius);
    }

    // The World slab assembly: the SAME formed-relief slab tops (BuildSlabTopCaps — slab-declared
    // exaggeration, born-rough sampling, NO World silhouette clamp) and lit strata walls the
    // exploded/mantle-layer views compose, with the declared JOINT GAP as the radial offset via the
    // pure WorldSlabAssemblyComposer — assembled, with visible joints (sketchfab exploded-plates
    // family, assembled state).
    private Node3D BuildWorldSlabAssemblyRoot()
    {
        var root = new Node3D { Name = "WorldSlabAssembly" };

        var document = _currentDocument;
        var snapshot = document?.GlobeSnapshot;
        if (document is null || snapshot is null)
            return root;

        var centroids = _lastCentroids;
        if (centroids is null)
            return root;

        var (slabCaps, slabPerPlateVertexColors) = BuildSlabTopCaps(document, snapshot);
        var thickness = ResolveCrustThicknessMetres(document);
        var solids = WorldSlabAssemblyComposer.BuildAssembly(
            slabCaps,
            centroids,
            thickness,
            _radialProfile.ThicknessDepthScale(),
            _worldSurfaceProfile);

        AddSlabMeshInstances(
            root,
            slabCaps,
            solids,
            centroids,
            _worldSurfaceProfile.SlabJointGapUnitRadius,
            slabPerPlateVertexColors);

        return root;
    }
}
