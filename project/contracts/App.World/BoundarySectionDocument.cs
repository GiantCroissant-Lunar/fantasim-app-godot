using System.Collections.Generic;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World.Composition;

/// <summary>
/// Godot-free boundary-normal section data for a selected plate-boundary arc. This is distinct from
/// the radial cutaway wedge: it represents the mechanics across one typed boundary.
/// </summary>
public sealed record BoundarySectionDocument(
    int PlateA,
    int PlateB,
    PlateBoundaryKind Kind,
    GlobeVec3 Origin,
    GlobeVec3 NormalAxis,
    IReadOnlyList<BoundarySectionSample> Samples,
    IReadOnlyList<BoundarySectionBand> InteriorBands,
    double Exaggeration,
    double PlanetRadiusMetres,
    string? LabelOverride,
    int? SubductingPlateId = null,
    bool IsCollision = false);

/// <summary>One signed cross-boundary sample in a <see cref="BoundarySectionDocument"/>.</summary>
public readonly record struct BoundarySectionSample(
    double SignedDistanceRad,
    double ElevationMetres,
    double CrustThicknessMetres,
    PlateBoundaryKind BoundaryKind);

public readonly record struct BoundarySectionColor(double R, double G, double B);

public readonly record struct BoundarySectionBand(
    string Label,
    BoundarySectionColor Color,
    double OuterRadius,
    double InnerRadius);
