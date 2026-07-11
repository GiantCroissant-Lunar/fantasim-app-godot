using System;
using System.Collections.Generic;
using FantaSim.App.Presentation;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Shared;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public sealed class PlateSurfaceMeshFactoryTests
{
    // GlobeCell corners are GlobeVec3 (float X/Y/Z), NOT CartesianPoint3 (verified against
    // contracts/App.World/Dto/WorldDtos.cs) — the plan's draft test used CartesianPoint3, adjusted
    // here to match the real record shape (see AGENT-SUMMARY.md Task 2 deviations).
    private static GlobeCell MakeCell(int cellId, GlobeVec3 c0, GlobeVec3 c1, GlobeVec3 c2)
        => new(cellId, PlateId: 0, c0, c1, c2);

    [Fact]
    public void BuildCellCentersNormalizesCentroidsAndSkipsOutOfRangeIds()
    {
        // One valid unit-ish cell, one out-of-range id, one degenerate (zero centroid) cell.
        var valid = MakeCell(cellId: 0,
            c0: new GlobeVec3(1, 0, 0), c1: new GlobeVec3(0, 1, 0), c2: new GlobeVec3(0, 0, 1));
        var outOfRange = MakeCell(cellId: 7,
            c0: new GlobeVec3(1, 0, 0), c1: new GlobeVec3(0, 1, 0), c2: new GlobeVec3(0, 0, 1));
        var degenerate = MakeCell(cellId: 1,
            c0: new GlobeVec3(1, 0, 0), c1: new GlobeVec3(-1, 0, 0), c2: new GlobeVec3(0, 0, 0));

        var centers = PlateSurfaceMeshFactory.BuildCellCenters(2, new[] { valid, outOfRange, degenerate });

        Assert.Equal(2, centers.Length);
        Assert.NotNull(centers[0]);
        var c = centers[0]!;
        var len = Math.Sqrt((c.X * c.X) + (c.Y * c.Y) + (c.Z * c.Z));
        Assert.Equal(1.0, len, precision: 9);   // unit-normalized
        Assert.Null(centers[1]);                 // degenerate centroid skipped
    }

    [Fact]
    public void BuildContinentsCellColorsUsesHalfFractionThresholdAndDefaultsToOcean()
    {
        var fractions = new Dictionary<int, double> { [0] = 0.75, [1] = 0.49 }; // cell 2 absent
        var colors = PlateSurfaceMeshFactory.BuildContinentsCellColors(3, fractions);

        Assert.Equal(3, colors.Length);
        Assert.Equal(ContinentsPalette.ToneFor(isLand: true, isFrontier: false), colors[0]);
        Assert.Equal(ContinentsPalette.ToneFor(isLand: false, isFrontier: false), colors[1]);
        Assert.Equal(ContinentsPalette.ToneFor(isLand: false, isFrontier: false), colors[2]);
    }
}
