using FantaSim.App.World;
using FantaSim.App.World.Composition;
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
    // PlateSolidBuilder output, dark unlit material). Both Scale = Vector3.One * 2.0f to match
    // PlateSurfaceRenderer and BuildCutawayFaceSector. No per-plate GPU rotation exists in this
    // path, so a baked position offset is exactly correct.
    private Node3D BuildExplodedSolidCrust(double? factorOverride = null)
    {
        var root = new Node3D { Name = "ExplodedCrust" };

        var document = _currentDocument;
        var snapshot = document?.GlobeSnapshot;
        if (document is null || snapshot is null)
            return root;

        var caps = _lastCaps;
        var centroids = _lastCentroids;
        if (caps is null || centroids is null)
            return root;

        // D1: the mantle-interior layer passes MantleLayerExplodeFactor so the slabs detach at a
        // modest, profile-declared fraction of DefaultMaxOffset. render.exploded passes null so
        // the agent look-dev knob (_explodedFactor) stays in control of that path.
        double factor = factorOverride ?? _explodedFactor;

        var thickness = ResolveCrustThicknessMetres(document);
        // D3: the slab thickness exaggeration is EXPLICIT and distinct from the surface relief
        // exaggeration (_lastExaggeration). The profile exposes the metres-to-unit-radius scale
        // PlateSolidBuilder expects (CrustThicknessExaggeration / PlanetRadiusMetres), so 30 km of
        // crust reads as ~0.038R slab walls — independent of the ~3e-5 surface relief lens.
        var solids = PlateSolidBuilder.Build(caps, thickness, _radialProfile.ThicknessDepthScale());
        var exploded = PlateSolidBuilder.ApplyExplodedFactor(solids, centroids, factor);

        var byPlate = new Dictionary<int, PlateSolidCentroid>(centroids.Count);
        foreach (var c in centroids)
            byPlate[c.PlateId] = c;

        var offsetMag = factor * PlateSolidBuilder.DefaultMaxOffset;

        for (int i = 0; i < caps.Count; i++)
        {
            var cap = caps[i];
            if (!byPlate.TryGetValue(cap.PlateId, out var centroid))
                continue;

            var solid = exploded[i];

            var topDto = BuildExplodedTopDto(cap, centroid, offsetMag);
            root.AddChild(BuildExplodedMeshInstance($"Plate{cap.PlateId}_Top", topDto, HypsoPlateMaterialOverride));

            var solidDto = BuildExplodedSolidDto(cap, solid);
            root.AddChild(BuildExplodedSolidMeshInstance($"Plate{cap.PlateId}_Solid", solidDto, PlanetShaderLibrary.ExplodedCrustDarkMaterial));
        }

        return root;
    }

    // M-B: same PlateCapMeshBuilder.Build* branch the surface uses (cached inputs), with the plate's
    // explode offset baked into the DTO positions (uniform per-plate translation — correct because
    // there is NO per-plate GPU rotation in this path).
    private PlateCapMeshDto BuildExplodedTopDto(PlateCap cap, PlateSolidCentroid centroid, double offsetMag)
    {
        var dx = centroid.CentroidDirection.X * offsetMag;
        var dy = centroid.CentroidDirection.Y * offsetMag;
        var dz = centroid.CentroidDirection.Z * offsetMag;

        PlateCapMeshDto dto = _lastIsTerrain
            ? PlateCapMeshBuilder.BuildTerrain(
                cap,
                _lastPerPlateVertexColors!,
                _lastPerCellEmission!,
                _lastJitter,
                _lastColorMode,
                _lastPerCellColor,
                _lastNormalMode)
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
    // the bottom+wall triangle range into a non-indexed Vector3 list (unlit dark material needs no
    // normals/UV).
    private static PlateCapMeshDto BuildExplodedSolidDto(PlateCap cap, PlateSolid solid)
    {
        int topIndexCount = cap.Surface.Triangles.Length;
        int bottomWallIndexCount = solid.Triangles.Length - topIndexCount;

        var positions = new float[bottomWallIndexCount * 3];
        int w = 0;
        for (int t = topIndexCount; t < solid.Triangles.Length; t++)
        {
            int idx = solid.Triangles[t];
            var p = solid.Positions[idx];
            positions[w++] = (float)p.X;
            positions[w++] = (float)p.Y;
            positions[w++] = (float)p.Z;
        }

        return new PlateCapMeshDto(
            PlateId: cap.PlateId,
            NormalMode: PlateCapMeshNormalMode.Flat,
            VertexCount: bottomWallIndexCount,
            TriangleCount: bottomWallIndexCount / 3,
            Positions: positions,
            Normals: Array.Empty<float>(),
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

    // M-B: BOTTOM+WALLS instance — unlit dark material needs only positions (no normals/UV). Matches
    // BuildCutawayFaceSector's ArrayMesh shape exactly.
    private static MeshInstance3D BuildExplodedSolidMeshInstance(string name, PlateCapMeshDto dto, Material material)
    {
        var vertices = new Vector3[dto.VertexCount];
        for (int i = 0; i < dto.VertexCount; i++)
        {
            int v3 = i * 3;
            vertices[i] = new Vector3(dto.Positions[v3 + 0], dto.Positions[v3 + 1], dto.Positions[v3 + 2]);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;

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
