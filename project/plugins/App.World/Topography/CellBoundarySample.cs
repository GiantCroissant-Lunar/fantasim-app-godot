using FantaSim.App.World;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World.Topography;

/// <summary>
/// One cell's relationship to its nearest plate boundary, computed Godot-free from the typed boundary
/// arcs, with exact zero distance when an edge-local arc is one of the cell's own edges (see
/// <see cref="CellBoundaryField"/>). Consumed by
/// <see cref="BoundaryProfileShape.Contribution"/> to shape the per-cell boundary-profile elevation term.
/// </summary>
/// <param name="Found">False when there are no boundary arcs (pre-onset / non-plate regime): the contribution is zero.</param>
/// <param name="SignedDistanceRad">Angular distance from the cell footprint to an incident boundary
/// edge, otherwise the great-circle centroid-to-nearest-arc-point distance (rad). For
/// convergent subduction: negative on the subducting (down-going) side, positive on the overriding side.
/// For convergent collision, divergent, and transform: always non-negative (the profile is symmetric).</param>
/// <param name="Kind">The nearest boundary's type (Convergent/Divergent/Transform). Inactive arcs are skipped.</param>
/// <param name="NearestPointIndex">Index of the nearest point within the edge-local arc's polyline;
/// retained for diagnostics and representative-section selection.</param>
/// <param name="TransformPhaseCoordinate">Stable world-space longitudinal coordinate used by
/// transform scarps. It is independent of edge-local point indexing, so adjacent visual edges do
/// not restart the oscillation at phase zero.</param>
/// <param name="CellPlateId">The owning plate id of this cell.</param>
/// <param name="ArcPlateA">Lower plate id of the nearest arc.</param>
/// <param name="ArcPlateB">Higher plate id of the nearest arc.</param>
/// <param name="SubductingPlateId">For convergent subduction only: the plate id that is subducting
/// (down-going). Null for collision, divergent, transform.</param>
/// <param name="IsCollision">True for a continent–continent convergent boundary (symmetric uplift, no trench/arc).</param>
public readonly record struct CellBoundarySample(
    bool Found,
    double SignedDistanceRad,
    PlateBoundaryKind Kind,
    int NearestPointIndex,
    double TransformPhaseCoordinate,
    int CellPlateId,
    int ArcPlateA,
    int ArcPlateB,
    int? SubductingPlateId,
    bool IsCollision)
{
    public GlobeVec3 NearestBoundaryPoint { get; init; }

    /// <summary>Unit tangent along the ordered boundary polyline.</summary>
    public GlobeVec3 AlongBoundaryDirection { get; init; }

    /// <summary>Unit surface tangent from the boundary into this sample's owning plate.</summary>
    public GlobeVec3 AcrossBoundaryDirection { get; init; }

    /// <summary>Stable world-space phase shared by every boundary kind.</summary>
    public double AlongBoundaryPhaseCoordinate { get; init; }
}
