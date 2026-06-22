using System.Collections.Generic;

namespace FantaSim.App.World.Composition;

/// <summary>
/// App-side field ids + descriptors for the atmosphere layers. Three Cell-domain scalar
/// fields produced by <see cref="AtmosphereBulkLayer"/> (bulk/0-D), plus one produced by
/// <see cref="AtmosphereCoupledLayer"/> (latitude-varying, coupled-climate regime).
/// </summary>
public static class AtmosphereFieldCatalog
{
    public static readonly FieldId AtmosphereGreenhouse   = new("atmosphere-greenhouse-c");
    public static readonly FieldId AtmosphereHydration     = new("atmosphere-hydration");
    public static readonly FieldId AtmospherePressure      = new("atmosphere-pressure-bar");
    public static readonly FieldId AtmosphereSurfaceTemp   = new("atmosphere-surface-temp-c");

    public static readonly FieldDescriptor AtmosphereGreenhouseField =
        new(AtmosphereGreenhouse, FieldDomain.Cell, FieldValueKind.Scalar);
    public static readonly FieldDescriptor AtmosphereHydrationField =
        new(AtmosphereHydration, FieldDomain.Cell, FieldValueKind.Scalar);
    public static readonly FieldDescriptor AtmospherePressureField =
        new(AtmospherePressure, FieldDomain.Cell, FieldValueKind.Scalar);
    public static readonly FieldDescriptor AtmosphereSurfaceTempField =
        new(AtmosphereSurfaceTemp, FieldDomain.Cell, FieldValueKind.Scalar);

    public static readonly IReadOnlyList<FieldDescriptor> All = new[]
    {
        AtmosphereGreenhouseField, AtmosphereHydrationField, AtmospherePressureField,
        AtmosphereSurfaceTempField,
    };

    /// <summary>
    /// Declare every catalog field into a composer (convenience for composition + tests).
    /// </summary>
    public static void DeclareInto(FieldComposer composer)
    {
        foreach (var d in All) composer.DeclareField(d);
    }
}
