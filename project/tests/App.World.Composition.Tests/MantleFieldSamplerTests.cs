using System;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.Geosphere.Asthenosphere.Convection;
using UnifyMaths;
using Xunit;

namespace App.World.Composition.Tests;

// Mantle x-ray view (M-A): sampler tests against the engine's VOLUMETRIC MantleAnomalyField.
// The physics of the field itself (dip, tip depth, blanket, plumes) is tested engine-side; here we
// assert the app-side sampling contract: determinism under Parallel.For, signed cold/warm structure
// landing in the right grid cells, shell clipping, and config validation.
public class MantleFieldSamplerTests
{
    private const double TicksPerMa = 100_000.0;

    private static readonly Vector3D TrenchA = new(1, 0, 0);
    private static readonly Vector3D TrenchB = new(0, 1, 0);

    private static MantleAnomalyField TrenchOnlyField(long ageMa = 30)
    {
        var history = new PlateBoundaryHistory(new[]
        {
            new BoundarySegmentHistory(TrenchA, TrenchB, PlateHistoryKind.Convergent,
                ActiveSinceTick: 0.0, RelativeRateRadPerTick: 0.0),
        });
        return new MantleAnomalyField(new MantleFieldConfig { Seed = 7 }, history, (long)(ageMa * TicksPerMa));
    }

    private static MantleViewConfig SmallGrid => new() { GridResolution = 28 };

    [Fact]
    public void Sample_IsDeterministic_DespiteParallelSampling()
    {
        var field = TrenchOnlyField();

        var a = MantleFieldSampler.Sample(field, SmallGrid);
        var b = MantleFieldSampler.Sample(field, SmallGrid);

        Assert.Equal(a.Anomaly.Length, b.Anomaly.Length);
        Assert.True(a.Anomaly.SequenceEqual(b.Anomaly), "parallel sampling must be bit-identical run to run");
    }

    [Fact]
    public void Sample_TrenchProducesCold_AntipodeStaysWarmOrNeutral()
    {
        var field = TrenchOnlyField(ageMa: 30);
        var grid = MantleFieldSampler.Sample(field, SmallGrid);

        // The slab region (near the trench mid-direction, upper mantle) must contain negative
        // (cold) values; the antipodal upper mantle must not (no slab there).
        var trenchMid = new Vector3D(1, 1, 0).Normalize();
        double coldSide = MinNear(grid, trenchMid, radius: 0.90, angularRadius: 0.5);
        double farSide = MinNear(grid, -trenchMid, radius: 0.90, angularRadius: 0.5);

        Assert.True(coldSide < -0.05, $"expected a cold slab anomaly near the trench, min={coldSide}");
        Assert.True(farSide > -0.05, $"antipodal upper mantle should have no cold slab, min={farSide}");
    }

    [Fact]
    public void Sample_BasalBlanketIsWarm_FarFromTheSlab()
    {
        var field = TrenchOnlyField(ageMa: 30);
        var grid = MantleFieldSampler.Sample(field, SmallGrid);

        // Near the CMB on the far side of the planet the engine's basal blanket is warm.
        var farDir = -new Vector3D(1, 1, 0).Normalize();
        double warm = MaxNear(grid, farDir, radius: 0.62, angularRadius: 0.6);
        Assert.True(warm > 0.1, $"expected the warm basal blanket far from the slab, max={warm}");
    }

    [Fact]
    public void Sample_IsZeroOutsideTheShell()
    {
        var field = TrenchOnlyField();
        var cfg = SmallGrid;
        var grid = MantleFieldSampler.Sample(field, cfg);

        int n = grid.N;
        for (int zi = 0; zi < n; zi++)
        for (int yi = 0; yi < n; yi++)
        for (int xi = 0; xi < n; xi++)
        {
            var (x, y, z) = grid.GridIndexToWorld(xi, yi, zi);
            double r = Math.Sqrt(x * x + y * y + z * z);
            if (r <= cfg.InnerRadius || r >= cfg.OuterRadius)
            {
                float v = grid.Anomaly[(zi * n + yi) * n + xi];
                Assert.True(v == 0f, $"outside-shell lattice point at r={r} has value {v}");
            }
        }
    }

    [Fact]
    public void Sample_HasNoNaNOrInf()
    {
        var field = TrenchOnlyField();
        var grid = MantleFieldSampler.Sample(field, SmallGrid);
        foreach (var v in grid.Anomaly)
            Assert.False(float.IsNaN(v) || float.IsInfinity(v), "sampled grid must be finite everywhere");
    }

    [Fact]
    public void Sample_RejectsInvalidConfig()
    {
        var field = TrenchOnlyField();
        Assert.Throws<ArgumentException>(() => MantleFieldSampler.Sample(field, new MantleViewConfig { GridResolution = 1 }));
        Assert.Throws<ArgumentException>(() => MantleFieldSampler.Sample(field, new MantleViewConfig { InnerRadius = 0.9, OuterRadius = 0.8 }));
    }

    /// <summary>Minimum sampled anomaly over lattice points within <paramref name="angularRadius"/> of
    /// <paramref name="dir"/> and within half a shell-band of <paramref name="radius"/>.</summary>
    private static double MinNear(MantleScalarField grid, Vector3D dir, double radius, double angularRadius)
        => Aggregate(grid, dir, radius, angularRadius, min: true);

    private static double MaxNear(MantleScalarField grid, Vector3D dir, double radius, double angularRadius)
        => Aggregate(grid, dir, radius, angularRadius, min: false);

    private static double Aggregate(MantleScalarField grid, Vector3D dir, double radius, double angularRadius, bool min)
    {
        int n = grid.N;
        double best = min ? double.PositiveInfinity : double.NegativeInfinity;
        for (int zi = 0; zi < n; zi++)
        for (int yi = 0; yi < n; yi++)
        for (int xi = 0; xi < n; xi++)
        {
            var (x, y, z) = grid.GridIndexToWorld(xi, yi, zi);
            double r = Math.Sqrt(x * x + y * y + z * z);
            if (r < 1e-9 || Math.Abs(r - radius) > 0.08)
                continue;
            double dot = (x * dir.X + y * dir.Y + z * dir.Z) / r;
            if (dot < Math.Cos(angularRadius))
                continue;
            float v = grid.Anomaly[(zi * n + yi) * n + xi];
            best = min ? Math.Min(best, v) : Math.Max(best, v);
        }
        return best;
    }
}
