using System.Collections.Generic;

namespace FantaSim.App.World;

/// <summary>Geodetic point on the globe.</summary>
public sealed record GeoPoint(double LatitudeDegrees, double LongitudeDegrees);

/// <summary>Polygon representing a plate cell on the globe surface.</summary>
public sealed record PlateCellPolygon(string PlateId, IReadOnlyList<GeoPoint> OuterRing, string? ElementId = null);

/// <summary>
/// Geodetic segment of a plate boundary. BoundaryType is the motion-derived classification at
/// the snapshot's tick — "convergent", "divergent", "transform", or "unknown" (no kinematics
/// for one of the adjacent plates, or effectively zero relative motion). The optional plate-depth
/// fields are schematic presentation metadata: when a convergent segment can be oriented, the
/// lower plate is the simplified underriding/subducting side and the upper plate is the side the
/// slab is drawn beneath. DipTarget is a nearby geodetic target on the upper-plate side used by
/// renderers to draw a visible dipping line instead of a flat surface stroke.
/// </summary>
public sealed record BoundaryGeoSegment(
    string BoundaryId,
    GeoPoint Start,
    GeoPoint End,
    string BoundaryType = "unknown",
    string? PlateAId = null,
    string? PlateBId = null,
    string? LowerPlateId = null,
    string? UpperPlateId = null,
    GeoPoint? DipTarget = null);

/// <summary>Complete globe geometry for cartographic placement.</summary>
public sealed record WorldGlobeGeometry(
    IReadOnlyList<string> PlateIds,
    IReadOnlyList<PlateCellPolygon> Cells,
    IReadOnlyList<BoundaryGeoSegment> BoundarySegments);
