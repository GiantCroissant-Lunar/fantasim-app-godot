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

public enum PlateCapMeshColorMode
{
    VertexEnvelope = 0,
    SourceCellFacet = 1,
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
        VertexTintJitter? jitter,
        PlateCapMeshColorMode colorMode = PlateCapMeshColorMode.VertexEnvelope,
        IReadOnlyList<RampColor>? perCellColors = null,
        PlateCapMeshNormalMode normalMode = PlateCapMeshNormalMode.Flat)
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
        var colorCache = provenance is null
            ? null
            : new Dictionary<int, (bool HasColor, RampColor Color)>();

        for (int t = 0; t < triCount; t++)
        {
            int cellId = t >= 0 && t < cap.CellIds.Length ? cap.CellIds[t] : -1;
            float emission = cellId >= 0 && cellId < perCellEmission.Count ? perCellEmission[cellId] : 0f;
            var flatNormal = surface.FlatNormals[t];
            RampColor? facetColor = null;
            if (colorMode == PlateCapMeshColorMode.SourceCellFacet)
            {
                facetColor = ResolveCellColor(cellId, perCellColors);
                if (jitter is not null)
                    facetColor = jitter.Apply(TriangleCenter(surface, t), facetColor.Value);
            }

            for (int v = 0; v < 3; v++)
            {
                int meshVertex = (t * 3) + v;
                int surfaceVertex = surface.Triangles[meshVertex];
                PackPoint(positions, meshVertex, surface.Positions[surfaceVertex]);
                PackPoint(normals, meshVertex, normalMode == PlateCapMeshNormalMode.Smooth
                    ? surface.SmoothNormals[surfaceVertex]
                    : flatNormal);

                RampColor color = facetColor ?? ResolveTerrainColor(
                        surfaceVertex,
                        vertexColors,
                        provenance,
                        colorCache);
                if (jitter is not null && colorMode == PlateCapMeshColorMode.VertexEnvelope)
                    color = jitter.Apply(surface.Positions[surfaceVertex], color);
                PackColor(colors, meshVertex, color);

                int uvBase = meshVertex * 2;
                uv2[uvBase + 0] = emission;
                uv2[uvBase + 1] = 0f;
            }
        }

        return new PlateCapMeshDto(
            cap.PlateId,
            normalMode,
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

    /// <summary>
    /// M0 <c>Continents</c> view (2026-07-06 spec §3.1): flat two-tone cap — land when the cap's
    /// plate is continental, ocean otherwise — with frontier cells (per-cell boundary code != 0,
    /// from the SAME reassigned membership that shaped the cap) darkened so the seam reads as a
    /// moving coastline without typed arc polylines (spec D4). Smooth normals like
    /// <see cref="BuildPlateIdentity"/>; the caller supplies zero elevations so the cap stays flat.
    /// </summary>
    public static PlateCapMeshDto BuildContinents(
        PlateCap cap,
        bool isLand,
        IReadOnlyList<byte> boundaryCells)
    {
        ArgumentNullException.ThrowIfNull(cap);
        ArgumentNullException.ThrowIfNull(boundaryCells);

        var surface = cap.Surface;
        int triCount = surface.TriangleCount;
        int vertexCount = triCount * 3;
        var positions = new float[vertexCount * 3];
        var normals = new float[vertexCount * 3];
        var colors = new float[vertexCount * 3];
        var uv2 = new float[vertexCount * 2];

        for (int t = 0; t < triCount; t++)
        {
            int cellId = t >= 0 && t < cap.CellIds.Length ? cap.CellIds[t] : -1;
            bool isFrontier = cellId >= 0 && cellId < boundaryCells.Count && boundaryCells[cellId] != 0;
            var tone = ContinentsPalette.ToneFor(isLand, isFrontier);

            for (int v = 0; v < 3; v++)
            {
                int meshVertex = (t * 3) + v;
                int surfaceVertex = surface.Triangles[meshVertex];
                PackPoint(positions, meshVertex, surface.Positions[surfaceVertex]);
                PackPoint(normals, meshVertex, surface.SmoothNormals[surfaceVertex]);
                PackColor(colors, meshVertex, tone);
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

    private static RampColor ResolveCellColor(int cellId, IReadOnlyList<RampColor>? perCellColors)
        => perCellColors is not null && cellId >= 0 && cellId < perCellColors.Count
            ? perCellColors[cellId]
            : MissingTerrainColor;

    private static CartesianPoint3 TriangleCenter(GlobeSurface surface, int triangleIndex)
    {
        int b = triangleIndex * 3;
        var p0 = surface.Positions[surface.Triangles[b + 0]];
        var p1 = surface.Positions[surface.Triangles[b + 1]];
        var p2 = surface.Positions[surface.Triangles[b + 2]];
        double x = (p0.X + p1.X + p2.X) / 3.0;
        double y = (p0.Y + p1.Y + p2.Y) / 3.0;
        double z = (p0.Z + p1.Z + p2.Z) / 3.0;
        double len = Math.Sqrt((x * x) + (y * y) + (z * z));
        return len > 1e-9
            ? new CartesianPoint3(x / len, y / len, z / len)
            : new CartesianPoint3(x, y, z);
    }

    // Resolves the terrain colour for one generated surface vertex. For fixed caps (no provenance)
    // or out-of-range base arrays it falls back to MissingTerrainColor. For adaptive caps it copies
    // Original base vertex colours and recursively averages Midpoint endpoint colours, so depth-N
    // midpoint vertices never read base-parallel arrays by generated vertex id.
    private static RampColor ResolveTerrainColor(
        int surfaceVertex,
        RampColor[] baseVertexColors,
        VertexProvenance[]? provenance,
        Dictionary<int, (bool HasColor, RampColor Color)>? cache)
    {
        if (provenance is null)
            return surfaceVertex >= 0 && surfaceVertex < baseVertexColors.Length
                ? baseVertexColors[surfaceVertex]
                : MissingTerrainColor;

        var resolved = TryResolveTerrainColor(
            surfaceVertex,
            baseVertexColors,
            provenance,
            cache ?? new Dictionary<int, (bool HasColor, RampColor Color)>(),
            new HashSet<int>());
        return resolved.HasColor ? resolved.Color : MissingTerrainColor;
    }

    private static (bool HasColor, RampColor Color) TryResolveTerrainColor(
        int surfaceVertex,
        RampColor[] baseVertexColors,
        VertexProvenance[] provenance,
        Dictionary<int, (bool HasColor, RampColor Color)> cache,
        HashSet<int> visiting)
    {
        if (surfaceVertex < 0 || surfaceVertex >= provenance.Length)
            return (false, MissingTerrainColor);
        if (cache.TryGetValue(surfaceVertex, out var cached))
            return cached;
        if (!visiting.Add(surfaceVertex))
            return (false, MissingTerrainColor);

        var result = provenance[surfaceVertex] switch
        {
            VertexProvenance.Original orig when orig.SourceIndex >= 0 && orig.SourceIndex < baseVertexColors.Length
                => (true, baseVertexColors[orig.SourceIndex]),
            VertexProvenance.Midpoint mp => AverageEndpointColors(
                TryResolveTerrainColor(mp.EndpointA, baseVertexColors, provenance, cache, visiting),
                TryResolveTerrainColor(mp.EndpointB, baseVertexColors, provenance, cache, visiting)),
            _ => (false, MissingTerrainColor),
        };

        visiting.Remove(surfaceVertex);
        cache[surfaceVertex] = result;
        return result;
    }

    private static (bool HasColor, RampColor Color) AverageEndpointColors(
        (bool HasColor, RampColor Color) a,
        (bool HasColor, RampColor Color) b)
    {
        // Match VertexColorEnvelope / GatherVertexHeights: component-wise arithmetic mean. If only
        // one endpoint has a resolvable base colour, use it; if neither does, the placeholder wins.
        return (a.HasColor, b.HasColor) switch
        {
            (true, true) => (true, new RampColor(
                (a.Color.R + b.Color.R) * 0.5,
                (a.Color.G + b.Color.G) * 0.5,
                (a.Color.B + b.Color.B) * 0.5)),
            (true, false) => a,
            (false, true) => b,
            _ => (false, MissingTerrainColor),
        };
    }
}
