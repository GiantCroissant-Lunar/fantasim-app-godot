using System;
using System.Collections.Generic;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Shared;

namespace FantaSim.App.World.Globe;

public enum PlateCapMeshNormalMode
{
    Flat = 0,
    Smooth = 1,
}

/// <summary>
/// Godot-free, non-indexed triangle mesh payload for one plate cap.
/// Arrays are packed as XYZ / RGB / UV pairs, with one duplicated vertex per triangle corner.
/// </summary>
public sealed record PlateCapMeshDto(
    int PlateId,
    PlateCapMeshNormalMode NormalMode,
    int VertexCount,
    int TriangleCount,
    float[] Positions,
    float[] Normals,
    float[] Colors,
    float[] Uv2);

/// <summary>
/// Builds render-ready, engine-free plate-cap mesh data from the topology/materialized globe surface.
/// </summary>
public static class PlateCapMeshBuilder
{
    private static readonly RampColor MissingTerrainColor = new(0.3, 0.35, 0.28);

    public static PlateCapMeshDto BuildTerrain(
        PlateCap cap,
        IReadOnlyDictionary<int, RampColor[]> perPlateVertexColors,
        IReadOnlyList<float> perCellEmission,
        VertexTintJitter? jitter)
    {
        ArgumentNullException.ThrowIfNull(cap);
        ArgumentNullException.ThrowIfNull(perPlateVertexColors);
        ArgumentNullException.ThrowIfNull(perCellEmission);

        var surface = cap.Surface;
        int triCount = surface.TriangleCount;
        int vertexCount = triCount * 3;
        var positions = new float[vertexCount * 3];
        var normals = new float[vertexCount * 3];
        var colors = new float[vertexCount * 3];
        var uv2 = new float[vertexCount * 2];

        perPlateVertexColors.TryGetValue(cap.PlateId, out var vertexColors);
        vertexColors ??= Array.Empty<RampColor>();
        var provenance = cap.VertexProvenance is { Length: int provLen } candidate && provLen == surface.VertexCount
            ? candidate
            : null;

        for (int t = 0; t < triCount; t++)
        {
            int cellId = t >= 0 && t < cap.CellIds.Length ? cap.CellIds[t] : -1;
            float emission = cellId >= 0 && cellId < perCellEmission.Count ? perCellEmission[cellId] : 0f;
            var flatNormal = surface.FlatNormals[t];

            for (int v = 0; v < 3; v++)
            {
                int meshVertex = (t * 3) + v;
                int surfaceVertex = surface.Triangles[meshVertex];
                PackPoint(positions, meshVertex, surface.Positions[surfaceVertex]);
                PackPoint(normals, meshVertex, flatNormal);

                RampColor color = ResolveTerrainColor(
                    surfaceVertex,
                    vertexColors,
                    provenance);
                if (jitter is not null)
                    color = jitter.Apply(surface.Positions[surfaceVertex], color);
                PackColor(colors, meshVertex, color);

                int uvBase = meshVertex * 2;
                uv2[uvBase + 0] = emission;
                uv2[uvBase + 1] = 0f;
            }
        }

        return new PlateCapMeshDto(
            cap.PlateId,
            PlateCapMeshNormalMode.Flat,
            vertexCount,
            triCount,
            positions,
            normals,
            colors,
            uv2);
    }

    public static PlateCapMeshDto BuildPlateIdentity(PlateCap cap)
    {
        ArgumentNullException.ThrowIfNull(cap);

        var surface = cap.Surface;
        int triCount = surface.TriangleCount;
        int vertexCount = triCount * 3;
        var positions = new float[vertexCount * 3];
        var normals = new float[vertexCount * 3];
        var colors = new float[vertexCount * 3];
        var uv2 = new float[vertexCount * 2];
        var plateColor = PlateIdentityPalette.ColorFor(cap.PlateId);

        for (int t = 0; t < triCount; t++)
        {
            for (int v = 0; v < 3; v++)
            {
                int meshVertex = (t * 3) + v;
                int surfaceVertex = surface.Triangles[meshVertex];
                PackPoint(positions, meshVertex, surface.Positions[surfaceVertex]);
                PackPoint(normals, meshVertex, surface.SmoothNormals[surfaceVertex]);
                PackColor(colors, meshVertex, plateColor);
            }
        }

        return new PlateCapMeshDto(
            cap.PlateId,
            PlateCapMeshNormalMode.Smooth,
            vertexCount,
            triCount,
            positions,
            normals,
            colors,
            uv2);
    }

    private static void PackPoint(float[] target, int vertexIndex, CartesianPoint3 point)
    {
        int b = vertexIndex * 3;
        target[b + 0] = (float)point.X;
        target[b + 1] = (float)point.Y;
        target[b + 2] = (float)point.Z;
    }

    private static void PackColor(float[] target, int vertexIndex, RampColor color)
    {
        int b = vertexIndex * 3;
        target[b + 0] = (float)color.R;
        target[b + 1] = (float)color.G;
        target[b + 2] = (float)color.B;
    }

    // Resolves the terrain colour for one generated surface vertex. For fixed caps (no provenance)
    // or out-of-range base arrays it falls back to MissingTerrainColor. For adaptive caps it copies
    // the base vertex colour for an Original vertex and interpolates the two endpoint base colours
    // for a Midpoint vertex, so refined midpoint vertices never read the fallback.
    private static RampColor ResolveTerrainColor(
        int surfaceVertex,
        RampColor[] baseVertexColors,
        VertexProvenance[]? provenance)
    {
        if (provenance is null)
            return surfaceVertex >= 0 && surfaceVertex < baseVertexColors.Length
                ? baseVertexColors[surfaceVertex]
                : MissingTerrainColor;

        if (surfaceVertex < 0 || surfaceVertex >= provenance.Length)
            return MissingTerrainColor;

        var p = provenance[surfaceVertex];
        return p is VertexProvenance.Midpoint mp
            ? InterpolateEndpointColors(mp.EndpointA, mp.EndpointB, baseVertexColors)
            : p is VertexProvenance.Original orig && orig.SourceIndex >= 0 && orig.SourceIndex < baseVertexColors.Length
                ? baseVertexColors[orig.SourceIndex]
                : MissingTerrainColor;
    }

    private static RampColor InterpolateEndpointColors(int a, int b, RampColor[] baseVertexColors)
    {
        bool hasA = a >= 0 && a < baseVertexColors.Length;
        bool hasB = b >= 0 && b < baseVertexColors.Length;
        // Match VertexColorEnvelope / GatherVertexHeights: component-wise arithmetic mean. If only
        // one endpoint has a base colour, use it; if neither does, the placeholder colour wins.
        return (hasA, hasB) switch
        {
            (true, true) => new RampColor(
                (baseVertexColors[a].R + baseVertexColors[b].R) * 0.5,
                (baseVertexColors[a].G + baseVertexColors[b].G) * 0.5,
                (baseVertexColors[a].B + baseVertexColors[b].B) * 0.5),
            (true, false) => baseVertexColors[a],
            (false, true) => baseVertexColors[b],
            _ => MissingTerrainColor,
        };
    }
}
