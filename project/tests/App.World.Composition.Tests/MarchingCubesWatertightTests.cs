using System.Collections.Generic;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.World.Composition.Tests;

// Mantle x-ray view (M-A), task 5: marching-cubes watertight guarantee. A smooth synthetic spherical
// anomaly (scalar = R - distance(center), isovalue 0) forms a single closed blob fully interior to
// the grid. A correctly-implemented marching-cubes pass with spatial edge deduplication produces a
// CLOSED manifold: every undirected edge is shared by exactly 2 triangles (no boundary edges).
public class MarchingCubesWatertightTests
{
    [Fact]
    public void Extract_SyntheticSphere_ProducesClosedManifold()
    {
        const int n = 24;
        const double radius = 8.0;
        var (cx, cy, cz) = ((n - 1) / 2.0, (n - 1) / 2.0, (n - 1) / 2.0);

        var scalars = new float[n * n * n];
        for (int z = 0; z < n; z++)
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            double dx = x - cx, dy = y - cy, dz = z - cz;
            double dist = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            scalars[(z * n + y) * n + x] = (float)(radius - dist);
        }

        MarchingCubes.Extract(scalars, n, n, n, isovalue: 0f, out var vertices, out var triangles);

        int triangleCount = triangles.Count / 3;
        Assert.True(triangleCount > 0, "marching cubes should produce triangles for a closed sphere anomaly.");
        Assert.True(vertices.Count >= 9, "mesh should have at least 3 vertices.");

        // Every undirected edge must be shared by exactly 2 triangles for a closed watertight surface.
        var edgeCounts = new Dictionary<(int, int), int>();
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            AddEdge(edgeCounts, a, b);
            AddEdge(edgeCounts, b, c);
            AddEdge(edgeCounts, c, a);
        }

        int boundaryEdges = 0;
        int nonPairEdges = 0;
        foreach (var kv in edgeCounts)
        {
            if (kv.Value == 1) boundaryEdges++;
            else if (kv.Value != 2) nonPairEdges++;
        }

        Assert.True(boundaryEdges == 0, $"mesh has {boundaryEdges} boundary edges (not closed).");
        Assert.True(nonPairEdges == 0, $"mesh has {nonPairEdges} edges shared by >2 triangles (non-manifold).");

        // Every vertex lies on a lattice edge crossing the sphere, so it sits within one cell
        // (spacing 1) of the true radius.
        for (int i = 0; i < vertices.Count; i += 3)
        {
            double dx = vertices[i] - cx, dy = vertices[i + 1] - cy, dz = vertices[i + 2] - cz;
            double dist = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            Assert.True(System.Math.Abs(dist - radius) <= 1.0,
                $"vertex at distance {dist} is more than one cell from the expected radius {radius}.");
        }
    }

    [Fact]
    public void Extract_IsDeterministic()
    {
        const int n = 16;
        var scalars = new float[n * n * n];
        for (int z = 0; z < n; z++)
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            double dx = x - 7.5, dy = y - 7.5, dz = z - 7.5;
            scalars[(z * n + y) * n + x] = (float)(5.0 - System.Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        MarchingCubes.Extract(scalars, n, n, n, 0f, out var v1, out var t1);
        MarchingCubes.Extract(scalars, n, n, n, 0f, out var v2, out var t2);

        Assert.Equal(v1, v2);
        Assert.Equal(t1, t2);
    }

    [Fact]
    public void Extract_TwoBlobs_ProducesTwoClosedComponents()
    {
        // Two disjoint spherical blobs exercise a different mix of the 256 cube cases and must
        // still extract closed (every undirected edge shared by exactly 2 triangles).
        const int n = 26;
        var scalars = new float[n * n * n];
        var centers = new[] { (6.5, 6.5, 6.5), (18.5, 18.5, 18.5) };
        for (int z = 0; z < n; z++)
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float best = float.NegativeInfinity;
            foreach (var (cx2, cy2, cz2) in centers)
            {
                double dx = x - cx2, dy = y - cy2, dz = z - cz2;
                float v = (float)(4.5 - System.Math.Sqrt(dx * dx + dy * dy + dz * dz));
                if (v > best) best = v;
            }
            scalars[(z * n + y) * n + x] = best;
        }

        MarchingCubes.Extract(scalars, n, n, n, 0f, out _, out var triangles);
        Assert.True(triangles.Count > 0);

        var edgeCounts = new Dictionary<(int, int), int>();
        for (int i = 0; i < triangles.Count; i += 3)
        {
            AddEdge(edgeCounts, triangles[i], triangles[i + 1]);
            AddEdge(edgeCounts, triangles[i + 1], triangles[i + 2]);
            AddEdge(edgeCounts, triangles[i + 2], triangles[i]);
        }
        Assert.All(edgeCounts.Values, c => Assert.Equal(2, c));
    }

    private static void AddEdge(Dictionary<(int, int), int> counts, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
    }
}
