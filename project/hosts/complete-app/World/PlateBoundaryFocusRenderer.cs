using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FantaSim.App.World.Dto;

namespace FantaSim.App.Common.Entry;

public partial class PlateBoundaryFocusRenderer : Node3D
{
    private enum BoundaryType
    {
        Convergent,
        Divergent,
        Transform
    }

    private readonly struct PointKey : IEquatable<PointKey>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public PointKey(GlobeVec3 v)
        {
            X = (int)Math.Round(v.X * 10000f);
            Y = (int)Math.Round(v.Y * 10000f);
            Z = (int)Math.Round(v.Z * 10000f);
        }

        public bool Equals(PointKey other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is PointKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    }

    private readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly PointKey P1;
        public readonly PointKey P2;
        public readonly GlobeVec3 V1;
        public readonly GlobeVec3 V2;

        public EdgeKey(GlobeVec3 v1, GlobeVec3 v2)
        {
            V1 = v1;
            V2 = v2;
            var pk1 = new PointKey(v1);
            var pk2 = new PointKey(v2);

            if (Compare(pk1, pk2) <= 0)
            {
                P1 = pk1;
                P2 = pk2;
            }
            else
            {
                P1 = pk2;
                P2 = pk1;
            }
        }

        private static int Compare(PointKey a, PointKey b)
        {
            int cx = a.X.CompareTo(b.X);
            if (cx != 0) return cx;
            int cy = a.Y.CompareTo(b.Y);
            if (cy != 0) return cy;
            return a.Z.CompareTo(b.Z);
        }

        public bool Equals(EdgeKey other) => P1.Equals(other.P1) && P2.Equals(other.P2);
        public override bool Equals(object? obj) => obj is EdgeKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(P1, P2);
    }

    public PlateBoundaryFocusRenderer(WorldGlobeSnapshot snapshot)
    {
        Name = "PlateBoundaryFocusRenderer";
        Scale = Vector3.One * 2.0f; // Align with PlateSurface scale

        BuildBoundaryGeometry(snapshot);
    }

    private void BuildBoundaryGeometry(WorldGlobeSnapshot snapshot)
    {
        AddChild(BuildOceanShell());

        var edgeToCells = new Dictionary<EdgeKey, List<GlobeCell>>();
        foreach (var cell in snapshot.Cells.Where(c => c.PlateId >= 0))
        {
            void AddEdge(GlobeVec3 v1, GlobeVec3 v2)
            {
                var key = new EdgeKey(v1, v2);
                if (!edgeToCells.TryGetValue(key, out var list))
                {
                    list = new List<GlobeCell>();
                    edgeToCells[key] = list;
                }
                list.Add(cell);
            }

            AddEdge(cell.C0, cell.C1);
            AddEdge(cell.C1, cell.C2);
            AddEdge(cell.C2, cell.C0);
        }

        var convVertices = new List<Vector3>();
        var convNormals = new List<Vector3>();
        var trenchVertices = new List<Vector3>();
        var trenchNormals = new List<Vector3>();

        var divVertices = new List<Vector3>();
        var divNormals = new List<Vector3>();
        var riftCoreVertices = new List<Vector3>();
        var riftCoreNormals = new List<Vector3>();

        var transVertices = new List<Vector3>();
        var transNormals = new List<Vector3>();

        foreach (var kvp in edgeToCells)
        {
            var list = kvp.Value;
            if (list.Count == 2)
            {
                var cellA = list[0];
                var cellB = list[1];
                if (cellA.PlateId != cellB.PlateId)
                {
                    var v1 = ToV3(kvp.Key.V1);
                    var v2 = ToV3(kvp.Key.V2);

                    var type = ClassifyBoundary(v1, v2, cellA, cellB, snapshot);

                    Vector3 mid = (v1 + v2) * 0.5f;
                    Vector3 normal = mid.Normalized();
                    Vector3 tangent = (v2 - v1).Normalized();
                    Vector3 u = tangent.Cross(normal).Normalized();

                    Vector3 cellACentroid = (ToV3(cellA.C0) + ToV3(cellA.C1) + ToV3(cellA.C2)) / 3f;
                    Vector3 cellBCentroid = (ToV3(cellB.C0) + ToV3(cellB.C1) + ToV3(cellB.C2)) / 3f;
                    Vector3 toB = cellBCentroid - cellACentroid;
                    if (u.Dot(toB) < 0f)
                    {
                        u = -u;
                    }

                    switch (type)
                    {
                        case BoundaryType.Convergent:
                            {
                                float w = 0.045f;
                                float hBase = 1.012f;
                                float hPeak = 1.085f;

                                Vector3 b1L = (v1 - w * u) * hBase;
                                Vector3 b1R = (v1 + w * u) * hBase;
                                Vector3 p1 = v1 * hPeak;

                                Vector3 b2L = (v2 - w * u) * hBase;
                                Vector3 b2R = (v2 + w * u) * hBase;
                                Vector3 p2 = v2 * hPeak;

                                // Left slope: b1L, p1, p2, b2L
                                AddQuad(convVertices, convNormals, b1L, p1, p2, b2L);
                                // Right slope: p1, b1R, b2R, p2
                                AddQuad(convVertices, convNormals, p1, b1R, b2R, p2);

                                AddRibbon(
                                    trenchVertices,
                                    trenchNormals,
                                    v1 + (0.055f * u),
                                    v2 + (0.055f * u),
                                    u,
                                    0.026f,
                                    1.024f);
                            }
                            break;

                        case BoundaryType.Divergent:
                            {
                                float w = 0.052f;
                                float h = 1.026f;

                                Vector3 q0 = (v1 - w * u) * h;
                                Vector3 q1 = (v1 + w * u) * h;
                                Vector3 q2 = (v2 + w * u) * h;
                                Vector3 q3 = (v2 - w * u) * h;

                                AddQuad(divVertices, divNormals, q0, q1, q2, q3);
                                AddRibbon(riftCoreVertices, riftCoreNormals, v1, v2, u, 0.008f, 1.055f);
                            }
                            break;

                        case BoundaryType.Transform:
                            {
                                var edge = v2 - v1;
                                AddRibbon(
                                    transVertices,
                                    transNormals,
                                    v1 + 0.08f * edge,
                                    v1 + 0.38f * edge,
                                    u,
                                    0.012f,
                                    1.060f);
                                AddRibbon(
                                    transVertices,
                                    transNormals,
                                    v1 + 0.62f * edge,
                                    v1 + 0.92f * edge,
                                    u,
                                    0.012f,
                                    1.060f);
                            }
                            break;
                    }
                }
            }
        }

        var convMesh = BuildMeshFromLists(convVertices, convNormals);
        if (convMesh is not null)
        {
            AddChild(BuildMeshInstance(
                "ConvergentMountainRidges",
                convMesh,
                BuildMaterial(new Color(0.78f, 0.68f, 0.48f), emission: 0.10f)));
        }

        var trenchMesh = BuildMeshFromLists(trenchVertices, trenchNormals);
        if (trenchMesh is not null)
        {
            AddChild(BuildMeshInstance(
                "ConvergentOceanTrenches",
                trenchMesh,
                BuildMaterial(new Color(0.02f, 0.10f, 0.18f, 0.92f), emission: 0.18f, transparent: true)));
        }

        var divMesh = BuildMeshFromLists(divVertices, divNormals);
        if (divMesh is not null)
        {
            AddChild(BuildMeshInstance(
                "DivergentYoungOceanCrust",
                divMesh,
                BuildMaterial(new Color(0.00f, 0.78f, 0.72f, 0.88f), emission: 0.45f, transparent: true)));
        }

        var riftCoreMesh = BuildMeshFromLists(riftCoreVertices, riftCoreNormals);
        if (riftCoreMesh is not null)
        {
            AddChild(BuildMeshInstance(
                "DivergentRiftAxis",
                riftCoreMesh,
                BuildMaterial(new Color(0.96f, 0.88f, 0.42f, 0.96f), emission: 0.60f, transparent: true)));
        }

        var transMesh = BuildMeshFromLists(transVertices, transNormals);
        if (transMesh is not null)
        {
            AddChild(BuildMeshInstance(
                "TransformFaultDashes",
                transMesh,
                BuildMaterial(new Color(0.94f, 0.93f, 0.86f, 0.94f), emission: 0.42f, transparent: true)));
        }
    }

    private static BoundaryType ClassifyBoundary(Vector3 v1, Vector3 v2, GlobeCell cellA, GlobeCell cellB, WorldGlobeSnapshot snapshot)
    {
        Vector3 mid = (v1 + v2) * 0.5f;
        Vector3 normal = mid.Normalized();
        Vector3 tangent = (v2 - v1).Normalized();
        Vector3 u = tangent.Cross(normal).Normalized();

        Vector3 cellACentroid = (ToV3(cellA.C0) + ToV3(cellA.C1) + ToV3(cellA.C2)) / 3f;
        Vector3 cellBCentroid = (ToV3(cellB.C0) + ToV3(cellB.C1) + ToV3(cellB.C2)) / 3f;
        Vector3 toB = cellBCentroid - cellACentroid;
        if (u.Dot(toB) < 0f)
        {
            u = -u;
        }

        Vector3 omegaA = GetPlateOmega(cellA.PlateId, snapshot);
        Vector3 omegaB = GetPlateOmega(cellB.PlateId, snapshot);

        Vector3 velA = omegaA.Cross(normal);
        Vector3 velB = omegaB.Cross(normal);
        Vector3 velRel = velB - velA;

        float vn = velRel.Dot(u);
        float vt = velRel.Dot(tangent);

        float relativeSpeed = velRel.Length();
        if (relativeSpeed < 1.0e-18f)
        {
            return BoundaryType.Transform;
        }

        if (Math.Abs(vn) / relativeSpeed > 0.38f)
        {
            return vn < 0f ? BoundaryType.Convergent : BoundaryType.Divergent;
        }
        return BoundaryType.Transform;
    }

    private static Vector3 GetPlateOmega(int plateId, WorldGlobeSnapshot snapshot)
    {
        foreach (var plate in snapshot.Plates)
        {
            if (plate.PlateId == plateId)
            {
                return ToV3(plate.Axis) * (float)plate.RatePerTick;
            }
        }
        return Vector3.Zero;
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
            MaterialOverride = BuildMaterial(new Color(0.01f, 0.18f, 0.25f, 0.86f), emission: 0.22f, transparent: true),
        };
    }

    private static MeshInstance3D BuildMeshInstance(string name, ArrayMesh mesh, Material material)
        => new()
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
        };

    private static StandardMaterial3D BuildMaterial(Color color, float emission, bool transparent = false)
        => new()
        {
            AlbedoColor = color,
            Transparency = transparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            EmissionEnabled = true,
            Emission = new Color(color.R, color.G, color.B),
            EmissionEnergyMultiplier = emission,
            Roughness = 0.82f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
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
