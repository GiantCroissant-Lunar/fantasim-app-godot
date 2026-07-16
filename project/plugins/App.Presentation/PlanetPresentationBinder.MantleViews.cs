using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Rendering;
using Godot;
using Microsoft.Extensions.Logging;
using WorldService = FantaSim.App.World.IService;

namespace FantaSim.App.Presentation;

// Mantle-interior LAYER view (D1) + the DEPRECATED render.mantle alias entry (directive 2,
// 2026-07-16). Split from PlanetPresentationBinder 2026-07-11 (planet-presentation-binder-split).
// The x-ray/ghost-shell presentation that preceded the layer path has been retired; render.mantle
// now routes to the geosphere.mantle layer selection (same path as timeline.select_layer).
internal sealed partial class PlanetPresentationBinder
{
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

    // render.mantle DEPRECATED ALIAS (directive 2): routes the legacy command to the geosphere.mantle
    // LAYER selection — the exact same code path as timeline.select_layer. enabled=true selects the
    // mantle layer (firing LayerSelectionChanged -> RebuildMantleLayer); enabled=false removes it via
    // the layer path's toggle. Returns null on success, or a rejection message when the mantle layer
    // is not active at the current tick (the shared LayerActivation predicate — never a silent no-op).
    // The resident render seam turns this into the deprecation-noted result JSON (see MantleAlias).
    public string? RequestMantleLayerAlias(bool enabled)
    {
        if (_disposed)
            return null;

        const string sphereId = "geosphere";
        const string layerId = "geosphere.mantle";

        if (!enabled)
        {
            // Deselect: remove mantle from the active set via the layer path's toggle (D5). Idempotent
            // when mantle is already inactive. Mirrors the layer-selection path; introduces no new state.
            bool mantleActive = false;
            foreach (var layer in _timeline.ActiveLayers)
            {
                if (string.Equals(layer.SphereId, sphereId, StringComparison.Ordinal)
                    && string.Equals(layer.LayerId, layerId, StringComparison.Ordinal))
                {
                    mantleActive = true;
                    break;
                }
            }
            if (mantleActive)
                _timeline.ToggleLayer(sphereId, layerId);
            return null;
        }

        // Activate: reject loudly through the shared predicate (same check as timeline.select_layer)
        // when the mantle layer is inactive at the current tick. Otherwise drive the layer selection,
        // which reconciles the composed mantle-interior view with separated crust slabs (no ghost shell).
        if (!LayerActivation.IsLayerActive(_timeline, sphereId, layerId))
            return $"Layer '{layerId}' in sphere '{sphereId}' is not active at tick {_timeline.Tick}.";

        _timeline.SelectLayer(sphereId, layerId);
        return null;
    }

    // D1: free the old mantle-interior layer root; if active, sample the volumetric field at the
    // playhead tick and mount the composed tree (core sphere + four isosurfaces + separated crust
    // slabs, NO ghost shell) via MantleInteriorViewComposer. Called on view-mode transition into
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
        // same material singletons and BuildIsosurfaceNode the retired x-ray path used.
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
    // uses so the solid reads when the document has no materialized thickness yet. Shared with the
    // cutaway/exploded path (BuildExplodedSolidCrust).
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
