using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Rendering;
using Godot;

namespace FantaSim.App.Common.Entry;

/// <summary>
/// Renders plate boundaries as SMOOTH great-circle polylines coloured by boundary kind, driven by
/// <see cref="PlateBoundaryArc"/> topology truth. Each arc's points are pre-subdivided Godot-free
/// (great-circle interpolation between the topology's ordered sample points), so here we only lift
/// them into a thin ribbon quad-strip that hugs the sphere just above the plate caps. This replaces
/// the earlier cell-edge derivation, which followed the 1280-cell grid and looked jagged.
/// </summary>
public partial class PlateBoundaryFocusRenderer : Node3D
{
    public PlateBoundaryFocusRenderer(IReadOnlyList<PlateBoundaryArc> arcs)
    {
        Name = "PlateBoundaryFocusRenderer";
        Scale = Vector3.One * 2.0f; // Align with PlateSurface scale.

        BuildBoundaryGeometry(arcs);
    }

    private void BuildBoundaryGeometry(IReadOnlyList<PlateBoundaryArc> arcs)
    {
        AddChild(BuildOceanShell());

        var byKind = new Dictionary<PlateBoundaryKind, Bucket>();
        foreach (var arc in arcs)
        {
            if (arc.Kind == PlateBoundaryKind.Inactive) continue;
            if (arc.Points.Count < 2) continue;

            if (!byKind.TryGetValue(arc.Kind, out var bucket))
            {
                bucket = new Bucket();
                byKind[arc.Kind] = bucket;
            }

            var style = BoundaryStyleMapper.Resolve(arc.Kind);
            AppendArcRibbon(bucket, arc.Points, (float)style.RibbonHalfWidth, (float)style.SurfaceHeight);
        }

        foreach (var (kind, bucket) in byKind)
        {
            var mesh = BuildMeshFromLists(bucket.Vertices, bucket.Normals);
            if (mesh is null) continue;

            var style = BoundaryStyleMapper.Resolve(kind);
            AddChild(BuildMeshInstance(
                KindName(kind),
                mesh,
                BuildMaterial(ToColor(style.Color), (float)style.EmissionEnergy, style.RenderOnTop)));
        }
    }

    private static void AppendArcRibbon(Bucket bucket, IReadOnlyList<GlobeVec3> points, float halfWidth, float height)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            var a = ToV3(points[i]);
            var b = ToV3(points[i + 1]);

            var tangent = b - a;
            if (tangent.LengthSquared() < 1e-10f) continue;
            tangent = tangent.Normalized();

            var radial = (a + b).Normalized();
            var side = tangent.Cross(radial);
            if (side.LengthSquared() < 1e-10f) continue;
            side = side.Normalized();

            AddRibbon(bucket.Vertices, bucket.Normals, a, b, side, halfWidth, height);
        }
    }

    private static string KindName(PlateBoundaryKind kind) => kind switch
    {
        PlateBoundaryKind.Convergent => "ConvergentBoundaries",
        PlateBoundaryKind.Divergent  => "DivergentBoundaries",
        PlateBoundaryKind.Transform  => "TransformBoundaries",
        _                            => "InactiveBoundaries",
    };

    private static Color ToColor(RampColor c) => new((float)c.R, (float)c.G, (float)c.B);

    private sealed class Bucket
    {
        public List<Vector3> Vertices { get; } = new();
        public List<Vector3> Normals { get; } = new();
    }

    private static void AddQuad(List<Vector3> vertices, List<Vector3> normals, Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3)
    {
        vertices.Add(q0);
        vertices.Add(q1);
        vertices.Add(q2);

        var n1 = CalculateNormal(q0, q1, q2);
        normals.Add(n1);
        normals.Add(n1);
        normals.Add(n1);

        vertices.Add(q0);
        vertices.Add(q2);
        vertices.Add(q3);

        var n2 = CalculateNormal(q0, q2, q3);
        normals.Add(n2);
        normals.Add(n2);
        normals.Add(n2);
    }

    private static void AddRibbon(
        List<Vector3> vertices,
        List<Vector3> normals,
        Vector3 start,
        Vector3 end,
        Vector3 side,
        float halfWidth,
        float height)
    {
        var q0 = (start - (halfWidth * side)).Normalized() * height;
        var q1 = (start + (halfWidth * side)).Normalized() * height;
        var q2 = (end + (halfWidth * side)).Normalized() * height;
        var q3 = (end - (halfWidth * side)).Normalized() * height;
        AddQuad(vertices, normals, q0, q1, q2, q3);
    }

    private static MeshInstance3D BuildOceanShell()
    {
        var mesh = new SphereMesh
        {
            Radius = 0.992f,
            Height = 1.984f,
            RadialSegments = 96,
            Rings = 48,
        };

        return new MeshInstance3D
        {
            Name = "PlateFocusOceanShell",
            Mesh = mesh,
            MaterialOverride = BuildMaterial(new Color(0.01f, 0.18f, 0.25f, 0.86f), emission: 0.22f),
        };
    }

    private static MeshInstance3D BuildMeshInstance(string name, ArrayMesh mesh, Material material)
        => new()
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
        };

    private static StandardMaterial3D BuildMaterial(Color color, float emission, bool renderOnTop = false)
        => new()
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            EmissionEnabled = true,
            Emission = new Color(color.R, color.G, color.B),
            EmissionEnergyMultiplier = emission,
            Roughness = 0.82f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = renderOnTop ? 10 : 0,
        };

    private static Vector3 CalculateNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var u = b - a;
        var v = c - a;
        var normal = u.Cross(v);
        return normal.LengthSquared() > 0.000001f ? normal.Normalized() : a.Normalized();
    }

    private static ArrayMesh? BuildMeshFromLists(List<Vector3> vertices, List<Vector3> normals)
    {
        if (vertices.Count == 0)
            return null;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static Vector3 ToV3(GlobeVec3 value)
        => new(value.X, value.Y, value.Z);
}
