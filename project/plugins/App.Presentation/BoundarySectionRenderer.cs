using System;
using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Rendering;
using Godot;

namespace FantaSim.App.Presentation;

/// <summary>
/// Bounded small-multiples renderer for <see cref="BoundarySectionDocument"/>: up to three flat
/// world-space panels laid out side by side, each encoding one boundary-normal section's interior
/// stratum bands, surface profile, kind accent, and (for a non-collision convergent section with a
/// subducting plate) a dipping slab guide on the negative signed-distance side. Godot-only and
/// data-consuming: it reads the document and the shared <see cref="BoundaryStyleMapper"/> accent
/// palette, and touches no App.World.Topography type.
/// </summary>
public partial class BoundarySectionRenderer : Node3D
{
    private const float PanelWidth = 2.0f;
    private const float PanelHeight = 2.0f;
    private const float PanelSpacing = 2.8f;
    private const float SurfaceHalfThickness = 0.025f;
    private const float FrameHalfThickness = 0.015f;
    private const float SlabHalfThickness = 0.02f;
    private const float StrataZ = -0.015f;
    private const float AccentZ = 0.04f;

    public BoundarySectionRenderer(IReadOnlyList<BoundarySectionDocument> sections)
    {
        Name = "BoundarySectionRenderer";
        var plans = BoundarySectionPanelPlanner.Create(sections);
        if (plans.Count == 0)
            return;

        int count = plans.Count;
        float groupOffset = -((count - 1) * PanelSpacing) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var panel = BuildPanel(plans[i]);
            panel.Position = new Vector3(groupOffset + i * PanelSpacing, 0.0f, 0.0f);
            AddChild(panel);
        }
    }

    private static Node3D BuildPanel(BoundarySectionPanelPlan plan)
    {
        var section = plan.Section;
        var root = new Node3D { Name = plan.Name };

        var style = BoundaryStyleMapper.Resolve(section.Kind);
        var accent = ToColor(style.Color);

        var stratumMesh = BuildStratumMesh(section);
        if (stratumMesh is not null)
            root.AddChild(BuildMeshInstance("Strata", stratumMesh, BuildStratumMaterial()));

        var accentMesh = BuildAccentMesh(plan);
        if (accentMesh is not null)
            root.AddChild(BuildMeshInstance("Accent", accentMesh, BuildAccentMaterial(accent, (float)style.EmissionEnergy)));

        return root;
    }

    private static ArrayMesh? BuildStratumMesh(BoundarySectionDocument section)
    {
        var bands = section.InteriorBands;
        if (bands is null || bands.Count == 0)
            return null;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();

        var left = -PanelWidth * 0.5f;
        var right = PanelWidth * 0.5f;

        foreach (var band in bands)
        {
            var outer = Math.Max(0.0, band.OuterRadius);
            var inner = Math.Max(0.0, band.InnerRadius);
            if (outer <= inner)
                continue;

            var yOuter = RadiusToY(outer);
            var yInner = RadiusToY(inner);
            var color = Brighten(new Color((float)band.Color.R, (float)band.Color.G, (float)band.Color.B));

            AddQuad(vertices, normals, colors, color,
                new Vector3(left, yInner, StrataZ),
                new Vector3(right, yInner, StrataZ),
                new Vector3(right, yOuter, StrataZ),
                new Vector3(left, yOuter, StrataZ));
        }

        return BuildColoredMesh(vertices, normals, colors);
    }

    private static ArrayMesh? BuildAccentMesh(BoundarySectionPanelPlan plan)
    {
        var section = plan.Section;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();

        AppendProfileRibbon(vertices, normals, section);
        AppendFrame(vertices, normals);
        AppendSlabGuide(vertices, normals, section, plan.DrawSlabGuide);

        if (vertices.Count == 0)
            return null;

        return BuildPlainMesh(vertices, normals);
    }

    private static void AppendProfileRibbon(List<Vector3> vertices, List<Vector3> normals, BoundarySectionDocument section)
    {
        var samples = section.Samples;
        if (samples is null || samples.Count < 2)
            return;

        double minSd = double.MaxValue;
        double maxSd = double.MinValue;
        for (int i = 0; i < samples.Count; i++)
        {
            var sd = samples[i].SignedDistanceRad;
            if (sd < minSd) minSd = sd;
            if (sd > maxSd) maxSd = sd;
        }

        double mid = (minSd + maxSd) * 0.5;
        double halfRange = Math.Max(maxSd - mid, mid - minSd);
        if (halfRange < 1e-9)
            halfRange = 0.01;

        var exag = section.Exaggeration > 0.0 ? section.Exaggeration : 1.0;
        var radius = section.PlanetRadiusMetres > 0.0 ? section.PlanetRadiusMetres : 6_371_000.0;
        float halfPanel = PanelWidth * 0.5f;
        float clampY = PanelHeight * 0.5f;

        Vector3 Prev(double sd, double elev)
        {
            var x = (float)((PanelWidth * 0.5) * ((sd - mid) / halfRange));
            if (x < -halfPanel) x = -halfPanel;
            if (x > halfPanel) x = halfPanel;
            var r = 1.0 + (elev / radius) * exag;
            var y = RadiusToY(r);
            if (y < -clampY) y = -clampY;
            if (y > clampY) y = clampY;
            return new Vector3(x, y, AccentZ);
        }

        for (int i = 0; i < samples.Count - 1; i++)
        {
            var a = Prev(samples[i].SignedDistanceRad, samples[i].ElevationMetres);
            var b = Prev(samples[i + 1].SignedDistanceRad, samples[i + 1].ElevationMetres);
            AddQuad(vertices, normals,
                new Vector3(a.X, a.Y - SurfaceHalfThickness, 0f),
                new Vector3(b.X, b.Y - SurfaceHalfThickness, 0f),
                new Vector3(b.X, b.Y + SurfaceHalfThickness, 0f),
                new Vector3(a.X, a.Y + SurfaceHalfThickness, 0f));
        }
    }

    private static void AppendFrame(List<Vector3> vertices, List<Vector3> normals)
    {
        var left = -PanelWidth * 0.5f;
        var right = PanelWidth * 0.5f;
        var bottom = -PanelHeight * 0.5f;
        var top = PanelHeight * 0.5f;
        var t = FrameHalfThickness;

        AddQuad(vertices, normals,
            new Vector3(left, top - t, AccentZ),
            new Vector3(right, top - t, AccentZ),
            new Vector3(right, top, AccentZ),
            new Vector3(left, top, AccentZ));
        AddQuad(vertices, normals,
            new Vector3(left, bottom, AccentZ),
            new Vector3(right, bottom, AccentZ),
            new Vector3(right, bottom + t, AccentZ),
            new Vector3(left, bottom + t, AccentZ));
        AddQuad(vertices, normals,
            new Vector3(left, bottom, AccentZ),
            new Vector3(left + t, bottom, AccentZ),
            new Vector3(left + t, top, AccentZ),
            new Vector3(left, top, AccentZ));
        AddQuad(vertices, normals,
            new Vector3(right - t, bottom, AccentZ),
            new Vector3(right, bottom, AccentZ),
            new Vector3(right, top, AccentZ),
            new Vector3(right - t, top, AccentZ));
    }

    // Slab dips from the boundary (x=0, surface) down into the negative signed-distance side,
    // the subducting plate's hanging wall.
    private static void AppendSlabGuide(
        List<Vector3> vertices,
        List<Vector3> normals,
        BoundarySectionDocument section,
        bool drawSlabGuide)
    {
        if (!drawSlabGuide) return;

        var start = new Vector3(0f, PanelHeight * 0.5f, AccentZ);
        var end = new Vector3(-PanelWidth * 0.25f, -PanelHeight * 0.25f, AccentZ);
        var dir = end - start;
        var perp = new Vector3(-dir.Y, dir.X, 0f);
        if (perp.LengthSquared() < 1e-10f)
            return;
        perp = perp.Normalized() * SlabHalfThickness;

        AddQuad(vertices, normals,
            start + perp,
            start - perp,
            end - perp,
            end + perp);
    }

    // Radius 1.0 maps to the panel top, radius 0.0 to the panel bottom (planet center).
    private static float RadiusToY(double radius)
        => PanelHeight * (float)(radius - 0.5);

    private static Color ToColor(RampColor c)
        => new((float)c.R, (float)c.G, (float)c.B);

    private static MeshInstance3D BuildMeshInstance(string name, ArrayMesh mesh, Material material)
        => new()
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

    private static StandardMaterial3D BuildStratumMaterial()
        => new()
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Roughness = 0.9f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = 9,
        };

    private static StandardMaterial3D BuildAccentMaterial(Color color, float emission)
        => new()
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = MathF.Max(3.0f, emission),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Roughness = 0.6f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = 10,
        };

    private static Color Brighten(Color color)
        => color.Lerp(Colors.White, 0.38f);

    private static void AddQuad(List<Vector3> vertices, List<Vector3> normals,
        Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3)
    {
        var n1 = CalculateNormal(q0, q1, q2);
        vertices.Add(q0); vertices.Add(q1); vertices.Add(q2);
        normals.Add(n1); normals.Add(n1); normals.Add(n1);

        var n2 = CalculateNormal(q0, q2, q3);
        vertices.Add(q0); vertices.Add(q2); vertices.Add(q3);
        normals.Add(n2); normals.Add(n2); normals.Add(n2);
    }

    private static void AddQuad(List<Vector3> vertices, List<Vector3> normals, List<Color> colors, Color color,
        Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3)
    {
        var n1 = CalculateNormal(q0, q1, q2);
        vertices.Add(q0); vertices.Add(q1); vertices.Add(q2);
        normals.Add(n1); normals.Add(n1); normals.Add(n1);
        colors.Add(color); colors.Add(color); colors.Add(color);

        var n2 = CalculateNormal(q0, q2, q3);
        vertices.Add(q0); vertices.Add(q2); vertices.Add(q3);
        normals.Add(n2); normals.Add(n2); normals.Add(n2);
        colors.Add(color); colors.Add(color); colors.Add(color);
    }

    private static Vector3 CalculateNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var u = b - a;
        var v = c - a;
        var normal = u.Cross(v);
        return normal.LengthSquared() > 0.000001f ? normal.Normalized() : new Vector3(0f, 0f, 1f);
    }

    private static ArrayMesh? BuildColoredMesh(List<Vector3> vertices, List<Vector3> normals, List<Color> colors)
    {
        if (vertices.Count == 0)
            return null;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static ArrayMesh? BuildPlainMesh(List<Vector3> vertices, List<Vector3> normals)
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
}
