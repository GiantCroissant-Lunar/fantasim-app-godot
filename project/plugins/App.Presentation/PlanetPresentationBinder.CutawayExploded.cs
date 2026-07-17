using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Rendering;
using Godot;

namespace FantaSim.App.Presentation;

// Cutaway wedge (W3a) + exploded solid crust (M-B) view state. Split from PlanetPresentationBinder
// 2026-07-11 (vault/plans/2026-07-11-planet-presentation-binder-split-plan.md).
internal sealed partial class PlanetPresentationBinder
{
    // W3a cutaway wedge state (inactive by default; width 0 = zero render change).
    private CutawayWedge _cutawayWedge = new(new UnifyMaths.Vector3D(0, 0, 1), 0, 0);
    private double _cutawayAzimuthDeg;
    private double _cutawayWidthDeg;
    private Node3D? _cutawayFaceRoot;
    private ShaderMaterial? _hypsoPlateMaterialOverride;

    // M-B exploded solid-crust state (inactive until render.exploded is invoked at least once).
    private bool _explodedActive;
    private double _explodedFactor;
    private Node3D? _explodedCrustRoot;

    // W3a: per-instance plate material so the cutaway wedge uniforms are binder-scoped (a static
    // singleton would let one binder's cutaway leak into another). Lazily built; the wedge uniforms
    // are updated by UpdateCutaway. Falls back to the shared static when the cutaway is inactive
    // (zero-cost default: same material reference as before, so inactive = truly zero render change).
    private ShaderMaterial HypsoPlateMaterialOverride => _hypsoPlateMaterialOverride ??= new ShaderMaterial
    {
        Shader = PlanetShaderLibrary.HypsoPlateShader,
    };

    // W3a: entry from render.cutaway. Width 0 = inactive: clears the wedge, disables the shader
    // discard, frees the cut-face root — zero render change vs. today.
    public void UpdateCutaway(double azimuthDeg, double widthDeg)
    {
        if (_disposed)
            return;

        _cutawayAzimuthDeg = azimuthDeg;
        _cutawayWidthDeg = widthDeg;
        _cutawayWedge = new CutawayWedge(new UnifyMaths.Vector3D(0, 0, 1), azimuthDeg, widthDeg);

        UpdateCutawayPlateShader();
        RebuildCutawayFaces();
        ApplyTimelineTick(_timeline.Tick);
    }

    // M-B: entry from render.exploded. Factor 0 = assembled solid crust (solids in place, thickness
    // and side walls visible at the silhouette); factor in (0,1] radially translates each plate along
    // its area-weighted centroid direction. Activation hides the single-surface plate root and shows
    // the per-plate solid slabs instead; there is no deactivate path for M-B (spec: activation only).
    public void UpdateExploded(double factor)
    {
        if (_disposed)
            return;

        _explodedActive = true;
        _explodedFactor = factor;
        RebuildExplodedCrust();
        ApplyTimelineTick(_timeline.Tick);
    }

    private void UpdateCutawayPlateShader()
    {
        var mat = HypsoPlateMaterialOverride;
        mat.SetShaderParameter("u_wedge_active", !_cutawayWedge.IsInactive);
        if (_cutawayWedge.IsInactive)
            return;

        var axis = _cutawayWedge.Axis;
        var reference = _cutawayWedge.Reference;
        var referenceCross = new UnifyMaths.Vector3D(
            axis.Y * reference.Z - axis.Z * reference.Y,
            axis.Z * reference.X - axis.X * reference.Z,
            axis.X * reference.Y - axis.Y * reference.X);

        mat.SetShaderParameter("u_wedge_axis", new Vector3((float)axis.X, (float)axis.Y, (float)axis.Z));
        mat.SetShaderParameter("u_wedge_reference", new Vector3((float)reference.X, (float)reference.Y, (float)reference.Z));
        mat.SetShaderParameter("u_wedge_reference_cross", new Vector3((float)referenceCross.X, (float)referenceCross.Y, (float)referenceCross.Z));
        mat.SetShaderParameter("u_wedge_start_rad", (float)(_cutawayWedge.NormalizedStart * Math.PI / 180.0));
        mat.SetShaderParameter("u_wedge_width_rad", (float)(_cutawayWedge.WidthDeg * Math.PI / 180.0));
    }

    private void RebuildCutawayFaces()
    {
        if (_cutawayFaceRoot is not null && GodotObject.IsInstanceValid(_cutawayFaceRoot))
        {
            _cutawayFaceRoot.GetParent()?.RemoveChild(_cutawayFaceRoot);
            _cutawayFaceRoot.QueueFree();
        }
        _cutawayFaceRoot = null;

        if (_cutawayWedge.IsInactive)
            return;

        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var body = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (body is null)
            return;

        _cutawayFaceRoot = BuildCutawayFaces();
        body.AddChild(_cutawayFaceRoot);
    }

    // M-B: free the old exploded crust root, then if active build a new one and parent it under
    // PlanetBody. Mirrors RebuildCutawayFaces. When exploded is active the single-surface plate
    // root is hidden so only the per-plate solid slabs render.
    private void RebuildExplodedCrust()
    {
        if (_explodedCrustRoot is not null && GodotObject.IsInstanceValid(_explodedCrustRoot))
        {
            _explodedCrustRoot.GetParent()?.RemoveChild(_explodedCrustRoot);
            _explodedCrustRoot.QueueFree();
        }
        _explodedCrustRoot = null;

        if (_plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot))
            _plateSurfaceRoot.Visible = !_explodedActive;

        if (!_explodedActive)
            return;

        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var explodedBody = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (explodedBody is null)
            return;

        _explodedCrustRoot = BuildExplodedSolidCrust();
        explodedBody.AddChild(_explodedCrustRoot);
    }

    // M-B: per-plate SOLID crust. Two MeshInstance3Ds per plate under a single root: the TOP (the
    // attributed cap surface DTO — same Continents/terrain colors as the hidden single-surface —
    // with the plate's explode offset baked into positions) and the BOTTOM+WALLS (the
    // PlateSolidBuilder output, lit strata material). Both Scale = Vector3.One * 2.0f to match
    // PlateSurfaceRenderer and BuildCutawayFaceSector. No per-plate GPU rotation exists in this
    // path, so a baked position offset is exactly correct.
    //
    // Directive 3b: the slab TOPS carry formed relief from the SAME truth + sampler + ramp as the
    // World view, displaced by the slab view's DECLARED exaggeration (SlabTopReliefProfile, ratio-
    // locked with RadialSectionProfile) — NOT _lastCaps, which are tuned for the full-globe
    // silhouette (relief clamped to 0.5%R) and read as smooth on the exaggerated slabs.
    private Node3D BuildExplodedSolidCrust(double? factorOverride = null)
    {
        var root = new Node3D { Name = "ExplodedCrust" };

        var document = _currentDocument;
        var snapshot = document?.GlobeSnapshot;
        if (document is null || snapshot is null)
            return root;

        var centroids = _lastCentroids;
        if (centroids is null)
            return root;

        var (slabCaps, slabPerPlateVertexColors) = BuildSlabTopCaps(document, snapshot);

        double factor = factorOverride ?? _explodedFactor;

        var thickness = ResolveCrustThicknessMetres(document);
        // D3: the slab thickness exaggeration is EXPLICIT and distinct from the surface relief
        // exaggeration. The profile exposes the metres-to-unit-radius scale PlateSolidBuilder expects
        // (CrustThicknessExaggeration / PlanetRadiusMetres), so 30 km of crust reads as ~0.038R slab
        // walls. The slab relief (in slabCaps' top positions) acts on disjoint vertices — no compounding.
        var solids = PlateSolidBuilder.Build(slabCaps, thickness, _radialProfile.ThicknessDepthScale());

        // Keep the temporary radial solids joint-shaped, but do not append the retired tongue
        // scaffold. Continuous buried underlap will come from the canonical crust-volume extractor;
        // showing a false ribbon here would contradict the state even in an exploded view.
        var explodedArcs = document.BoundaryArcs;
        if (explodedArcs is { Count: > 0 })
        {
            solids = WorldSlabAssemblyComposer.ShapeSlabJoints(
                solids,
                explodedArcs,
                _jointMechanicsProfile,
                centroids,
                _worldSurfaceProfile.SlabJointGapUnitRadius);
        }

        var exploded = PlateSolidBuilder.ApplyExplodedFactor(solids, centroids, factor);

        // Slice 4 (structural): the exploded world is thick pieces around a SMALLER interior —
        // never a planet-size ball under a skin (the ball-under-skin misread, 2026-07-17). The
        // core gives the separated shell something to read against; material eye-tuned later.
        var coreMesh = new MeshInstance3D
        {
            Name = "ExplodedCore",
            Mesh = new SphereMesh { Radius = 0.8f, Height = 1.6f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = PlanetShaderLibrary.BuildMoltenInteriorMaterial(),
            Scale = Vector3.One * 2.0f,
        };
        root.AddChild(coreMesh);

        AddSlabMeshInstances(
            root,
            slabCaps,
            exploded,
            centroids,
            factor * PlateSolidBuilder.DefaultMaxOffset,
            slabPerPlateVertexColors);

        return root;
    }

    // Shared per-plate slab mesh emission for the solid-slab family (exploded look-dev crust, the
    // mantle layer's separated slabs, and the default World slab assembly): for each cap, the TOP
    // (attributed cap surface DTO with the plate's radial offset baked into positions) and the
    // BOTTOM+WALLS (the PlateSolidBuilder output, lit strata material). The solids list is parallel
    // to slabCaps and already carries the radial translation; offsetMag re-applies the SAME offset
    // to the TOP DTO positions so tops and walls stay welded.
    private void AddSlabMeshInstances(
        Node3D root,
        IReadOnlyList<PlateCap> slabCaps,
        IReadOnlyList<PlateSolid> solids,
        IReadOnlyList<PlateSolidCentroid> centroids,
        double offsetMag,
        IReadOnlyDictionary<int, RampColor[]> slabPerPlateVertexColors)
    {
        var byPlate = new Dictionary<int, PlateSolidCentroid>(centroids.Count);
        foreach (var c in centroids)
            byPlate[c.PlateId] = c;

        for (int i = 0; i < slabCaps.Count; i++)
        {
            var cap = slabCaps[i];
            if (!byPlate.TryGetValue(cap.PlateId, out var centroid))
                continue;

            var solid = solids[i];

            var topDto = BuildExplodedTopDto(cap, centroid, offsetMag, slabPerPlateVertexColors);
            root.AddChild(BuildExplodedMeshInstance($"Plate{cap.PlateId}_Top", topDto, HypsoPlateMaterialOverride));

            var solidDto = BuildExplodedSolidDto(cap, solid);
            root.AddChild(BuildExplodedSolidMeshInstance($"Plate{cap.PlateId}_Solid", solidDto, PlanetShaderLibrary.SlabWallStrataMaterial));
        }
    }

    // Directive 3b: builds the formed-relief slab TOP caps via SlabTopReliefComposer (the pure seam
    // that marries CellElevations + TectonicDetailSampler + the slab's declared exaggeration), and
    // the topology-aligned vertex colors from the SAME GlobePlateSurfaces instance. The slab caps'
    // shared-vertex topology must match the coloring arrays; building both from one instance holds by
    // construction, even when the World path used adaptive surfaces (_lastCaps would have a different
    // vertex count). Non-terrain colorings (Continents/PlateIdentity) are cell-parallel or flat and
    // need no slab alignment.
    private (IReadOnlyList<PlateCap> Caps, IReadOnlyDictionary<int, RampColor[]> VertexColors) BuildSlabTopCaps(
        PlanetPresentationDocument document,
        WorldGlobeSnapshot snapshot)
    {
        var slabSurfaces = SlabTopReliefComposer.BuildPlateSurfaces(snapshot, document.CellFeatures, _slabProfile);
        var elevations = ResolveSlabTopElevations(document, snapshot);

        var caps = slabSurfaces.BuildSurfaces(
            elevations,
            exaggeration: _slabProfile.MetresToUnitRadius(_radialProfile),
            maxDisplacementUnitRadius: _slabProfile.MaxDisplacementUnitRadius);

        var vertexColors = _lastIsTerrain && _lastPerCellColor is { Count: > 0 }
            ? PlateSurfaceMeshFactory.BuildPerPlateVertexColors(
                slabSurfaces,
                _lastPerCellColor as RampColor[] ?? Array.Empty<RampColor>())
            : new Dictionary<int, RampColor[]>();

        return (caps, vertexColors);
    }

    // Same elevation resolution as BindPlateSurface: for terrain, use CellElevations when present and
    // length-matched, else flat-zero. The slab relief is the SAME truth the World surface reads.
    private static IReadOnlyList<double> ResolveSlabTopElevations(PlanetPresentationDocument document, WorldGlobeSnapshot snapshot)
    {
        return document.CellElevations is { } cellElevations && cellElevations.Count == snapshot.CellCount
            ? cellElevations
            : new double[snapshot.CellCount];
    }

    // M-B: same PlateCapMeshBuilder.Build* branch the surface uses (cached inputs), with the plate's
    // explode offset baked into the DTO positions (uniform per-plate translation — correct because
    // there is NO per-plate GPU rotation in this path). The vertex colors come from the slab-aligned
    // dictionary (terrain) or the cached cell-parallel arrays (Continents/PlateIdentity).
    private PlateCapMeshDto BuildExplodedTopDto(
        PlateCap cap,
        PlateSolidCentroid centroid,
        double offsetMag,
        IReadOnlyDictionary<int, RampColor[]> slabPerPlateVertexColors)
    {
        var dx = centroid.CentroidDirection.X * offsetMag;
        var dy = centroid.CentroidDirection.Y * offsetMag;
        var dz = centroid.CentroidDirection.Z * offsetMag;

        PlateCapMeshDto dto = _lastIsTerrain
            ? PlateCapMeshBuilder.BuildTerrain(
                cap,
                slabPerPlateVertexColors,
                _lastPerCellEmission!,
                _lastJitter,
                _lastColorMode,
                _lastPerCellColor,
                _worldSurfaceProfile.FacetedSlabTops
                    ? PlateCapMeshNormalMode.Flat
                    : _lastNormalMode)
            : _lastViewMode == GlobeViewMode.Continents
                ? PlateCapMeshBuilder.BuildContinents(
                    cap,
                    _lastContinentsCellColors!,
                    _lastContinentsFrontier!)
                : PlateCapMeshBuilder.BuildPlateIdentity(cap);

        if (offsetMag == 0.0)
            return dto;

        var positions = dto.Positions;
        for (int v = 0; v < positions.Length; v += 3)
        {
            positions[v + 0] = (float)(positions[v + 0] + dx);
            positions[v + 1] = (float)(positions[v + 1] + dy);
            positions[v + 2] = (float)(positions[v + 2] + dz);
        }
        return dto;
    }

    // M-B: BOTTOM + SIDE WALLS from the exploded solid. The solid's Triangles = [top | bottom | walls]
    // concatenated; top+bottom each have cap.Surface.Triangles.Length indices; walls follow. We deref
    // the bottom+wall triangle range into a non-indexed Vector3 list and compute flat per-face normals
    // (one normal per triangle, assigned to its 3 vertices) so the lit SlabWallStrataMaterial shades
    // the thickness under lighting (directive 3b — the M-B "wall lighting" open item).
    private static PlateCapMeshDto BuildExplodedSolidDto(PlateCap cap, PlateSolid solid)
    {
        int topIndexCount = cap.Surface.Triangles.Length;
        int bottomWallIndexCount = solid.Triangles.Length - topIndexCount;
        int triangleCount = bottomWallIndexCount / 3;

        var positions = new float[bottomWallIndexCount * 3];
        var normals = new float[bottomWallIndexCount * 3];
        int w = 0;
        for (int t = 0; t < triangleCount; t++)
        {
            int i0 = solid.Triangles[topIndexCount + (t * 3) + 0];
            int i1 = solid.Triangles[topIndexCount + (t * 3) + 1];
            int i2 = solid.Triangles[topIndexCount + (t * 3) + 2];
            var p0 = solid.Positions[i0];
            var p1 = solid.Positions[i1];
            var p2 = solid.Positions[i2];

            positions[w + 0] = (float)p0.X;
            positions[w + 1] = (float)p0.Y;
            positions[w + 2] = (float)p0.Z;
            positions[w + 3] = (float)p1.X;
            positions[w + 4] = (float)p1.Y;
            positions[w + 5] = (float)p1.Z;
            positions[w + 6] = (float)p2.X;
            positions[w + 7] = (float)p2.Y;
            positions[w + 8] = (float)p2.Z;

            // Flat face normal: normalize((p1-p0) x (p2-p0)). The solid's winding makes walls face
            // outward and the bottom face inward, so the normals point the correct way for lighting.
            double ux = p1.X - p0.X, uy = p1.Y - p0.Y, uz = p1.Z - p0.Z;
            double vx = p2.X - p0.X, vy = p2.Y - p0.Y, vz = p2.Z - p0.Z;
            double nx = (uy * vz) - (uz * vy);
            double ny = (uz * vx) - (ux * vz);
            double nz = (ux * vy) - (uy * vx);
            double len = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
            float fnx, fny, fnz;
            if (len > 1e-12)
            {
                fnx = (float)(nx / len);
                fny = (float)(ny / len);
                fnz = (float)(nz / len);
            }
            else
            {
                fnx = 0f;
                fny = 0f;
                fnz = 1f;
            }

            for (int k = 0; k < 3; k++)
            {
                normals[w + (k * 3) + 0] = fnx;
                normals[w + (k * 3) + 1] = fny;
                normals[w + (k * 3) + 2] = fnz;
            }
            w += 9;
        }

        return new PlateCapMeshDto(
            PlateId: cap.PlateId,
            NormalMode: PlateCapMeshNormalMode.Flat,
            VertexCount: bottomWallIndexCount,
            TriangleCount: triangleCount,
            Positions: positions,
            Normals: normals,
            Colors: Array.Empty<float>(),
            Uv2: Array.Empty<float>());
    }

    private static MeshInstance3D BuildExplodedMeshInstance(string name, PlateCapMeshDto dto, Material material)
    {
        var vertices = new Vector3[dto.VertexCount];
        var normals = new Vector3[dto.VertexCount];
        var colors = new Color[dto.VertexCount];
        var uv2 = new Vector2[dto.VertexCount];

        for (int i = 0; i < dto.VertexCount; i++)
        {
            int v3 = i * 3;
            vertices[i] = new Vector3(dto.Positions[v3 + 0], dto.Positions[v3 + 1], dto.Positions[v3 + 2]);
            normals[i] = new Vector3(dto.Normals[v3 + 0], dto.Normals[v3 + 1], dto.Normals[v3 + 2]);
            colors[i] = new Color(dto.Colors[v3 + 0], dto.Colors[v3 + 1], dto.Colors[v3 + 2]);
            int uv = i * 2;
            uv2[i] = new Vector2(dto.Uv2[uv + 0], dto.Uv2[uv + 1]);
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
            Name = name,
            Mesh = mesh,
            Scale = Vector3.One * 2.0f,
            MaterialOverride = material,
        };
    }

    // M-B: BOTTOM+WALLS instance — positions + flat per-face normals (lit strata material needs
    // normals to shade thickness under lighting).
    private static MeshInstance3D BuildExplodedSolidMeshInstance(string name, PlateCapMeshDto dto, Material material)
    {
        var vertices = new Vector3[dto.VertexCount];
        var normals = new Vector3[dto.VertexCount];
        for (int i = 0; i < dto.VertexCount; i++)
        {
            int v3 = i * 3;
            vertices[i] = new Vector3(dto.Positions[v3 + 0], dto.Positions[v3 + 1], dto.Positions[v3 + 2]);
            normals[i] = new Vector3(dto.Normals[v3 + 0], dto.Normals[v3 + 1], dto.Normals[v3 + 2]);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Scale = Vector3.One * 2.0f,
            MaterialOverride = material,
        };
    }

    // W3a: two flat half-disc cut faces (one per wedge boundary azimuth), per-vertex COLOR encodes
    // stratum bands. Crust thickness from CellCrustThickness when available (mean), else default.
    private Node3D BuildCutawayFaces()
    {
        var root = new Node3D { Name = "CutawayFaces" };

        var document = _currentDocument;
        var exaggeration = document?.CutawayExaggeration ?? 1.0;
        var planetRadiusMetres = ResolvePlanetRadiusMetres(document);

        var crustThickness = document?.CellCrustThickness;
        double meanCrust = CutawayStratumProfile.DefaultCrustThicknessMetres;
        if (crustThickness is { Count: > 0 })
        {
            double sum = 0;
            int n = 0;
            foreach (var t in crustThickness)
            {
                if (t > 0) { sum += t; n++; }
            }
            if (n > 0)
                meanCrust = sum / n;
        }

        var bands = CutawayStratumProfile.ComputeBands(
            meanCrust,
            CutawayStratumProfile.DefaultLithosphereLidThicknessMetres,
            exaggeration,
            planetRadiusMetres);

        var axis = _cutawayWedge.Axis;
        var reference = _cutawayWedge.Reference;
        var referenceCross = new UnifyMaths.Vector3D(
            axis.Y * reference.Z - axis.Z * reference.Y,
            axis.Z * reference.X - axis.X * reference.Z,
            axis.X * reference.Y - axis.Y * reference.X);

        var startDeg = _cutawayWedge.NormalizedStart;
        var endDeg = startDeg + _cutawayWedge.WidthDeg;

        root.AddChild(BuildCutawayFaceSector("CutFaceStart", startDeg, axis, reference, referenceCross, bands));
        root.AddChild(BuildCutawayFaceSector("CutFaceEnd", endDeg, axis, reference, referenceCross, bands));

        return root;
    }

    // Half-disc in plane(boundaryDir, axis): point = r*(cos(theta)*boundaryDir + sin(theta)*axis),
    // theta in [-pi/2, pi/2], r in [0,1]. Strata are concentric rings colored per band.
    private MeshInstance3D BuildCutawayFaceSector(
        string name,
        double azimuthDeg,
        UnifyMaths.Vector3D axis,
        UnifyMaths.Vector3D reference,
        UnifyMaths.Vector3D referenceCross,
        IReadOnlyList<StratumBand> bands)
    {
        const int angularSegments = 32;

        var boundaryDir = new UnifyMaths.Vector3D(
            reference.X * Math.Cos(azimuthDeg * Math.PI / 180.0) + referenceCross.X * Math.Sin(azimuthDeg * Math.PI / 180.0),
            reference.Y * Math.Cos(azimuthDeg * Math.PI / 180.0) + referenceCross.Y * Math.Sin(azimuthDeg * Math.PI / 180.0),
            reference.Z * Math.Cos(azimuthDeg * Math.PI / 180.0) + referenceCross.Z * Math.Sin(azimuthDeg * Math.PI / 180.0));

        var vertices = new List<Vector3>();
        var colors = new List<Color>();

        for (int b = 0; b < bands.Count; b++)
        {
            var band = bands[b];
            var outerR = Math.Max(0.0, band.OuterRadius);
            var innerR = Math.Max(0.0, band.InnerRadius);
            if (outerR <= innerR)
                continue;

            var bandColor = new Color(
                (float)band.Color.R,
                (float)band.Color.G,
                (float)band.Color.B);

            for (int s = 0; s < angularSegments; s++)
            {
                double t0 = -Math.PI / 2 + (s * Math.PI / angularSegments);
                double t1 = -Math.PI / 2 + ((s + 1) * Math.PI / angularSegments);

                var p0_outer = PolarToCartesian(outerR, t0, boundaryDir, axis);
                var p1_outer = PolarToCartesian(outerR, t1, boundaryDir, axis);
                var p0_inner = PolarToCartesian(innerR, t0, boundaryDir, axis);
                var p1_inner = PolarToCartesian(innerR, t1, boundaryDir, axis);

                vertices.Add(p0_outer); colors.Add(bandColor);
                vertices.Add(p1_outer); colors.Add(bandColor);
                vertices.Add(p0_inner); colors.Add(bandColor);

                vertices.Add(p1_outer); colors.Add(bandColor);
                vertices.Add(p1_inner); colors.Add(bandColor);
                vertices.Add(p0_inner); colors.Add(bandColor);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Scale = Vector3.One * 2.0f,
            MaterialOverride = HypsoPlateMaterialOverride,
        };
    }

    private static Vector3 PolarToCartesian(
        double radius,
        double theta,
        UnifyMaths.Vector3D boundaryDir,
        UnifyMaths.Vector3D axis)
    {
        var cosT = Math.Cos(theta);
        var sinT = Math.Sin(theta);
        return new Vector3(
            (float)(radius * (cosT * boundaryDir.X + sinT * axis.X)),
            (float)(radius * (cosT * boundaryDir.Y + sinT * axis.Y)),
            (float)(radius * (cosT * boundaryDir.Z + sinT * axis.Z)));
    }
}
