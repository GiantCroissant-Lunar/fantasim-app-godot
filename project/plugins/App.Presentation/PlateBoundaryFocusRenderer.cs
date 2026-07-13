using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Rendering;
using Godot;

namespace FantaSim.App.Presentation;

/// <summary>
/// Renders plate boundaries as SMOOTH great-circle polylines coloured by boundary kind, driven by
/// <see cref="PlateBoundaryArc"/> topology truth. Each local shared-edge segment is pre-subdivided
/// Godot-free with great-circle interpolation, so here we only batch the segments by kind and lift
/// them into thin ribbon quad-strips that hug the sphere just above the plate caps.
/// </summary>
public partial class PlateBoundaryFocusRenderer : Node3D
{
    // M-A mantle x-ray look pass: when the mantle view is active the boundary arcs restyle to THIN,
    // desaturated soft-green filaments (spec NORTH-STAR eye-gate §4 / reference 2: "green boundary
    // wireframe" over a ghosted shell) so they locate trenches/ridges without competing with the
    // interior isosurfaces. The ocean shell and every arc POSITION are untouched; only ribbon
    // half-width, color, and emission change. Toggling is idempotent and rebuilds only the per-kind
    // arc meshes — the ocean shell is built once and never restyles.
    private const double MantleRibbonWidthScale = 0.25; // 4x thinner filament (spec: ~3-4x).
    private const double MantleEmissionEnergy = 0.35;   // soft glow, not a beacon.

    private readonly IReadOnlyList<PlateBoundaryArc> _arcs;
    private readonly List<MeshInstance3D> _arcInstances = new();
    private bool _mantleStyleActive;

    public PlateBoundaryFocusRenderer(IReadOnlyList<PlateBoundaryArc> arcs)
    {
        Name = "PlateBoundaryFocusRenderer";
        Scale = Vector3.One * 2.0f; // Align with PlateSurface scale.

        _arcs = arcs;
        // The ocean shell is structural (dark backdrop for the arcs in the surface view) and does not
        // restyle with the mantle toggle, so it is built once and persists across arc rebuilds.
        AddChild(BuildOceanShell());
        RebuildArcGeometry(mantleStyle: false);
    }

    // M-A: restyle the boundary arcs for the mantle x-ray view. Idempotent — a no-op when the
    // requested state matches the current state, so it is safe to invoke on every ApplyTimelineTick
    // (the same path that drives arc visibility). Rebinds reconstruct this renderer fresh, so the
    // mantle style re-applies through the first ApplyTimelineTick after mount when x-ray is active.
    public void ApplyMantleViewStyle(bool active)
    {
        if (active == _mantleStyleActive)
            return;
        _mantleStyleActive = active;
        RebuildArcGeometry(mantleStyle: active);
    }

    // Mantle arc style derives from the normal doctrine but narrows the ribbon, swaps the saturated
    // primaries for a near-uniform desaturated green-white family (subtle hue shifts preserve
    // type-coding), and lowers emission so the filaments locate without dominating. SurfaceHeight and
    // RenderOnTop are inherited unchanged so arc positions and draw order are identical to the normal
    // view.
    private static BoundaryStyle MantleStyle(PlateBoundaryKind kind)
    {
        var baseStyle = BoundaryStyleMapper.Resolve(kind);
        var mantleColor = kind switch
        {
            PlateBoundaryKind.Convergent => new RampColor(0.74, 0.86, 0.70), // soft green, slightly warm
            PlateBoundaryKind.Divergent => new RampColor(0.66, 0.84, 0.78),  // soft green, slightly cool
            PlateBoundaryKind.Transform => new RampColor(0.78, 0.87, 0.74),  // soft green, slightly yellow
            _ => new RampColor(0.70, 0.80, 0.72),                             // muted green-gray
        };
        return baseStyle with
        {
            Color = mantleColor,
            RibbonHalfWidth = baseStyle.RibbonHalfWidth * MantleRibbonWidthScale,
            EmissionEnergy = MantleEmissionEnergy,
        };
    }

    private static BoundaryStyle ResolveStyle(PlateBoundaryKind kind, bool mantleStyle)
        => mantleStyle ? MantleStyle(kind) : BoundaryStyleMapper.Resolve(kind);

    private void RebuildArcGeometry(bool mantleStyle)
    {
        foreach (var instance in _arcInstances)
        {
            if (instance is not null && GodotObject.IsInstanceValid(instance))
            {
                instance.GetParent()?.RemoveChild(instance);
                instance.QueueFree();
            }
        }
        _arcInstances.Clear();

        var byKind = new Dictionary<PlateBoundaryKind, Bucket>();
        foreach (var arc in _arcs)
        {
            if (arc.Kind == PlateBoundaryKind.Inactive) continue;
            if (arc.Points.Count < 2) continue;

            if (!byKind.TryGetValue(arc.Kind, out var bucket))
            {
                bucket = new Bucket();
                byKind[arc.Kind] = bucket;
            }

            var style = ResolveStyle(arc.Kind, mantleStyle);
            AppendArcRibbon(bucket, arc.Points, (float)style.RibbonHalfWidth, (float)style.SurfaceHeight);
        }

        foreach (var (kind, bucket) in byKind)
        {
            var mesh = BuildMeshFromLists(bucket.Vertices, bucket.Normals);
            if (mesh is null) continue;

            var style = ResolveStyle(kind, mantleStyle);
            var instance = BuildMeshInstance(
                KindName(kind),
                mesh,
                BuildMaterial(ToColor(style.Color), (float)style.EmissionEnergy, style.RenderOnTop));
            AddChild(instance);
            _arcInstances.Add(instance);
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
