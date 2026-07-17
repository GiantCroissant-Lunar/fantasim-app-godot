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

    // Slice-2 declared joint-mechanics parameters (subduction dip, overriding margin raise, edge
    // band width, divergent widening, structural clearance floor) — all eye-tuned via the user gate.
    private SlabJointMechanicsProfile _jointMechanicsProfile = SlabJointMechanicsProfile.Default;

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

        // Slices 2+3: joint mechanics + subduction tongues via the chaining composer overload —
        // convergent underride (dip + raised margin + the watertight tongue reaching under the
        // overriding lip), divergent widen, transform identity. Kind + polarity come from the
        // document's existing boundary data (contracts-tier).
        var boundaryArcs = document.BoundaryArcs;
        IReadOnlyList<FantaSim.App.World.Globe.PlateSolid> solids;
        if (boundaryArcs is { Count: > 0 })
        {
            var joints = SlabJointClassifier.Classify(boundaryArcs, document.BoundarySections);
            solids = WorldSlabAssemblyComposer.BuildAssembly(
                slabCaps,
                centroids,
                thickness,
                _radialProfile.ThicknessDepthScale(),
                _worldSurfaceProfile,
                joints,
                _jointMechanicsProfile);
            _log?.LogInformation(
                "World slab joints shaped: joints={JointCount}, convergent={ConvergentCount}, tongues chained.",
                joints.Count,
                CountConvergent(joints));
        }
        else
        {
            solids = WorldSlabAssemblyComposer.BuildAssembly(
                slabCaps,
                centroids,
                thickness,
                _radialProfile.ThicknessDepthScale(),
                _worldSurfaceProfile);
        }

        // The molten interior beneath the assembled slabs: every joint gap glows orange from
        // within (acceptance image behavior). Sits just under the slab undersides.
        var moltenGlow = new MeshInstance3D
        {
            Name = "MoltenInterior",
            Mesh = new SphereMesh { Radius = 0.86f, Height = 1.72f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = PlanetShaderLibrary.BuildMoltenInteriorMaterial(),
            Scale = Vector3.One * 2.0f,
        };
        root.AddChild(moltenGlow);

        AddSlabMeshInstances(
            root,
            slabCaps,
            solids,
            centroids,
            _worldSurfaceProfile.SlabJointGapUnitRadius,
            slabPerPlateVertexColors);

        return root;
    }

    private static int CountConvergent(IReadOnlyList<SlabJointClassification> joints)
    {
        int n = 0;
        for (int i = 0; i < joints.Count; i++)
        {
            if (joints[i].Kind == SlabJointKind.Convergent)
                n++;
        }
        return n;
    }
}
