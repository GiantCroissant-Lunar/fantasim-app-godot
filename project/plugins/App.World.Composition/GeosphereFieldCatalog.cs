using System.Collections.Generic;

namespace FantaSim.App.World.Composition;

/// <summary>
/// App-side field ids + descriptors. The id strings match the canonical world field-id conventions.
/// NOTE: GeosphereFieldIds (FantaSim.World.Contracts.Fields) is not yet present in the current
/// fantasim-world engine (it lives in the ref-projects only). Using literal strings here; promote
/// to GeosphereFieldIds constants when the engine exposes them (O2).
/// </summary>
public static class GeosphereFieldCatalog
{
    public static readonly FieldId PlateBoundaryDistance = new("plate-boundary-distance-m");
    public static readonly FieldId Elevation            = new("elevation-m");
    public static readonly FieldId CrustThickness       = new("crust-thickness-m");

    // App-LOCAL magma-ocean fields (sphere-regimes step 3). NOT yet in world field ids;
    // promote them there when a corrected world-side producer is restored (same path as replacing
    // SyntheticCrustLayer). Declared here so the composer registers them centrally.
    public static readonly FieldId SurfaceTemperature = new("surface-temperature-k");
    public static readonly FieldId MeltFraction       = new("melt-fraction");
    public static readonly FieldId HeatFlow           = new("heat-flow-mw-m2");

    public static readonly FieldDescriptor PlateBoundaryDistanceField =
        new(PlateBoundaryDistance, FieldDomain.Cell, FieldValueKind.Scalar);
    public static readonly FieldDescriptor ElevationField =
        new(Elevation, FieldDomain.Cell, FieldValueKind.Scalar);
    public static readonly FieldDescriptor CrustThicknessField =
        new(CrustThickness, FieldDomain.Cell, FieldValueKind.Scalar);
    public static readonly FieldDescriptor SurfaceTemperatureField =
        new(SurfaceTemperature, FieldDomain.Cell, FieldValueKind.Scalar);
    public static readonly FieldDescriptor MeltFractionField =
        new(MeltFraction, FieldDomain.Cell, FieldValueKind.Scalar);
    public static readonly FieldDescriptor HeatFlowField =
        new(HeatFlow, FieldDomain.Cell, FieldValueKind.Scalar);

    public static readonly IReadOnlyList<FieldDescriptor> All = new[]
    {
        PlateBoundaryDistanceField, ElevationField, CrustThicknessField,
        SurfaceTemperatureField, MeltFractionField, HeatFlowField,
    };

    /// <summary>
    /// Declare every catalog field into a composer (convenience for composition + tests).
    /// </summary>
    public static void DeclareInto(FieldComposer composer)
    {
        foreach (var d in All) composer.DeclareField(d);
    }
}
