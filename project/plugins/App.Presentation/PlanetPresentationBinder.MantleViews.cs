using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Rendering;
using Godot;
using Microsoft.Extensions.Logging;
using WorldService = FantaSim.App.World.IService;

namespace FantaSim.App.Presentation;

// Mantle x-ray (M-A) + mantle-interior LAYER (D1) views. Split from PlanetPresentationBinder
// 2026-07-11 (vault/plans/2026-07-11-planet-presentation-binder-split-plan.md).
internal sealed partial class PlanetPresentationBinder
{
    // M-A mantle x-ray state (inactive by default). _mantleXrayRoot holds the dark core sphere plus
    // the four isosurface MeshInstance3Ds (cold/warm x outer/inner) sampled from the engine's
    // volumetric MantleAnomalyField at the current tick.
    private Node3D? _mantleXrayRoot;
    private bool _mantleXrayActive;

    // D1 mantle-interior LAYER view state (inactive by default). _mantleLayerRoot holds the composed
    // tree from MantleInteriorViewComposer: core sphere + four isosurfaces + separated crust slabs
    // (NO ghost shell — the slabs are the reference frame). Driven by viewMode == MantleInterior,
    // reconciled in ApplyTimelineTick; entering/leaving the layer builds/frees the root.
    private Node3D? _mantleLayerRoot;
    private bool _mantleLayerActive;

    // D3 radial section profile: the single declared source of truth for crust thickness and mantle
    // depth scaling. Defaults cover the canonical Earth-like planet; a future look-dev knob or a
    // document-carried profile can override. Thickness exaggeration here feeds PlateSolidBuilder;
    // the core-sphere radius (CMB × mantle scale) feeds the mantle interior backdrop.
    private RadialSectionProfile _radialProfile = RadialSectionProfile.Default;

    // M-A: entry from render.mantle. enabled=true samples the conditioned convection field at the
    // playhead tick and mounts cold/warm isosurface meshes; enabled=false clears them and restores the
    // plate surface. The ghosted crust + boundary wireframe visibility is applied through the standard
    // ApplyTimelineTick path (which checks _mantleXrayActive), mirroring how cutaway re-applies.
    public void UpdateMantle(bool enabled)
    {
        if (_disposed)
            return;

        _mantleXrayActive = enabled;
        RebuildMantleXray();
        ApplyTimelineTick(_timeline.Tick);
    }

    // M-A: free the old mantle x-ray root; if active, sample the volumetric field at the playhead
    // tick and mount the four isosurface meshes + dark core sphere under PlanetBody.
    private void RebuildMantleXray()
    {
        if (_mantleXrayRoot is not null && GodotObject.IsInstanceValid(_mantleXrayRoot))
        {
            _mantleXrayRoot.GetParent()?.RemoveChild(_mantleXrayRoot);
            _mantleXrayRoot.QueueFree();
        }
        _mantleXrayRoot = null;

        if (!_mantleXrayActive)
            return;

        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var body = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (body is null)
            return;

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _log.LogWarning("Mantle x-ray skipped: world service is not registered.");
            return;
        }

        FantaSim.App.World.MantleIsosurfaceSet set;
        try
        {
            set = world.GetMantleIsosurfacesAsync(_timeline.Tick);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Mantle x-ray sampling failed at t={Tick}: {Message}", _timeline.Tick, ex.Message);
            return;
        }

        _mantleXrayRoot = BuildMantleXrayRoot(set);
        body.AddChild(_mantleXrayRoot);
        _log.LogInformation(
            "Mantle x-ray mounted at t={Tick}: cold outer/inner={ColdOuter}/{ColdInner} verts, warm outer/inner={WarmOuter}/{WarmInner} verts.",
            _timeline.Tick,
            set.ColdOuter.Vertices.Length / 3, set.ColdInner.Vertices.Length / 3,
            set.WarmOuter.Vertices.Length / 3, set.WarmInner.Vertices.Length / 3);
    }

    // D1: free the old mantle-interior layer root; if active, sample the volumetric field at the
    // playhead tick and mount the composed tree (core sphere + four isosurfaces + separated crust
    // slabs, NO ghost shell) via MantleInteriorViewComposer. Mirrors RebuildMantleXray's lifecycle
    // but composes the slabs instead of ghosting the surface. Called on view-mode transition into
    // MantleInterior (reconciled in ApplyTimelineTick).
    private void RebuildMantleLayer()
    {
        if (_mantleLayerRoot is not null && GodotObject.IsInstanceValid(_mantleLayerRoot))
        {
            _mantleLayerRoot.GetParent()?.RemoveChild(_mantleLayerRoot);
            _mantleLayerRoot.QueueFree();
        }
        _mantleLayerRoot = null;

        if (!_mantleLayerActive)
            return;

        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var body = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (body is null)
            return;

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _log.LogWarning("Mantle layer view skipped: world service is not registered.");
            return;
        }

        FantaSim.App.World.MantleIsosurfaceSet set;
        try
        {
            set = world.GetMantleIsosurfacesAsync(_timeline.Tick);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Mantle layer sampling failed at t={Tick}: {Message}", _timeline.Tick, ex.Message);
            return;
        }

        // Separated crust slabs at the profile's declared layer explode factor (0.4 of max offset).
        Node3D slabRoot = BuildExplodedSolidCrust(RadialSectionProfile.MantleLayerExplodeFactor);
        // SCALE COMPENSATION (windowed gate 2026-07-08): BuildExplodedSolidCrust's child mesh
        // instances each carry the house x2 scale (they were built for the standalone
        // render.exploded path, parented directly under PlanetBody). The composer root applies
        // the house x2 as well — without compensation the slabs render x4 while the isosurfaces
        // (unit-built children) render x2, and giant slab shells englobe the interior. Halving
        // the slab root nets the slabs back to x2. Follow-up: unify the scaling convention so
        // piece builders emit unit-scale nodes and ONLY composition roots scale.
        slabRoot.Scale = Vector3.One * 0.5f;

        // Four isosurface entries (opaque inner cores first, translucent outer halos last) — the
        // same material singletons and BuildIsosurfaceNode the x-ray path uses.
        var entries = new List<MantleInteriorViewComposer.IsosurfaceEntry>(4);
        if (!set.ColdInner.IsEmpty)
            entries.Add(new(BuildIsosurfaceNode("ColdInner", set.ColdInner, ColdInnerMaterial), RenderPriority: 0));
        if (!set.WarmInner.IsEmpty)
            entries.Add(new(BuildIsosurfaceNode("WarmInner", set.WarmInner, WarmInnerMaterial), RenderPriority: 0));
        if (!set.ColdOuter.IsEmpty)
            entries.Add(new(BuildIsosurfaceNode("ColdOuter", set.ColdOuter, ColdOuterMaterial), RenderPriority: 2));
        if (!set.WarmOuter.IsEmpty)
            entries.Add(new(BuildIsosurfaceNode("WarmOuter", set.WarmOuter, WarmOuterMaterial), RenderPriority: 2));

        _mantleLayerRoot = MantleInteriorViewComposer.Compose(
            coreSphere: BuildCoreSphere(),
            isosurfaces: entries,
            separatedSlabRoot: slabRoot);
        body.AddChild(_mantleLayerRoot);
        _log.LogInformation(
            "Mantle interior layer mounted at t={Tick}: cold outer/inner={ColdOuter}/{ColdInner} verts, warm outer/inner={WarmOuter}/{WarmInner} verts, slabs={SlabChildren}.",
            _timeline.Tick,
            set.ColdOuter.Vertices.Length / 3, set.ColdInner.Vertices.Length / 3,
            set.WarmOuter.Vertices.Length / 3, set.WarmInner.Vertices.Length / 3,
            slabRoot.GetChildCount());
    }

    // The four method-lock surfaces plus stage dressing (spec ingredient 6): opaque INNER cores
    // (deep blue slab hearts / red-orange plume hearts, drawn in the opaque pass), translucent
    // OUTER halos (drawn in the transparent pass with explicit render priority AFTER opaques),
    // and the dark core sphere at the CMB radius. All geometry is unit-sphere; the root applies
    // the house globe scale (x2, matching the plate surface and cutaway nodes).
    private Node3D BuildMantleXrayRoot(FantaSim.App.World.MantleIsosurfaceSet set)
    {
        var root = new Node3D { Name = "MantleXray", Scale = Vector3.One * 2.0f };
        root.AddChild(BuildCoreSphere());
        if (!set.ColdInner.IsEmpty)
            root.AddChild(BuildIsosurfaceNode("ColdInner", set.ColdInner, ColdInnerMaterial));
        if (!set.WarmInner.IsEmpty)
            root.AddChild(BuildIsosurfaceNode("WarmInner", set.WarmInner, WarmInnerMaterial));
        if (!set.ColdOuter.IsEmpty)
            root.AddChild(BuildIsosurfaceNode("ColdOuter", set.ColdOuter, ColdOuterMaterial));
        if (!set.WarmOuter.IsEmpty)
            root.AddChild(BuildIsosurfaceNode("WarmOuter", set.WarmOuter, WarmOuterMaterial));
        root.AddChild(BuildGhostShell());
        return root;
    }

    // Translucent ghost shell at the surface radius — the reference-image framing device: the
    // viewer sees the interior THROUGH a faint skin that still reads as "the planet". Drawn last
    // in the transparent pass (priority above the outer isosurfaces), back faces culled so the
    // far side doesn't double the haze.
    private static MeshInstance3D BuildGhostShell() => new()
    {
        Name = "GhostShell",
        Mesh = new SphereMesh { Radius = 1.0f, Height = 2.0f, RadialSegments = 96, Rings = 48 },
        MaterialOverride = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.75f, 0.82f, 0.9f, 0.10f),
            Roughness = 0.9f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Back,
            RenderPriority = 3,
        },
    };

    // Dark core sphere at the CMB radius — the backdrop the anomaly volumes read against. D3: the
    // geometric radius comes from _radialProfile (CMB × mantle depth scale), not a 0.55 literal.
    private MeshInstance3D BuildCoreSphere()
    {
        double r = _radialProfile.DisplayedCoreSphereRadius();
        return new MeshInstance3D
        {
            Name = "MantleCore",
            Mesh = new SphereMesh { Radius = (float)r, Height = (float)(2.0 * r), RadialSegments = 48, Rings = 24 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.05f, 0.055f, 0.07f),
                Roughness = 1.0f,
                Metallic = 0.0f,
            },
        };
    }

    private static MeshInstance3D BuildIsosurfaceNode(
        string name,
        FantaSim.App.World.MantleIsosurfaceMesh mesh,
        Material material)
    {
        int vertexCount = mesh.Vertices.Length / 3;
        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new Vector3(mesh.Vertices[3 * i], mesh.Vertices[3 * i + 1], mesh.Vertices[3 * i + 2]);
            normals[i] = new Vector3(mesh.Normals[3 * i], mesh.Normals[3 * i + 1], mesh.Normals[3 * i + 2]);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = mesh.Triangles;

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Name = name,
            Mesh = arrayMesh,
            MaterialOverride = material,
        };
    }

    // M-B: per-cell crust thickness in metres, with the SAME null/mean fallback BuildCutawayFaces
    // uses so the solid reads when the document has no materialized thickness yet.
    private IReadOnlyList<double> ResolveCrustThicknessMetres(PlanetPresentationDocument document)
    {
        var crustThickness = document.CellCrustThickness;
        if (crustThickness is { Count: > 0 })
            return crustThickness;

        double meanCrust = CutawayStratumProfile.DefaultCrustThicknessMetres;
        var snapshot = document.GlobeSnapshot;
        int cellCount = snapshot is not null ? snapshot.CellCount : 0;
        if (cellCount <= 0)
            return new[] { meanCrust };
        var fallback = new double[cellCount];
        Array.Fill(fallback, meanCrust);
        return fallback;
    }
    private ShaderMaterial? _coldInnerMaterial;
    private ShaderMaterial? _coldOuterMaterial;
    private ShaderMaterial? _warmInnerMaterial;
    private ShaderMaterial? _warmOuterMaterial;

    // Opaque inner cores. Cold = deep blue with a faint glow so the slab hearts stay legible inside
    // the dark shell; warm = red-orange with a stronger emissive (the plumes are the focal point).
    private ShaderMaterial ColdInnerMaterial => _coldInnerMaterial ??= PlanetShaderLibrary.BuildIsosurfaceMaterial(
        new Color(0.10f, 0.22f, 0.75f), emission: 0.5f, alpha: 1.0f, priority: 0);

    private ShaderMaterial WarmInnerMaterial => _warmInnerMaterial ??= PlanetShaderLibrary.BuildIsosurfaceMaterial(
        new Color(0.95f, 0.30f, 0.08f), emission: 1.6f, alpha: 1.0f, priority: 0);

    // Translucent outer halos, drawn AFTER the opaques with explicit render priority (spec
    // ingredient 4: layered translucency is what reads as volumetric). Cold outer is tuned to the
    // same visual weight as warm outer: blue reads darker than orange against the dark core and the
    // cold field is dimmer (slab peak ~0.55 vs plume ~1.0), so the halo needs a brighter tint, a
    // higher emission (0.25 -> 0.55, matching warm), and a touch more alpha (0.22 -> 0.28) to read
    // as a distinct translucent envelope rather than a single flat surface.
    private ShaderMaterial ColdOuterMaterial => _coldOuterMaterial ??= PlanetShaderLibrary.BuildIsosurfaceMaterial(
        new Color(0.35f, 0.60f, 0.98f), emission: 0.55f, alpha: 0.28f, priority: 1);

    private ShaderMaterial WarmOuterMaterial => _warmOuterMaterial ??= PlanetShaderLibrary.BuildIsosurfaceMaterial(
        new Color(1.0f, 0.55f, 0.15f), emission: 0.6f, alpha: 0.22f, priority: 2);
}
