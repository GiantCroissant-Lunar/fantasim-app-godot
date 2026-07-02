using System.Collections.Generic;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World;

/// <summary>
/// Motion-derived classification of a plate boundary at the snapshot's tick. Mirrors the
/// topology engine's <c>BoundaryType</c> as a contract-side, UnifyGeometry-free enum so the
/// host seam can colour polylines without referencing the topology package.
/// </summary>
public enum PlateBoundaryKind
{
    /// <summary>Plates move into the boundary (collision / subduction).</summary>
    Convergent,

    /// <summary>Plates move apart (mid-ocean ridge / rift).</summary>
    Divergent,

    /// <summary>Boundary-parallel shear (transform fault).</summary>
    Transform,

    /// <summary>No significant relative motion; renderers typically omit it.</summary>
    Inactive,
}

/// <summary>
/// One plate boundary as a smooth great-circle polyline: the unordered plate pair it separates,
/// the motion-derived <see cref="Kind"/>, and the ordered unit-sphere points along the arc. Points
/// are pre-subdivided (Godot-free, from the topology truth's ordered sample points via great-circle
/// interpolation) so the host seam only lifts them into line geometry. All points are unit length.
/// </summary>
/// <param name="PlateA">The lower plate id of the pair (<c>PlateA &lt; PlateB</c>).</param>
/// <param name="PlateB">The higher plate id of the pair.</param>
/// <param name="Kind">Motion-derived boundary type at the snapshot tick.</param>
/// <param name="Points">Ordered unit-sphere points along the smooth arc (at least two).</param>
public sealed record PlateBoundaryArc(
    int PlateA,
    int PlateB,
    PlateBoundaryKind Kind,
    IReadOnlyList<GlobeVec3> Points);
