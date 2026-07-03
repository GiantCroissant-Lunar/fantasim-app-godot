using FantaSim.App.World.Rendering;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Per-vertex color envelope: the colour analogue of
/// <c>GlobeSurfaceBuilder.GatherVertexHeights</c>. Turns a per-FACE (per-cell) colour field into the
/// per-VERTEX colour field that a Gouraud-shaded renderer consumes. Each shared vertex gets the
/// component-wise arithmetic MEAN of the colours of the faces incident to it, so a corner shared by
/// cells with different ramp colours takes the average and the seam reads as a smooth gradient
/// instead of a hard step.
/// </summary>
/// <remarks>
/// <para>
/// Pure function over its inputs — no IO, no Godot types, no topology of its own. It operates on the
/// SAME shared-vertex index buffer <see cref="GlobePlateSurfaces"/> already builds for the elevation
/// envelope, so the two gathers agree vertex-for-vertex: whatever corner the elevation mean
/// smooths, the colour mean smooths too. Simple mean (not area-weighted) matches the elevation
/// convention; a vertex referenced by no face yields <see cref="RampColor"/> black (0,0,0),
/// matching the 0.0 height convention.
/// </para>
/// <para>
/// The gather is GLOBALLY consistent by construction: when <see cref="GlobePlateSurfaces"/> feeds it
/// the global triangle/cell-id arrays (deduped across ALL plates), a cross-plate boundary corner is
/// ONE global vertex incident to cells from both plates, so its mean includes both plates' colours
/// and both caps read the same value at that corner — no colour step across the seam, the same
/// property the elevation envelope already guarantees for the geometry.
/// </para>
/// </remarks>
public static class VertexColorEnvelope
{
    /// <summary>
    /// Gathers a per-face colour field into a per-vertex colour field. Each vertex colour is the
    /// component-wise arithmetic mean of the colours of the faces incident to that vertex. A vertex
    /// referenced by no face yields black (0,0,0).
    /// </summary>
    /// <param name="vertexCount">Total number of shared vertices (defines the output length).</param>
    /// <param name="triangles">Triangle corner indices; length must be a multiple of 3.</param>
    /// <param name="perFaceColors">One <see cref="RampColor"/> per triangle (face), in triangle order.</param>
    /// <returns>A per-vertex <see cref="RampColor"/> array of length <paramref name="vertexCount"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triangles"/> or <paramref name="perFaceColors"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertexCount"/> is negative.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="triangles"/> length is not a multiple of 3, <paramref name="perFaceColors"/>
    /// count does not match the face count, or a triangle index is out of range.
    /// </exception>
    public static RampColor[] GatherVertexColors(
        int vertexCount,
        IReadOnlyList<int> triangles,
        IReadOnlyList<RampColor> perFaceColors)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(perFaceColors);
        ArgumentOutOfRangeException.ThrowIfNegative(vertexCount);

        if (triangles.Count % 3 != 0)
        {
            throw new ArgumentException(
                $"Triangle index count ({triangles.Count}) must be a multiple of 3.",
                nameof(triangles));
        }

        int faceCount = triangles.Count / 3;
        if (perFaceColors.Count != faceCount)
        {
            throw new ArgumentException(
                $"Per-face color count ({perFaceColors.Count}) must equal face count ({faceCount}).",
                nameof(perFaceColors));
        }

        var sums = new double[vertexCount * 3]; // R,G,B packed contiguously per vertex
        var counts = new int[vertexCount];

        for (int t = 0; t < faceCount; t++)
        {
            var color = perFaceColors[t];
            for (int corner = 0; corner < 3; corner++)
            {
                int index = triangles[(t * 3) + corner];
                if ((uint)index >= (uint)vertexCount)
                {
                    throw new ArgumentException(
                        $"Triangle index {index} is out of range for {vertexCount} vertices.",
                        nameof(triangles));
                }
                int rb = index * 3;
                sums[rb + 0] += color.R;
                sums[rb + 1] += color.G;
                sums[rb + 2] += color.B;
                counts[index] += 1;
            }
        }

        var result = new RampColor[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            if (counts[i] == 0)
            {
                result[i] = default; // (0,0,0) — matches GatherVertexHeights' 0.0 convention
                continue;
            }
            int rb = i * 3;
            double inv = 1.0 / counts[i];
            result[i] = new RampColor(sums[rb + 0] * inv, sums[rb + 1] * inv, sums[rb + 2] * inv);
        }
        return result;
    }
}