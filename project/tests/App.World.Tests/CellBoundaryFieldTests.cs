using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Topography;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Pins <see cref="CellBoundaryField.Build"/>: the nearest-arc distance/type per cell, the signed distance
/// convention (negative on the subducting side of a convergent boundary), and the no-arcs case.
/// </summary>
public sealed class CellBoundaryFieldTests
{
    private static readonly GlobeVec3 East = new(1, 0, 0);
    private static readonly GlobeVec3 North = new(0, 1, 0);

    private static PlateBoundaryArc Arc(int a, int b, PlateBoundaryKind kind, params GlobeVec3[] pts) =>
        new(a, b, kind, pts);

    private static GlobeCell Cell(int id, int plate, GlobeVec3 pos) =>
        new(id, plate, pos, pos, pos);

    [Fact]
    public void Nearest_arc_distance_and_kind_correct()
    {
        // A cell at East is nearest to a divergent arc through East (distance 0).
        var arcs = new[] { Arc(0, 1, PlateBoundaryKind.Divergent, East, East) };
        var cells = new[] { Cell(0, plate: 0, East) };

        var field = CellBoundaryField.Build(cells, arcs, new Dictionary<(int, int), ConvergentBoundaryPolarity>());

        Assert.True(field[0].Found);
        Assert.Equal(0.0, field[0].SignedDistanceRad, 6);
        Assert.Equal(PlateBoundaryKind.Divergent, field[0].Kind);
        Assert.Equal(0, field[0].ArcPlateA);
        Assert.Equal(1, field[0].ArcPlateB);
    }

    [Fact]
    public void Signed_distance_negative_for_subducting_side()
    {
        // Cell on the subducting plate (1), offset from the arc so it has a real distance ⇒ negative signed.
        var arcs = new[] { Arc(0, 1, PlateBoundaryKind.Convergent, East, East) };
        var cells = new[] { Cell(0, plate: 1, North) };
        var polarity = new Dictionary<(int, int), ConvergentBoundaryPolarity>
        {
            [(0, 1)] = new ConvergentBoundaryPolarity(SubductingPlateId: 1, OverridingPlateId: 0, IsCollision: false),
        };

        var field = CellBoundaryField.Build(cells, arcs, polarity);

        Assert.True(field[0].Found);
        Assert.True(field[0].SignedDistanceRad < 0.0, $"subducting side must be negative, got {field[0].SignedDistanceRad}");
        Assert.Equal(1, field[0].SubductingPlateId);
        Assert.False(field[0].IsCollision);
    }

    [Fact]
    public void Signed_distance_positive_for_overriding_side()
    {
        var arcs = new[] { Arc(0, 1, PlateBoundaryKind.Convergent, East, East) };
        var cells = new[] { Cell(0, plate: 0, East) };
        var polarity = new Dictionary<(int, int), ConvergentBoundaryPolarity>
        {
            [(0, 1)] = new ConvergentBoundaryPolarity(SubductingPlateId: 1, OverridingPlateId: 0, IsCollision: false),
        };

        var field = CellBoundaryField.Build(cells, arcs, polarity);

        Assert.True(field[0].Found);
        Assert.True(field[0].SignedDistanceRad >= 0.0, $"overriding side must be non-negative, got {field[0].SignedDistanceRad}");
    }

    [Fact]
    public void Collision_convergent_keeps_non_negative_distance_on_both_sides()
    {
        var arcs = new[] { Arc(0, 1, PlateBoundaryKind.Convergent, East, East) };
        var cells = new[]
        {
            Cell(0, plate: 0, East),
            Cell(1, plate: 1, North),
        };
        var polarity = new Dictionary<(int, int), ConvergentBoundaryPolarity>
        {
            [(0, 1)] = new ConvergentBoundaryPolarity(SubductingPlateId: 0, OverridingPlateId: 1, IsCollision: true),
        };

        var field = CellBoundaryField.Build(cells, arcs, polarity);

        Assert.True(field[0].SignedDistanceRad >= 0.0);
        Assert.True(field[1].SignedDistanceRad >= 0.0);
        Assert.True(field[0].IsCollision);
    }

    [Fact]
    public void Symmetric_positive_for_divergent_and_transform()
    {
        var arcs = new[]
        {
            Arc(0, 1, PlateBoundaryKind.Divergent, East, East),
            Arc(2, 3, PlateBoundaryKind.Transform, North, North),
        };
        var cells = new[]
        {
            Cell(0, plate: 0, East),
            Cell(1, plate: 2, North),
        };

        var field = CellBoundaryField.Build(cells, arcs, new Dictionary<(int, int), ConvergentBoundaryPolarity>());

        Assert.True(field[0].SignedDistanceRad >= 0.0);
        Assert.True(field[1].SignedDistanceRad >= 0.0);
    }

    [Fact]
    public void No_arcs_yields_all_not_found()
    {
        var cells = new[] { Cell(0, plate: 0, East), Cell(1, plate: 1, North) };

        var field = CellBoundaryField.Build(cells, System.Array.Empty<PlateBoundaryArc>(),
            new Dictionary<(int, int), ConvergentBoundaryPolarity>());

        Assert.All(field, s => Assert.False(s.Found));
    }

    [Fact]
    public void Nearest_point_index_is_recorded()
    {
        // A two-point arc; a cell at North is closer to the North point (index 1) than the East point (index 0).
        var arcs = new[] { Arc(0, 1, PlateBoundaryKind.Divergent, East, North) };
        var cells = new[] { Cell(0, plate: 0, North) };

        var field = CellBoundaryField.Build(cells, arcs, new Dictionary<(int, int), ConvergentBoundaryPolarity>());

        Assert.Equal(1, field[0].NearestPointIndex);
    }

    [Fact]
    public void Coarse_real_boundary_cell_receives_narrow_profile_while_same_plate_interior_stays_zero()
    {
        var (boundaryCell, interiorCell, arc) = RealSharedEdgeFixture(frequency: 2);
        var polarity = new Dictionary<(int, int), ConvergentBoundaryPolarity>
        {
            [(0, 1)] = new ConvergentBoundaryPolarity(0, 1, IsCollision: false),
        };

        var field = CellBoundaryField.Build(new[] { boundaryCell, interiorCell }, new[] { arc }, polarity);
        double boundaryContribution = BoundaryProfileShape.Contribution(field[0], BoundaryProfileParameters.Default);
        double interiorContribution = BoundaryProfileShape.Contribution(field[1], BoundaryProfileParameters.Default);

        Assert.Equal(-double.Epsilon, field[0].SignedDistanceRad);
        Assert.True(boundaryContribution < -100.0,
            $"coarse subducting boundary cell must receive the 0.06-rad trench profile; was {boundaryContribution:F6}");
        Assert.Equal(0.0, interiorContribution, 9);
    }

    [Fact]
    public void Finite_cell_distance_preserves_high_frequency_boundary_profile()
    {
        var (boundaryCell, interiorCell, arc) = RealSharedEdgeFixture(frequency: 4);
        var polarity = new Dictionary<(int, int), ConvergentBoundaryPolarity>
        {
            [(0, 1)] = new ConvergentBoundaryPolarity(0, 1, IsCollision: false),
        };

        var field = CellBoundaryField.Build(new[] { boundaryCell, interiorCell }, new[] { arc }, polarity);

        Assert.Equal(-double.Epsilon, field[0].SignedDistanceRad);
        Assert.True(
            BoundaryProfileShape.Contribution(field[0], BoundaryProfileParameters.Default) < -100.0);
        Assert.Equal(
            0.0,
            BoundaryProfileShape.Contribution(field[1], BoundaryProfileParameters.Default),
            9);
    }

    [Fact]
    public void Junction_tie_uses_first_incident_edge_in_order()
    {
        var up = new GlobeVec3(0, 0, 1);
        var junctionCell = new GlobeCell(0, 0, East, North, up);
        var divergent = Arc(0, 1, PlateBoundaryKind.Divergent, East, North);
        var transform = Arc(0, 2, PlateBoundaryKind.Transform, North, up);
        var noPolarity = new Dictionary<(int, int), ConvergentBoundaryPolarity>();

        var divergentFirst = CellBoundaryField.Build(
            new[] { junctionCell },
            new[] { divergent, transform },
            noPolarity);
        var transformFirst = CellBoundaryField.Build(
            new[] { junctionCell },
            new[] { transform, divergent },
            noPolarity);

        Assert.Equal(0.0, divergentFirst[0].SignedDistanceRad);
        Assert.Equal(PlateBoundaryKind.Divergent, divergentFirst[0].Kind);
        Assert.Equal(0.0, transformFirst[0].SignedDistanceRad);
        Assert.Equal(PlateBoundaryKind.Transform, transformFirst[0].Kind);
    }

    private static (GlobeCell BoundaryCell, GlobeCell InteriorCell, PlateBoundaryArc Arc) RealSharedEdgeFixture(
        int frequency)
    {
        var tessellation = new GeodesicSphereTessellation(frequency);
        const int boundaryCellId = 0;
        int adjacentCellId = tessellation.Space
            .Neighbors(new GeodesicCoord(boundaryCellId, frequency))
            .First().FaceIndex;
        var boundaryCorners = tessellation.GetBoundary(new GeodesicCoord(boundaryCellId, frequency));
        var adjacentCorners = tessellation.GetBoundary(new GeodesicCoord(adjacentCellId, frequency));
        var shared = boundaryCorners
            .Where(point => adjacentCorners.Any(other =>
                Vector3D.Dot(point.ToVector3D(), other.ToVector3D()) >= 1.0 - 1e-12))
            .ToArray();
        Assert.Equal(2, shared.Length);

        var edgeMidpoint = Normalize(shared[0].ToVector3D() + shared[1].ToVector3D());
        int interiorCellId = Enumerable.Range(0, tessellation.CellCount)
            .Where(cell => cell != boundaryCellId && cell != adjacentCellId)
            .MinBy(cell => Vector3D.Dot(
                tessellation.GetCenter(new GeodesicCoord(cell, frequency)).ToVector3D(),
                edgeMidpoint));
        var arc = new PlateBoundaryArc(
            0,
            1,
            PlateBoundaryKind.Convergent,
            BoundaryArcSampler.SubdivideGreatCircle(shared[0], shared[1], subdiv: 16));

        return (
            ToGlobeCell(tessellation, boundaryCellId, plateId: 0, frequency),
            ToGlobeCell(tessellation, interiorCellId, plateId: 0, frequency),
            arc);
    }

    private static GlobeCell ToGlobeCell(
        GeodesicSphereTessellation tessellation,
        int cellId,
        int plateId,
        int frequency)
    {
        var corners = tessellation.GetBoundary(new GeodesicCoord(cellId, frequency));
        return new GlobeCell(
            cellId,
            plateId,
            ToGlobeVec3(corners[0]),
            ToGlobeVec3(corners[1]),
            ToGlobeVec3(corners[2]));
    }

    private static GlobeVec3 ToGlobeVec3(SphericalPoint point)
    {
        var vector = point.ToVector3D();
        return new GlobeVec3((float)vector.X, (float)vector.Y, (float)vector.Z);
    }

    private static Vector3D Normalize(Vector3D vector)
    {
        double length = vector.Length();
        return vector * (1.0 / length);
    }
}
