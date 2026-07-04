using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Topography;
using FantaSim.Geosphere.Crust;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class BoundarySectionBuilderTests
{
    private static readonly BoundaryProfileParameters P = BoundaryProfileParameters.Default;
    private static readonly GlobeVec3 X = new(1, 0, 0);
    private static readonly GlobeVec3 Y = new(0, 1, 0);

    [Fact]
    public void BuildForArc_convergent_subduction_preserves_polarity_and_profile_shape()
    {
        var arc = Arc(PlateBoundaryKind.Convergent);
        var globe = Globe();
        var state = new Dictionary<int, CellCrustState>
        {
            [0] = State(0, continentalFraction: 1.0),
            [1] = State(1, continentalFraction: 0.0),
        };

        var section = BoundarySectionBuilder.BuildForArc(
            globe,
            arc,
            state,
            features: null,
            P,
            sampleCount: 65);

        Assert.NotNull(section);
        Assert.Equal(PlateBoundaryKind.Convergent, section!.Kind);
        Assert.Equal(1, section.SubductingPlateId);
        Assert.False(section.IsCollision);
        Assert.Contains(section.InteriorBands, band => band.Label == "crust");

        double trench = section.Samples
            .Where(sample => sample.SignedDistanceRad < 0.0)
            .Min(sample => sample.ElevationMetres);
        double arcRise = section.Samples
            .Where(sample => sample.SignedDistanceRad > 0.0)
            .Max(sample => sample.ElevationMetres);

        Assert.True(trench < -1000.0, $"expected a trench on the subducting side, got {trench}");
        Assert.True(arcRise > 1000.0, $"expected an overriding-side volcanic arc/uplift, got {arcRise}");
    }

    [Fact]
    public void BuildForArc_divergent_section_has_rift_axis_below_flanks()
    {
        var section = BoundarySectionBuilder.BuildForArc(
            Globe(),
            Arc(PlateBoundaryKind.Divergent),
            new Dictionary<int, CellCrustState>(),
            features: null,
            P,
            sampleCount: 65);

        Assert.NotNull(section);
        Assert.Equal(PlateBoundaryKind.Divergent, section!.Kind);

        var axis = section.Samples.MinBy(sample => Math.Abs(sample.SignedDistanceRad));
        double highestFlank = section.Samples.Max(sample => sample.ElevationMetres);

        Assert.True(highestFlank > axis.ElevationMetres,
            $"expected divergent flanks ({highestFlank}) above the rift axis ({axis.ElevationMetres})");
    }

    [Fact]
    public void BuildForArc_transform_section_is_narrow_and_subtle()
    {
        var section = BoundarySectionBuilder.BuildForArc(
            Globe(),
            Arc(PlateBoundaryKind.Transform),
            new Dictionary<int, CellCrustState>(),
            features: null,
            P,
            sampleCount: 65);

        Assert.NotNull(section);
        Assert.Equal(PlateBoundaryKind.Transform, section!.Kind);

        double maxAbs = section.Samples.Max(sample => Math.Abs(sample.ElevationMetres));
        var edge = section.Samples.OrderByDescending(sample => Math.Abs(sample.SignedDistanceRad)).First();

        Assert.True(maxAbs <= P.TransformScarpAmplitude + 1e-6,
            $"transform section must stay subtle, max abs={maxAbs}");
        Assert.Equal(0.0, edge.ElevationMetres, precision: 9);
    }

    private static WorldGlobeSnapshot Globe()
        => new(
            Frequency: 1,
            CellCount: 2,
            PlateCount: 2,
            TicksPerAnchor: 100_000L,
            Cells: new[]
            {
                Cell(0, plateId: 0, X),
                Cell(1, plateId: 1, Y),
            },
            Plates: Array.Empty<GlobePlate>());

    private static GlobeCell Cell(int id, int plateId, GlobeVec3 point)
        => new(id, plateId, point, point, point);

    private static PlateBoundaryArc Arc(PlateBoundaryKind kind)
        => new(0, 1, kind, new[] { X, Y });

    private static CellCrustState State(int cellId, double continentalFraction)
        => new(
            cellId,
            ContinentalFraction: continentalFraction,
            OrogenicPressure: 0.0,
            VolcanicActivity: 0.0,
            CrustAgeTicks: 0.0);
}
